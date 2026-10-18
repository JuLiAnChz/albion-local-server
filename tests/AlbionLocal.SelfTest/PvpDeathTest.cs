using System.Numerics;
using AlbionLocal.Photon;
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
/// StageE E3 (analysis/work/stageE/pvp/SPEC.md §4.5): the gamedata numbers of a death; then <see cref="FakePeer"/> players on a
/// <see cref="ManualClock"/> in cluster 0204 (its objects: loot bags need them) under RED, BLACK, YELLOW and ORANGE rules: the lethal hit (Died, KilledPlayer, the
/// kill penalty, the player loot bag with its protection, the item loss), the Leave after 3 s, the respawn; the execute and its
/// refusals, the faction execute of a non-lethal zone, the die button; the orange zone's kept equipment.
/// </summary>
public static class PvpDeathTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("pvp E3: gamedata ItemLoss events, attacker multipliers, loot / respawn / fame numbers, pvpwarningtype", Data),
        ("pvp E3 red: a lethal hit kills (Died, KilledPlayer, -100, NewLoot type 1 + 220, items 60-80 %, stack loss), protection 120 s, Leave after 3 s, respawn", Red),
        ("pvp E3 execute: black (1065 on slot 26, 1.5 s, KnockedOutToDead), refusals, yellow faction execute without loss, the die button", Execute),
        ("pvp E3 orange: the equipment stays (-5 %), the inventory drops", Orange),
        ("pvp E3 leaving: the silver drop moves silver 1:1 under a pickup factor, a disconnect while knocked down kills", Leaving),
        ("revive (stageN): a party mate bleeds out (BleedOut 693), damage stops the slot-27 revive, a full one raises it at 30 %", PartyRevive),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static void Data()
    {
        var d = Client.GameData!.PvpDeath;
        Check(d.AliveToDead is { EquipmentWear: { MinFactor: 0.2, MaxFactor: 0.4, MinimumRemaining: 0.01 }, EquipmentStack.Factor: 0.33, EquipmentDrops: true, InventoryDrops: true }
              && d.AliveToDead.EquipmentWear.For("rewardtoken").Min == 1 && d.AliveToDead.InventoryStack!.For("trashitem") == 1,
              "AliveToDead: 20-40 %, 1 % left, rewardtoken all, a third of each stack (trashitem all), everything drops");
        Check(d.KnockedOutToDead.EquipmentWear is { MinFactor: 0.15, MaxFactor: 0.35 }, "KnockedOutToDead: 15-35 %");
        Check(d.AliveToDeadOrange is { EquipmentDrops: false, InventoryDrops: true, EquipmentWear.MaxFactor: 0.05, InventoryWear.MaxFactor: 0.4 }
              && d.KnockedOutToDeadOrange is { EquipmentWear: null, EquipmentDrops: false, InventoryWear.MinFactor: 0.15 },
              "orange: the equipment stays (AliveToDeadOrange 5 %, KnockedOutToDeadOrange none), the inventory drops");
        Check(d.DurabilityMultiplier(1) == 1 && d.DurabilityMultiplier(2) == 1.15 && d.DurabilityMultiplier(11) == 2.5 && d.DurabilityMultiplier(30) == 2.5,
              "MultipleAttackerMultiplier: 1, 1.15 for 2, 2.50 from 11");
        Check(d is { AllowedProtectionSeconds: 120, ForcedProtectionSeconds: 90, VanishSeconds: 1800, VisibilityAfterDeathSeconds: 3, AutoRespawnSeconds: 600,
              KillPlayerIvFactor: 1, SilverDropFactor: 0.75, FameSharingRadius: 50 }, "Loot 120/90/1800 s, playervisibilityafterdeath 3, autorespawn 600, KillPlayerIv 1, SilverDropFromPvp 0.75, 50 m");
        Check(ClusterPvp.Of(System.Xml.Linq.XElement.Parse("<a pvpwarningtype=\"orange\"/>")).Orange && !ClusterPvp.Default.Orange, "pvpwarningtype orange");
        Check(Death.RespawnInfos() is { Length: Death.RespawnOptions } infos && infos[0] && infos.Count(x => x) == 1, "GetRespawnInfos: 9 options, the first set");
    }

    internal sealed record Fighter(WorldSession Session, FakePeer Peer, Player Player);

    /// <summary>
    /// A world on a manual clock with the cluster data, every map under <paramref name="rules"/> (<see cref="World.PvpOverride"/>), maps
    /// seeded, mobs without AI; the players stand in 0204 next to its guard tower (as CombatTest), x metres east of it; respawn in 0204.
    /// </summary>
    internal sealed class Arena : IDisposable
    {
        private readonly string _dir = NewDir();
        private readonly ManualClock _clock = new();
        public readonly SqliteCharacterRepository Characters;
        public readonly World World;
        private readonly Vector2 _stand;

        public Arena(ClusterPvp rules, Func<ServerConfig, ServerConfig>? config = null)
        {
            Characters = Repository(_dir);
            var anchor = GameTime.Now;
            var store = Client.Clusters!;
            var settings = new ServerConfig { JoinByCharacterId = true, PvpRespawnCluster = "0204" };
            World = new World(config?.Invoke(settings) ?? settings, Characters, Client.Items, Client.GameData, Client.Spells)
            {
                Sync = new object(), Clock = _clock, GameTicks = () => anchor + _clock.Now.Ticks, Clusters = store, PvpOverride = rules, MapSeed = 7, MobAi = false,
            };
            _stand = store.Get("0204").MobSpawnPoints.First(p => p.Mob == "T6_MOB_GUARD_TOWER").Position + new Vector2(3, 0);
        }

        public Map Map => World.Maps.Find("0204")!;

        public Fighter Join(string name, float x, Action<Character>? setup = null)
        {
            var c = StarterKit.Default.NewCharacter(name);
            (c.Cluster, c.PositionX, c.PositionY) = ("0204", _stand.X + x, _stand.Y);
            c.Equipment[0] = new InventoryItem("T4_MAIN_SWORD");
            setup?.Invoke(c);
            Check(Characters.Create(c), $"character {name} created");
            return Rejoin(c.Id);
        }

        public Fighter Rejoin(Guid id)
        {
            var peer = new FakePeer();
            var session = new WorldSession(peer, World);
            peer.Disconnected = session.OnDisconnected;
            session.OnRequest(Request(new JoinRequest { ss = id }, 1), encrypted: false);
            return new Fighter(session, peer, session.Player!);
        }

        public void Run(double seconds)
        {
            for (var i = 0; i < Math.Round(seconds * 20); i++)
            {
                _clock.Advance(TimeSpan.FromMilliseconds(50));
                lock (World.Sync) World.Update();
            }
        }

        public short? Attack(Fighter from, Fighter to)
        {
            var n = from.Peer.Responses.Count;
            Send(from.Session, new AttackStartRequest { wf = World.GameNow, co = to.Player.Id, cp = 0 });
            return from.Peer.Responses.Count > n ? from.Peer.Responses[^1].ReturnCode : null;
        }

        /// <summary>ChangeFlaggingPrepare (op 354) to <paramref name="to"/>: the response's return code.</summary>
        public short Flag(Fighter f, byte to)
        {
            var n = f.Peer.Responses.Count;
            f.Session.OnRequest(Request(new ChangeFlaggingPrepareRequest { eo = to }, 5), encrypted: false);
            return f.Peer.Responses.Count > n ? f.Peer.Responses[^1].ReturnCode : (short)-1;
        }

        /// <summary>CastStart of the execute on <paramref name="to"/>; the response's return code, null = accepted (no response).</summary>
        public short? Execute(Fighter from, Fighter to)
        {
            var n = from.Peer.Responses.Count;
            Send(from.Session, new CastStartRequest { wf = World.GameNow, dx = Death.ExecuteSlot, d0 = Death.DefaultExecuteId, d1 = to.Player.Id, d4 = 1 });
            return from.Peer.Responses.Count > n ? from.Peer.Responses[^1].ReturnCode : null;
        }

        /// <summary><paramref name="f"/> at 1 health, so the next hit is lethal.</summary>
        public void Weaken(Fighter f)
        {
            lock (World.Sync) f.Player.Health.Set(1, Math.Max(World.GameNow, f.Player.Health.Stamp));
        }

        /// <summary><paramref name="f"/> knocked down by itself (as a GM .kill: a PvE knockdown).</summary>
        public void KnockDown(Fighter f)
        {
            lock (World.Sync) Knockdown.DamageSelf(f.Player, 1_000_000);
        }

        public void Place(Fighter f, float x) { lock (World.Sync) f.Player.Teleport(_stand + new Vector2(x, 0), 0, DateTime.UtcNow); }

        public void Dispose()
        {
            World.Dispose();
            Characters.Dispose();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }

    /// <summary>The bag's items as (name, count, durability share of the full one).</summary>
    private static List<(string Name, int Count, double Share)> Contents(LootObject bag) =>
        [.. bag.Items.Items.Select(i => (i.Template.Name, i.Count, i.Template.FullDurabilityRaw > 0 ? i.DurabilityRaw / (double)i.Template.FullDurabilityRaw : 1))];

    private static void Red()
    {
        using var arena = new Arena(new ClusterPvp(PvpRule.PvpAllowed, PlayersAreLethal: true, WarningType: "red"));
        var world = arena.World;
        var a = arena.Join("Ann", 5, ch => ch.Flag = Pvp.Red);
        var b = arena.Join("Bob", 7, ch => ch.Inventory.Add(new InventoryItem("T6_ORE", 30)));
        var c = arena.Join("Cid", 6);
        var d = arena.Join("Dan", 4, ch => ch.Flag = Pvp.Red);
        Check(world.Parties.Invite(a.Player, d.Player.Character.Id, null, out _) == 0 && world.Parties.AnswerInvitation(d.Player, true, false, Parties.NoReason) == 0,
              "Dan joins Ann's party");
        var equipped = b.Player.Inventory.Equipment.Items.Select(i => (i.Template.Name, i.DurabilityRaw)).ToList();
        var types = b.Player.Inventory.EquipmentTypeIds();
        var fame = Death.KillFame(b.Player);
        Check(equipped.Count >= 2 && fame > 0, $"Bob wears {equipped.Count} items worth {fame / 10000.0} kill fame");

        // 1. Red A kills Blue B with a damage hit: no knockdown, Died, KilledPlayer, UpdateFame {5 false}, the kill penalty.
        arena.Weaken(b);
        var (am, bm, cm) = (a.Peer.Events.Count, b.Peer.Events.Count, c.Peer.Events.Count);
        Check(arena.Attack(a, b) is null, "Red A on Blue B: accepted");
        arena.Run(1);
        var gotB = Since(b.Peer, bm);
        Check(b.Player.Combat.Down is { Kind: KnockdownKind.Dead } && !gotB.Any(x => x.Code == EventCodes.KnockedDown)
              && gotB.Any(x => x.Code == EventCodes.HealthUpdate && L(x.P, 0) == b.Player.Id && !x.P.ContainsKey(3)), "B is dead: the lethal HealthUpdate, no KnockedDown");
        foreach (var (who, peer, mark) in new[] { ("A", a.Peer, am), ("B", b.Peer, bm), ("C", c.Peer, cm) })
            Check(Since(peer, mark).Where(x => x.Code == EventCodes.Died).ToList() is [var died] && L(died.P, 1) == b.Player.Id && L(died.P, 9) == a.Player.Id
                  && L(died.P, 5) == Pvp.Blue && L(died.P, 13) == Pvp.Red && died.P[2] is "Bob" && died.P[10] is "Ann",
                  $"{who} gets Died {{1 B, 2 Bob, 5 0, 9 A, 10 Ann, 13 255}}");
        var gotA = Since(a.Peer, am);
        Check(gotA.FirstOrDefault(x => x.Code == EventCodes.KilledPlayer).P is { } kp && L(kp, 0) == a.Player.Id && L(kp, 1) == b.Player.Id && kp[2] is "Bob" && L(kp, 3) == fame
              && gotA.FirstOrDefault(x => x.Code == EventCodes.UpdateFame).P is { } uf && L(uf, 2) == fame && uf.GetValueOrDefault((byte)5) is null or false
              && a.Player.Character.Fame == fame, $"A: KilledPlayer {{1 B, 2 Bob, 3 {fame}}} and UpdateFame {{2 fame, no key 5}} ({Show(gotA.Where(x => x.Code is EventCodes.KilledPlayer or EventCodes.UpdateFame))})");
        Check(gotA.Where(x => x.Code == EventCodes.ReputationUpdate).Select(x => (L(x.P, 6), F(x.P, 2))).ToList() is [(Reputation.LostAttack, -1), (Reputation.LostKill, -100)],
              $"A: ReputationUpdate attack -1, then kill -100 (NEUTRAL killpenalty) ({Show(gotA.Where(x => x.Code == EventCodes.ReputationUpdate))})");

        // The loot bag: NewLoot type 1 with the looters (A and its party) and LootEquipmentChanged with B's gear; B's own items are gone.
        var bag = arena.Map.Objects.Values.OfType<PlayerLootBag>().Single();
        var gotC = Since(c.Peer, cm);
        var lootAt = gotC.FindIndex(x => x.Code == EventCodes.NewLoot);
        Check(lootAt >= 0 && gotC[lootAt].P is var nl && L(nl, 0) == bag.Id && L(nl, 7) == 1 && L(nl, 2) == b.Player.Id && nl[3] is "Bob"
              && nl[10] is byte[] { Length: 32 } looters && looters.Chunk(16).Select(g => new Guid(g)).ToHashSet().SetEquals([a.Player.Character.Id, d.Player.Character.Id])
              && nl[23] is byte[] victim && new Guid(victim) == b.Player.Character.Id
              && L(nl, 24) == Pvp.Blue && L(nl, 25) == 3 && L(nl, 26) == -1,
              $"C sees NewLoot {{0 bag, 2 B, 3 Bob, 7 1, 10 [A, D], 23 B's Guid, 24 0, 25 3, 26 -1}} ({Show(gotC.Where(x => x.Code == EventCodes.NewLoot))})");
        Check(lootAt + 1 < gotC.Count && gotC[lootAt + 1] is { Code: EventCodes.LootEquipmentChanged } lec && L(lec.P, 0) == bag.Id
              && lec.P[1] is int[] look && look.SequenceEqual(types), $"then LootEquipmentChanged {{0 bag, 1 B's 10 equipment types [{string.Join(",", types)}]}} ({Show(gotC.Skip(lootAt).Take(2))})");
        Check(b.Player.Inventory.Equipment.Count == 0 && b.Player.Inventory.Bag.Count == 0
              && arena.Characters.Find(b.Player.Character.Id) is { } saved && saved.Equipment.All(i => i is null) && saved.Inventory.All(i => i is null || i.Name != "T6_ORE"),
              "B's equipment and inventory are empty, saved so at once");
        Check(gotB.Count(x => x.Code == EventCodes.InventoryState && x.P[0] is long[] ids && ids.All(id => id == 0)) == 2, "B gets InventoryState of both emptied containers");
        var contents = Contents(bag);
        Check(contents.Where(x => x.Name == "T6_ORE").ToList() is [var ore] && ore.Count is 20 or 21, $"the ore stack loses a third (30 -> {string.Join(",", contents.Where(x => x.Name == "T6_ORE").Select(x => x.Count))})");
        foreach (var (name, raw) in equipped)
        {
            var item = contents.FirstOrDefault(x => x.Name == name);
            Check(item.Name is null || item.Share is >= 0.599 and <= 0.801, $"{name}: destroyed or in the bag at 60-80 % ({item.Share:0.###}, was {raw})");
        }
        Check(contents.Count(x => x.Name != "T6_ORE") >= 1, $"some of the gear survives into the bag ({contents.Count} items)");

        // 2. Protection: C may not open it for 120 s, A at once; then C may. 3 s after the death the others get B's Leave.
        arena.Place(a, 7);
        arena.Place(c, 7);
        cm = c.Peer.Events.Count;
        Send(c.Session, new RegisterToObjectRequest { zg = bag.Id });
        Check(!Since(c.Peer, cm).Any(x => x.Code == EventCodes.AttachItemContainer), "C (no looter) cannot open the bag");
        am = a.Peer.Events.Count;
        Send(a.Session, new RegisterToObjectRequest { zg = bag.Id });
        Check(Since(a.Peer, am).Any(x => x.Code == EventCodes.AttachItemContainer && L(x.P, 0) == bag.Id), "A (the killer) opens it");
        cm = c.Peer.Events.Count;
        arena.Run(2.5);
        Check(Since(c.Peer, cm).Any(x => x.Code == EventCodes.Leave && L(x.P, 0) == b.Player.Id), "about 3 s after the death the others get B's Leave");
        var e = arena.Join("Eve", 8);
        Check(!e.Peer.Events.Any(x => Code(x) == (int)EventCodes.NewCharacter && L(Wire(x), 0) == b.Player.Id), "a late observer gets no NewCharacter of dead B");
        arena.Run(118);
        cm = c.Peer.Events.Count;
        Send(c.Session, new RegisterToObjectRequest { zg = bag.Id });
        Check(Since(c.Peer, cm).Any(x => x.Code == EventCodes.AttachItemContainer), "after 120 s C opens it");

        // 3. The respawn: GetRespawnInfos, Respawn → the cluster change to the respawn city; the Join there: full health and Respawn.
        var n = b.Peer.Responses.Count;
        Send(b.Session, new GetRespawnInfosRequest());
        Check(b.Peer.Responses.Count == n + 1 && b.Peer.Responses[^1].Parameters[0] is bool[] { Length: 9 } options && options[0], "GetRespawnInfos {0 bool[9]}");
        Send(b.Session, new RespawnRequest { aax = 0 });
        var push = b.Peer.Responses[^1];
        Check(push.Parameters[ReservedKeys.OperationCode] is (short)OperationCodes.ChangeCluster && push.Parameters[0] is "0204"
              && b.Player.Character is { Cluster: "0204", PendingTransfer.Via: Death.RespawnVia } moved
              && new Vector2(moved.PositionX, moved.PositionY) == Transfers.DefaultSpawn(Client.Clusters!.Get("0204")),
              "Respawn: the ChangeCluster push to 0204 (Pvp:RespawnCluster), at its respawn point, committed as a respawn");
        var again = arena.Rejoin(b.Player.Character.Id);
        arena.Run(0.1);
        Check(again.Player.Combat.Down is null && again.Player.Health.At(world.GameNow) == again.Player.Health.Max
              && again.Peer.Events.Select(x => Code(x)).SkipWhile(x => x != (int)EventCodes.JoinFinished).Contains((int)EventCodes.Respawn),
              "B joins 0204 standing with full health, a Respawn event after JoinFinished");
    }

    private static void Execute()
    {
        using (var arena = new Arena(new ClusterPvp(PvpRule.PvpForced, PlayersAreLethal: true, WarningType: "black")))
        {
            var world = arena.World;
            var a = arena.Join("Ann", 5);
            var b = arena.Join("Bob", 6);
            var c = arena.Join("Cid", 4.5f);
            Check(world.Parties.Invite(a.Player, c.Player.Character.Id, null, out _) == 0 && world.Parties.AnswerInvitation(c.Player, true, false, Parties.NoReason) == 0,
                  "Cid joins Ann's party");
            var equipped = b.Player.Inventory.Equipment.Items.Select(i => i.Template.Name).ToList();
            Check(arena.Execute(a, b) == WorldSession.CastRefused, "no execute on a standing player");
            arena.KnockDown(b);
            arena.KnockDown(c);
            Check(b.Player.Combat.Down is { Kind: KnockdownKind.Pve } && arena.Attack(a, b) == WorldSession.AttackInvalidTarget, "B knocked down (PvE): no auto-attack on it");
            Check(arena.Execute(a, c) == WorldSession.CastRefused, "no execute on a party member (Friendly in black)");
            Check(arena.Execute(a, b) is null && a.Player.PvpState.Execute is not null, "A starts an execute on B");
            Send(a.Session, new CastCancelRequest { wf = world.GameNow });
            arena.Run(1.6);
            Check(a.Player.PvpState.Execute is null && b.Player.Combat.Down is { Kind: KnockdownKind.Pve }, "A's own CastCancel ends the execute: B still knocked down");
            var bm = b.Peer.Events.Count;
            Check(arena.Execute(a, b) is null && a.Player.PvpState.Execute is not null, "A executes B: accepted");
            Check(Since(b.Peer, bm).FirstOrDefault(x => x.Code == EventCodes.CastStart).P is { } cs && L(cs, 0) == a.Player.Id && L(cs, 5) == Death.DefaultExecuteId
                  && L(cs, 7) == b.Player.Id && L(cs, 9) == Death.ExecuteSlot && L(cs, 4) - L(cs, 1) == (long)(1.5 * TimeSpan.TicksPerSecond),
                  "B sees CastStart {0 A, 5 1065, 7 B, 9 26, 1.5 s}");
            arena.Run(1.4);
            Check(b.Player.Combat.Down is { Kind: KnockdownKind.Pve }, "B still knocked down before 1.5 s");
            arena.Run(0.2);
            var gotB = Since(b.Peer, bm);
            Check(b.Player.Combat.Down is { Kind: KnockdownKind.Dead } && gotB.Any(x => x.Code == EventCodes.CastSpell && L(x.P, 0) == a.Player.Id && L(x.P, 3) == Death.DefaultExecuteId)
                  && gotB.Any(x => x.Code == EventCodes.Died && L(x.P, 9) == a.Player.Id), "1.5 s later: CastSpell 1065, B dies (Died {9 A})");
            var bag = arena.Map.Objects.Values.OfType<PlayerLootBag>().Single();
            Check(Contents(bag).Where(x => equipped.Contains(x.Name)).All(x => x.Share is >= 0.649 and <= 0.851) && b.Player.Inventory.Equipment.Count == 0,
                  $"KnockedOutToDead: the gear drops at 65-85 % ({string.Join(", ", Contents(bag).Select(x => $"{x.Name} {x.Share:0.##}"))})");
            Check(bag.ProtectedUntil - bag.CreatedTicks == 90 * TimeSpan.TicksPerSecond, "PvpForced: 90 s of loot protection");

            // The die button: refused before showdiebuttondelay, then the death.
            var e = arena.Join("Eve", 20);
            arena.KnockDown(e);
            Send(e.Session, new SuicideRequest());
            Check(e.Player.Combat.Down is { Kind: KnockdownKind.Pve }, "Suicide within 3 s of the knockdown: refused");
            arena.Run(3.1);
            var em = e.Peer.Events.Count;
            Send(e.Session, new SuicideRequest());
            Check(e.Player.Combat.Down is { Kind: KnockdownKind.Dead } && Since(e.Peer, em).Any(x => x.Code == EventCodes.Died && L(x.P, 1) == e.Player.Id),
                  "after 3 s the die button kills (Died)");
        }
        using (var arena = new Arena(new ClusterPvp(PvpRule.PvpAllowed, PlayersAreLethal: false)))
        {
            var world = arena.World;
            var a = arena.Join("Ann", 5, ch => ch.Flag = Pvp.Red);
            var b = arena.Join("Bob", 6);
            var f = arena.Join("Fay", 5.5f, ch => (ch.Faction, ch.Flag) = (2, 2));
            var g = arena.Join("Gus", 6.5f, ch => (ch.Faction, ch.Flag) = (5, 5));
            arena.KnockDown(b);
            arena.KnockDown(g);
            Check(arena.Execute(a, b) == WorldSession.CastRefused && Death.ExecuteRefusal(a.Player, b.Player, world.GameNow) == "no execute in a non-lethal cluster",
                  "yellow: Red A may not execute Blue B");
            var items = g.Player.Inventory.Equipment.Count;
            Check(arena.Execute(f, g) is null, "yellow: Forest F executes Swamp G (enemy factions)");
            arena.Run(1.6);
            Check(g.Player.Combat.Down is { Kind: KnockdownKind.Dead } && g.Player.Inventory.Equipment.Count == items
                  && !arena.Map.Objects.Values.OfType<PlayerLootBag>().Any(), "G dies, keeps its items, no loot bag [infer]");
        }
    }

    private static void Orange()
    {
        using var arena = new Arena(new ClusterPvp(PvpRule.PvpAllowed, PlayersAreLethal: true, WarningType: "orange"));
        var world = arena.World;
        var a = arena.Join("Ann", 5, ch => ch.Flag = Pvp.Red);
        var b = arena.Join("Bob", 7, ch => ch.Inventory.Add(new InventoryItem("T6_ORE", 30)));
        var sword = b.Player.Inventory.Equipment[0]!;
        var (before, worn) = (sword.DurabilityRaw, b.Player.Inventory.Equipment.Count);
        arena.Weaken(b);
        Check(arena.Attack(a, b) is null, "Red A on Blue B");
        arena.Run(1);
        Check(b.Player.Combat.Down is { Kind: KnockdownKind.Dead } && b.Player.Inventory.Equipment.Count == worn && b.Player.Inventory.Equipment[0] == sword
              && sword.DurabilityRaw == before - (long)Math.Round(sword.Template.FullDurabilityRaw * 0.05), $"B keeps its equipment, the sword -5 % ({before} -> {sword.DurabilityRaw})");
        var bag = arena.Map.Objects.Values.OfType<PlayerLootBag>().Single();
        Check(b.Player.Inventory.Bag.Count == 0 && Contents(bag).Any(x => x.Name == "T6_ORE") && !bag.Items.Contains(sword),
              "the inventory drops into the bag, the gear not");
    }

    private static void Leaving()
    {
        using var arena = new Arena(new ClusterPvp(PvpRule.PvpAllowed, PlayersAreLethal: true, WarningType: "red"),
            c => c with { PvpDeathSilverDrop = true, SilverPickupFactor = 2, SilverPickupTax = 0.1 });
        var a = arena.Join("Ann", 5, ch => ch.Flag = Pvp.Red);
        var b = arena.Join("Bob", 7, ch => ch.Silver = 1000 * 10000L);
        var (aBefore, bBefore) = (a.Player.Character.Silver, b.Player.Character.Silver);
        arena.Weaken(b);
        Check(arena.Attack(a, b) is null, "Red A on Blue B");
        arena.Run(1);
        var pile = arena.Map.Objects.Values.OfType<SilverPile>().SingleOrDefault();
        Check(b.Player.Combat.Down is { Kind: KnockdownKind.Dead } && pile is { SourceKind: SilverPile.FromPlayer }, "B dies and leaves a silver pile");
        Send(a.Session, new TakeSilverRequest { wf = arena.World.GameNow, ago = pile!.Id });
        Check(a.Player.Character.Silver - aBefore == bBefore - b.Player.Character.Silver && a.Player.Character.Silver > aBefore,
              $"A gains what B lost ({(a.Player.Character.Silver - aBefore) / 10000} = {(bBefore - b.Player.Character.Silver) / 10000}), factor 2 and tax not applied");

        // Knocked down by a mob in a lethal cluster, then the connection ends: it dies first, its items in a bag; it joins at the respawn point.
        var f = arena.Join("Fay", 20);
        arena.KnockDown(f);
        var id = f.Player.Character.Id;
        f.Peer.Disconnect();
        Check(arena.Map.Objects.Values.OfType<PlayerLootBag>().Count(x => x.VictimCharacter == id) == 1 && arena.Characters.Find(id) is { } saved
              && saved.Equipment.All(i => i is null) && f.Player.Character is { Cluster: "0204" } left
              && new Vector2(left.PositionX, left.PositionY) == Transfers.DefaultSpawn(Client.Clusters!.Get("0204")),
              "a disconnect while knocked down: the death's loot bag, the character left at the respawn point");
    }

    /// <summary>
    /// StageN revive (analysis/work/stageN/revive/SPEC.md): with Pve:PartyRevive, Bob knocked down next to party mate Ann bleeds out
    /// (BleedOut 693 instead of KnockedDown); Ann's PLAYER_BLEEDOUT_REVIVE on slot 27 is cut by a mob's hit; the second one gets Bob up
    /// after 6 s with 30 % health (Respawn 88).
    /// </summary>
    private static void PartyRevive()
    {
        using var arena = new Arena(new ClusterPvp(PvpRule.PvpAllowed, PlayersAreLethal: false), cfg => cfg with { PvePartyRevive = true });
        var world = arena.World;
        var a = arena.Join("Ann", 5);
        var b = arena.Join("Bob", 6);
        Check(world.Parties.Invite(a.Player, b.Player.Character.Id, null, out _) == 0 && world.Parties.AnswerInvitation(b.Player, true, false, Parties.NoReason) == 0,
              "Bob joins Ann's party");
        var am = a.Peer.Events.Count;
        arena.KnockDown(b);
        Check(b.Player.Combat.Down is { BleedOutPoints: 120 } && Since(a.Peer, am).Any(x => x.Code == EventCodes.BleedOut && L(x.P, 4) == b.Player.Id && L(x.P, 7) == 120 * 10000)
              && !Since(a.Peer, am).Any(x => x.Code == EventCodes.KnockedDown), "Bob bleeds out: Ann sees BleedOut {4 Bob, 7 120 points}, no KnockedDown");
        var spell = world.Spells!.IdOf("PLAYER_BLEEDOUT_REVIVE");
        short? StartRevive()
        {
            var n = a.Peer.Responses.Count;
            Send(a.Session, new CastStartRequest { wf = world.GameNow, dx = AlbionLocal.Server.Systems.Revive.Slot, d0 = spell, d1 = b.Player.Id, d4 = 1 });
            return a.Peer.Responses.Count > n ? a.Peer.Responses[^1].ReturnCode : null;
        }
        Check(StartRevive() is null && a.Player.PvpState.Revive is { } cast && cast.End - cast.Start == 6 * TimeSpan.TicksPerSecond, "Ann starts the 6 s revive");
        arena.Run(1);
        var mob = arena.Map.Objects.Values.OfType<Mob>().First();
        lock (world.Sync) Combat.DamagePlayer(mob, a.Player, 10, EffectType.Physical, HitOrigin.Direct, -1, world.GameNow);
        Check(a.Player.PvpState.Revive is null && b.Player.Combat.Down is not null, "a mob's hit on Ann interrupts it: Bob still down");
        Check(StartRevive() is null, "Ann starts again");
        var bm = b.Peer.Events.Count;
        var up = b.Player.Health.Max;
        arena.Run(6.1);
        Check(b.Player.Combat.Down is null && Math.Abs(b.Player.Health.Value - up * 0.3f) <= 1 && b.Player.Health.Max < up
              && Since(b.Peer, bm).Any(x => x.Code == EventCodes.Respawn && L(x.P, 0) == b.Player.Id),
              $"6 s later Bob is up (Respawn) at 30 % of his health, BLEEDOUT_REVIVE_DEBUFF on (-10 % max) ({b.Player.Health.Value:0}/{b.Player.Health.Max:0})");
    }
}
