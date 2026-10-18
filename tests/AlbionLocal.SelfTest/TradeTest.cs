using System.Numerics;
using AlbionLocal.Photon.Serialization;
using AlbionLocal.Server;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;
using static AlbionLocal.SelfTest.AlbionTests;
using static AlbionLocal.SelfTest.CombatTest;

namespace AlbionLocal.SelfTest;

/// <summary>
/// The player trade window (analysis/work/stageJ/trade/SPEC.md §4) between <see cref="FakePeer"/> players in a city map (the client
/// data's items, no cluster files) on a <see cref="ManualClock"/>: the invite codes, the invitation, offers with their revisions and
/// per-recipient Updates, the accept rules and the commit with its conservation and joint save; then the cancels and guards.
/// </summary>
public static class TradeTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("trade: invite 6443/6441/6442, rc 0 + 175, accept 178 rev 2 + 180, refused adds resync, Updates per side, accept revision/delay/withdraw, commit 179, conservation, one SaveMany", Commit),
        ("trade: a change resets accepts, a destroyed offer leaves it, disconnect and decline 177 to both, the fit after the take, 9 m, the lapse, allowtrade 6446/6448, gold rc 1", Guards),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private const string Stack = "T4_PLANKS", Sword = "T4_MAIN_SWORD";

    private sealed record Trader(WorldSession Session, FakePeer Peer, Player Player);

    /// <summary>A world on a manual clock whose maps follow <paramref name="rules"/>, with a recording repository; each test runs in it.</summary>
    private sealed class Bench : IDisposable
    {
        private readonly string _dir = NewDir();
        private readonly SqliteCharacterRepository _db;
        public readonly PersistenceTest.FlakyRepository Repo;
        public readonly ManualClock Clock = new();
        public readonly World World;

        public Bench(ClusterPvp rules)
        {
            _db = Repository(_dir);
            Repo = new PersistenceTest.FlakyRepository(_db);
            var anchor = GameTime.Now;
            World = new World(new ServerConfig { JoinByCharacterId = true }, Repo, Client.Items, Client.GameData, Client.Spells)
            {
                Sync = new object(), Clock = Clock, GameTicks = () => anchor + Clock.Now.Ticks, PvpFallback = rules,
            };
        }

        public Trader Join(string name, float x, long silver = 0, params InventoryItem?[] bag)
        {
            var c = StarterKit.Default.NewCharacter(name);
            (c.Cluster, c.PositionX, c.PositionY, c.Silver) = ("0000", x, 5, silver);
            c.Equipment[0] = new InventoryItem(Sword);
            c.Inventory = [.. bag];
            Check(Repo.Create(c), $"character {name} created");
            var peer = new FakePeer();
            var session = new WorldSession(peer, World);
            peer.Disconnected = session.OnDisconnected;
            session.OnRequest(Request(new JoinRequest { ss = c.Id }, 1), encrypted: false);
            return new Trader(session, peer, session.Player!);
        }

        public void Run(double seconds)
        {
            for (var i = 0; i < Math.Round(seconds * 20); i++)
            {
                Clock.Advance(TimeSpan.FromMilliseconds(50));
                lock (World.Sync) World.Update();
            }
        }

        public void Place(Trader t, float x)
        {
            lock (World.Sync) t.Player.Teleport(new Vector2(x, 5), 0, DateTime.UtcNow);
        }

        public void Dispose()
        {
            World.Dispose();
            _db.Dispose();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }

    private static OperationResponse Call(Trader t, AlbionRequest req)
    {
        var n = t.Peer.Responses.Count;
        t.Session.OnRequest(Request(req, 7), encrypted: false);
        Check(t.Peer.Responses.Count == n + 1, $"one response to {req.GetType().Name}");
        return t.Peer.Responses[^1];
    }

    private static List<Dictionary<byte, object?>> Of(Trader t, EventCodes code, int mark) => [.. Since(t.Peer, mark).Where(e => e.Code == code).Select(e => e.P)];

    private static long[] Longs(Dictionary<byte, object?> p, byte k) => p.TryGetValue(k, out var v) && v is Array a ? [.. a.Cast<object>().Select(Convert.ToInt64)] : [];

    /// <summary>Key <paramref name="k"/> holds <paramref name="n"/> int[] (the spells of one entry each).</summary>
    private static bool Spells(Dictionary<byte, object?> p, byte k, int n) => n == 0 || p.TryGetValue(k, out var v) && v is Array a && a.Length == n && a.Cast<object>().All(x => x is int[]);

    private static int Invite(Trader from, Trader to) => Call(from, new InviteToPlayerTradeRequest { sr = to.Player.Id }).ReturnCode;

    private static int Add(Trader t, int id, Item item, int slot) => Call(t, new PlayerTradeAddItemRequest { yg = item.Id, yh = id, yi = slot }).ReturnCode;

    private static int Accept(Trader t, int id, int revision, bool yes = true) => Call(t, new PlayerTradeAcceptTradeRequest { yd = id, ye = revision, yf = yes }).ReturnCode;

    /// <summary>Count per item type over the bags and equipment of <paramref name="ts"/>.</summary>
    private static Dictionary<string, int> Counts(params Trader[] ts) =>
        ts.SelectMany(t => t.Player.Inventory.Bag.Items.Concat(t.Player.Inventory.Equipment.Items)).GroupBy(i => i.Template.Name).ToDictionary(g => g.Key, g => g.Sum(i => i.Count));

    private static bool Same(Dictionary<string, int> a, Dictionary<string, int> b) => a.Count == b.Count && a.All(kv => b.GetValueOrDefault(kv.Key) == kv.Value);

    private static void Commit()
    {
        using var bench = new Bench(ClusterPvp.City);
        var locked = Client.Items.All.First(t => t.Attrs.GetValueOrDefault("tradable") == "false" && t.IsSimple);
        var a = bench.Join("Ann", 5, 50_000_0000, new InventoryItem(Stack, 5), new InventoryItem(locked.Name, 1));
        var b = bench.Join("Bob", 15, 0, new InventoryItem(Sword));
        var c = bench.Join("Cid", 7);
        Check(Client.Items.Find(Stack) is { MaxStackSize: > 5 } && a.Player.Observers.Contains(b.Player), "the stack type stacks; Ann sees Bob");

        // Invite codes: 10 m 6443; a valid invite rc 0 {0 B, 6 id} and 175 {0 A, 6 id}; again 6441; a third player to B 6442.
        Check(Invite(a, b) == Trades.TooFarAway, "10 m: 6443");
        bench.Place(b, 9);
        var bm = b.Peer.Events.Count;
        var r = Call(a, new InviteToPlayerTradeRequest { sr = b.Player.Id });
        var id = (int)L(r.Parameters, 6);
        Check(r.ReturnCode == 0 && id > 0 && L(r.Parameters, 0) == b.Player.Id && r.Parameters[1] is "Bob", "4 m: rc 0 with B's id, name and the trade id");
        Check(Of(b, EventCodes.InvitationPlayerTrade, bm) is [var inv] && L(inv, 0) == a.Player.Id && L(inv, 6) == id && inv[1] is "Ann", "B: 175 {0 A, 1 Ann, 6 id}");
        Check(Invite(a, b) == Trades.YouAlreadyTrading && Invite(c, b) == Trades.PlayerAlreadyTrading, "invite again 6441; a third player to B 6442");

        // B accepts: 178 revision 2 and 180 (false, false) to both.
        var am = a.Peer.Events.Count;
        bm = b.Peer.Events.Count;
        Check(Call(b, new PlayerTradeInvitationAcceptRequest { yj = id }).ReturnCode == 0, "op 163: rc 0");
        foreach (var (t, m) in new[] { (a, am), (b, bm) })
            Check(Of(t, EventCodes.PlayerTradeUpdate, m) is [var u] && L(u, 1) == 2 && Of(t, EventCodes.PlayerTradeAcceptChange, m) is [var ac] && ac.GetValueOrDefault((byte)1) is null or false
                  && ac.GetValueOrDefault((byte)2) is null or false, $"{t.Player.Character.Name}: 178 rev 2, 180 (false, false)");

        // Refused adds (non-tradable, equipped, slot 11, the same item twice): rc 1 and a resync 178 to both, rev + 1, the offer as it was.
        var bag = a.Player.Inventory.Bag;
        var stack = bag.Items.First(i => i.Template.Name == Stack);
        var sword = b.Player.Inventory.Bag.Items.First(i => i.Template.Name == Sword);
        am = a.Peer.Events.Count;
        bm = b.Peer.Events.Count;
        Check(Add(a, id, bag.Items.First(i => i.Template == locked), 0) == 1, "a non-tradable item: rc 1");
        Check(Of(a, EventCodes.PlayerTradeUpdate, am) is [var rs] && L(rs, 1) == 3 && Longs(rs, 6).Length == 0 && Of(b, EventCodes.PlayerTradeUpdate, bm) is [var rsb] && L(rsb, 1) == 3,
              "the refusal resyncs both: 178 rev 3, the offer empty");
        Check(Add(a, id, a.Player.Inventory.Equipment.Items.First(), 0) == 1 && Add(a, id, stack, 11) == 1, "an equipped item and slot 11: rc 1");
        Check(Add(a, id, stack, 0) == 0 && Add(a, id, stack, 1) == 1, "the stack in slot 0: rc 0; again in slot 1: rc 1");
        am = a.Peer.Events.Count;
        bm = b.Peer.Events.Count;
        Check(Add(b, id, sword, 3) == 0 && Call(a, new PlayerTradeSetSilverOrGoldRequest { ym = id, yn = 1000_0000 }).ReturnCode == 0, "B's sword in slot 3, 1000 silver from A");
        var ua = Of(a, EventCodes.PlayerTradeUpdate, am)[^1];
        var ub = Of(b, EventCodes.PlayerTradeUpdate, bm)[^1];
        var revision = (int)L(ua, 1);
        Check(L(ub, 1) == revision && revision > 3, $"both sides at the same revision ({revision})");
        Check(Longs(ua, 6) is [var own] && own == stack.Id && Longs(ua, 17) is [3] && Longs(ua, 25) is [1] && L(ua, 2) == 1000_0000,
              "A's 178: own ids [the stack], the partner's slot 3 and count 1, own silver 1000");
        Check(Longs(ub, 6) is [var mine] && mine == sword.Id && Longs(ub, 18) is [var type] && type == stack.Template.Index && Longs(ub, 25) is [5] && L(ub, 4) == 1000_0000,
              "B's 178: own ids [the sword] only, the partner's type the stack's and count 5, partner silver 1000");
        Check(Spells(ua, 13, 1) && Spells(ua, 14, 1) && Spells(ua, 23, 1) && Spells(ua, 24, 1) && Spells(ub, 23, 1), "one int[] per entry in keys 13/14/23/24");

        // Accept: an old revision rc 1, within the delay rc 1, a withdrawal at once rc 0; after the delay both accept → 179 to both.
        Check(Accept(a, id, revision - 1) == 1 && Accept(a, id, revision) == 1 && Accept(a, id, revision, yes: false) == 0, "old revision rc 1, too soon rc 1, withdraw rc 0");
        var before = Counts(a, b);
        var silver = a.Player.Character.Silver + b.Player.Character.Silver;
        var aSilver = a.Player.Character.Silver;
        bench.Run(2);
        am = a.Peer.Events.Count;
        bm = b.Peer.Events.Count;
        Check(Accept(a, id, revision) == 0 && Of(b, EventCodes.PlayerTradeAcceptChange, bm) is [var acb] && acb[2] is true, "A accepts: B sees partner accepted");
        Check(Accept(b, id, revision) == 0 && Of(a, EventCodes.PlayerTradeFinished, am) is [var fa] && L(fa, 0) == id && Of(b, EventCodes.PlayerTradeFinished, bm).Count == 1,
              "B accepts: 179 to both");
        Check(bench.World.Trades.Of(a.Player) is null && bag.Items.Any(i => i.Template.Name == Sword) && !bag.Items.Any(i => i.Template.Name == Stack)
              && b.Player.Inventory.Bag.Items.Where(i => i.Template.Name == Stack).Sum(i => i.Count) == 5 && !b.Player.Inventory.Bag.Items.Any(i => i.Template.Name == Sword),
              "A has the sword, not the stack; B 5 of the stack, not the sword");
        Check(a.Player.Character.Silver == aSilver - 1000_0000 && b.Player.Character.Silver == 1000_0000 && a.Player.Character.Silver + b.Player.Character.Silver == silver
              && Same(before, Counts(a, b)), "silver A -1000, B +1000; every item type and the silver conserved");
        bench.World.Saves.Flush();
        Check(bench.Repo.Joint is [var joint] && joint.Length == 2 && joint.Contains(a.Player.Character.Id) && joint.Contains(b.Player.Character.Id), "SaveMany once with both characters");
        Check(bench.Repo.Find(b.Player.Character.Id)!.Silver == 1000_0000, "and the database has it");
    }

    private static void Guards()
    {
        using var bench = new Bench(ClusterPvp.City);
        var swords = Enumerable.Range(0, Inventory.BagSlots - 1).Select(_ => (InventoryItem?)new InventoryItem(Sword)).ToArray();
        var a = bench.Join("Ann", 5, 10_000_0000, new InventoryItem(Stack, 5), new InventoryItem(Sword), new InventoryItem(Stack, 7));
        var b = bench.Join("Bob", 8, 0, [new InventoryItem(Stack, 5), .. swords]);
        int Open()
        {
            var id = (int)L(Call(a, new InviteToPlayerTradeRequest { sr = b.Player.Id }).Parameters, 6);
            Check(Call(b, new PlayerTradeInvitationAcceptRequest { yj = id }).ReturnCode == 0, "a trade opens");
            return id;
        }
        int Rev() => (int)bench.World.Trades.Of(a.Player)!.Revision;
        var aStack = a.Player.Inventory.Bag[0]!;
        var aSword = a.Player.Inventory.Bag[1]!;
        var bStack = b.Player.Inventory.Bag[0]!;

        // A change resets both accepts and needs a new accept; the gold window refused.
        var id = Open();
        Check(Call(a, new PlayerTradeSetSilverOrGoldRequest { ym = id, yn = 1, yo = true }).ReturnCode == 1, "gold = true: rc 1");
        bench.Run(2);
        Check(Accept(a, id, Rev()) == 0 && bench.World.Trades.Of(a.Player)!.A.Accepted, "A accepted");
        var am = a.Peer.Events.Count;
        Check(Add(b, id, bStack, 0) == 0 && Of(a, EventCodes.PlayerTradeAcceptChange, am) is [var reset] && reset.GetValueOrDefault((byte)1) is null or false
              && !bench.World.Trades.Of(a.Player)!.A.Accepted, "B adds: 180 (false, false), A's accept gone");

        // An offered item destroyed: the tick takes it out (178 without it).
        Check(Add(a, id, a.Player.Inventory.Bag[2]!, 0) == 0, "A offers its second stack");
        var gone = a.Player.Inventory.Bag[2]!;
        a.Session.OnRequest(Request(new InventoryDestroyItemRequest { ri = gone.Id }, null), encrypted: false);
        am = a.Peer.Events.Count;
        bench.Run(0.05);
        Check(Of(a, EventCodes.PlayerTradeUpdate, am) is [var u] && Longs(u, 6).Length == 0 && bench.World.Trades.Of(a.Player)!.A.Slots.Count == 0, "the destroyed stack leaves the offer");

        // The fit after the take: B's bag is full and B gives its stack; A gives a stack of the same type and a sword: two slots needed,
        // one free → the accepts reset, a SystemMessage to B, nothing moves. Without the sword it fits and nothing is lost.
        var before = Counts(a, b);
        Check(Add(a, id, aStack, 0) == 0 && Add(a, id, aSword, 1) == 0, "A offers the stack and the sword");
        bench.Run(2);
        var bm = b.Peer.Events.Count;
        Check(Accept(a, id, Rev()) == 0 && Accept(b, id, Rev()) == 0, "both accept");
        Check(bench.World.Trades.Of(a.Player) is { A.Accepted: false, B.Accepted: false } && Of(b, EventCodes.SystemMessage, bm).Count == 1 && Of(b, EventCodes.PlayerTradeFinished, bm).Count == 0
              && Same(before, Counts(a, b)) && b.Player.Inventory.Bag.Contains(bStack), "no room: accepts reset, B told, nothing moved");
        Check(Call(a, new PlayerTradeRemoveItemRequest { yk = id, yl = 1 }).ReturnCode == 0, "A takes the sword out");
        bench.Run(2);
        bm = b.Peer.Events.Count;
        Check(Accept(a, id, Rev()) == 0 && Accept(b, id, Rev()) == 0 && Of(b, EventCodes.PlayerTradeFinished, bm).Count == 1, "now it fits: 179");
        Check(Same(before, Counts(a, b)) && b.Player.Inventory.Bag.Count == Inventory.BagSlots && b.Player.Inventory.Bag.Items.Where(i => i.Template.Name == Stack).Sum(i => i.Count) == 5,
              "nothing lost: B's bag full again with A's 5");

        // B declines while invited: 177 to both. Distance 9 m: 177 to both. The lapse: 177.
        foreach (var why in new[] { "decline", "far", "lapse" })
        {
            var tid = (int)L(Call(a, new InviteToPlayerTradeRequest { sr = b.Player.Id }).Parameters, 6);
            if (why != "decline" && why != "lapse") Check(Call(b, new PlayerTradeInvitationAcceptRequest { yj = tid }).ReturnCode == 0, "accepted");
            (am, bm) = (a.Peer.Events.Count, b.Peer.Events.Count);
            if (why == "decline") Check(Call(b, new PlayerTradeCancelRequest()).ReturnCode == 0, "op 162: rc 0");
            if (why == "far") bench.Place(b, 15);
            bench.Run(why == "lapse" ? 15.1 : 0.05);
            Check(Of(a, EventCodes.PlayerTradeCancel, am) is [var ca] && L(ca, 0) == tid && Of(b, EventCodes.PlayerTradeCancel, bm).Count == 1 && bench.World.Trades.Of(b.Player) is null,
                  $"{why}: 177 to both");
            bench.Place(b, 8);
        }

        // B disconnects during an open trade: 177 to A, nothing moved.
        id = Open();
        Check(Add(b, id, b.Player.Inventory.Bag.Items.First(i => i.Template.Name == Stack), 0) == 0, "B offers its stack");
        before = Counts(a, b);
        am = a.Peer.Events.Count;
        b.Peer.Disconnect();
        Check(Of(a, EventCodes.PlayerTradeCancel, am).Count == 1 && bench.World.Trades.Of(a.Player) is null && Same(before, Counts(a, b)), "disconnect: 177 to A, nothing moved");

        // The cluster rule: Disabled 6446; GroupOnly 6448 outside a party, rc 0 in one.
        foreach (var (rule, code) in new[] { (PlayerTradeRule.Disabled, Trades.NotAllowedInCluster), (PlayerTradeRule.GroupOnly, Trades.OnlyInParty) })
        {
            using var zone = new Bench(ClusterPvp.City with { AllowTrade = rule });
            var x = zone.Join("Xan", 5);
            var y = zone.Join("Yul", 7);
            Check(Invite(x, y) == code, $"allowtrade {rule}: {code}");
            if (rule != PlayerTradeRule.GroupOnly) continue;
            Check(zone.World.Parties.Invite(x.Player, y.Player.Character.Id, null, out _) == 0 && zone.World.Parties.AnswerInvitation(y.Player, true, false, Parties.NoReason) == 0, "a party");
            Check(Invite(x, y) == 0, "GroupOnly in one party: rc 0");
        }
        Check(ClusterPvp.Of(new System.Xml.Linq.XElement("archetype", new System.Xml.Linq.XAttribute("allowtrade", "GroupOnly"))).AllowTrade == PlayerTradeRule.GroupOnly
              && ClusterPvp.Of(new System.Xml.Linq.XElement("archetype")).AllowTrade == PlayerTradeRule.Allowed, "worldsettings allowtrade parsed, Allowed without it");
    }
}
