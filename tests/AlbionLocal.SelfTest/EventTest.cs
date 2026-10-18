using System.Numerics;
using AlbionLocal.Photon.Serialization;
using AlbionLocal.Server;
using AlbionLocal.Server.AI;
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
/// The world events (PVE-DESIGN.md §2.H, §3 step 7): the shipped content/events.json and its parser, and on FakePeer sessions in 0204
/// with a manual clock a scheduled invasion (announced, waves of the cluster's faction and tier, aggro, the damage shares, the
/// personal reward chest and fame), a GM champion hunt (its mob not leashing, a second event on the map refused) that runs out of
/// time, .event list/stop, and a last wave gone with no player damage failing.
/// </summary>
public static class EventTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("world events: content/events.json (invasion, champion), parser defaults and refusals, compass", Data),
        ("world events: scheduled 0204 invasion, waves, aggro, damage shares, personal reward chest and fame; GM champion hunt times out; .event", Live),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static int Code(EventData e) => Convert.ToInt32(e.Parameters.GetValueOrDefault(ReservedKeys.EventCode));

    private static void Data()
    {
        var file = EventSettings.Load(Path.Combine(DataDir(), "..", "content", "events.json"));
        Check(file is { Enabled: true, Scheduled: false, IntervalMinutes: (30, 60), MinPlayers: 1, AnnounceToAll: false } && file.Events.Count == 2
              && file.Find("INVASION") is { Waves: [{ Kind: WaveKind.Standard, Count: (6, 10) }, _, { Kind: WaveKind.Champion, HealthFactor: 1.5 }], Chest: "DEFAULT_TREASURE_VETERAN", RewardFactor: 2 }
              && file.Find("champion") is { Waves: [{ Kind: WaveKind.Miniboss, HealthFactor: 3 }], Weight: 0.5 }
              && file.Events.Select(e => e.Name).SequenceEqual(EventSettings.Default.Events.Select(e => e.Name)),
              "content/events.json: not scheduled (stage I; 30-60 min when on), the invasion (2 standard waves, then 1-2 champions x1.5) and the champion hunt, as built in");
        var parsed = EventSettings.Parse("""{ "Events": [ { "Name": "x", "Waves": [ { "Kind": "Boss" } ] } ] }""", "test");
        Check(parsed.Events is [{ Name: "x", DurationMinutes: 10, Waves: [{ Kind: WaveKind.Boss, Count: (1, 1), HealthFactor: 1 }] }]
              && Throws(() => EventSettings.Parse("""{ "Events": [ { "Name": "x", "Waves": [ { "Kind": "dragon" } ] } ] }""", "test"))
              && Throws(() => EventSettings.Parse("""{ "Events": [ { "Name": "x" } ] }""", "test"))
              && Throws(() => EventSettings.Parse("""{ "IntervalMinutes": [0, 5] }""", "test")),
              "events.json parsing: kinds in any case, defaults; an unknown Kind, an event without Waves, a 0 interval refused");
        Check(WorldEvents.Compass(new Vector2(0, 50)) == "north" && WorldEvents.Compass(new Vector2(30, -30)) == "south-east"
              && WorldEvents.Compass(new Vector2(-40, 2)) == "west" && WorldEvents.Compass(new Vector2(0.2f, 0)) == "here", "compass: +z north, +x east");
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
        var settings = EventSettings.Default with
        {
            Scheduled = true, IntervalMinutes = (1, 1),
            Events =
            [
                new EventType
                {
                    Name = "invasion", Waves = [new(WaveKind.Standard, (2, 2)), new(WaveKind.Champion, (1, 1), 1.5)], WaveDelaySeconds = 10, ChestMinutes = 1,
                    RewardFame = 500,
                },
                new EventType { Name = "champion", Title = "{faction} champion hunt", Weight = 0, Waves = [new(WaveKind.Miniboss, (1, 1), 3)], DurationMinutes = 2 },
            ],
        };
        var world = new World(new ServerConfig { JoinByCharacterId = true }, characters, Client.Items, Client.GameData, Client.Spells)
        {
            Sync = new object(), Clock = clock, Clusters = Client.Clusters, MapSeed = 9, EventSettings = settings,
        };
        world.MobAi = false;
        void Tick(double seconds)
        {
            clock.Advance(TimeSpan.FromSeconds(seconds));
            lock (world.Sync) world.Update();
        }
        Client.Clusters!.Get("0204"); // resolved now: the Join fills the map at once
        var hero = NewHero(characters, new Vector2(0, 0));
        var (a, peerA) = Join(world, hero.Id);
        var friend = NewHero(characters, new Vector2(2, 0));
        var (b, peerB) = Join(world, friend.Id);
        var (me, you) = (a.Player!, b.Player!);
        bool Told(FakePeer peer, string what) => peer.Events.Any(e => Code(e) == (int)EventCodes.SystemMessage && e.Parameters[0] is string s && s.Contains(what, StringComparison.Ordinal));
        Tick(0.1);
        Check(world.Events.Running.Count == 0 && world.Events.NextScheduled is { } first && first - world.Now == TimeSpan.FromMinutes(1),
              "nothing at the start; the first scheduled event in IntervalMinutes (1)");

        // The scheduled start (the mob AI on for this tick: a wave mob goes for the nearest player within its pursuitradius, else waits).
        world.MobAi = true;
        Tick(60);
        var run = world.Events.Running.SingleOrDefault();
        var aggro = run?.Mobs.All(m => (Vector2.Distance(m.Position, Vector2.Zero) <= Vector2.Distance(m.Position, new Vector2(2, 0)) ? me : you) is var near
            && (Vector2.Distance(m.Position, near.PositionAt(DateTime.UtcNow)) <= m.Template.PursuitRadius ? m.Threat.Count == 1 && m.TargetId == near.Id : m.Threat.Count == 0)) == true; // before the switch off settles them
        world.MobAi = false;
        var map = world.Maps.Find("0204")!;
        var tier = map.Template!.Name.Tier;
        Check(run is { Type.Name: "invasion", Wave: 0, Scheduled: true, Mobs.Count: 2 } && run.Map == map
              && run.Mobs.All(m => m.Event == run && m.Template.Faction == run.Faction && m.Template.Tier == tier && m.Template.Category is "" or "standard" or "trash"
                  && map.Objects.ContainsKey(m.Id) && Vector2.Distance(m.Position, run.Point) <= 6.01f && m.Slot is null) && aggro,
              $"a scheduled invasion in 0204: 2 standard {run?.Faction} T{tier} mobs on the ring around its point, after the nearest player only within its pursuitradius (got {run?.Mobs.Count})");
        var distance = Vector2.Distance(run!.Point, Vector2.Zero);
        Check(distance is >= 40 and <= 120 && map.Template.MobSpawnPoints.Concat(map.Template.Resources?.MobSpawnPoints ?? []).Any(p => p.Position == run.Point)
              && Told(peerA, $"{run.Title} in 0204: {distance:0} m {WorldEvents.Compass(run.Point)} of you") && Told(peerB, "2 waves") && Told(peerA, "wave 1 of 2, 2 x"),
              $"its point a mob spawn point 40-120 m from the player ({distance:0} m), the start line with distance and direction, the wave line to both");

        // The friend scratches one mob, the hero kills both: the friend's share is under 5 %.
        lock (world.Sync)
        {
            Combat.Damage(you, run.Mobs[0], 1, EffectType.Physical, HitOrigin.Direct, 0, world.GameNow);
            foreach (var m in run.Mobs.ToList()) Combat.Damage(me, m, 1e9, EffectType.Physical, HitOrigin.Direct, 0, world.GameNow);
        }
        Tick(0.1);
        Check(run is { Mobs.Count: 0, NextWaveAt: not null, Wave: 0 } && run.Damage[friend.Id] == 1 && run.Damage[hero.Id] > 100,
              "the wave dead: the next one waits WaveDelaySeconds; the damage counted per player (the friend 1)");
        Tick(10);
        var champion = run.Mobs.SingleOrDefault();
        Check(run.Wave == 1 && champion is { Template.Category: "champion", HealthFactor: 1.5 } && champion.Health.Max == MathF.Round(champion.Template.HitPointsMax * 1.5f)
              && Told(peerB, "wave 2 of 2, 1 x"), $"wave 2: a champion with x1.5 health (got {champion?.Template.Name})");
        var fame = me.Character.Fame;
        lock (world.Sync) Combat.Damage(me, champion!, 1e9, EffectType.Physical, HitOrigin.Direct, 0, world.GameNow);
        var mobFame = me.Character.Fame - fame;
        Tick(0.1);
        var chest = run.Chest;
        Check(run.Outcome == WorldEvents.Outcome.Won && world.Events.Running.Count == 0 && chest is { Personal: true, LootFactor: 2 } && map.Objects.ContainsKey(chest.Id)
              && chest.Position == run.Point && chest.ViewerState(me).State == 6 && chest.ViewerState(you).State == 4
              && me.Character.Fame - fame - mobFame == Rates.ScaleFame(500 * 10000, world.Rates.Fame) && Told(peerA, "beaten! Reward chests") && Told(peerB, "for 1 player(s)"),
              $"the win: a personal reward chest at the point, Unlocked for the hero alone, 500 fame for it, the line to both (got {chest})");
        lock (world.Sync)
            Check(world.Dungeons.OpenChest(you, chest!) is { } why && why.StartsWith("locked", StringComparison.Ordinal) && world.Dungeons.OpenChest(me, chest!) is null && chest!.RolledFor(me),
                  "the friend without a share may not open it; the hero's opening rolls its own loot");
        Check(world.Events.NextScheduled - world.Now == TimeSpan.FromMinutes(1), "the next scheduled event counts from the end");
        Tick(60);
        Check(!map.Objects.ContainsKey(chest!.Id), "the chest goes after ChestMinutes");

        // A GM champion hunt in front of the hero: not won in its 2 minutes, it fails and its mob goes; the schedule leaves its map alone.
        string Run(string line)
        {
            var replies = new List<string>();
            var c = new Server.Commands.CommandContext(world, me, AccountLevel.Admin, (t, error) => replies.Add((error ? "error: " : "") + t));
            lock (world.Sync) world.Commands.Execute(c, line);
            return string.Join("\n", replies);
        }
        foreach (var r in world.Events.Running.ToList()) lock (world.Sync) world.Events.Stop(r); // a scheduled one that came meanwhile
        world.MobAi = true; // the AI on at the start and for a second: it goes for the hero 10 m away and does not leash
        var reply = Run(".event start champion here");
        var hunt = world.Events.Running.SingleOrDefault(r => r.Type.Name == "champion");
        var boss = hunt?.Mobs.SingleOrDefault();
        Check(hunt is { Scheduled: false } && boss is { HealthFactor: 3 } && boss.Template.Category is "miniboss" or "champion" && Vector2.Distance(hunt.Point, me.PositionAt(DateTime.UtcNow)) is > 9.9f and < 10.1f
              && Told(peerB, "champion hunt in 0204") && Run(".event list").Contains($"#{hunt.Id} champion", StringComparison.Ordinal),
              $".event start champion here: one x3 miniboss (or champion) 10 m in front, announced, listed ({reply})");
        for (var i = 0; i < 4; i++) Tick(0.25);
        var second = Run(".event start invasion");
        Check(boss!.AI.State == MobState.Combat && boss.Threat.Count == 1 && second.StartsWith("error: an event already runs on this map", StringComparison.Ordinal),
              $"the hunt's mob after the nearest player, not leashing ({boss.AI.State}); a second event on the map refused ({second})");
        world.MobAi = false; // (the switch off settles it)
        Tick(118); // (with the second of the AI above)
        Check(world.Events.Running.Contains(hunt!) && !boss!.Removed, "still running before its 2 minutes");
        Tick(1.5);
        Check(hunt!.Outcome == WorldEvents.Outcome.Failed && boss!.Removed && !boss.Dead && Told(peerA, "the invaders have retreated") && hunt.Chest is null,
              "after DurationMinutes: failed, its mob despawned, the retreat line, no chest");
        Run(".event start invasion here");
        var called = world.Events.Running.Single(r => !r.Scheduled);
        var mobs = called.Mobs.ToList();
        Check(Run(".event stop").EndsWith("stopped", StringComparison.Ordinal) && called.Outcome == WorldEvents.Outcome.Stopped && mobs.All(m => m.Removed)
              && Told(peerA, "was called off") && Run(".event stop").StartsWith("error: ", StringComparison.Ordinal),
              ".event stop: the event on the map ends, its mobs go, the line; a second stop finds none");

        // A last wave gone with no player damage (despawned) is no win: it fails with the retreat line.
        Run(".event start champion here");
        var empty = world.Events.Running.Single(r => !r.Scheduled);
        lock (world.Sync) foreach (var m in empty.Mobs.ToList()) SpawnManager.Despawn(m, world);
        Tick(0.1);
        Check(empty is { Outcome: WorldEvents.Outcome.Failed, Chest: null } && world.Events.Running.Count == 0, "a last wave gone with no player damage: failed, no chest");
        world.Dispose();
        characters.Dispose();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    internal static Character NewHero(SqliteCharacterRepository characters, Vector2 at)
    {
        var c = StarterKit.Default.NewCharacter("E" + Guid.NewGuid().ToString("N")[..10]);
        c.Cluster = "0204";
        c.PositionX = at.X;
        c.PositionY = at.Y;
        Check(characters.Create(c), "character created");
        return c;
    }

    internal static (WorldSession Session, FakePeer Peer) Join(World world, Guid key)
    {
        var peer = new FakePeer();
        var session = new WorldSession(peer, world);
        lock (world.Sync) session.OnRequest(Request(new JoinRequest { ss = key }, null), encrypted: true);
        Check(peer.Responses is [{ ReturnCode: 0 }], "join answered");
        return (session, peer);
    }
}
