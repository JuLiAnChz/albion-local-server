using System.Numerics;
using AlbionLocal.Photon;
using AlbionLocal.Photon.Serialization;
using AlbionLocal.Server;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;
using static AlbionLocal.SelfTest.AlbionTests;

namespace AlbionLocal.SelfTest;

/// <summary>
/// Static dungeons (analysis/work/stageN/static-dungeons/SPEC.md): Stoneroot Caverns (DNG-KPR-02-MAIN-04, DUNGEON_YELLOW T5) walked into
/// from its main entrance in Tharcal Fissure 4206 and out again, with its T5 mobs and boss points, the world.xml links of every static
/// dungeon, the event schedule of staticdungeons.xml, 499 on the Join and the world map's keys 65-68.
/// </summary>
public static class StaticDungeonTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("static dungeon: Stoneroot Caverns from 4206's entrance and back (98 dungeons linked both ways, T5 mobs and bosses, yellow rules, 499 on the Join, events in world-map keys 65-68)", Stoneroot),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static ClusterStore Store => Client.Clusters!;

    private const string Dungeon = "DNG-KPR-02-MAIN-04", Entrance = "4206";

    private static void Stoneroot()
    {
        var info = Store.World;
        var t = Store.Get(Dungeon);
        _ = Store.Get(Entrance);
        // world.xml: every exit of a dungeon cluster and every entrance leads to an exit of its target (the 15 unconnected entrance stubs aside).
        var dungeonClusters = info.Clusters.Where(c => c.Attrs.ContainsKey("belongstostaticdungeonid")).ToList();
        var entrances = info.Clusters.SelectMany(c => c.Exits.Where(e => e.Attrs.ContainsKey("staticdungeonid") && e.Target != Guid.Empty)).ToList();
        var broken = dungeonClusters.SelectMany(c => c.Exits).Concat(entrances).Where(e => info.Find(e.TargetCluster)?.Exits.Any(x => x.Id == e.Target) != true).ToList();
        Check(broken.Count == 0 && entrances.Count == 348, $"all {dungeonClusters.Count} dungeon clusters and {entrances.Count} entrances link both ways (broken {broken.Count})");

        Check(t.Info is { Type: "DUNGEON_YELLOW", DisplayName: "Stoneroot Caverns" } && t.Pvp is { Rule: PvpRule.PvpAllowed, PlayersAreLethal: false, WarningType: "yellow" },
              "Stoneroot: DUNGEON_YELLOW, its archetype's rules (PvP allowed, not lethal: a knockdown)");
        var points = t.MobSpawnPoints;
        Check(points.Count > 500 && points.All(p => p.Block is { Tier: 5 }) && points.Count(p => p.SpawnBehavior!.EndsWith("_BOSS", StringComparison.Ordinal)) == 6
              && points.All(p => p.Block!.RespawnSecondsMin > 0),
              $"{points.Count} spawn points, all T5 Keeper blocks with respawn timers, 6 boss points");
        Check(t.LootChests.Count(c => c.Behaviour?.IsPoi == true) == 15 && t.LootChests.Count(c => c.Behaviour?.Kind == ChestSpawnKind.MobUpgrade) == 6, "15 POI and 6 boss chest spots");

        var dir = NewDir();
        var characters = Repository(dir);
        var clock = new ManualClock();
        var now = GameTime.Now;
        var world = new World(new ServerConfig { JoinByCharacterId = true }, characters, Client.Items, Client.GameData)
            { Sync = new object(), Clock = clock, Clusters = Store, MapSeed = 7, GameTicks = () => now };
        var statics = world.StaticDungeons;
        var stoneroot = statics.Of(Dungeon)!;
        Check(statics.Dungeons.Count == 98 && statics.Dungeons.Values.Count(d => d.Clusters.Count == 2) == 58
              && stoneroot is { Type: "DUNGEON_YELLOW", Tier: 5, Clusters: [Dungeon] } && stoneroot.Entrances.Count(e => e.Main) == 1 && stoneroot.Entrances.Single(e => e.Main).Cluster == Entrance
              && stoneroot.Entrances.Count == 3, "98 static dungeons (58 of two clusters); Stoneroot T5, its main entrance in 4206 and two side ones");
        // The schedule: an event in each slot, at most 1 h (the group's duration), the gaps within mincooldown..maxcooldown (2700-8100 s).
        var e0 = statics.SlotEvent(stoneroot, now)!.Value;
        var e1 = statics.SlotEvent(stoneroot, now, 1)!.Value;
        var gap = (e1.Start - e0.End) / TimeSpan.TicksPerSecond;
        Check(e0.Def.Name == "T5_STANDARD_EVENT_ROYAL" && e0.End - e0.Start == TimeSpan.TicksPerHour && gap is >= 2700 and <= 8100 && statics.SlotEvent(stoneroot, now)!.Value == e0,
              $"T5_STANDARD_EVENT_ROYAL, 3600 s, the next one {gap} s after, the same schedule when asked again");

        var c = StarterKit.Default.NewCharacter("Delver");
        var inside = info.Find(Entrance)!.Exits.Single(e => e.Attrs.GetValueOrDefault("staticdungeonid") == stoneroot.Id.ToString());
        (c.Cluster, c.PositionX, c.PositionY) = (Entrance, inside.Position.X, inside.Position.Y);
        Check(characters.Create(c), "character created");
        now = e0.Start + TimeSpan.TicksPerMinute; // the event runs
        var (a, peerA) = Join(world, c.Id);
        Check(Events(peerA, EventCodes.StaticDungeonEntrancesDungeonEventStatusUpdates) is [var status]
              && status.d0c is [var id] && id == stoneroot.Id && status.d0d![0] == e0.Start && status.d0e![0] == e0.End && status.d0f![0] == 1 && status.d0g![0] == 4,
              "Join in 4206: 499 with Stoneroot's running event (start, end, standard) and value HIGH (not loaded yet)");
        var map = world.Maps.Find(Entrance)!;
        var cinfo = WorldSession.ClusterInfo(info, Entrance, statics)!;
        Check(cinfo.bz2 is [var running] && running == stoneroot.Id && cinfo.bz3 is [var end] && end == e0.End && cinfo.bz4 is [1]
              && cinfo.bz5 is { Count: 1 } values && values[stoneroot.Id.ToString()] == 4,
              "the world map of 4206: key 65-67 the running event (id, end, rarity standard), 68 the value by id");
        now = e0.End;
        Check(WorldSession.ClusterInfo(info, Entrance, statics) is { bz2.Length: 0, bz3.Length: 0, bz4.Length: 0, bz5.Count: 1 } && statics.ChestRolls(Dungeon) == 1,
              "after its end: 65-67 empty, 68 still there, chests draw once");

        clock.Advance(TimeSpan.FromSeconds(11)); // the open-world exit cooldown
        var door = map.Objects.Values.OfType<Exit>().Single(x => x.World.Id == inside.Id);
        Check(Change(world, a, peerA, door.Id, 5) is [{ ReturnCode: 0 } inResponse] && inResponse.Parameters[0] is Dungeon, "the main entrance answers {0: DNG-KPR-02-MAIN-04}: a plain shared cluster");
        var (b, peerB) = Join(world, c.Id);
        var dmap = world.Maps.Find(Dungeon)!;
        var mobs = dmap.Objects.Values.OfType<Mob>().ToList();
        var back = info.Find(Dungeon)!.Exits.Single(x => x.Id == inside.Target);
        Check(b.Player!.Map == dmap && Vector2.Distance(b.Player.PositionAt(DateTime.UtcNow), back.Position) < 20 && mobs.Count > 300 && mobs.All(m => m.Template.Tier == 5)
              && mobs.Count(m => ((MobSpawnPoint)m.Spawn).SpawnBehavior?.EndsWith("_BOSS", StringComparison.Ordinal) == true) == 6,
              $"inside near the exit back ({b.Player.PositionAt(DateTime.UtcNow)} vs {back.Position}), {mobs.Count} T5 mobs, the 6 bosses up");
        Check(Events(peerB, EventCodes.StaticDungeonEntrancesDungeonEventStatusUpdates) is [{ d0c: [var own] } s2] && own == stoneroot.Id && s2.d0d![0] == 0 && s2.d0g![0] == 4,
              "Join inside: 499 for Stoneroot itself, no event now (start 0), value HIGH (all mobs alive)");
        foreach (var m in mobs.Take(5))
            lock (world.Sync) Combat.Damage(b.Player, m, 1e9, EffectType.Physical, HitOrigin.Direct, 0, world.GameNow);
        var fill = statics.Fill(stoneroot);
        Check(mobs.Count(m => m.Dead) == 5 && Math.Abs(fill - (mobs.Count - 5) / (double)mobs.Count) < 1e-9 && statics.Value(stoneroot) == 4,
              $"5 mobs killed: the fill is the living share ({fill:0.000}), still HIGH (>= 0.75)");

        clock.Advance(TimeSpan.FromSeconds(11));
        var exitBack = dmap.Objects.Values.OfType<Exit>().Single(x => x.World.Id == back.Id);
        Check(Change(world, b, peerB, exitBack.Id, 6) is [{ ReturnCode: 0 } outResponse] && outResponse.Parameters[0] is Entrance, "its exit leads back to 4206");
        var (cc, _) = Join(world, c.Id);
        Check(cc.Player!.Map!.Cluster == Entrance && Vector2.Distance(cc.Player.PositionAt(DateTime.UtcNow), inside.Position) < 20, $"back at the entrance ({cc.Player.PositionAt(DateTime.UtcNow)} vs {inside.Position})");
        world.Dispose();
        characters.Dispose();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private static (WorldSession Session, FakePeer Peer) Join(World world, Guid character)
    {
        var peer = new FakePeer();
        var session = new WorldSession(peer, world);
        lock (world.Sync) session.OnRequest(Request(new JoinRequest { ss = character }, null), encrypted: true);
        Check(peer.Responses is [{ ReturnCode: 0 }], "join answered");
        return (session, peer);
    }

    private static List<OperationResponse> Change(World world, WorldSession session, FakePeer peer, long exit, int requestId)
    {
        var before = peer.Responses.Count;
        var obj = session.Player!.Map!.Objects[exit];
        lock (world.Sync)
        {
            session.OnRequest(Request(new MoveRequest { wf = DateTime.UtcNow.Ticks, v6 = [obj.Position.X, obj.Position.Y], v8 = [obj.Position.X, obj.Position.Y], v9 = 0f }, null), encrypted: false);
            session.OnRequest(Request(new ChangeClusterRequest { ee = exit, ef = "", eg = [-1] }, requestId), encrypted: false);
        }
        return [.. peer.Responses.Skip(before)];
    }

    private static List<StaticDungeonEntrancesDungeonEventStatusUpdatesEvent> Events(FakePeer peer, EventCodes code) =>
        [.. peer.Events.Where(e => Convert.ToInt32(e.Parameters.GetValueOrDefault(ReservedKeys.EventCode)) == (int)code).Select(e =>
        {
            var m = new StaticDungeonEntrancesDungeonEventStatusUpdatesEvent();
            AlbionSerializer.FillFields(m, e.Parameters);
            return m;
        })];
}
