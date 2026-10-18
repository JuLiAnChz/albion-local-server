using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Xml;
using System.Xml.Linq;

namespace AlbionLocal.Server.Data;

/// <summary>
/// cluster/tiles.bin (tiles.xml, 47 MB of XML, 159,078 tiles; SPEC stage 2 §2): what the cluster templates need of it,
/// read in one stream. Kept: the component children of every tile that has any (about 11,000; most tiles are bare
/// visuals), the compoundcontents/t components (they override a tile's own, clusterdump.py GameData.components), and of
/// each compound tile only the children whose components are a kind the resolver places (<see cref="Load"/>'s
/// predicate) with their position, yaw, compound layer (1 LOW, 2 MEDIUM, 3 HIGH, 4 ROAM) and number, plus the count of
/// numbers all its children take (<see cref="CompoundSize"/>; <see cref="SpawnKey"/>), and the walk-blocking cells of
/// every tile whose collision grid has any (<see cref="Collision"/>: 2,833 of the 2,992 grids, used by about 81,600
/// tiles; ~1 MB of cells, the names ~10 MB). Heights, effects and paths are skipped. The client reads the same file into
/// its landscape (a81.kl); here nothing of it is drawn. Built once, by the first cluster load (<see cref="ClusterStore"/>).
/// </summary>
public sealed class TileIndex
{
    private readonly Dictionary<string, XElement[]> _tiles;
    private readonly Dictionary<string, XElement[]> _contents;
    private readonly Dictionary<string, CompoundChild[]> _compounds;
    private readonly Dictionary<string, int> _sizes;
    private readonly Dictionary<string, TileCollision> _collisions;
    private readonly Dictionary<string, TileFootprint> _footprints;
    private readonly Dictionary<string, CompoundChild[]> _compoundColliders;

    private TileIndex(Dictionary<string, XElement[]> tiles, Dictionary<string, XElement[]> contents, Dictionary<string, CompoundChild[]> compounds,
        Dictionary<string, int> sizes, Dictionary<string, TileCollision> collisions, Dictionary<string, TileFootprint> footprints,
        Dictionary<string, CompoundChild[]> compoundColliders)
    {
        _tiles = tiles;
        _contents = contents;
        _compounds = compounds;
        _sizes = sizes;
        _collisions = collisions;
        _footprints = footprints;
        _compoundColliders = compoundColliders;
    }

    /// <summary>
    /// The width, height and offset of tile <paramref name="name"/> (kept for the tiles with components and the bare ones
    /// <see cref="Load"/> was asked for: the snap of a placed resource prefab, <see cref="ResourcePlacement.Snap"/>); null for others.
    /// </summary>
    public TileFootprint? Footprint(string name) => _footprints.TryGetValue(name, out var f) ? f : null;

    /// <summary>Whether <paramref name="name"/> is a compound tile (with kept children or not).</summary>
    public bool IsCompound(string name) => _sizes.ContainsKey(name);

    /// <summary>The walk-blocking cells of tile <paramref name="name"/>; null for a tile without any (or an unknown name).</summary>
    public TileCollision? Collision(string name) => _collisions.GetValueOrDefault(name);

    /// <summary>Tiles with walk-blocking cells.</summary>
    public int TilesWithCollision => _collisions.Count;

    /// <summary>Tile definitions in the file, and those with components.</summary>
    public int TileCount { get; private init; }

    public int TilesWithComponents => _tiles.Count;

    /// <summary>Compound tiles in the file, their children, and the children kept.</summary>
    public int CompoundCount { get; private init; }

    public int CompoundChildren { get; private init; }

    public int KeptChildren { get; private init; }

    public TimeSpan LoadTime { get; private init; }

    /// <summary>
    /// The components of tile <paramref name="name"/> before the template's inline ones: its compoundcontents/t entry
    /// when that has any, else the tile definition's; empty for a bare tile or an unknown name.
    /// </summary>
    public IReadOnlyList<XElement> Components(string name) => ComponentsOf(_contents, _tiles, name);

    private static XElement[] ComponentsOf(Dictionary<string, XElement[]> contents, Dictionary<string, XElement[]> tiles, string name) =>
        contents.TryGetValue(name, out var c) && c.Length > 0 ? c : tiles.GetValueOrDefault(name) ?? [];

    /// <summary>The kept children of compound tile <paramref name="name"/>; empty for a compound without any, or not a compound.</summary>
    public IReadOnlyList<CompoundChild> Compound(string name) => _compounds.GetValueOrDefault(name) ?? [];

    /// <summary>
    /// Stage I bots (bots SPEC §1.6): the children of compound tile <paramref name="name"/> whose tile has walk-blocking cells
    /// (<see cref="Collision"/>), kept apart from <see cref="Compound"/> so that no <see cref="CompoundChild.Number"/> or
    /// <see cref="SpawnKey"/> moves; their Number is 0. Empty for a compound without any, or not a compound. The walls, houses and
    /// fences of a compound, which the bots' walk grid needs (<see cref="WalkGrid"/>).
    /// </summary>
    public IReadOnlyList<CompoundChild> CompoundColliders(string name) => _compoundColliders.GetValueOrDefault(name) ?? [];

    /// <summary>
    /// The numbers all children of compound tile <paramref name="name"/> take, kept or not (<see cref="CompoundChild.Number"/>):
    /// one per component of each, at least one; 0 for an unknown compound.
    /// </summary>
    public int CompoundSize(string name) => _sizes.GetValueOrDefault(name);

    /// <summary>
    /// Streams the file once. <paramref name="keepFootprint"/> names the bare tiles (no components of their own; their
    /// compoundcontents comes last) whose <see cref="Footprint"/> is kept as well: the resource prefabs. <paramref name="keepChild"/> says, by component name, which compound children are worth
    /// keeping; it is asked after the whole file is read, because compoundcontents (the components of most node and
    /// spawn tiles in compounds) comes last. Until then the children are held compactly, names shared.
    /// </summary>
    public static TileIndex Load(GameDataFile file, Func<string, bool> keepChild, Func<string, bool>? keepFootprint = null)
    {
        keepFootprint ??= _ => false;
        var watch = Stopwatch.StartNew();
        var tiles = new Dictionary<string, XElement[]>(StringComparer.Ordinal);
        var contents = new Dictionary<string, XElement[]>(StringComparer.Ordinal);
        var raw = new List<(string Name, CompoundChild[] Children)>();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        string Shared(string s) => names.TryGetValue(s, out var known) ? known : names[s] = s;
        // collisions/c by id, only the grids with a walk-blocking cell; the section comes before the tiles (345319), so a
        // tile's grid is known when the tile is read.
        var grids = new Dictionary<int, byte[]>();
        var collisions = new Dictionary<string, TileCollision>(StringComparer.Ordinal);
        var footprints = new Dictionary<string, TileFootprint>(StringComparer.Ordinal);
        void Footprint(string? name, string? collision, string? width, string? height, string? offset)
        {
            if (name is null || !int.TryParse(collision, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || !grids.TryGetValue(id, out var cells)) return;
            static int Metres(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : 0;
            var (w, h) = (Metres(width), Metres(height));
            var at = (offset ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // A grid that does not fit the tile, or an offset that does not read: no collision (neither seen in 345319).
            if (w * h != cells.Length || at.Length != 2 || !float.TryParse(at[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var ox)
                || !float.TryParse(at[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var oz)) return;
            collisions[name] = new TileCollision(w, h, new Vector2(ox, oz), cells);
        }
        int tileCount = 0, childCount = 0;
        file.Read(reader =>
        {
            reader.MoveToContent();
            if (reader.IsEmptyElement) return 0;
            reader.Read();
            while (!reader.EOF && reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    reader.Read();
                    continue;
                }
                switch (reader.LocalName)
                {
                    case "tile":
                        tileCount++;
                        if (reader.IsEmptyElement)
                        {
                            if (reader.GetAttribute("collision") is { } collision)
                                Footprint(reader.GetAttribute("name"), collision, reader.GetAttribute("width"), reader.GetAttribute("height"), reader.GetAttribute("offset"));
                            if (reader.GetAttribute("name") is { } bare && keepFootprint(bare)
                                && Size(reader.GetAttribute("width"), reader.GetAttribute("height"), reader.GetAttribute("offset")) is { } bareSize)
                                footprints.TryAdd(bare, bareSize);
                            reader.Read();
                            break;
                        }
                        var tile = (XElement)XNode.ReadFrom(reader);
                        Footprint((string?)tile.Attribute("name"), (string?)tile.Attribute("collision"), (string?)tile.Attribute("width"),
                            (string?)tile.Attribute("height"), (string?)tile.Attribute("offset"));
                        if ((string?)tile.Attribute("name") is { } tileName && (tile.HasElements || keepFootprint(tileName)))
                        {
                            if (tile.HasElements) tiles[tileName] = [.. tile.Elements()]; // names are unique (345319)
                            if (Size((string?)tile.Attribute("width"), (string?)tile.Attribute("height"), (string?)tile.Attribute("offset")) is { } size)
                                footprints.TryAdd(tileName, size);
                        }
                        break;
                    case "collisions":
                        foreach (var grid in ((XElement)XNode.ReadFrom(reader)).Elements("c"))
                            if (int.TryParse((string?)grid.Attribute("id"), out var gridId) && TileCollision.WalkBlocking((string?)grid.Attribute("value")) is { } cells)
                                grids[gridId] = cells;
                        break;
                    case "compoundtile":
                        var compound = (XElement)XNode.ReadFrom(reader);
                        var children = new List<CompoundChild>();
                        foreach (var e in compound.Elements())
                        {
                            if (e.Name.LocalName == "tile") children.Add(Child(e, null));
                            else if (e.Name.LocalName == "layer" && (string?)e.Attribute("id") is { } layer)
                                foreach (var t in e.Elements("tile")) children.Add(Child(t, Shared(layer)));
                        }
                        childCount += children.Count;
                        if ((string?)compound.Attribute("name") is { } compoundName) raw.Add((compoundName, [.. children]));
                        break;
                    case "compoundcontents":
                        foreach (var t in ((XElement)XNode.ReadFrom(reader)).Elements("t"))
                            if ((string?)t.Attribute("name") is { } contentName) contents[contentName] = [.. t.Elements()];
                        break;
                    default:
                        reader.Skip(); // heights, effects, paths
                        break;
                }
            }
            return 0;
        });
        // width, height (whole numbers) and offset "x y" (0 0 when absent), as the port's tile_def reads them.
        static TileFootprint? Size(string? width, string? height, string? offset)
        {
            if (!int.TryParse(width, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w)) return null;
            var o = TileMath.V2(offset) ?? Vector2.Zero;
            return new TileFootprint(w, TileMath.Int(height), o.X, o.Y);
        }
        CompoundChild Child(XElement t, string? layer)
        {
            var (x, y, z) = TileMath.V3((string?)t.Attribute("pos"));
            return new CompoundChild(Shared((string?)t.Attribute("name") ?? ""), layer, x, y, z, TileMath.YawOf(t), 0);
        }

        // Every child is numbered, kept or not, so that keeping one more kind later moves no number (SpawnKey).
        var compounds = new Dictionary<string, CompoundChild[]>(StringComparer.Ordinal);
        var sizes = new Dictionary<string, int>(StringComparer.Ordinal);
        var keep = new Dictionary<string, (bool Keep, int Numbers)>(StringComparer.Ordinal);
        (bool Keep, int Numbers) Of(string name)
        {
            if (keep.TryGetValue(name, out var known)) return known;
            var components = ComponentsOf(contents, tiles, name);
            return keep[name] = (components.Any(c => keepChild(c.Name.LocalName)), Math.Max(1, components.Length));
        }
        var kept = 0;
        var compoundColliders = new Dictionary<string, CompoundChild[]>(StringComparer.Ordinal);
        foreach (var (name, children) in raw)
        {
            if (children.Where(c => collisions.ContainsKey(c.Name)).ToArray() is { Length: > 0 } blocking) compoundColliders[name] = blocking;
            var useful = new List<CompoundChild>();
            var number = 0;
            foreach (var child in children)
            {
                var (k, numbers) = Of(child.Name);
                if (k) useful.Add(child with { Number = number });
                number += numbers;
            }
            sizes[name] = number;
            if (useful.Count == 0) continue;
            compounds[name] = [.. useful];
            kept += useful.Count;
        }
        watch.Stop();
        return new TileIndex(tiles, contents, compounds, sizes, collisions, footprints, compoundColliders)
        {
            TileCount = tileCount,
            CompoundCount = raw.Count,
            CompoundChildren = childCount,
            KeptChildren = kept,
            LoadTime = watch.Elapsed,
        };
    }
}

/// <summary>
/// One child tile of a tiles.xml compoundtile, in the compound's frame: (x, y, z) with y the height, yaw in degrees;
/// <see cref="Layer"/> = the compound layer id it is in (null = always placed; else only when the template's
/// compoundtile lists it in activelayers); <see cref="Number"/> = the first of its numbers among the compound's (its
/// k-th component is Number + k): the children before it, kept or not, take one per component, at least one.
/// </summary>
public readonly record struct CompoundChild(string Name, string? Layer, double X, double Y, double Z, double Yaw, int Number);

/// <summary>
/// The walk-blocking cells of a tile, from tiles.xml collisions/c (base64, two bytes per 1 m cell, row by row: a row per
/// metre of the tile's height (z) from offset.z, a cell per metre of its width (x) from offset.x, in the tile's own frame;
/// the attribute collision="id" of the tile names its grid, which many tiles share). The first byte of a cell is a
/// shape (3-6 at box corners: presumably half cells; every blocked cell is taken as full here), the second its flags: bit 0 blocks
/// walking (the bank entrance's pillars 1, the marketplace entrance's building 3; also seen 16, 32, 19, 51, 255).
/// Cell order and bit meaning are read from the data (the marketplace door's spawnpos lies in a free cell only with rows
/// counted from offset.z); the client's reader is not traced. Used for the arrival push (Transfers.Clear) and, rasterised over a
/// whole cluster, for the bots' walk grid (<see cref="WalkGrid"/>).
/// </summary>
public sealed record TileCollision(int Width, int Height, Vector2 Offset, byte[] Flags)
{
    /// <summary>
    /// The flag byte of each cell of collisions/c value <paramref name="base64"/> when any cell blocks walking (flag bit 0);
    /// null for a grid without one or a value that does not decode.
    /// </summary>
    public static byte[]? WalkBlocking(string? base64)
    {
        if (base64 is null) return null;
        byte[] raw;
        try { raw = Convert.FromBase64String(base64); }
        catch (FormatException) { return null; }
        var flags = new byte[raw.Length / 2];
        var any = false;
        for (var i = 0; i < flags.Length; i++)
        {
            flags[i] = raw[2 * i + 1];
            any |= (flags[i] & 1) != 0;
        }
        return any ? flags : null;
    }

    /// <summary>Whether a walk-blocking cell lies closer than <paramref name="radius"/> to <paramref name="local"/> (tile frame, x and z); touching is clear.</summary>
    public bool Blocks(Vector2 local, float radius)
    {
        double x = local.X - Offset.X, z = local.Y - Offset.Y;
        var (c0, c1) = (Math.Max(0, (int)Math.Floor(x - radius)), Math.Min(Width - 1, (int)Math.Floor(x + radius)));
        var (r0, r1) = (Math.Max(0, (int)Math.Floor(z - radius)), Math.Min(Height - 1, (int)Math.Floor(z + radius)));
        for (var r = r0; r <= r1; r++)
            for (var c = c0; c <= c1; c++)
            {
                if ((Flags[r * Width + c] & 1) == 0) continue;
                double dx = Math.Max(Math.Max(c - x, 0), x - (c + 1)), dz = Math.Max(Math.Max(r - z, 0), z - (r + 1));
                if (dx * dx + dz * dz < (double)radius * radius) return true;
            }
        return false;
    }
}
