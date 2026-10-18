using System.Globalization;
using System.Text.Json;
using AlbionLocal.Photon;

namespace AlbionLocal.Server.Content;

/// <summary>
/// The hellgates' tables (analysis/work/stageL/hellgates/SPEC.md §5.8, §5.9): content/hellgates.json, as content/corrupted.json. The switches
/// that differ between the presets are config keys (Hellgates:*, <see cref="HellgateOptions"/>); here the chat lines and an optional weight
/// override of the mapsets' maps. A key the file leaves out keeps the built-in value (<see cref="Default"/>, the shipped file's); a missing file
/// or one that does not parse gives the built-in values with a log line. Immutable.
/// </summary>
public sealed record HellgateSettings
{
    /// <summary>The built-in chat lines (SPEC §5.9), with their {placeholders}; "" switches a line off.</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultTexts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["hg.mode"] = "Hellgate: {mode} ({teamsize}v{teamsize}, IP {ip}). Change with .hg mode.",
        ["hg.versus"] = "This hellgate has an enemy team: once you have {points} points, {n} hostile adventurers arrive. Knockdowns only, nothing is lost.",
        ["hg.refused"] = "The hellgate refuses you: {why}.",
        ["hg.progress"] = "Demons slain: {points}/{required}.",
        ["hg.guardian"] = "The hellgate's guardian {name} has appeared.",
        ["hg.enemies"] = "{n} hostile adventurers have entered the hellgate.",
        ["hg.won"] = "The hellgate is conquered. Claim the chest and use the exit portal.",
        ["hg.lost"] = "Your party has fallen. The exit portal is open.",
        ["hg.closing"] = "The hellgate closes in {seconds} s.",
        ["hg.closed"] = "The hellgate has closed.",
        ["hg.waiting"] = "Waiting for {names} at the hellgate shrine; the party goes in without them in {seconds} s.",
        // Phase B (SPEC §7)
        ["hg.match"] = "Another party has entered the hellgate: defeat it to claim the reward.",
        ["hg.beaten"] = "The other party has won this hellgate. Escape to leave.",
        ["hg.infamy"] = "Hellgate infamy ({mode}): +{gain}, {total} now.",
        ["hg.milestone"] = "Hellgate infamy milestone ({mode}): {value} reached.",
        ["hg.chain"] = "The way to the next hellgate is open (chain {chain}, chests +{bonus} %).",
        ["hg.area"] = "The hellgate closes in on you: stay inside the ring of fire.",
        ["hg.areafinal"] = "The ring of fire closes completely.",
    };

    public static HellgateSettings Default { get; } = new();

    /// <summary>The weight of a map instead of hellgate.xml's (0 = never picked; Hellgates:BossMapFactor still applies); empty = the data's.</summary>
    public IReadOnlyDictionary<string, double> Maps { get; init; } = new Dictionary<string, double>(StringComparer.Ordinal);

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

    public static HellgateSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("content", $"{path} missing: built-in hellgate settings");
            return Default;
        }
        try
        {
            var s = Parse(File.ReadAllText(path), path);
            Log.Info("content", $"hellgates: {s.Texts.Count} chat lines" + (s.Maps.Count > 0 ? $", {s.Maps.Count} map weight(s) changed" : ""));
            return s;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            Log.Error("content", $"{path}: {e.Message}; built-in hellgate settings used");
            return Default;
        }
    }

    public static HellgateSettings Parse(string text, string path)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("not a JSON object");
        var s = Default;
        foreach (var p in root.EnumerateObject())
        {
            var v = p.Value;
            switch (p.Name)
            {
                case "Maps":
                    s = s with
                    {
                        Maps = v.ValueKind == JsonValueKind.Object
                            ? v.EnumerateObject().ToDictionary(m => m.Name, m => m.Value.TryGetDouble(out var w) && w >= 0 ? w : throw new InvalidDataException($"{p.Name}:{m.Name}: not a weight 0 or more"), StringComparer.Ordinal)
                            : throw new InvalidDataException($"{p.Name}: not an object of map -> weight"),
                    };
                    break;
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
