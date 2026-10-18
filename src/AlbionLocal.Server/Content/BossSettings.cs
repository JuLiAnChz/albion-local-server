using System.Globalization;
using System.Numerics;
using System.Text.Json;
using AlbionLocal.Photon;

namespace AlbionLocal.Server.Content;

/// <summary>How a world boss slot comes back (analysis/work/stageH/bosses/SPEC.md §4.1, Bosses:Schedule).</summary>
public enum BossSchedule
{
    /// <summary>At the next spawn time of worldbosses.xml (every odd UTC hour in the base file) after its death.</summary>
    Data,

    /// <summary>uniform(respawn min, max) after its death (the mob's respawntimeseconds, an extra spawn's RespawnMinutes), ÷ Rates:Respawn.</summary>
    Respawn,

    /// <summary>Whichever of the two comes first.</summary>
    Both,
}

/// <summary>Who gets the loot of a killed world boss.</summary>
public enum BossLoot
{
    /// <summary>The official kill path: one bag for the killer (and its party).</summary>
    Killer,

    /// <summary>One bag (one roll, as the official kill) that every contributor may open: the default.</summary>
    Shared,

    /// <summary>A bag of its own roll for every contributor (no competition for the drop, but the table is rolled once per contributor).</summary>
    Personal,
}

/// <summary>
/// A world boss the server adds where the client data has none (SPEC §6.1 ExtraSpawns: the T4 PvE clusters 0204 and 1207): the
/// worldbosses.xml boss, where it stands and faces, how it comes back and its own health and damage factors (null = the file's).
/// It has no map label in the client (that comes from world.xml), which the client does not need.
/// </summary>
public sealed record BossSpawn(string Cluster, string Boss, Vector2 Position, float Direction, BossSchedule Schedule, (double Min, double Max) RespawnMinutes,
    double? HealthFactor, double? DamageFactor);

/// <summary>
/// The world bosses (SPEC §4.1, §6.1; PVE-DESIGN.md §2.E, §3 step 6): content/bosses.json, as content/journals.json. The WORLDBOSS
/// spawn points of the client data get their boss (<see cref="Systems.WorldBosses"/>), with the schedule, announcements, health and
/// damage factors and the loot rule here, and <see cref="ExtraSpawns"/> add bosses of the server's own. A key the file leaves out
/// keeps the built-in value (<see cref="Default"/>; the shipped file's extra spawns are not built in), a missing file or one that does
/// not parse gives the built-in values with a log line. Immutable.
/// </summary>
public sealed record BossSettings
{
    public static BossSettings Default { get; } = new();

    /// <summary>Whether world bosses spawn at all (off: the WORLDBOSS points stay empty, as before stageH).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The boss data file of the client's region: worldbosses (odd UTC hours) or worldbosses_asia (even hours; SPEC §10 Q8).</summary>
    public string DataFile { get; init; } = "worldbosses";

    /// <summary>How the data's bosses come back (an extra spawn has its own).</summary>
    public BossSchedule Schedule { get; init; } = BossSchedule.Data;

    /// <summary>Whether a boss stands from its map's first fill (else it comes at its schedule's first time).</summary>
    public bool SpawnAtStart { get; init; } = true;

    /// <summary>Whether a boss's spawn and death go out as a system chat line (SystemMessage).</summary>
    public bool Announce { get; init; } = true;

    /// <summary>Who reads the announcements: only the players in the boss's cluster (false, SPEC §4.1) or every player in game.</summary>
    public bool AnnounceToAll { get; init; }

    /// <summary>The spawn line: {boss} its name, {cluster} the cluster id, {x} {z} its position.</summary>
    public string AnnounceSpawn { get; init; } = "World boss {boss} has appeared in {cluster} at ({x}, {z})";

    /// <summary>The death line ({boss}, {cluster}, {next}: when it comes back, UTC).</summary>
    public string AnnounceKill { get; init; } = "World boss {boss} was defeated in {cluster}; next at {next} UTC";

    /// <summary>The death line of a boss that does not come back (a one-off .boss spawn): {boss}, {cluster}.</summary>
    public string AnnounceKillOnce { get; init; } = "World boss {boss} was defeated in {cluster}";

    /// <summary>Factor on the boss's maximum health (NewMob key 14): 0.3 for two or three friends, 0.15 alone; 1 = the data's.</summary>
    public double HealthFactor { get; init; } = 1;

    /// <summary>Factor on the damage the boss deals (autos and spells).</summary>
    public double DamageFactor { get; init; } = 1;

    /// <summary>Who gets the loot (the boss's T0_LOOTCHEST_RAIDBOSS bag).</summary>
    public BossLoot Loot { get; init; } = BossLoot.Shared;

    /// <summary>The share of the boss's threat (damage and healing against it) a player needs to count as a contributor; the killer always does.</summary>
    public double LootMinShare { get; init; } = 0.05;

    /// <summary>The server's own bosses (none built in; the shipped file puts one into 0204 and one into 1207).</summary>
    public IReadOnlyList<BossSpawn> ExtraSpawns { get; init; } = [];

    public static BossSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("content", $"{path} missing: built-in world boss settings");
            return Default;
        }
        try
        {
            var s = Parse(File.ReadAllText(path), path);
            Log.Info("content", string.Create(CultureInfo.InvariantCulture,
                $"bosses: {(s.Enabled ? "on" : "off")}, {s.DataFile}, schedule {s.Schedule}, health x{s.HealthFactor}, damage x{s.DamageFactor}, loot {s.Loot}, {s.ExtraSpawns.Count} extra spawn(s)"));
            return s;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            Log.Error("content", $"{path}: {e.Message}; built-in world boss settings used");
            return Default;
        }
    }

    /// <summary>
    /// The file: Enabled, DataFile, Schedule, SpawnAtStart, Announce, AnnounceToAll, AnnounceSpawn, AnnounceKill, AnnounceKillOnce, HealthFactor, DamageFactor,
    /// Loot, LootMinShare, ExtraSpawns [{ Cluster, Boss, Position [x, z], Direction, Schedule, RespawnMinutes [min, max], HealthFactor, DamageFactor }].
    /// </summary>
    public static BossSettings Parse(string text, string path)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("not a JSON object");
        var s = Default;
        double Num(JsonElement v, string key, double min, double max) =>
            v.TryGetDouble(out var d) && d >= min && d <= max ? d : throw new InvalidDataException($"{key}: {v} is not {min}..{max}");
        bool Bool(JsonElement v, string key) => v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : throw new InvalidDataException($"{key}: not true or false");
        string Str(JsonElement v, string key) => v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new InvalidDataException($"{key}: not a string");
        T Enum<T>(JsonElement v, string key) where T : struct, System.Enum =>
            System.Enum.TryParse<T>(Str(v, key), ignoreCase: true, out var e) && System.Enum.IsDefined(e) ? e
                : throw new InvalidDataException($"{key}: {v} is not one of {string.Join(", ", System.Enum.GetNames<T>()).ToLowerInvariant()}");
        double[] Pair(JsonElement v, string key, double min, double max) =>
            v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 2 ? [.. v.EnumerateArray().Select(x => Num(x, key, min, max))]
                : throw new InvalidDataException($"{key}: not a list of two numbers");
        foreach (var p in root.EnumerateObject())
        {
            switch (p.Name)
            {
                case "Enabled": s = s with { Enabled = Bool(p.Value, p.Name) }; break;
                case "DataFile":
                    s = s with { DataFile = Str(p.Value, p.Name) is var f && f is "worldbosses" or "worldbosses_asia" ? f : throw new InvalidDataException($"{p.Name}: not worldbosses or worldbosses_asia") };
                    break;
                case "Schedule": s = s with { Schedule = Enum<BossSchedule>(p.Value, p.Name) }; break;
                case "SpawnAtStart": s = s with { SpawnAtStart = Bool(p.Value, p.Name) }; break;
                case "Announce": s = s with { Announce = Bool(p.Value, p.Name) }; break;
                case "AnnounceToAll": s = s with { AnnounceToAll = Bool(p.Value, p.Name) }; break;
                case "AnnounceSpawn": s = s with { AnnounceSpawn = Str(p.Value, p.Name) }; break;
                case "AnnounceKill": s = s with { AnnounceKill = Str(p.Value, p.Name) }; break;
                case "AnnounceKillOnce": s = s with { AnnounceKillOnce = Str(p.Value, p.Name) }; break;
                case "HealthFactor": s = s with { HealthFactor = Num(p.Value, p.Name, 0.001, 100) }; break;
                case "DamageFactor": s = s with { DamageFactor = Num(p.Value, p.Name, 0, 100) }; break;
                case "Loot": s = s with { Loot = Enum<BossLoot>(p.Value, p.Name) }; break;
                case "LootMinShare": s = s with { LootMinShare = Num(p.Value, p.Name, 0, 1) }; break;
                case "ExtraSpawns":
                    if (p.Value.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"{p.Name}: not a list");
                    var list = new List<BossSpawn>();
                    foreach (var e in p.Value.EnumerateArray())
                    {
                        if (e.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{p.Name}: an entry is not an object");
                        string? cluster = null, boss = null;
                        Vector2? at = null;
                        var spawn = new BossSpawn("", "", default, 0, BossSchedule.Respawn, (20, 40), null, null);
                        foreach (var q in e.EnumerateObject())
                        {
                            var key = $"{p.Name}.{q.Name}";
                            switch (q.Name)
                            {
                                case "Cluster": cluster = Str(q.Value, key); break;
                                case "Boss": boss = Str(q.Value, key); break;
                                case "Position": at = Pair(q.Value, key, -10_000, 10_000) is [var x, var z] ? new Vector2((float)x, (float)z) : null; break;
                                case "Direction": spawn = spawn with { Direction = (float)Num(q.Value, key, -360, 720) }; break;
                                case "Schedule": spawn = spawn with { Schedule = Enum<BossSchedule>(q.Value, key) }; break;
                                case "RespawnMinutes":
                                    var r = Pair(q.Value, key, 0, 7 * 24 * 60);
                                    spawn = spawn with { RespawnMinutes = (Math.Min(r[0], r[1]), Math.Max(r[0], r[1])) };
                                    break;
                                case "HealthFactor": spawn = spawn with { HealthFactor = Num(q.Value, key, 0.001, 100) }; break;
                                case "DamageFactor": spawn = spawn with { DamageFactor = Num(q.Value, key, 0, 100) }; break;
                                default: Log.Warn("content", $"{path}: unknown key {key}, skipped"); break;
                            }
                        }
                        if (cluster is not { Length: > 0 } || boss is not { Length: > 0 } || at is not { } pos)
                            throw new InvalidDataException($"{p.Name}: an entry without Cluster, Boss or Position");
                        list.Add(spawn with { Cluster = cluster, Boss = boss.ToUpperInvariant(), Position = pos });
                    }
                    s = s with { ExtraSpawns = list };
                    break;
                default: Log.Warn("content", $"{path}: unknown key {p.Name}, skipped"); break;
            }
        }
        return s;
    }
}
