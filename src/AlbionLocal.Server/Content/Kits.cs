using System.Text.Json;
using AlbionLocal.Photon;
using AlbionLocal.Server.Data;

namespace AlbionLocal.Server.Content;

/// <summary>One item of a kit: a uniquename, the count (one stack, or that many pieces of equipment) and the quality (equipment only).</summary>
public sealed record KitItem(string Name, int Count = 1, int Quality = 1);

/// <summary>A named item list of <see cref="Kits"/>, with its .kit list line; <see cref="Items"/> holds the included kits' items first.</summary>
public sealed record Kit(string Name, string Help, IReadOnlyList<KitItem> Items);

/// <summary>
/// The GM's item kits (.kit, Commands/Builtin/ItemCommands): content/kits.json, ARCHITECTURE.md §5.2. Read at server start; a
/// missing file means no kits. Bad content does not stop the server (principle 6): a file that does not parse is logged and
/// gives no kits, a bad entry (a wrong JSON type included), an unknown item (with the closest known name), a kind the server
/// cannot create, a count outside 1..999 or a quality outside 1..5 or above the item's maxqualitylevel is logged and skipped,
/// and so is an include of an unknown kit or of itself.
/// </summary>
public sealed class Kits
{
    public static Kits None { get; } = new([]);

    private readonly Dictionary<string, Kit> _kits;

    private Kits(IEnumerable<Kit> kits) => _kits = kits.ToDictionary(k => k.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The kits by name, in name order.</summary>
    public IReadOnlyList<Kit> All => [.. _kits.Values.OrderBy(k => k.Name, StringComparer.Ordinal)];

    /// <summary>The kit called <paramref name="name"/> (any case); null = none.</summary>
    public Kit? Find(string name) => _kits.GetValueOrDefault(name);

    /// <summary>
    /// StageL corrupted (SPEC §4.8, Corrupted:MapsOpenDungeons): every kit's corrupted dungeon maps as the invasion tickets that took their
    /// place (<see cref="EventSettings.MoveTickets"/>): T5_CORRUPTED_NONLETHAL_MAP → T5_GVGSEASONREWARD_FAMEBUFF_SHORT, T6_CORRUPTED_LETHAL_MAP →
    /// T6_GVGSEASONREWARD_FAMEBUFF, the counts kept.
    /// </summary>
    public Kits MoveTickets() => new(_kits.Values.Select(k => k with
    {
        Items = [.. k.Items.Select(i => i.Name.ToUpperInvariant() switch
        {
            "T5_CORRUPTED_NONLETHAL_MAP" => i with { Name = "T5_GVGSEASONREWARD_FAMEBUFF_SHORT" },
            "T6_CORRUPTED_LETHAL_MAP" => i with { Name = "T6_GVGSEASONREWARD_FAMEBUFF" },
            _ => i,
        })],
    }));

    /// <param name="items">The item table the kits' items are checked against; null or empty (a hand-made test table) = not checked.</param>
    public static Kits Load(string path, ItemStore? items = null)
    {
        if (!File.Exists(path))
        {
            Log.Info("content", $"{path} missing: no GM kits");
            return None;
        }
        try
        {
            var kits = Parse(File.ReadAllText(path), path, items is { Count: > 0 } ? items : null);
            Log.Info("content", $"GM kits: {string.Join(", ", kits.All.Select(k => $"{k.Name} ({k.Items.Count})"))}");
            return kits;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            Log.Error("content", $"{path}: {e.Message}; no GM kits");
            return None;
        }
    }

    /// <summary>
    /// The file: { "&lt;kit&gt;": { "Help": "…", "Kits": ["&lt;kit&gt;", …], "Items": [item, …] }, … }. An item is a uniquename or
    /// { "Name", "Count", "Quality" }. "Kits" puts the named kits' items before the own ones; "list" is no kit name (.kit list).
    /// </summary>
    private static Kits Parse(string text, string path, ItemStore? items)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("expected an object");
        var raw = new Dictionary<string, (string Help, List<string> Includes, List<KitItem> Items)>(StringComparer.OrdinalIgnoreCase);
        foreach (var kit in doc.RootElement.EnumerateObject())
        {
            if (kit.Name.Length == 0 || kit.Name.Any(char.IsWhiteSpace) || kit.Name.Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warn("content", $"{path}: kit name '{kit.Name}' is empty, has a space or is 'list', skipped");
                continue;
            }
            if (kit.Value.ValueKind != JsonValueKind.Object)
            {
                Log.Warn("content", $"{path}: kit {kit.Name} is not an object, skipped");
                continue;
            }
            var (help, includes, list) = ("", new List<string>(), new List<KitItem>());
            foreach (var key in kit.Value.EnumerateObject())
            {
                // A wrong type ("Help": 3, "Kits": "food", "Items": {}) skips this key only, not every kit (GetString and
                // EnumerateArray throw InvalidOperationException); a bad item inside Items skips that item (Item).
                try
                {
                    switch (key.Name)
                    {
                        case "Help":
                            help = key.Value.GetString() ?? "";
                            break;
                        case "Kits":
                            includes.AddRange([.. key.Value.EnumerateArray().Select(e => e.GetString() ?? "")]); // all or none
                            break;
                        case "Items":
                            var i = 0;
                            foreach (var e in key.Value.EnumerateArray())
                                if (Item(e, $"kit {kit.Name} item #{i++}", path, items) is { } item) list.Add(item);
                            break;
                        default:
                            Log.Warn("content", $"{path}: kit {kit.Name}: unknown key '{key.Name}', skipped");
                            break;
                    }
                }
                catch (InvalidOperationException e)
                {
                    Log.Warn("content", $"{path}: kit {kit.Name}: '{key.Name}' has the wrong type ({e.Message}), skipped");
                }
            }
            raw[kit.Name] = (help, includes, list);
        }
        // Includes one level deep: an included kit's own items, not the kits it includes in turn (no cycles to chase).
        return new Kits(raw.Select(k =>
        {
            var all = new List<KitItem>();
            foreach (var name in k.Value.Includes)
            {
                if (name.Equals(k.Key, StringComparison.OrdinalIgnoreCase) || !raw.TryGetValue(name, out var other))
                    Log.Warn("content", $"{path}: kit {k.Key} includes '{name}', which is {(raw.ContainsKey(name) ? "itself" : "no kit")}; skipped");
                else
                    all.AddRange(other.Items);
            }
            all.AddRange(k.Value.Items);
            return new Kit(k.Key, k.Value.Help, all);
        }));
    }

    private static KitItem? Item(JsonElement e, string where, string path, ItemStore? items)
    {
        KitItem? item;
        try
        {
            item = e.ValueKind == JsonValueKind.String ? new KitItem(e.GetString()!) : e.Deserialize<KitItem>();
        }
        catch (Exception x) when (x is JsonException or InvalidOperationException)
        {
            // "Count": "10", a number for the name, …: this entry only.
            Log.Warn("content", $"{path}: {where} is no item name or {{ Name, Count, Quality }} ({x.Message}), skipped");
            return null;
        }
        string? why = item switch
        {
            null or { Name: null or "" } => "has no item name",
            { Count: < 1 or > Commands.Builtin.ItemCommands.MaxStack } => $"count {item.Count} is not 1..{Commands.Builtin.ItemCommands.MaxStack}",
            { Quality: < 1 or > 5 } => $"quality {item.Quality} is not 1..5",
            _ when items is null => null,
            _ when items.Find(item.Name) is null => $"unknown item {item.Name} (closest: {items.Closest(item.Name)})",
            _ when !items.Find(item.Name)!.CanCreate => $"{item.Name} is a {items.Find(item.Name)!.Kind}, which cannot be created yet",
            _ when items.Find(item.Name)! is { IsEquipment: true } t && item.Quality > t.MaxQuality =>
                $"quality {item.Quality} is above the {t.MaxQuality} of {item.Name} (maxqualitylevel)",
            _ => null,
        };
        if (why is null) return item;
        Log.Warn("content", $"{path}: {where} {why}, skipped");
        return null;
    }
}
