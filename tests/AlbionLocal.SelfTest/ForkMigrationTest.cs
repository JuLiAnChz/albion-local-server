using System.Text.Json;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Persistence;
using Microsoft.Data.Sqlite;
using static AlbionLocal.SelfTest.AlbionTests;

namespace AlbionLocal.SelfTest;

public static class ForkMigrationTest
{
    private static void Check(bool value, string why) => AlbionLocal.SelfTest.AlbionTests.Check(value, why);

    public static IEnumerable<(string, Action)> All =>
    [
        ("fork migration: released v30 keeps playtime, balances, items and progression", () => Upgrade(30)),
        ("fork migration: released v31 also keeps the tutorial checkpoint", () => Upgrade(31)),
        ("fork migration: released v32 also keeps the tracked Destiny Board node", () => Upgrade(32)),
        ("fork migration: a failed bridge rolls back schema and data and can be retried", Rollback),
    ];

    private static readonly Guid CharacterId = Guid.Parse("00000000-0000-0000-0000-000000000123");
    private static readonly OpeningTutorialProgress Tutorial = new(27, Guid.Empty, 1234) { JourneyQuest = "saved-checkpoint", JourneyTask = 8 };

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Fixture(string path, int version)
    {
        Execute(path, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fork-schema-v32.sql")));
        Execute(path, """
            INSERT INTO accounts (id, level, name, display_name, password)
            VALUES ('00000000-0000-0000-0000-000000000456', 0, 'migrationaccount', 'MigrationAccount', 'synthetic-test-value');
            INSERT INTO characters (id, account, name, template, appearance, customization, clothes, cluster,
                position_x, position_y, created, equipment_container, inventory_container, silver, fame, played_ms)
            VALUES ('00000000-0000-0000-0000-000000000123', '00000000-0000-0000-0000-000000000456', 'MigrationProbe',
                1, 0, '[]', '[]', '0004', 12, 34, 638000000000000000,
                '00000000-0000-0000-0000-000000000789', '00000000-0000-0000-0000-000000000987', 12340000, 5432100, 1234567);
            INSERT INTO items (character, container, slot, uniquename, count, quality)
            VALUES ('00000000-0000-0000-0000-000000000123', 0, 0, 'T4_MAIN_SWORD', 1, 3);
            INSERT INTO progression (character, completed, levels, progress, tracked)
            VALUES ('00000000-0000-0000-0000-000000000123', '[12]', '{"123":2}', '{"123":77}', 123);
            """);
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE characters SET tutorial_opening = $tutorial";
            command.Parameters.AddWithValue("$tutorial", JsonSerializer.Serialize(Tutorial));
            command.ExecuteNonQuery();
        }
        if (version < 32) Execute(path, "ALTER TABLE progression DROP COLUMN tracked;");
        if (version < 31) Execute(path, "ALTER TABLE characters DROP COLUMN tutorial_opening;");
        Execute(path, $"PRAGMA user_version={version};");
    }

    private static void Upgrade(int version)
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "released-fork.db");
            Fixture(path, version);
            using (var repository = new SqliteCharacterRepository(path))
            {
                var restored = repository.Find(CharacterId)!;
                Check(restored is { Cluster: "0004", PositionX: 12, PositionY: 34, PlayedMilliseconds: 1234567, Silver: 12340000, Fame: 5432100 },
                    "location, balances, fame and historical playtime survive");
                Check(restored.Equipment[0] is { Name: "T4_MAIN_SWORD", Quality: 3 }, "equipment survives with its quality");
                Check(restored.Progression is { } p && p.Completed.SequenceEqual([12]) && p.Levels[123] == 2 && p.Progress[123] == 77
                    && p.Tracked == (version == 32 ? 123 : null), "board progress and any saved tracking survive");
                Check(restored.Tutorial == (version >= 31 ? Tutorial : null), "tutorial checkpoint is preserved, never invented");
                Check(repository.FindAccount("migrationaccount")?.PasswordHash == "synthetic-test-value", "account credentials survive");
                restored.Silver++;
                repository.Save(restored);
            }
            using (var reopened = new SqliteCharacterRepository(path))
                Check(reopened.Find(CharacterId)!.Silver == 12340001, "upgraded database reopens and accepts complete character saves");
            using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            Check(Convert.ToInt32(command.ExecuteScalar()) == 43, "fork joins the upstream schema history at v43");
            command.CommandText = "PRAGMA integrity_check";
            Check((string?)command.ExecuteScalar() == "ok", "upgraded database is structurally intact");
            command.CommandText = "PRAGMA foreign_key_check";
            using var foreignKeys = command.ExecuteReader();
            Check(!foreignKeys.Read(), "upgraded references remain valid");
        }
        finally { Directory.Delete(dir, true); }
    }

    private static void Rollback()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "rollback.db");
            Fixture(path, 32);
            Execute(path, "CREATE TABLE market_sales (incompatible INTEGER);");
            var failed = false;
            try { using var repository = new SqliteCharacterRepository(path); }
            catch (SqliteException) { failed = true; }
            Check(failed, "synthetic incompatible schema refuses the upgrade");
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version";
                Check(Convert.ToInt32(command.ExecuteScalar()) == 32, "failed bridge does not advance the released schema version");
                command.CommandText = "SELECT count(*) FROM pragma_table_info('characters') WHERE name = 'faction_standing'";
                Check(Convert.ToInt32(command.ExecuteScalar()) == 0, "columns added before the failure roll back");
                command.CommandText = "SELECT silver FROM characters WHERE name = 'MigrationProbe'";
                Check(Convert.ToInt64(command.ExecuteScalar()) == 12340000, "failed bridge preserves character data");
            }
            Execute(path, "DROP TABLE market_sales;");
            using var retried = new SqliteCharacterRepository(path);
            Check(retried.Find(CharacterId)?.Progression?.Tracked == 123, "retry after removing the synthetic obstruction succeeds");
        }
        finally { Directory.Delete(dir, true); }
    }
}
