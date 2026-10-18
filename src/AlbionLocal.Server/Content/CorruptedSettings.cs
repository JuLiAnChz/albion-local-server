using System.Globalization;
using System.Text.Json;
using AlbionLocal.Photon;

namespace AlbionLocal.Server.Content;

/// <summary>The completion reward of a corrupted dungeon difficulty (stageL corrupted SPEC §4.3): whole silver and fame, before the rates.</summary>
public sealed record CorruptedCompletion(long Silver, long Fame);

/// <summary>The reward of a repelled (or won) invasion of a difficulty (SPEC §4.4, phase B): whole silver before the rates, and infamy (× Corrupted:InfamyFactor).</summary>
public sealed record CorruptedInvaderReward(long Silver, long Infamy);

/// <summary>How a consenting owner's bot invader is timed (content/corrupted.json Rolls, SPEC §4.4).</summary>
public enum CorruptedRolls
{
    /// <summary>Once, Corrupted:BotInvaderDelaySeconds after the shrine use (the pve default).</summary>
    Fixed,

    /// <summary>By the data's rolls: every rollinterval (30 s) after safeperiod (60 s), with chanceperrollpvp (5.9 %).</summary>
    Official,
}

/// <summary>
/// The corrupted dungeons' tables (analysis/work/stageL/corrupted/SPEC.md §4.3, §4.6, §4.7): content/corrupted.json, as content/expeditions.json.
/// The switches and factors that differ between the presets are config keys (Corrupted:*, <see cref="CorruptedOptions"/>); here the
/// completion rewards per difficulty, the invasion rewards and the bot invader's timing (phase B), the chat lines and an optional weight
/// override of the 20 maps. A key the file leaves out keeps the
/// built-in value (<see cref="Default"/>, the shipped file's); a missing file or one that does not parse gives the built-in values with a
/// log line. Immutable.
/// </summary>
public sealed record CorruptedSettings
{
    /// <summary>The built-in chat lines (SPEC §4.6), with their {placeholders}.</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultTexts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["cd.refused"] = "Corrupted dungeon: {reason}.",
        ["cd.entered"] = "{rank} dungeon, T{tier}: slay demons to draw out the demon ({points}/{required}).",
        ["cd.demon"] = "The demon has appeared in its lair.",
        ["cd.completed"] = "Demon slain: +{infamy} infamy, +{silver} silver. The boss chest is open; leave through the start or the escape.",
        ["cd.rank"] = "Infamy {value} reached: corrupted rewards +{bonus}%.",
        ["cd.closing"] = "The dungeon closes in {seconds} s.",
        ["cd.closed"] = "The corrupted dungeon has closed.",
        ["cd.left"] = "You left the corrupted dungeon: +{infamy} infamy.",
        ["cd.shrine"] = "The Demonic Shrine has marked you: you may be invaded now.",
        ["cd.invaded"] = "An invader has come: beat him or destroy {shards} Demonic Shards.",
        ["cd.invading"] = "You invade {owner}'s dungeon: beat its owner.",
        ["cd.repelled"] = "The invader is gone: +{infamy} infamy, +{silver} silver.",
        ["cd.lost"] = "The invader has beaten you and left.",
        ["cd.won"] = "You have beaten the dungeon's owner: +{infamy} infamy, +{silver} silver. You return in {seconds} s.",
        ["cd.beaten"] = "The dungeon's owner has repelled you. You return in {seconds} s.",
        ["cd.over"] = "The invasion is over without a fight: no reward.",
        ["cd.chain"] = "Next dungeon: streak {streak}, completion rewards +{bonus}%.",
    };

    public static CorruptedSettings Default { get; } = new();

    /// <summary>The completion reward by difficulty name (beginner, intermediate, pro; SPEC §4.3 [design]), × Rates and × Corrupted:RewardFactor.</summary>
    public IReadOnlyDictionary<string, CorruptedCompletion> Completion { get; init; } = new Dictionary<string, CorruptedCompletion>(StringComparer.OrdinalIgnoreCase)
    {
        ["beginner"] = new(15_000, 20_000), ["intermediate"] = new(40_000, 60_000), ["pro"] = new(120_000, 200_000),
    };

    /// <summary>
    /// The reward of the owner who repels an invasion (beats the invader or destroys the shards), and of a player invader who wins, by difficulty
    /// name (SPEC §4.4 InvaderReward [design]): whole silver × Rates:Silver, infamy (the data's infamykillbonus 300 / 1500 / 6000) × InfamyFactor.
    /// </summary>
    public IReadOnlyDictionary<string, CorruptedInvaderReward> InvaderReward { get; init; } = new Dictionary<string, CorruptedInvaderReward>(StringComparer.OrdinalIgnoreCase)
    {
        ["beginner"] = new(5_000, 300), ["intermediate"] = new(15_000, 1_500), ["pro"] = new(40_000, 6_000),
    };

    /// <summary>How a consenting owner's bot invader is timed (Fixed: once after Corrupted:BotInvaderDelaySeconds; Official: the data's rolls).</summary>
    public CorruptedRolls Rolls { get; init; } = CorruptedRolls.Fixed;

    /// <summary>The weight of each corrupted map instead of corrupteddungeons.xml's (0 = never picked); empty = the data's.</summary>
    public IReadOnlyDictionary<string, double> Maps { get; init; } = new Dictionary<string, double>(StringComparer.Ordinal);

    /// <summary>The chat lines by key (SPEC §4.6): the built-in ones with the file's over them.</summary>
    public IReadOnlyDictionary<string, string> Texts { get; init; } = DefaultTexts;

    /// <summary>The completion of difficulty <paramref name="name"/>; none = no silver or fame.</summary>
    public CorruptedCompletion CompletionOf(string name) => Completion.GetValueOrDefault(name) ?? new CorruptedCompletion(0, 0);

    /// <summary>The invasion reward of difficulty <paramref name="name"/>; none = no silver or infamy.</summary>
    public CorruptedInvaderReward InvaderRewardOf(string name) => InvaderReward.GetValueOrDefault(name) ?? new CorruptedInvaderReward(0, 0);

    /// <summary>Text <paramref name="key"/> with each {name} of <paramref name="values"/> filled in ("" for an unknown key).</summary>
    public string Text(string key, params (string Name, object Value)[] values)
    {
        var text = Texts.GetValueOrDefault(key) ?? DefaultTexts.GetValueOrDefault(key) ?? "";
        foreach (var (name, value) in values)
            text = text.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return text;
    }

    public static CorruptedSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("content", $"{path} missing: built-in corrupted dungeon settings");
            return Default;
        }
        try
        {
            var s = Parse(File.ReadAllText(path), path);
            Log.Info("content", $"corrupted dungeons: completion {string.Join(", ", s.Completion.Select(c => $"{c.Key} {c.Value.Silver}/{c.Value.Fame}"))}"
                + (s.Maps.Count > 0 ? $", {s.Maps.Count} map weight(s) changed" : ""));
            return s;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            Log.Error("content", $"{path}: {e.Message}; built-in corrupted dungeon settings used");
            return Default;
        }
    }

    public static CorruptedSettings Parse(string text, string path)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("not a JSON object");
        var s = Default;
        long Whole(JsonElement v, string key) => v.TryGetInt64(out var n) && n >= 0 ? n : throw new InvalidDataException($"{key}: {v} is not a whole number 0 or more");
        foreach (var p in root.EnumerateObject())
        {
            var v = p.Value;
            switch (p.Name)
            {
                case "Completion":
                    if (v.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{p.Name}: not an object of difficulty -> {{Silver, Fame}}");
                    var completion = new Dictionary<string, CorruptedCompletion>(s.Completion, StringComparer.OrdinalIgnoreCase);
                    foreach (var d in v.EnumerateObject())
                    {
                        var old = completion.GetValueOrDefault(d.Name) ?? new CorruptedCompletion(0, 0);
                        completion[d.Name] = old with
                        {
                            Silver = d.Value.TryGetProperty("Silver", out var silver) ? Whole(silver, $"{p.Name}:{d.Name}:Silver") : old.Silver,
                            Fame = d.Value.TryGetProperty("Fame", out var fame) ? Whole(fame, $"{p.Name}:{d.Name}:Fame") : old.Fame,
                        };
                    }
                    s = s with { Completion = completion };
                    break;
                case "InvaderReward":
                    if (v.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{p.Name}: not an object of difficulty -> {{Silver, Infamy}}");
                    var invader = new Dictionary<string, CorruptedInvaderReward>(s.InvaderReward, StringComparer.OrdinalIgnoreCase);
                    foreach (var d in v.EnumerateObject())
                    {
                        var old = invader.GetValueOrDefault(d.Name) ?? new CorruptedInvaderReward(0, 0);
                        invader[d.Name] = old with
                        {
                            Silver = d.Value.TryGetProperty("Silver", out var silver) ? Whole(silver, $"{p.Name}:{d.Name}:Silver") : old.Silver,
                            Infamy = d.Value.TryGetProperty("Infamy", out var infamy) ? Whole(infamy, $"{p.Name}:{d.Name}:Infamy") : old.Infamy,
                        };
                    }
                    s = s with { InvaderReward = invader };
                    break;
                case "Rolls":
                    s = s with
                    {
                        Rolls = v.ValueKind == JsonValueKind.String && Enum.TryParse<CorruptedRolls>(v.GetString(), ignoreCase: true, out var rolls) && Enum.IsDefined(rolls) ? rolls
                            : throw new InvalidDataException($"{p.Name}: '{v}' is not one of {string.Join(", ", Enum.GetNames<CorruptedRolls>())}"),
                    };
                    break;
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
