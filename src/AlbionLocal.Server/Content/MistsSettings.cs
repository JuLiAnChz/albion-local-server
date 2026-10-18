using System.Globalization;
using System.Text.Json;
using AlbionLocal.Photon;

namespace AlbionLocal.Server.Content;

/// <summary>
/// The Mists' tables (analysis/work/stageL/mists/SPEC.md §6.10, §6.11): content/mists.json, as content/hellgates.json. The switches that differ
/// between the presets are config keys (Mists:*, <see cref="MistsOptions"/>); here what a mists.xml &lt;treasures&gt; subtype spawns (the official
/// treasure tables are the server's own; the client knows only the objects), the treasures' spacing, the arrival distance and the chat lines.
/// A key the file leaves out keeps the built-in value (<see cref="Default"/>, the shipped file's); a missing file or one that does not parse
/// gives the built-in values with a log line. Phase B: Knightfall Abbey's treasures, the standing and favour per fame, the resource wisps'
/// chance. Immutable.
/// </summary>
public sealed record MistsSettings
{
    /// <summary>
    /// The built-in treasure map (SPEC §6.4 [inference by name], Q7): a subtype → the names one of its rolls draws from, "{tier}" the zone's tier
    /// (4-8). A LootChestSpawnpoint of lootchests.xml becomes a chest, a mobs.xml mob a mob (MYTHICALMOBS: the mist boss). Subtypes left out
    /// (WISPS, SPECIALMOBS, RESOURCE_*: phase B, or no client object) spawn nothing.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultTreasures = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["CHEST"] = ["MISTS_TREASURE_SOLO"],
        ["COFFER"] = ["MISTS_TREASURE_COFFER_SOLO"],
        ["MYTHICALMOBS"] = ["T{tier}_MOB_MISTS_GRIFFIN", "T{tier}_MOB_MISTS_SPIDER", "T{tier}_MOB_MISTS_FAIRYDRAGON"],
    };

    /// <summary>Phase B: Knightfall Abbey's treasures (its treasurespawnpoint tiles of the subtype): the lootchests.xml MISTS_DUNGEON_* spawnpoints.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultDungeonTreasures = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["CHEST"] = ["MISTS_DUNGEON_TREASURE_SOLO"],
        ["COFFER"] = ["MISTS_DUNGEON_TREASURE_COFFER_SOLO"],
    };

    /// <summary>The built-in chat lines (SPEC §6.11), with their {placeholders}; "" switches a line off.</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultTexts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Entered"] = "The {mode} ({rarity}, T{tier}): it fades in {minutes} min.",
        ["Closing"] = "The mist thickens: this zone closes in {seconds} s.",
        ["Closed"] = "The zone dissolved; you are back at {place}.",
        ["TeamFull"] = "This mist takes at most {max} travellers together; you enter on your own.",
        ["DuoAlone"] = "You enter the Greater Mists alone: its creatures are weakened for you.",
        ["BossDown"] = "{boss} has fallen. A reward chest awaits.",
        ["ReturnExpired"] = "The way back has faded.",
        ["Refused"] = "The mist refuses you: {why}.",
        ["Standing"] = "Brecilien standing +{gain} ({total}, rank {rank}).",
        ["Shrine"] = "The room is cleared: the {set} shrine blesses you.",
        ["StaticBlocked"] = "The mists entrance stays closed to you for {seconds} s.",
        ["StandingTooLow"] = "This needs Brecilien standing {need}; you have {have}.",
        ["RoadsClosed"] = "The roads entrance of Brecilien is not open on this server yet.",
    };

    public static MistsSettings Default { get; } = new();

    /// <summary>The treasures by mists.xml subtype (<see cref="DefaultTreasures"/> with the file's over them; an empty list switches one off).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Treasures { get; init; } = DefaultTreasures;

    /// <summary>MinTreasureSpacing (30 m): no two treasures of a zone this close.</summary>
    public double MinTreasureSpacing { get; init; } = 30;

    /// <summary>ArrivalMinDistance (40 m): no treasure this close to a zone's arrival points [design].</summary>
    public double ArrivalMinDistance { get; init; } = 40;

    /// <summary>Phase B: DungeonTreasures, Knightfall Abbey's (<see cref="DefaultDungeonTreasures"/> with the file's over them).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DungeonTreasures { get; init; } = DefaultDungeonTreasures;

    /// <summary>Phase B: StandingPerFame (1): Brecilien standing per point of mob fame earned in the mists (× Mists:StandingFactor).</summary>
    public double StandingPerFame { get; init; } = 1;

    /// <summary>Phase B: FavorPerStanding (0.01): the trader's FAVOR per point of standing gained [design: FAVOR's source is not in the client data].</summary>
    public double FavorPerStanding { get; init; } = 0.01;

    /// <summary>Phase B: ResourceWispChance (0.5): the chance of a MISTS_SOLO_BLACK zone to hold a resource wisp (a portal into a resource zone) [design].</summary>
    public double ResourceWispChance { get; init; } = 0.5;

    /// <summary>The chat lines by key: the built-in ones with the file's over them.</summary>
    public IReadOnlyDictionary<string, string> Texts { get; init; } = DefaultTexts;

    /// <summary>Text <paramref name="key"/> with each {name} of <paramref name="values"/> filled in ("" for an unknown key).</summary>
    public string Text(string key, params (string Name, object Value)[] values)
    {
        var text = Texts.GetValueOrDefault(key) ?? DefaultTexts.GetValueOrDefault(key) ?? "";
        foreach (var (name, value) in values)
            text = text.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return text;
    }

    public static MistsSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("content", $"{path} missing: built-in mists settings");
            return Default;
        }
        try
        {
            var s = Parse(File.ReadAllText(path), path);
            Log.Info("content", $"mists: {s.Treasures.Count} treasure subtype(s), {s.Texts.Count} chat lines");
            return s;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            Log.Error("content", $"{path}: {e.Message}; built-in mists settings used");
            return Default;
        }
    }

    public static MistsSettings Parse(string text, string path)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("not a JSON object");
        var s = Default;
        double Number(JsonProperty p) => p.Value.TryGetDouble(out var d) && d >= 0 ? d : throw new InvalidDataException($"{p.Name}: not a number 0 or more");
        foreach (var p in root.EnumerateObject())
        {
            var v = p.Value;
            switch (p.Name)
            {
                case "Treasures":
                    if (v.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{p.Name}: not an object of subtype -> names");
                    var treasures = new Dictionary<string, IReadOnlyList<string>>(DefaultTreasures, StringComparer.Ordinal);
                    foreach (var t in v.EnumerateObject())
                        treasures[t.Name] = t.Value.ValueKind == JsonValueKind.Array && t.Value.EnumerateArray().All(n => n.ValueKind == JsonValueKind.String)
                            ? [.. t.Value.EnumerateArray().Select(n => n.GetString()!)]
                            : throw new InvalidDataException($"{p.Name}:{t.Name}: not an array of names");
                    s = s with { Treasures = treasures };
                    break;
                case "DungeonTreasures":
                    if (v.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{p.Name}: not an object of subtype -> names");
                    var inside = new Dictionary<string, IReadOnlyList<string>>(DefaultDungeonTreasures, StringComparer.Ordinal);
                    foreach (var t in v.EnumerateObject())
                        inside[t.Name] = t.Value.ValueKind == JsonValueKind.Array && t.Value.EnumerateArray().All(n => n.ValueKind == JsonValueKind.String)
                            ? [.. t.Value.EnumerateArray().Select(n => n.GetString()!)]
                            : throw new InvalidDataException($"{p.Name}:{t.Name}: not an array of names");
                    s = s with { DungeonTreasures = inside };
                    break;
                case "StandingPerFame": s = s with { StandingPerFame = Number(p) }; break;
                case "FavorPerStanding": s = s with { FavorPerStanding = Number(p) }; break;
                case "ResourceWispChance": s = s with { ResourceWispChance = Math.Min(1, Number(p)) }; break;
                case "MinTreasureSpacing": s = s with { MinTreasureSpacing = Number(p) }; break;

                case "ArrivalMinDistance": s = s with { ArrivalMinDistance = Number(p) }; break;
                case "Texts":
                    if (v.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{p.Name}: not an object of key -> text");
                    var texts = new Dictionary<string, string>(DefaultTexts, StringComparer.Ordinal);
                    foreach (var t in v.EnumerateObject())
                        texts[t.Name] = t.Value.ValueKind == JsonValueKind.String ? t.Value.GetString()! : throw new InvalidDataException($"{p.Name}:{t.Name}: not a string");
                    s = s with { Texts = texts };
                    break;
                default: Log.Warn("content", $"{path}: unknown key {p.Name}, skipped"); break;
            }
        }
        return s;
    }
}
