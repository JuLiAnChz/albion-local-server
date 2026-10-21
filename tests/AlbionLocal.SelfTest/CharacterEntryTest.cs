using AlbionLocal.Server;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Protocol;
using Microsoft.Data.Sqlite;
using static AlbionLocal.SelfTest.AlbionTests;

namespace AlbionLocal.SelfTest;

public static class CharacterEntryTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("character entry: location, played milliseconds and premium days use their wire units", WireFields),
        ("character entry: creation response identifies the starting map before relogin", Creation),
        ("character entry: online time counts once across snapshots and disconnect, not while offline", OnlineTime),
        ("character entry: schema v29 keeps characters and initializes unknown historic playtime to zero", Migration),
    ];

    private static void Check(bool value, string why) => AlbionTests.Check(value, why);

    private static void WireFields()
    {
        var dir = NewDir();
        try
        {
            using var repository = new SqliteCharacterRepository(Path.Combine(dir, "save.db"));
            var character = new StarterKit { Cluster = "1008" }.NewCharacter("EntryProbe");
            repository.Create(character);
            void Login(PremiumMode mode, long expectedTime, long expectedDays)
            {
                var peer = new FakePeer();
                var session = new LoginSession(peer, new ServerConfig { PremiumMode = mode }, repository, StarterKit.Default);
                session.OnRequest(Request(TestLogin(), 1), encrypted: false);
                var response = peer.Responses.Single();
                Check(response.ReturnCode == 0 && response.Parameters[10] is string[] and ["1008"]
                    && response.Parameters[13] is string[] and ["1008"], "cluster instance and world ids, not character GUID or empty location");
                Check(response.Parameters[34] is long[] played && played.SequenceEqual([expectedTime])
                    && response.Parameters[35] is long[] days && days.SequenceEqual([expectedDays]), "duration units match the inspected client");
            }
            Login(PremiumMode.None, 0, 0);
            character.PlayedMilliseconds = 123456;
            repository.Save(character);
            Login(PremiumMode.Free, 123456, 365);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static void OnlineTime()
    {
        var dir = NewDir();
        try
        {
            using var repository = new SqliteCharacterRepository(Path.Combine(dir, "save.db"));
            repository.Create(new StarterKit().NewCharacter("TimeProbe"));
            var clock = new ManualClock();
            using var world = new World(new ServerConfig { JoinByCharacterId = true }, repository, Items()) { Sync = new object(), Clock = clock };
            var id = repository.List()[0].Id;
            WorldSession Join()
            {
                var session = new WorldSession(new FakePeer(), world);
                session.OnRequest(Request(new JoinRequest { ss = id }, 1), encrypted: false);
                return session;
            }
            var first = Join();
            clock.Advance(TimeSpan.FromMilliseconds(1234));
            world.Saves.SaveAll();
            world.Saves.Flush();
            Check(repository.Find(id)!.PlayedMilliseconds == 1234, "stationary player's periodic snapshot includes online time");
            world.Saves.SaveAll(); world.Saves.Flush();
            Check(repository.Find(id)!.PlayedMilliseconds == 1234, "same instant is not counted twice");
            clock.Advance(TimeSpan.FromMilliseconds(766));
            first.OnDisconnected("test");
            world.Saves.Flush();
            Check(repository.Find(id)!.PlayedMilliseconds == 2000, "disconnect settles final interval once");
            clock.Advance(TimeSpan.FromHours(5));
            first.Player!.StorePlayedTime();
            Check(first.Player.Character.PlayedMilliseconds == 2000, "a departed player cannot accumulate offline time on a late snapshot");
            var second = Join();
            clock.Advance(TimeSpan.FromMilliseconds(500));
            second.OnDisconnected("test"); world.Saves.Flush();
            Check(repository.Find(id)!.PlayedMilliseconds == 2500, "offline hours excluded after a rejoin");
        }
        finally { Directory.Delete(dir, true); }
    }

    private static void Creation()
    {
        var dir = NewDir();
        try
        {
            using var repository = new SqliteCharacterRepository(Path.Combine(dir, "save.db"));
            var peer = new FakePeer();
            var starter = new StarterKit { Cluster = "1008" };
            var session = new LoginSession(peer, new ServerConfig { PremiumMode = PremiumMode.None }, repository, starter);
            session.OnRequest(Request(TestLogin(), 1), encrypted: false);
            session.OnRequest(Request(new CreateCharacterRequest { h7 = "SceneProbe" }, 2), encrypted: false);
            var response = peer.Responses.Last();
            Check(response.ReturnCode == 0 && response.Parameters[1] is "1008" && response.Parameters[2] is "1008",
                "the new entry receives instance and world location without another Login");
            Check(repository.Find("SceneProbe") is { PlayedMilliseconds: 0, Silver: 0, Cluster: "1008" }, "new fixture starts with zero duration and silver");
        }
        finally { Directory.Delete(dir, true); }
    }

    private static void Migration()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "legacy.db");
            using (var repository = new SqliteCharacterRepository(path)) repository.Create(StarterKit.Default.NewCharacter("LegacyProbe"));
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "ALTER TABLE characters DROP COLUMN tutorial_opening; ALTER TABLE characters DROP COLUMN played_ms; PRAGMA user_version=29;";
                command.ExecuteNonQuery();
            }
            using var migrated = new SqliteCharacterRepository(path);
            Check(migrated.Find("LegacyProbe") is { PlayedMilliseconds: 0, Cluster: "0000" } c
                && c.Equipment[0]?.Name == "T4_MAIN_SWORD", "legacy position and equipment preserved; duration is not invented from creation date");
        }
        finally { Directory.Delete(dir, true); }
    }
}
