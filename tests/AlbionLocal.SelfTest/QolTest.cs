using System.Numerics;
using AlbionLocal.Server;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;
using static AlbionLocal.SelfTest.AlbionTests;
using static AlbionLocal.SelfTest.CombatTest;

namespace AlbionLocal.SelfTest;

/// <summary>StageN qol (analysis/work/stageN/qol/SPEC.md): player emotes, loadouts, /stuck and the minimap ping, in Thetford with two players.</summary>
public static class QolTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("qol emotes: 77 to both, 7807 for an unknown index, a looping one ends with 78 on a walk", Emote),
        ("qol loadouts: create, update, read, headers, equip from the inventory, saved (v29)", Loadouts),
        ("qol /stuck: after the delay to the nearest exit arrival, StuckCancel, then the cooldown refuses", StuckCommand),
        ("qol minimap ping: 181 to the pinger alone, then to the party on the map; a fast second one dropped", Ping),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    internal sealed class Zone : IDisposable
    {
        private readonly string _dir = NewDir();
        private readonly ManualClock _clock = new();
        public readonly AlbionLocal.Server.Persistence.SqliteCharacterRepository Characters;
        public readonly World World;
        public readonly (WorldSession S, FakePeer P) A, B;
        public readonly Vector2 Spot;

        public Zone(string cluster = "0000")
        {
            Characters = Repository(_dir);
            var anchor = GameTime.Now;
            var store = Client.Clusters!;
            World = new World(ServerConfig.PveDefaults with { JoinByCharacterId = true }, Characters, Client.Items, Client.GameData, Client.Spells)
            {
                Sync = new object(), Clock = _clock, Clusters = store, GameTicks = () => anchor + _clock.Now.Ticks, MapSeed = 3,
            };
            Spot = store.Get(cluster).Exits.Select(e => e.SpawnPosition).FirstOrDefault(p => p is not null) ?? Vector2.Zero;
            A = Join("Ann", cluster, Spot);
            B = Join("Bob", cluster, Spot);
        }

        private (WorldSession, FakePeer) Join(string name, string cluster, Vector2 pos)
        {
            var c = StarterKit.Default.NewCharacter(name);
            (c.Cluster, c.PositionX, c.PositionY) = (cluster, pos.X, pos.Y);
            Check(Characters.Create(c), $"character {name} created");
            var peer = new FakePeer();
            var s = new WorldSession(peer, World);
            peer.Disconnected = s.OnDisconnected;
            s.OnRequest(Request(new JoinRequest { ss = c.Id }, 1), encrypted: false);
            return (s, peer);
        }

        public void Run(double seconds)
        {
            for (var i = 0; i < Math.Round(seconds * 20); i++)
            {
                _clock.Advance(TimeSpan.FromMilliseconds(50));
                lock (World.Sync) World.Update();
            }
        }

        /// <summary>Moves the clock <paramref name="seconds"/> on in one step and runs one world update (timers due by then fire).</summary>
        public void Jump(double seconds)
        {
            _clock.Advance(TimeSpan.FromSeconds(seconds));
            lock (World.Sync) World.Update();
        }

        /// <summary>Sends <paramref name="req"/> from <paramref name="who"/> and returns its response (null = none).</summary>
        public Photon.Serialization.OperationResponse? Send((WorldSession S, FakePeer P) who, AlbionRequest req)
        {
            var n = who.P.Responses.Count;
            lock (World.Sync) who.S.OnRequest(Request(req, 9), encrypted: false);
            return who.P.Responses.Count > n ? who.P.Responses[^1] : null;
        }

        public void Dispose()
        {
            World.Dispose();
            Characters.Dispose();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }

    private static void Emote()
    {
        using var z = new Zone();
        var a = z.A.S.Player!;
        int am = z.A.P.Events.Count, bm = z.B.P.Events.Count;
        Check(z.Send(z.A, new PlayEmoteRequest { yb = 12, yc = z.B.S.Player!.Id }) is { ReturnCode: 0 }, "sit (12) at Bob: rc 0");
        static bool Sit(Dictionary<byte, object?> p, long id, long target) => L(p, 0) == id && L(p, 1) == 12 && L(p, 2) == target;
        Check(Since(z.A.P, am).Any(e => e.Code == EventCodes.PlayEmote && Sit(e.P, a.Id, z.B.S.Player!.Id))
              && Since(z.B.P, bm).Any(e => e.Code == EventCodes.PlayEmote && Sit(e.P, a.Id, z.B.S.Player!.Id)) && a.Emote is { Loops: true },
              "PlayEmote 77 {0 Ann, 1 12, 2 Bob} to Ann and Bob; the sit is kept as looping");
        Check(z.Send(z.A, new PlayEmoteRequest { yb = 99 }) is { ReturnCode: Emotes.NotUnlocked }, "an unknown emote: rc 7807 (NOT_UNLOCKED)");
        bm = z.B.P.Events.Count;
        var to = z.Spot + new Vector2(3, 0);
        z.Send(z.A, new MoveRequest { wf = z.World.GameNow, v6 = [z.Spot.X, z.Spot.Y], v8 = [to.X, to.Y], v9 = 5f });
        Check(a.Emote is null && Since(z.B.P, bm).Any(e => e.Code == EventCodes.StopEmote && L(e.P, 0) == a.Id), "Ann walks: StopEmote 78 {0 Ann} to Bob");
    }

    private static void Loadouts()
    {
        using var z = new Zone();
        var a = z.A.S.Player!;
        var me = a.Character.Id;
        var sword = a.Inventory.Equipment[0]!;
        var made = z.Send(z.A, new LoadoutCreateRequest { tw = me, tx = "PvE", ty = 2, tz = 13 });
        var id = made?.Parameters[0] as string;
        Check(made is { ReturnCode: 0 } && id is { Length: > 0 } && z.Send(z.A, new LoadoutCreateRequest { tw = Guid.NewGuid(), tx = "x" }) is { ReturnCode: WorldSession.LoadoutAccessDenied },
              "create: rc 0 with an id; another owner: AccessDenied");
        var names = Enumerable.Repeat("", 10).ToArray();
        names[0] = sword.Template.Name;
        var updated = z.Send(z.A, new LoadoutUpdateRequest { ud = me, ue = id, uf = 0, ug = "PvE", uh = 2, ui = 13, uj = names, uk = [sword.Quality, 0, 0, 0, 0, 0, 0, 0, 0, 0], uo = new int[10] });
        var read = z.Send(z.A, new LoadoutReadRequest { ua = me, ub = id });
        var heads = z.Send(z.A, new LoadoutReadHeadersRequest { uc = me });
        Check(updated is { ReturnCode: 0 } && L(updated.Parameters, 1) == 1 && read is { ReturnCode: 0 } && read.Parameters[6] is string[] r && r[0] == sword.Template.Name
              && heads?.Parameters[2] is string[] { Length: 1 } hs && hs[0] == id && heads.Parameters[4] is string[] { Length: 1 } hn && hn[0] == "PvE",
              "update: version 1; read gives the slot back; headers list it by name");
        lock (z.World.Sync)
        {
            a.Inventory.Equipment.Take(0);
            a.Inventory.Bag.Put(a.Inventory.Bag.FreeSlot(), sword);
        }
        var equip = z.Send(z.A, new LoadoutEquipRequest { t3 = me, t5 = id });
        Check(equip is { ReturnCode: 0 } && equip.Parameters[1] is byte[] st && st[0] == WorldSession.EquipDone && st[1] == 0 && a.Inventory.Equipment[0] == sword,
              "equip: the sword goes from the bag back into the main hand (status 1), other slots untouched (0)");
        lock (z.World.Sync) z.Characters.Save(a.Character.Clone());
        Check(z.Characters.Find(me)?.Loadouts is [{ Name: "PvE", Version: 1, Icon: 13 } saved] && saved.Equipment[0] == sword.Template.Name, "saved with the character (schema v29)");
    }

    private static void StuckCommand()
    {
        using var z = new Zone();
        var a = z.A.S.Player!;
        var away = z.Spot + new Vector2(25, 0);
        lock (z.World.Sync) a.Teleport(away, 0, DateTime.UtcNow);
        var am = z.A.P.Events.Count;
        z.Send(z.A, new StuckRequest());
        Check(a.StuckDue is not null && !Since(z.A.P, am).Any(e => e.Code == EventCodes.StuckCancel), "/stuck accepted: waiting, no StuckCancel yet");
        z.Jump(z.World.GameData!.StuckDelaySeconds + 1);
        var here = a.PositionAt(DateTime.UtcNow);
        var exits = z.World.Clusters!.Get("0000").Exits.Select(e => e.SpawnPosition ?? e.Position).ToList();
        var nearest = exits.OrderBy(e => Vector2.Distance(e, away)).First();
        Check(Vector2.Distance(here, nearest) < 3 && Since(z.A.P, am).Any(e => e.Code == EventCodes.StuckCancel && L(e.P, 0) == a.Id),
              $"after {z.World.GameData.StuckDelaySeconds} s: moved to the nearest exit arrival ({here} ~ {nearest}) and StuckCancel 466");
        am = z.A.P.Events.Count;
        z.Send(z.A, new StuckRequest());
        Check(a.StuckDue is null && Since(z.A.P, am).Any(e => e.Code == EventCodes.StuckCancel) && Since(z.A.P, am).Any(e => e.Code == EventCodes.SystemMessage),
              "again at once: refused by the cooldown (StuckCancel and a chat line)");
    }

    private static void Ping()
    {
        using var z = new Zone();
        var a = z.A.S.Player!;
        var b = z.B.S.Player!;
        int am = z.A.P.Events.Count, bm = z.B.P.Events.Count;
        var at = z.Spot + new Vector2(10, 5);
        z.Send(z.A, new SendMiniMapPingRequest { aco = at, acp = 1 });
        Check(Since(z.A.P, am).Any(e => e.Code == EventCodes.MiniMapPing && e.P[2] as string == "Ann") && !Since(z.B.P, bm).Any(e => e.Code == EventCodes.MiniMapPing),
              "no party: MiniMapPing {0 pos, 1 Attack, 2 Ann} to Ann only");
        lock (z.World.Sync)
        {
            z.World.Parties.Invite(a, b.Character.Id, null, out _);
            z.World.Parties.AnswerInvitation(b, true, false, 0);
        }
        Check(z.World.Parties.Of(a.Character.Id) is { } party && party.Find(b.Character.Id) is not null, "Ann and Bob in one party");
        z.Run(1);
        am = z.A.P.Events.Count;
        bm = z.B.P.Events.Count;
        z.Send(z.A, new SendMiniMapPingRequest { aco = at, acp = 2 });
        z.Send(z.A, new SendMiniMapPingRequest { aco = at, acp = 2 });
        Check(Since(z.B.P, bm).Count(e => e.Code == EventCodes.MiniMapPing && L(e.P, 1) == 2) == 1 && Since(z.A.P, am).Count(e => e.Code == EventCodes.MiniMapPing) == 1,
              "in a party: one ping (Defend) to Ann and Bob, the second within 0.5 s dropped");
    }
}
