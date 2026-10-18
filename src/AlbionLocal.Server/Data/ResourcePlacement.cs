using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace AlbionLocal.Server.Data;

/// <summary>
/// The client's resource placement (stage 4b, analysis/work/stage4b/SPEC.md §4.4-§4.8, "SPEC" in this file; reference
/// port/placement.py with the verifier's corrections): when it loads an open-world cluster the client puts resource
/// prefabs (trees, rocks, fiber, ore, mob spawn points) on the cluster's resourcenode tiles, deterministically from a
/// date seed (d0.a 0x1808BAA40, called through a81.p 0x180517EF0), and binds a list-39 entry only to a harvestable tile
/// it placed itself, at that exact position (cjb.a 0x180AA1B10, |d| &lt; 0.001). So the server computes the same layout
/// and announces every harvestable it produces (<see cref="ResourceLayout.Harvestables"/>), nothing more.
/// <para>
/// The inputs: the cluster's node tiles (<see cref="NodeTile"/>; template instances sorted by slot id, rolled dyn slots
/// left out, their templates in document order), the preset (<see cref="ResourcePresets"/>), the prefab mapping
/// (<see cref="ResourcePrefabMappings"/>), the cluster's continent and faction, Login key 79 and the seed
/// d0.a(world.xml cluster id, key 57). A cluster that fails validation (<see cref="Validate"/>) gets nothing, in the client
/// and here.
/// </para>
/// </summary>
public static class ResourcePlacement
{
    /// <summary>
    /// d0.a 0x1808BAA40 (SPEC §4.7): pass 1 places the prefabs, a fresh generator per preset entry and Next(b) + a of the
    /// main one as the count; pass 2 puts the fillers on every node left. Returns prefab name → (node, entry) in insertion
    /// order (Dictionary&lt;string, List&lt;d3&gt;&gt;), and the count of each entry.
    /// </summary>
    public static (List<(string Name, List<(NodeTile Node, PresetEntry Entry)> Nodes)> Result, List<(PresetEntry Entry, int Count)> Counts) Place(
        ResourcePreset preset, IReadOnlyDictionary<(string Resource, int Tier), PrefabLists> mapping, int continent, int faction, IReadOnlySet<string>? flags,
        NodeAreas areas, int seed, IReadOnlyDictionary<(int NodeType, string Resource), List<NodeTile>> nodes)
    {
        var result = new List<(string Name, List<(NodeTile, PresetEntry)> Nodes)>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        void Add(string name, NodeTile node, PresetEntry entry)
        {
            if (!index.TryGetValue(name, out var i)) { index[name] = i = result.Count; result.Add((name, [])); }
            result[i].Nodes.Add((node, entry));
        }
        var main = new ClientRandom(seed);
        var remaining = new Dictionary<(int, string), List<NodeTile>>();
        List<NodeTile> Left(PresetEntry e) =>
            remaining.TryGetValue((e.NodeType, e.Resource), out var l) ? l : remaining[(e.NodeType, e.Resource)] = [.. nodes[(e.NodeType, e.Resource)]];
        var counts = new List<(PresetEntry, int)>();
        foreach (var e in preset.Entries)
        {
            var rng = new ClientRandom(seed);
            var count = main.Next(e.B) + e.A;
            counts.Add((e, count));
            var left = Left(e);
            if (mapping.GetValueOrDefault((e.Resource, e.Tier)) is { Prefabs.Count: > 0 } lists)
                Assign(count, lists.Prefabs, continent, faction, flags, areas, left, rng, (name, node) => Add(name, node, e));
        }
        foreach (var e in preset.Entries)
        {
            var rng = new ClientRandom(seed);
            var left = Left(e);
            if (mapping.GetValueOrDefault((e.Resource, e.Tier)) is { Fillers.Count: > 0 } lists && left.Count > 0)
                Assign(left.Count, lists.Fillers, continent, faction, flags, areas, left, rng, (name, node) => Add(name, node, e));
        }
        return (result, counts);
    }

    /// <summary>
    /// d0.a(int, List&lt;bnv&gt;, …) 0x1808B88E0 (SPEC §4.7): <paramref name="count"/> times a random node of
    /// <paramref name="nodes"/> gets the prefab furthest below its quota (weights from bnu.c 0x1806EFA00, penalty placed ×
    /// 1000 / i), skipping prefabs not allowed in a territory or faction fortress the node lies in; a placed node is
    /// swapped with the last and removed. The choice of prefab is a deterministic quota; the generator only picks nodes.
    /// </summary>
    private static void Assign(int count, IReadOnlyList<ResourcePrefab> prefabs, int continent, int faction, IReadOnlySet<string>? flags, NodeAreas areas,
        List<NodeTile> nodes, ClientRandom rng, Action<string, NodeTile> place)
    {
        var active = prefabs.Select(p => p.Active(continent, faction, flags)).ToArray();
        var total = prefabs.Where((_, j) => active[j]).Sum(p => p.Weight);
        var unit = total / 1000;
        // Only an active prefab is divided by it: with none active (an event prefab off) every weight is 0 and nothing is placed.
        if (unit == 0 && active.Any(a => a))
            throw new InvalidDataException($"active prefabs of weight 0 among {prefabs.Count} ({string.Join(", ", prefabs.Take(3).Select(p => p.Name))}): the client divides by zero");
        var w = prefabs.Select((p, j) => active[j] ? p.Weight / unit : 0).ToArray();
        var placed = new long[prefabs.Count];
        for (var i = 0; i < count; i++)
        {
            var at = rng.Next(nodes.Count);
            var node = nodes[at];
            int best = 0;
            long bestScore = 0;
            for (var j = 0; j < prefabs.Count; j++)
            {
                if (w[j] <= 0) continue;
                if (!prefabs[j].AllowedInTerritory && areas.InTerritory(node.X, node.Z)) continue;
                if (!prefabs[j].AllowedInFactionFortress && areas.InFortress(node.X, node.Z)) continue;
                var penalty = i == 0 ? 0 : placed[j] * 1000 / i;
                if (bestScore < w[j] - penalty) (best, bestScore) = (j, w[j] - penalty);
            }
            if (w[best] <= 0) continue;
            place(prefabs[best].Name, node);
            placed[best]++;
            nodes[at] = nodes[^1];
            nodes.RemoveAt(nodes.Count - 1);
        }
    }

    /// <summary>
    /// d0.a 0x1808B9FE0 (SPEC §4.6): the whole cluster gets no nodes when it has none, no preset or mapping, two nodes at
    /// the same (x, z) within 1e-4 (HashSet&lt;Vector2&gt; with a72(1e-4f), hash (int)x ^ (int)z), or fewer nodes of a
    /// (node type, resource) than the preset needs. Returns why it fails, null when it passes.
    /// </summary>
    public static string? Validate(ResourcePreset? preset, object? mapping, IReadOnlyDictionary<(int NodeType, string Resource), List<NodeTile>> nodes)
    {
        if (nodes.Count == 0 || preset is null || mapping is null)
            return $"no {(nodes.Count == 0 ? "resource node tiles" : preset is null ? "resourcedistpresets.xml preset" : "resourceprefabmappings.xml mapping")}";
        var eps2 = 1e-4f * 1e-4f;
        var buckets = new Dictionary<int, List<(float X, float Z)>>();
        foreach (var list in nodes.Values)
            foreach (var n in list)
            {
                var h = (int)n.X ^ (int)n.Z;
                if (!buckets.TryGetValue(h, out var bucket)) buckets[h] = bucket = [];
                foreach (var (qx, qz) in bucket)
                {
                    float dz = n.Z - qz, dx = n.X - qx;
                    if (Math.Abs(dz * dz + dx * dx) <= eps2)
                        return string.Create(CultureInfo.InvariantCulture, $"two resource node tiles at ({n.X}, {n.Z})");
                }
                bucket.Add((n.X, n.Z));
            }
        foreach (var ((nt, res), need) in preset.Required)
            if (nodes.GetValueOrDefault((nt, res)) is not { } list || list.Count < need)
                return $"preset {preset.Name}: only {nodes.GetValueOrDefault((nt, res))?.Count ?? 0} {res} {ResourcePresets.NodeTypes.GetValueOrDefault(nt)} node tiles, it needs {need}";
        return null;
    }

    /// <summary>a9u.q 0x180542C70: the fixed rotation of fmodf(rot, 360): ≤ 45 → 0, ≤ 135 → 1, ≤ 225 → 2, ≤ 315 → 3, else 0.</summary>
    internal static int RotationIndex(float degrees)
    {
        var r = degrees % 360f; // fmodf: the C# float remainder is exact and keeps the sign, as fmodf does
        return r <= 45f ? 0 : r <= 135f ? 1 : r <= 225f ? 2 : r <= 315f ? 3 : 0;
    }

    /// <summary>
    /// a9f.q(a9t, Vector2, FixedRotations) 0x180537F20: a plain prefab tile snaps to whole metres of its corner: offset by
    /// the tile's footprint for the rotation, Math.Round (to even) per axis, offset back, in floats.
    /// </summary>
    internal static Vector2 Snap(TileFootprint tile, float x, float z, int rotation)
    {
        float ox = tile.OffsetX, oy = tile.OffsetY, w = tile.Width, h = tile.Height;
        var (offX, offY) = rotation switch
        {
            1 => (oy, -(w + ox)),
            2 => (-(w + ox), -(h + oy)),
            3 => (-(h + oy), ox),
            _ => (ox, oy),
        };
        float vx = x + offX, vy = z + offY;
        return new Vector2((float)(Math.Round((double)vx, MidpointRounding.ToEven) - offX), (float)(Math.Round((double)vy, MidpointRounding.ToEven) - offY));
    }
}

/// <summary>
/// One resourcenode tile of the cluster (a81.p(a9u, string, string) 0x180519710 → a81.h): world position as floats (y the
/// height) and direction (instance rot + compound yaw + tile yaw, mod 360).
/// </summary>
public readonly record struct NodeTile(float X, float Y, float Z, double Direction);

/// <summary>A tiles.xml tile's width, height and offset (the first definition with a width): what the snap of a plain prefab needs.</summary>
public readonly record struct TileFootprint(int Width, int Height, float OffsetX, float OffsetY);

/// <summary>
/// The territory rectangles (a81.k: monolith and watchtower territorybarrierpos/size, default 140; a81.p_t1db2
/// 0x18051AF90) and faction fortress rectangles (a81.l: factionfortress barrierpos/size) of the cluster, from the critical
/// scene members (the verifier's correction: every watchtower sits there). Only prefabs with allowedinterritory or
/// allowedinfactionfortress = false (Outlands) care. The geometry (barrier position turned with the member, centre and
/// size, an axis-aligned rectangle min ≤ p &lt; max) is inferred (SPEC §7).
/// </summary>
public sealed class NodeAreas
{
    private readonly List<(float X0, float Z0, float X1, float Z1)> _territory = [];
    private readonly List<(float X0, float Z0, float X1, float Z1)> _fortress = [];

    public int Territories => _territory.Count;
    public int Fortresses => _fortress.Count;

    /// <summary>Adds the rectangles of a member's components placed at (<paramref name="x"/>, <paramref name="z"/>) turned by <paramref name="direction"/>.</summary>
    public void Add(IEnumerable<XElement> components, float x, float z, double direction)
    {
        foreach (var c in components)
        {
            var (list, pos, size) = c.Name.LocalName switch
            {
                "monolith" or "watchtower" => (_territory, "territorybarrierpos", "territorybarriersize"),
                "factionfortress" => (_fortress, "barrierpos", "barriersize"),
                _ => (null, "", ""),
            };
            if (list is null) continue;
            var bp = TileMath.V2((string?)c.Attribute(pos)) ?? Vector2.Zero;
            var bs = TileMath.V2((string?)c.Attribute(size)) ?? new Vector2(140, 140);
            var (rx, rz) = TileMath.Rotate(bp.X, bp.Y, direction);
            double cx = x + rx, cz = z + rz;
            list.Add(((float)(cx - bs.X / 2), (float)(cz - bs.Y / 2), (float)(cx + bs.X / 2), (float)(cz + bs.Y / 2)));
        }
    }

    public bool InTerritory(float x, float z) => In(_territory, x, z);

    public bool InFortress(float x, float z) => In(_fortress, x, z);

    private static bool In(List<(float X0, float Z0, float X1, float Z1)> rects, float x, float z) => rects.Any(r => r.X0 <= x && x < r.X1 && r.Z0 <= z && z < r.Z1);
}

/// <summary>
/// One prefab the placement put on a node tile (SPEC §4.8): <see cref="Position"/> is where the client adds it to the
/// landscape (a plain tile snapped, a compound at whole metres), <see cref="Node"/> the node tile it came from.
/// </summary>
public sealed record PlacedPrefab(string Name, string Resource, int Tier, int NodeType, bool Compound, Vector2 Node, float NodeDirection, Vector2 Position, float Height);

/// <summary>
/// The open-world layout the client computes for a cluster (stage 4b): the dynamic-template roll of its dyn slots and
/// the resource placement. <see cref="Harvestables"/> are the live resource nodes (list 39 or, enchanted, event 40);
/// <see cref="SpawnPoints"/> the mob camp and roaming mob prefabs, <see cref="MobSpawnPoints"/> the mob spawn points of
/// all placed prefabs (stage 5). Empty with
/// <see cref="Invalid"/> set when the cluster fails validation (the client places nothing either).
/// </summary>
public sealed record ResourceLayout
{
    public required LayoutSession Session { get; init; }

    /// <summary>d0.a's seed string ("1207" + date ticks) and seed.</summary>
    public string SeedText { get; init; } = "";
    public int Seed { get; init; }

    /// <summary>The dynamic-template seed string (cni.b() + date ticks); "" when the cluster has no roll.</summary>
    public string DynamicSeedText { get; init; } = "";

    /// <summary>Slot → rolled dynamic template (only the slots that got one).</summary>
    public IReadOnlyDictionary<string, DynamicRoll> Rolls { get; init; } = new Dictionary<string, DynamicRoll>();

    public string? Preset { get; init; }
    public string? Mapping { get; init; }

    /// <summary>Why the cluster has no resource nodes (no preset, no node tiles, a failed validation); null = placed.</summary>
    public string? Invalid { get; init; }

    /// <summary>Node tiles per (node type, resource), as the client collected them.</summary>
    public IReadOnlyDictionary<(int NodeType, string Resource), int> NodeCounts { get; init; } = new Dictionary<(int, string), int>();

    /// <summary>Each preset entry and the count pass 1 drew for it.</summary>
    public IReadOnlyList<(PresetEntry Entry, int Count)> Counts { get; init; } = [];

    /// <summary>Every prefab placed, in the client's order (prefab by prefab as the placement first used it).</summary>
    public IReadOnlyList<PlacedPrefab> Placements { get; init; } = [];

    /// <summary>The live resource nodes: each placed tile or compound child with a harvestable component, in placement order.</summary>
    public IReadOnlyList<HarvestableSpawn> Harvestables { get; init; } = [];

    /// <summary>The placements of the MOBCAMP and ROAMINGMOB entries: the mob camps and roaming mobs of the layout.</summary>
    public IEnumerable<PlacedPrefab> SpawnPoints => Placements.Where(p => p.Resource is "MOBCAMP" or "ROAMINGMOB");

    /// <summary>
    /// The mob spawn points of every placed prefab (stage 5 SPEC §5a.1): those of the mob camps and roaming mobs and the
    /// critters of hide and fiber nodes, in placement order, bound to their mobs like the template's own points.
    /// </summary>
    public IReadOnlyList<MobSpawnPoint> MobSpawnPoints { get; init; } = [];

    /// <summary>
    /// StageG dungeons (SPEC §8.1): the random dungeon exit markers of the placed prefabs (the BBS_GROUP_DUNGEON compounds and the
    /// like), placed as their harvestables are; with the template's own in <see cref="ClusterTemplate.DungeonMarkers"/>.
    /// </summary>
    public IReadOnlyList<DungeonExitMarker> DungeonMarkers { get; init; } = [];

    public string Summary() => Invalid is not null ? $"no resource nodes ({Invalid})"
        : string.Create(CultureInfo.InvariantCulture, $"{Harvestables.Count} resource nodes, {SpawnPoints.Count()} mob spawn prefabs, {MobSpawnPoints.Count} mob spawn points ({Preset}/{Mapping}, {Session.Day})");
}
