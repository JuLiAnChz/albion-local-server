using System.Numerics;
using System.Text.Json;
using AlbionLocal.Photon;

namespace AlbionLocal.Server.Content;

/// <summary>
/// Buildings the server adds to a cluster (ARCHITECTURE.md §5.2 content/spawns, stage 6 SPEC §6c "Server content"):
/// content/spawns/&lt;cluster&gt;.json, e.g. the ownerless public crafting stations of Thetford, which officially stand on
/// player plots. Each entry is a buildings.xml uniquename (its tiles.xml tile of the same name is what the client
/// places), a position and a facing; MapSpawner makes a NewBuilding of it without owner keys, as the NPC buildings. Bad
/// content does not stop the server (principle 6): a file that does not parse, or an entry without a building name or
/// position, is logged and skipped; an unknown building is skipped when the map is populated (with the closest name).
/// </summary>
public sealed class StationSpawns
{
    /// <summary>One added building.</summary>
    public sealed record Entry(string Building, Vector2 Position, float Direction);

    private readonly Dictionary<string, IReadOnlyList<Entry>> _byCluster;

    public StationSpawns(IReadOnlyDictionary<string, IReadOnlyList<Entry>> byCluster) =>
        _byCluster = new Dictionary<string, IReadOnlyList<Entry>>(byCluster, StringComparer.OrdinalIgnoreCase);

    public static StationSpawns None { get; } = new(new Dictionary<string, IReadOnlyList<Entry>>());

    /// <summary>The buildings added to <paramref name="cluster"/> (file order); empty = none.</summary>
    public IReadOnlyList<Entry> For(string cluster) => _byCluster.GetValueOrDefault(cluster) ?? [];

    public int Count => _byCluster.Values.Sum(l => l.Count);

    /// <summary>
    /// Every &lt;cluster&gt;.json of <paramref name="dir"/>: { "Buildings": [ { "Building": "T8_SMELTER", "Position": [x, z],
    /// "Direction": degrees }, … ] }; a missing folder = none.
    /// </summary>
    public static StationSpawns Load(string dir)
    {
        if (!Directory.Exists(dir)) return None;
        var all = new Dictionary<string, IReadOnlyList<Entry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            var cluster = Path.GetFileNameWithoutExtension(path);
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var list = new List<Entry>();
                foreach (var key in doc.RootElement.EnumerateObject())
                {
                    if (key.Name != "Buildings" || key.Value.ValueKind != JsonValueKind.Array)
                    {
                        Log.Warn("content", $"{path}: unknown key '{key.Name}' (or not an array), skipped");
                        continue;
                    }
                    var i = 0;
                    foreach (var e in key.Value.EnumerateArray())
                    {
                        i++;
                        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("Building", out var b) || b.GetString() is not { Length: > 0 } name
                            || !e.TryGetProperty("Position", out var p) || p.ValueKind != JsonValueKind.Array || p.GetArrayLength() != 2)
                        {
                            Log.Warn("content", $"{path}: building #{i} needs \"Building\" and \"Position\": [x, z], skipped");
                            continue;
                        }
                        var direction = e.TryGetProperty("Direction", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetSingle() : 0f;
                        list.Add(new Entry(name, new Vector2(p[0].GetSingle(), p[1].GetSingle()), direction));
                    }
                }
                all[cluster] = list;
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or IOException)
            {
                Log.Error("content", $"{path}: {e.Message}; its buildings are not added");
            }
        }
        if (all.Count > 0) Log.Info("content", $"spawns: {all.Values.Sum(l => l.Count)} added building(s) in {string.Join(", ", all.Keys)}");
        return new StationSpawns(all);
    }
}
