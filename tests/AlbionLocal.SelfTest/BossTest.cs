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

namespace AlbionLocal.SelfTest;

/// <summary>
/// The world bosses (stageH, analysis/work/stageH/bosses/SPEC.md, "SPEC" here, §8 assertions 1, 2, 11): the data (worldbosses.xml, the
/// schedule, 1007's and 2355's WORLDBOSS points, the shipped content/bosses.json), and on FakePeer sessions with a manual clock the 1007
/// keepers with HealthFactor and DamageFactor, the kill (announced, a personal bag per contributor), the respawn and .boss.
/// </summary>
public static class BossTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("world bosses: worldbosses.xml (12, odd / even hours), the schedule, 1007 and 2355 boss points, content/bosses.json", Data),
        ("world bosses: 1007 keepers with health and damage factors, kill announced with personal bags, respawn, .boss spawn/kill/list", Live),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static int Code(EventData e) => Convert.ToInt32(e.Parameters.GetValueOrDefault(ReservedKeys.EventCode));

    private static bool Near(Vector2 a, Vector2 b, float tolerance = 0.01f) => Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;

    private static ClusterStore Store => Client.Clusters!;

    private static readonly Lazy<WorldBossData> Bosses = new(() => WorldBossData.Load(GameDataFile.Open(ClientGameDataDir(), "worldbosses.bin").Root()));

    private static DateTime Utc(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    private static void Data()
    {
        var data = Bosses.Value;
        var asia = WorldBossData.Load(GameDataFile.Open(ClientGameDataDir(), "worldbosses_asia.bin").Root());
        var seer = data.Find("WORLD_BOSS_DAILY_T5_KEEPER");
        Check(data.Bosses.Count == 12 && seer is { Mob: "T5_MOB_KEEPER_SEER_ELITE_BOSS", DayCount: 1 } && data.Bosses.Values.All(b => b.Days[1].Length == 12),
              $"worldbosses.xml: 12 bosses of one day of 12 times, DAILY_T5_KEEPER = T5_MOB_KEEPER_SEER_ELITE_BOSS (got {data.Bosses.Count})");
        Check(seer!.NextSpawn(Utc(17, 10, 30)) == Utc(17, 11, 0) && seer.NextSpawn(Utc(17, 23, 30)) == Utc(18, 1, 0) && seer.NextSpawn(Utc(17, 11, 0)) == Utc(17, 13, 0)
              && seer.DayOf(Utc(17, 0, 0)) == 1 && seer.DayOf(new DateTime(2017, 7, 1, 0, 0, 0, DateTimeKind.Utc)) == 1,
              "the schedule: 10:30 -> 11:00, 23:30 -> 01:00 the next day, 11:00 -> 13:00; one day: day 1 for every date");
        Check(asia.Find("WORLD_BOSS_DAILY_T5_KEEPER")?.NextSpawn(Utc(17, 10, 30)) == Utc(17, 12, 0), "worldbosses_asia.xml: the even hours (10:30 -> 12:00)");

        static string Points(ClusterTemplate t) => string.Join(" ", t.MobSpawnPoints.Where(p => p.MobName == MobSpawnPoint.WorldBossName)
            .OrderBy(p => p.WorldBoss, StringComparer.Ordinal).Select(p => $"{p.WorldBoss}@{p.Position.X:0.##},{p.Position.Y:0.##}r{p.Direction:0.##}"));
        var p1007 = Store.Get("1007").MobSpawnPoints.Where(p => p.MobName == MobSpawnPoint.WorldBossName).ToList();
        Check(p1007.Count == 2 && p1007.Any(p => p.WorldBoss == "WORLD_BOSS_DAILY_T5_KEEPER" && Near(p.Position, new Vector2(-10.06f, -78.24f)) && Math.Abs(p.Direction - 182) < 0.01)
              && p1007.Any(p => p.WorldBoss == "WORLD_BOSS_T5_KEEPER" && Near(p.Position, new Vector2(-5.72f, 6.25f)) && Math.Abs(p.Direction - 180) < 0.01),
              $"1007: two WORLDBOSS points, DAILY_T5_KEEPER at (-10.06, -78.24) r 182 and T5_KEEPER at (-5.72, 6.25) r 180 (got {Points(Store.Get("1007"))})");
        var p2355 = Store.Get("2355").MobSpawnPoints.Where(p => p.MobName == MobSpawnPoint.WorldBossName).ToList();
        Check(p2355.Count == 2 && p2355.Any(p => Near(p.Position, new Vector2(-32.17f, 30.35f))) && p2355.Any(p => Near(p.Position, new Vector2(64.21f, 30.06f))),
              $"2355: the boss points at (-32.17, 30.35) and (64.21, 30.06), the template at x+10 (got {Points(Store.Get("2355"))})");

        var file = BossSettings.Load(Path.Combine(DataDir(), "..", "content", "bosses.json"));
        Check(file is { Enabled: true, Schedule: BossSchedule.Data, Loot: BossLoot.Shared, AnnounceToAll: false, HealthFactor: 1 } && file.ExtraSpawns.Count == 2
              && file.ExtraSpawns.Select(e => e.Cluster).Order().SequenceEqual(["0204", "1207"])
              && file.ExtraSpawns.All(e => data.Find(e.Boss) is not null && e.Schedule == BossSchedule.Respawn && e.HealthFactor is > 0 and < 1),
              "content/bosses.json: data schedule, shared loot, announced to the cluster, one extra boss each in 0204 and 1207 (worldbosses.xml names, respawn, reduced health)");
        var bad = BossSettings.Parse("""{ "Schedule": "Respawn", "ExtraSpawns": [ { "Cluster": "0204", "Boss": "x", "Position": [1, 2] } ] }""", "test");
        Check(bad is { Schedule: BossSchedule.Respawn, ExtraSpawns: [{ Boss: "X", RespawnMinutes: (20, 40), HealthFactor: null }] }
              && Throws(() => BossSettings.Parse("""{ "Loot": "everyone" }""", "test")) && Throws(() => BossSettings.Parse("""{ "ExtraSpawns": [ { "Cluster": "0204" } ] }""", "test")),
              "bosses.json parsing: names in any case, extra spawn defaults, a bad Loot or an entry without Boss/Position refused");
        Check(WorldBosses.DisplayName(Store.Mobs.Find("T5_MOB_KEEPER_SEER_ELITE_BOSS")!) == "Keeper Seer (T5)", "the chat name of T5_MOB_KEEPER_SEER_ELITE_BOSS: Keeper Seer (T5)");
    }

    private static bool Throws(Action a)
    {
        try { a(); return false; }
        catch (InvalidDataException) { return true; }
    }

    private static void Live()
    {
        var dir = NewDir();
        var characters = Repository(dir);
        var clock = new ManualClock();
        var anchor = Utc(17, 10, 30).Ticks;
        var settings = BossSettings.Default with
        {
            HealthFactor = 0.3, DamageFactor = 0.5, Loot = BossLoot.Personal,
            ExtraSpawns = [new BossSpawn("1007", "WORLD_BOSS_DAILY_T5_KEEPER", new Vector2(20, -60), 90, BossSchedule.Respawn, (1, 1), 0.1, null)],
        };
        var world = new World(new ServerConfig { JoinByCharacterId = true }, characters, Client.Items, Client.GameData, Client.Spells)
        {
            Sync = new object(), Clock = clock, Clusters = Store, MapSeed = 5, GameTicks = () => anchor + clock.Now.Ticks,
            BossData = Bosses.Value, BossSettings = settings, LootOverride = _ => [("T4_ORE", 1)],
        };
        world.Rates = world.Rates with { Respawn = 100 };
        world.MobAi = false;
        void Tick(double seconds)
        {
            clock.Advance(TimeSpan.FromSeconds(seconds));
            lock (world.Sync) world.Update();
        }
        var seerAt = new Vector2(-10.06f, -78.24f);
        var hero = NewHero(characters, seerAt + new Vector2(0, 6));
        var (a, peerA) = Join(world, hero.Id);
        var friend = NewHero(characters, seerAt + new Vector2(3, 6));
        var (b, peerB) = Join(world, friend.Id);
        var (me, you) = (a.Player!, b.Player!);
        Tick(0.1);
        var map = world.Maps.Find("1007")!;
        var slots = world.Bosses.SlotsOf("1007");
        var seer = slots.Single(s => !s.Extra && s.Boss.Name == "WORLD_BOSS_DAILY_T5_KEEPER");
        var boss = seer.Mob;
        var sent = peerA.Events.LastOrDefault(e => Code(e) == (int)EventCodes.NewMob && Convert.ToInt64(e.Parameters[0]) == boss?.Id)?.Parameters;
        Check(slots.Count == 3 && slots.All(s => s.Mob is { Removed: false } m && map.Objects.ContainsKey(m.Id)) && boss is { HealthFactor: 0.3, DamageFactor: 0.5 }
              && boss.Health.Max == MathF.Round(154_803 * 0.3f) && sent is not null && Convert.ToSingle(sent[14]) == boss.Health.Max
              && slots.Single(s => s.Extra).Mob!.Health.Max == MathF.Round(154_803 * 0.1f) && boss.Spawn.Behavior == "GUARD",
              $"1007 at its first fill: both keepers and the extra seer stand, the seer's max health 154 803 x 0.3 in NewMob key 14, the extra's x 0.1 (got {boss?.Health.Max})");
        Check(SpellEffects.DealtFactor(boss!, spell: false, EffectType.Physical, vsPlayer: true, world.GameNow) == 0.5, "DamageFactor 0.5 on the boss's damage");

        // Both fight it (threat with the AI on), the friend kills it: a bag of its own roll for each, the death announced to both.
        world.MobAi = true;
        lock (world.Sync)
        {
            Combat.Damage(me, boss!, boss!.Health.Max * 0.4, EffectType.Physical, HitOrigin.Direct, 0, world.GameNow);
            Combat.Damage(you, boss, 1e9, EffectType.Physical, HitOrigin.Direct, 0, world.GameNow);
        }
        world.MobAi = false;
        var bags = map.Objects.Values.OfType<LootBag>().Where(g => g.Mob == boss).ToList();
        bool Told(FakePeer peer, string what) => peer.Events.Any(e => Code(e) == (int)EventCodes.SystemMessage && e.Parameters[0] is string s && s.Contains(what, StringComparison.Ordinal));
        Check(boss.Dead && bags.Count == 2 && bags.Any(g => g.Looters.SequenceEqual([hero.Id]) && g.PartyIds is null) && bags.Any(g => g.Looters.SequenceEqual([friend.Id]))
              && Told(peerA, "Keeper Seer (T5) was defeated in 1007") && Told(peerB, "was defeated in 1007") && seer is { Present: false, Mob: null, NextSpawn: not null },
              $"the kill: a personal bag for each contributor (got {bags.Count}), the death line to both players, the slot waits");
        var due = seer.NextSpawn!.Value - world.Bosses.UtcNow;
        Check(due > TimeSpan.Zero && due <= TimeSpan.FromHours(1) && seer.NextSpawn == Utc(17, 11, 0),
              $"Schedule data: back at the next odd hour, 11:00 UTC (got {seer.NextSpawn:HH:mm:ss})");
        Tick(6);
        Check(!map.Objects.ContainsKey(boss.Id) && seer.Mob is null, "the corpse goes after 5 s, the boss is not back before its time");
        Tick(30 * 60 - 6);
        Check(seer.Mob is { Dead: false } back && back != boss && map.Objects.ContainsKey(back.Id) && Told(peerA, "Keeper Seer (T5) has appeared in 1007 at (-10, -78)"),
              "at 11:00 the seer stands again, announced with its position");

        // .boss kill on the extra (Schedule respawn, 1 min / Rates:Respawn 100 = 0.6 s): back within a second; .boss spawn of a standing one refused.
        var extra = slots.Single(s => s.Extra);
        string Run(string line)
        {
            var replies = new List<string>();
            var c = new Server.Commands.CommandContext(world, me, AccountLevel.Admin, (t, error) => replies.Add((error ? "error: " : "") + t));
            lock (world.Sync) world.Commands.Execute(c, line);
            return string.Join("\n", replies);
        }
        lock (world.Sync) me.Teleport(extra.Point.Position + new Vector2(2, 0), 0, DateTime.UtcNow);
        var killed = extra.Mob!;
        var reply = Run(".boss kill");
        Check(killed.Removed && !killed.Dead && reply.Contains("despawned", StringComparison.Ordinal) && extra.NextSpawn is { } next && next - world.Bosses.UtcNow <= TimeSpan.FromSeconds(0.61),
              $".boss kill: the nearest boss despawned, its respawn slot back within 0.6 s ({reply})");
        Tick(1);
        Check(extra.Mob is { Dead: false } again && again != killed, "the extra seer is back a second later");
        Check(Run(".boss spawn WORLD_BOSS_DAILY_T5_KEEPER").StartsWith("error: ", StringComparison.Ordinal), ".boss spawn of a standing slot's boss: refused");
        var gm = Run(".boss spawn T8_MOB_UNDEAD_GENERAL_ELITE_BOSS here");
        var oneOff = map.Objects.Values.OfType<Mob>().SingleOrDefault(m => m.Template.Name == "T8_MOB_UNDEAD_GENERAL_ELITE_BOSS");
        Check(oneOff is { Boss.OneOff: true, HealthFactor: 0.3 } && Told(peerB, "Undead General (T8) has appeared in 1007"),
              $".boss spawn <mob> here: a one-off boss 5 m in front, with the file's factors, announced ({gm})");
        lock (world.Sync) Combat.Damage(me, oneOff!, 1e9, EffectType.Physical, HitOrigin.Direct, 0, world.GameNow);
        Check(oneOff!.Boss!.NextSpawn is null && !world.Bosses.SlotsOf("1007").Contains(oneOff.Boss) && Told(peerA, "Undead General (T8) was defeated in 1007")
              && !Told(peerA, "never"), "a one-off boss never comes back, its death line without a next time");
        var list = Run(".boss list");
        Check(list.Split('\n').Length == 4 && list.Contains("WORLD_BOSS_T5_KEEPER", StringComparison.Ordinal) && list.Contains("extra", StringComparison.Ordinal),
              $".boss list: the three slots of 1007 ({list.Replace('\n', '|')})");
        world.Dispose();
        characters.Dispose();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private static Character NewHero(SqliteCharacterRepository characters, Vector2 at)
    {
        var c = StarterKit.Default.NewCharacter("B" + Guid.NewGuid().ToString("N")[..10]);
        c.Cluster = "1007";
        c.PositionX = at.X;
        c.PositionY = at.Y;
        Check(characters.Create(c), "character created");
        return c;
    }

    private static (WorldSession Session, FakePeer Peer) Join(World world, Guid key)
    {
        var peer = new FakePeer();
        var session = new WorldSession(peer, world);
        lock (world.Sync) session.OnRequest(Request(new JoinRequest { ss = key }, null), encrypted: true);
        Check(peer.Responses is [{ ReturnCode: 0 }], "join answered");
        return (session, peer);
    }
}
