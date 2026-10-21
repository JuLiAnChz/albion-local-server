using System.Numerics;
using AlbionLocal.Server;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;
using Microsoft.Data.Sqlite;
using static AlbionLocal.SelfTest.AlbionTests;

namespace AlbionLocal.SelfTest;

public static class OpeningTutorialTest
{
    private static void Check(bool condition, string why) => AlbionTests.Check(condition, why);
    public static IEnumerable<(string, Action)> All =>
    [
        ("opening tutorial: new character uses resolved beach, starter gear and empty progression", Creation),
        ("opening tutorial: route, dialogue, delivery and tower reach the explicit boundary", Route),
        ("opening tutorial: proximity, premature delivery and repeated requests cannot skip tasks", Refusals),
        ("opening tutorial: staying requires continuous online time; reconnect resumes the task", StayAndRejoin),
        ("opening tutorial: SQLite restart preserves progress and migrates legacy characters", Persistence),
    ];
    private sealed class Lab : IDisposable
    {
        public readonly string Dir = NewDir();
        public readonly OpeningTutorialData Data = OpeningTutorialData.Load(ClientGameDataDir(), Client.Clusters!);
        public readonly ManualClock Clock = new();
        public readonly SqliteCharacterRepository Repository;
        public readonly World World;
        public FakePeer Peer = null!;
        public WorldSession Session = null!;
        public Player Player => Session.Player!;
        public int Stage => Player.Character.Tutorial!.Stage;
        public Lab()
        {
            Repository = new SqliteCharacterRepository(Path.Combine(Dir, "save.db"));
            World = new World(new ServerConfig { JoinByCharacterId = true, TutorialOpening = true, PremiumMode = PremiumMode.None },
                Repository, Client.Items, Client.GameData, Client.Spells)
            { Sync = new object(), Clock = Clock, GameTicks = () => 638000000000000000L + Clock.Now.Ticks,
                Clusters = Client.Clusters, OpeningTutorial = Data };
            Repository.Create(Data.Create("OpeningProbe", Client.Items, Client.GameData!));
            Rejoin();
        }
        public void Rejoin()
        {
            Session?.OnDisconnected("tutorial rejoin test");
            World.Saves.Flush();
            Peer = new FakePeer(); Session = new WorldSession(Peer, World);
            Session.OnRequest(Request(new JoinRequest { ss = Repository.Find("OpeningProbe")!.Id }, 1), false);
            Check(Session.Player is not null, "opening character joined");
        }
        public void Go(string marker)
        {
            Player.Teleport(Data.Markers(marker)[0].Position, 0, DateTime.UtcNow);
            OpeningTutorial.Update(Player);
        }
        public void GoTask() => Go((Stage < 5 ? Data.First : Data.Tower).Tasks[Stage < 5 ? Stage : Stage - 6].Marker);
        public short Act(int action, long? npc = null, byte slot = 0)
        {
            Session.OnRequest(Request(new QuestGiverRequestRequest { yz = npc ?? Player.Map!.Objects.Values.OfType<TutorialQuestGiver>().Single().Id,
                y0 = action, y1 = slot }, 2), false);
            return Peer.Responses.Last().ReturnCode;
        }
        public void ToCrafter()
        {
            GoTask(); GoTask(); GoTask();
            Clock.Advance(TimeSpan.FromSeconds(Data.First.Tasks[2].Seconds)); OpeningTutorial.Update(Player);
            Check(Stage == 3, "mirador route reached conversation step");
            Player.Teleport(Data.Crafter.Position, 0, DateTime.UtcNow);
        }
        public void Dispose() { World.Dispose(); Repository.Dispose(); Directory.Delete(Dir, true); }
    }
    private static void Creation()
    {
        using var lab = new Lab();
        var p = lab.Player; var c = p.Character;
        Check(c.Cluster == lab.Data.Tutorial.StartCluster && new Vector2(c.PositionX, c.PositionY) == lab.Data.Start.Position,
            "startpoint used, not death respawn or Thetford");
        Check(c.Silver == 0 && c.Inventory.All(i => i is null) && c.Progression is not null,
            "zero silver, no lab kit, independent progression");
        Check(c.Equipment.Where(i => i is not null).Select(i => i!.Name).Order().SequenceEqual(lab.Data.Tutorial.StartingItems.Order()), "only declared starter gear");
        Check(lab.Peer.Events.Any(e => EventCode(e) == EventCodes.TutorialUpdate)
            && lab.Peer.Events.Any(e => EventCode(e) == EventCodes.FullQuestInfo), "real Join emits tutorial and quest events");
        Check(p.Inventory.Equipment.Items.All(i => i.Active.Any(s => s >= 0)), "starter abilities selected by existing spell system");
        var settings = Path.Combine(lab.Dir, "absent-settings.json");
        var defaults = ServerConfig.Load(settings, []);
        Check(!defaults.TutorialOpening && ServerConfig.Load(settings, ["--Tutorial:Opening=true"]).TutorialOpening, "opening is explicit opt-in");
        var loginPeer = new FakePeer();
        var login = new LoginSession(loginPeer, lab.World.Config, lab.Repository, StarterKit.Default) { World = lab.World };
        login.OnRequest(Request(TestLogin(), 1), false);
        login.OnRequest(Request(new CreateCharacterRequest { h7 = "CreatedOpeningProbe" }, 2), false);
        Check(loginPeer.Responses.Last().ReturnCode == 0
            && lab.Repository.Find("CreatedOpeningProbe") is { Tutorial.Stage: 0, PlayedMilliseconds: 0 } created
            && created.Cluster == lab.Data.Tutorial.StartCluster && created.Equipment[0] is null,
            "actual Login/CreateCharacter path uses tutorial rather than the ordinary T4 kit");
    }
    private static EventCodes EventCode(AlbionLocal.Photon.Serialization.EventData e) => (EventCodes)Convert.ToInt32(e.Parameters[252]);
    private static void Route()
    {
        using var lab = new Lab();
        lab.ToCrafter(); Check(lab.Stage == 3, "standing by NPC does not count as speaking");
        Check(lab.Act(0) == 0 && lab.Stage == 4, "interaction marks objective fulfilled");
        Check(lab.Act(3) == 0 && lab.Stage == 5, "complete explicitly delivers first quest");
        Check(lab.Act(1) == 0 && lab.Stage == 6, "accept explicitly starts second quest");
        var questId = lab.Player.Character.Tutorial!.QuestId;
        lab.GoTask(); Check(lab.Stage == 7, "first stairs point");
        lab.GoTask(); Check(lab.Stage == 8, "last stairs point opens boat-search task");
        lab.Go(lab.Data.Tower.Tasks[2].Marker);
        Check(lab.Stage == 8 && lab.Player.Character.Tutorial!.QuestId == questId, "boat task deliberately does not finish itself");
        Check(lab.Player.Character.Silver == 0, "empty rewards do not invent silver");
    }
    private static void Refusals()
    {
        using var lab = new Lab();
        var id = lab.Player.Map!.Objects.Values.OfType<TutorialQuestGiver>().Single().Id;
        Check(lab.Act(0, id) != 0 && lab.Stage == 0, "far-away dialogue refused");
        lab.Player.Teleport(lab.Data.Crafter.Position, 0, DateTime.UtcNow);
        Check(lab.Act(0, id + 1000000) != 0 && lab.Stage == 0, "wrong NPC refused");
        Check(lab.Act(3) != 0 && lab.Act(1) != 0 && lab.Stage == 0, "premature delivery/acceptance refused");
        lab.Session.OnRequest(Request(new SetNextTutorialStateRequest(), 3), false);
        Check(lab.Stage == 0, "timestamp-only tutorial request cannot skip route");
        lab.ToCrafter(); lab.Act(0); lab.Act(3);
        var after = lab.Player.Character.Tutorial;
        Check(lab.Act(3) != 0 && lab.Player.Character.Tutorial == after, "duplicate delivery has no effect");
        Check(lab.Act(1, slot: 7) != 0 && lab.Stage == 5, "invalid slot refused");
        lab.Act(1); after = lab.Player.Character.Tutorial;
        Check(lab.Act(1) != 0 && lab.Player.Character.Tutorial == after, "duplicate accept retains quest instance");
    }
    private static void StayAndRejoin()
    {
        using var lab = new Lab(); lab.GoTask(); lab.GoTask(); lab.GoTask();
        lab.Clock.Advance(TimeSpan.FromSeconds(0.6)); OpeningTutorial.Update(lab.Player);
        Check(lab.Stage == 2, "incomplete duration does not advance");
        lab.Player.Teleport(lab.Data.Start.Position, 0, DateTime.UtcNow); OpeningTutorial.Update(lab.Player);
        lab.GoTask(); lab.Clock.Advance(TimeSpan.FromSeconds(0.6)); OpeningTutorial.Update(lab.Player);
        Check(lab.Stage == 2, "leaving resets duration rather than adding visits");
        lab.Rejoin(); lab.Clock.Advance(TimeSpan.FromHours(6));
        OpeningTutorial.Update(lab.Player);
        Check(lab.Stage == 2, "offline/wait before first tick cannot satisfy stay");
        lab.Clock.Advance(TimeSpan.FromSeconds(1)); OpeningTutorial.Update(lab.Player);
        Check(lab.Stage == 3, "new continuous interval completes once");
    }
    private static void Persistence()
    {
        var dir = NewDir(); var path = Path.Combine(dir, "save.db");
        try
        {
            var progress = new OpeningTutorialProgress(7, Guid.NewGuid(), 638000000000000000L) { TutorialState = 4 };
            using (var repository = new SqliteCharacterRepository(path))
            {
                var c = new StarterKit().NewCharacter("PersistOpening"); c.Tutorial = progress;
                repository.Create(c); var copy = repository.Find(c.Id)!;
                copy.Tutorial = copy.Tutorial! with { Stage = 8 };
                Check(repository.Find(c.Id)!.Tutorial == progress, "read copy cannot mutate stored tutorial");
                repository.Save(copy);
            }
            using (var repository = new SqliteCharacterRepository(path))
                Check(repository.Find("PersistOpening")!.Tutorial == progress with { Stage = 8 }, "restart preserves immutable route and quest instance");
            using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "ALTER TABLE characters DROP COLUMN tutorial_opening; PRAGMA user_version=30;"; cmd.ExecuteNonQuery();
            }
            using var migrated = new SqliteCharacterRepository(path);
            Check(migrated.Find("PersistOpening") is { Tutorial: null, Cluster: "0000" }, "schema30 migration leaves old characters outside tutorial");
        }
        finally { Directory.Delete(dir, true); }
    }
}
