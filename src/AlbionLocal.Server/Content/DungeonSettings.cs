using System.Globalization;
using System.Text.Json;
using AlbionLocal.Photon;

namespace AlbionLocal.Server.Content;

/// <summary>
/// The random dungeon numbers neither the client data nor the captures settle (analysis/work/stageG/dungeons/SPEC.md, §6, §8,
/// §9, §14): content/dungeons.json, as content/fishing.json. Every one is an [assume] tunable; a key the file leaves out keeps
/// the built-in value (<see cref="Default"/>, the shipped file's), a missing file or one that does not parse gives the built-in
/// values with a log line. Immutable.
/// </summary>
public sealed record DungeonSettings
{
    public static DungeonSettings Default { get; } = new();

    /// <summary>Whether open-world clusters get random dungeon exits on their markers at all.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// The marker subtypes whose dungeon is one per party (or per player out of a party) behind the same exit; the others
    /// are one per exit for everyone who enters it (SOLO and EVENT_SOLO [assume]).
    /// </summary>
    public IReadOnlySet<string> PerPartySubtypes { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SOLO", "EVENT_SOLO" };

    /// <summary>The floors of every dungeon (0 = the generator's Size draw; SPEC §5.3 step 2: "1 is enough at first"), path lengths still drawn.</summary>
    public int Floors { get; init; }

    /// <summary>A new map fills its markers at once, without the exits' activationtime (true [assume]); later exits wait for it.</summary>
    public bool FillAtStart { get; init; } = true;

    /// <summary>Whether the fixed exit tiles (randomdungeonfixedexit) get an exit (false [assume]: SPEC §8.1; sent with key 5 true and their own tile in key 6, as the road dungeons of stageL roads §7.1, untried in the open world).</summary>
    public bool FixedExits { get; init; }

    /// <summary>The smallest trigger square side the server accepts (SPEC §2.2: pos ± max(size/2, 5)), metres; Transfer:PositionTolerance is added.</summary>
    public float MinSquare { get; init; } = 10;

    /// <summary>
    /// Seconds of the channel when a portal is clicked (ExitEnterStart 237, stageM exit NOTES §2): gamedata.xml
    /// ExitSettings/@channeledexitusagetime, 2. The client runs its own bar that long whatever this says, so keep them equal.
    /// </summary>
    public float ExitChannelSeconds { get; init; } = 2;

    /// <summary>Metres the player may move during that channel before it is cancelled (as Expeditions:CancelDistance) [assume].</summary>
    public float ExitCancelDistance { get; init; } = 1;

    /// <summary>Seconds between two passes of the exit and dungeon timers (rerolls, despawns, refills, closing).</summary>
    public float UpdateSeconds { get; init; } = 1;

    /// <summary>Seconds a GM's exit (.rd) stays when nobody uses it (0 = until the map unloads).</summary>
    public float GmExitSeconds { get; init; } = 600;

    /// <summary>
    /// Seconds a dungeon counts as occupied after a player was sent into one of its floors (the commit), until that player's Join:
    /// the client builds the floor scene before it joins, which can take longer than a solo dungeon's empty lifetime (120 [assume]).
    /// </summary>
    public float ArrivalGraceSeconds { get; init; } = 120;

    /// <summary>Where a character whose dungeon is gone goes when it has no saved return point.</summary>
    public string FallbackCluster { get; init; } = "0000";

    /// <summary>Whether a dungeon floor gets the loot chests of its reward layers (SPEC §7).</summary>
    public bool Chests { get; init; } = true;

    /// <summary>
    /// The ChestState a chest spawns with (NewLootChest keys 5 and 23): 6 Unlocked [assume]; the SPEC gives no lock rule (the boss chests
    /// are locked by <see cref="BossChests"/>, stageH). Only 6 (Unlocked) can be opened.
    /// </summary>
    public byte ChestState { get; init; } = 6;

    /// <summary>
    /// Whether a player who dies inside a dungeon (a lethal clustertype) respawns at the dungeon's entrance in the open world (in front
    /// of its exit, SPEC §6.4) rather than at its usual respawn cluster [assume].
    /// </summary>
    public bool RespawnAtEntrance { get; init; } = true;

    // ---- stageH PvE (PVE-DESIGN.md §3 step 10) ----

    /// <summary>
    /// Group scaling (the formula of Expeditions:GroupScaling, stageH expeditions SPEC §12.1): the mobs of a dungeon whose subtype is in
    /// <see cref="GroupScaledSubtypes"/> have their max health (and, with <see cref="ScaleDamage"/>, their damage) × min(ScalingMax,
    /// ScalingBase + ScalingPerPlayer × players), the players being the entering player's party members online when the dungeon is made,
    /// or the characters sent into it so far when more (1: 0.55, 2: 0.70, 3: 0.85, 4+: 1.0 with the defaults). A floor is scaled at its
    /// fill: the mobs already standing keep their factor. Off: the official group mobs.
    /// </summary>
    public bool GroupScaling { get; init; } = true;

    /// <summary>The marker subtypes scaled by <see cref="GroupScaling"/> (VETERAN and ELITE, the group dungeons; the solo ones are made for one).</summary>
    public IReadOnlySet<string> GroupScaledSubtypes { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "VETERAN", "ELITE" };

    public double ScalingBase { get; init; } = 0.40;
    public double ScalingPerPlayer { get; init; } = 0.15;
    public double ScalingMax { get; init; } = 1.0;
    public bool ScaleDamage { get; init; } = true;

    /// <summary>The factor of <see cref="GroupScaling"/> for <paramref name="players"/> in a dungeon of <paramref name="subtype"/> (1 when off or not scaled).</summary>
    public double GroupFactor(string subtype, int players) => GroupScaling && GroupScaledSubtypes.Contains(subtype)
        ? Math.Max(0.01, Math.Min(ScalingMax, ScalingBase + ScalingPerPlayer * Math.Max(1, players))) : 1;

    /// <summary>
    /// Boss chests (ChestState 0, BossLocked): a floor chest whose LootChestSpawnpoint is a boss one (its name holds BOSS:
    /// …_LOOTCHEST_BOSS_RARE, ANNIVERSARY_…_MINIBOSS) spawns locked while a boss or miniboss (mobs.xml mobtypecategory) stands within
    /// <see cref="BossChestRadius"/> of it, or, with none that near, while any boss or miniboss of the floor stands; it unlocks for everyone
    /// (UpdateLootChest) when the last of them dies. A floor without one leaves it unlocked. Off: <see cref="ChestState"/> for every chest.
    /// </summary>
    public bool BossChests { get; init; } = true;

    /// <summary>Metres around a boss chest within which a boss or miniboss guards it (from the mob's spawn point) [assume].</summary>
    public float BossChestRadius { get; init; } = 40;

    /// <summary>
    /// "Dungeon of the day": the dungeons of one faction of <see cref="DailyFactions"/> (the generator's: HERETIC, KEEPER, MORGANA, UNDEAD)
    /// give × DailyBonus on their mobs' fame, silver and loot and on their chests' loot (on top of the floor level's factors), the
    /// faction taking turns each day at <see cref="DailyResetHour"/> UTC (the day number mod the list). Frozen when a dungeon is made. 1 = off.
    /// </summary>
    public double DailyBonus { get; init; } = 1.5;

    public IReadOnlyList<string> DailyFactions { get; init; } = ["HERETIC", "KEEPER", "MORGANA", "UNDEAD"];

    /// <summary>The UTC hour the dungeon of the day changes (10, the official daily reset).</summary>
    public int DailyResetHour { get; init; } = 10;

    /// <summary>The chat line (SystemMessage) a player gets on entering a dungeon of the day ({faction}, {bonus}); "" = none.</summary>
    public string DailyText { get; init; } = "Dungeon of the day: {faction} dungeons give x{bonus} fame, silver and loot today.";

    /// <summary>The faction of the dungeon of the day at <paramref name="utc"/>; null when <see cref="DailyBonus"/> is 1 or no faction is listed.</summary>
    public string? DailyFaction(DateTime utc)
    {
        if (DailyBonus == 1 || DailyFactions.Count == 0) return null;
        var day = DateOnly.FromDateTime(utc.AddHours(-DailyResetHour)).DayNumber;
        return DailyFactions[day % DailyFactions.Count];
    }

    public static DungeonSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("content", $"{path} missing: built-in random dungeon settings");
            return Default;
        }
        try
        {
            var s = Parse(File.ReadAllText(path), path);
            Log.Info("content", $"dungeons: {(s.Enabled ? "on" : "off")}, per party: {string.Join("/", s.PerPartySubtypes)}, floors {(s.Floors == 0 ? "as drawn" : s.Floors.ToString(CultureInfo.InvariantCulture))}");
            return s;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            Log.Error("content", $"{path}: {e.Message}; built-in random dungeon settings used");
            return Default;
        }
    }

    /// <summary>
    /// The file: Enabled, PerPartySubtypes [names], Floors, FillAtStart, FixedExits, MinSquare, ExitChannelSeconds, ExitCancelDistance, UpdateSeconds, GmExitSeconds, ArrivalGraceSeconds,
    /// FallbackCluster, Chests, ChestState, RespawnAtEntrance; stageH PvE: GroupScaling, GroupScaledSubtypes [names], ScalingBase, ScalingPerPlayer,
    /// ScalingMax, ScaleDamage, BossChests, BossChestRadius, DailyBonus, DailyFactions [names], DailyResetHour, DailyText.
    /// </summary>
    public static DungeonSettings Parse(string text, string path)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("not a JSON object");
        var s = Default;
        float Num(JsonElement v, string key, float min) =>
            v.TryGetSingle(out var f) && f >= min ? f : throw new InvalidDataException($"{key}: {v} is not a number of {min} or more");
        double Dbl(JsonElement v, string key, double min) =>
            v.TryGetDouble(out var f) && f >= min ? f : throw new InvalidDataException($"{key}: {v} is not a number of {min} or more");
        bool Bool(JsonElement v, string key) => v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : throw new InvalidDataException($"{key}: not true or false");
        List<string> Names(JsonElement v, string key) => v.ValueKind == JsonValueKind.Array
            ? [.. v.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0)]
            : throw new InvalidDataException($"{key}: not a list of names");
        foreach (var p in root.EnumerateObject())
        {
            switch (p.Name)
            {
                case "Enabled": s = s with { Enabled = Bool(p.Value, p.Name) }; break;
                case "PerPartySubtypes": s = s with { PerPartySubtypes = Names(p.Value, p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) }; break;
                case "Floors": s = s with { Floors = p.Value.TryGetInt32(out var f) && f is >= 0 and <= 255 ? f : throw new InvalidDataException($"{p.Name}: {p.Value}") }; break;
                case "FillAtStart": s = s with { FillAtStart = Bool(p.Value, p.Name) }; break;
                case "FixedExits": s = s with { FixedExits = Bool(p.Value, p.Name) }; break;
                case "MinSquare": s = s with { MinSquare = Num(p.Value, p.Name, 0) }; break;
                case "ExitChannelSeconds": s = s with { ExitChannelSeconds = Num(p.Value, p.Name, 0) }; break;
                case "ExitCancelDistance": s = s with { ExitCancelDistance = Num(p.Value, p.Name, 0) }; break;
                case "UpdateSeconds": s = s with { UpdateSeconds = Num(p.Value, p.Name, 0.05f) }; break;
                case "GmExitSeconds": s = s with { GmExitSeconds = Num(p.Value, p.Name, 0) }; break;
                case "ArrivalGraceSeconds": s = s with { ArrivalGraceSeconds = Num(p.Value, p.Name, 0) }; break;
                case "FallbackCluster": s = s with { FallbackCluster = p.Value.GetString() is { Length: > 0 } c ? c : throw new InvalidDataException($"{p.Name}: empty") }; break;
                case "Chests": s = s with { Chests = Bool(p.Value, p.Name) }; break;
                case "ChestState": s = s with { ChestState = p.Value.TryGetByte(out var cs) && cs <= 8 ? cs : throw new InvalidDataException($"{p.Name}: {p.Value} is no ChestState 0-8") }; break;
                case "RespawnAtEntrance": s = s with { RespawnAtEntrance = Bool(p.Value, p.Name) }; break;
                case "GroupScaling": s = s with { GroupScaling = Bool(p.Value, p.Name) }; break;
                case "GroupScaledSubtypes": s = s with { GroupScaledSubtypes = Names(p.Value, p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) }; break;
                case "ScalingBase": s = s with { ScalingBase = Dbl(p.Value, p.Name, 0) }; break;
                case "ScalingPerPlayer": s = s with { ScalingPerPlayer = Dbl(p.Value, p.Name, 0) }; break;
                case "ScalingMax": s = s with { ScalingMax = Dbl(p.Value, p.Name, 0.01) }; break;
                case "ScaleDamage": s = s with { ScaleDamage = Bool(p.Value, p.Name) }; break;
                case "BossChests": s = s with { BossChests = Bool(p.Value, p.Name) }; break;
                case "BossChestRadius": s = s with { BossChestRadius = Num(p.Value, p.Name, 0) }; break;
                case "DailyBonus": s = s with { DailyBonus = Dbl(p.Value, p.Name, 0) }; break;
                case "DailyFactions": s = s with { DailyFactions = [.. Names(p.Value, p.Name).Select(n => n.ToUpperInvariant())] }; break;
                case "DailyResetHour":
                    s = s with { DailyResetHour = p.Value.TryGetInt32(out var h) && h is >= 0 and <= 23 ? h : throw new InvalidDataException($"{p.Name}: {p.Value} is no hour 0-23") };
                    break;
                case "DailyText": s = s with { DailyText = p.Value.GetString() ?? "" }; break;
                default: Log.Warn("content", $"{path}: unknown key {p.Name}, skipped"); break;
            }
        }
        return s;
    }
}
