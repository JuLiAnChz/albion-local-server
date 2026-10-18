using System.Globalization;
using System.Numerics;
using System.Text;

namespace AlbionLocal.Server.Data;

/// <summary>
/// One placed template of a layout (an a9r, SPEC §3 row 9): slot key, template, folder, cell centre, rotation, active layers; stageL mists
/// (mists SPEC §3.2, §6.3): its own height <paramref name="Y"/> (a random dungeon's constant <see cref="DungeonLayout.Height"/>, a mists slot's
/// y) and, in a layout of type 2 (a9i), its room flag bytes <paramref name="Rooms"/> (null = none, written as i32 0).
/// </summary>
public sealed record DungeonTemplate(string Slot, string Template, string Folder, Vector2 Position, float Rotation, IReadOnlyList<string> Layers,
    float Y = DungeonLayout.Height, byte[]? Rooms = null);

/// <summary>
/// What the client's layouts share (a80, read by a80.aq 0x180511780 before the subclass tail; stageL mists SPEC §3.2): the instance id (a80.c),
/// the cluster file name (a80.b), the bounds (a80.d, a80.e), the templates and the clustertype the server resolves the instance as.
/// <see cref="DungeonLayout"/> is type 1 (a9p), <see cref="MistsLayout"/> type 2 (a9i); <see cref="ClusterTemplate.FromLayout"/> takes either.
/// </summary>
public abstract record A80Layout
{
    /// <summary>a80.c, the instance id (= ChangeCluster key 0 = Join key 8).</summary>
    public required string Id { get; init; }

    /// <summary>a80.b, the cluster file name ("&lt;id&gt;_RDG_FR_GRN_T4_HER_NON", "SOLO-YELLOW-COMMON-01_MIS_MI_GRN_T5_UND_MIS").</summary>
    public required string Name { get; init; }

    public required Vector2 Origin { get; init; }
    public required Vector2 Size { get; init; }
    public required IReadOnlyList<DungeonTemplate> Templates { get; init; }

    /// <summary>The display name (a9p.w; the mists' mode display name, not written).</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>The worldsettings clustertype (a9p.x; a mists zone's mode clustertype, ChangeCluster key 4): the PvP and danger rules.</summary>
    public required string ClusterType { get; init; }

    /// <summary>The bytes the client reads (ChangeCluster key 3, SelectCharacter key 4).</summary>
    public abstract byte[] Write();

    /// <summary>
    /// a80.av's common part: the type byte, c, b, d, e, g/h/i (0), the MiniMapData (<paramref name="mini"/>: min, max, height range, base
    /// level, material preset), the template dictionary (a9r.g: key, template, Vector3 with each template's own y, rotation, false, the
    /// layers; with <paramref name="rooms"/>, layout type 2, an i32 count and that many room flag bytes).
    /// </summary>
    protected void WriteHead(BinaryWriter w, byte type, MiniMap mini, bool rooms)
    {
        void S(string? s) => w.Write(s ?? throw new InvalidDataException("a layout string is null (BinaryWriter.Write(null) throws in the client's writer)"));
        w.Write(type);                                        // a80.a layout type
        S(Id); S(Name);                                       // a80.c, a80.b
        w.Write(Origin.X); w.Write(Origin.Y); w.Write(Size.X); w.Write(Size.Y); // a80.d, a80.e
        w.Write(0f); w.Write(0f); w.Write(0f);                // a80.g/h/i
        w.Write(mini.Min.X); w.Write(mini.Min.Y); w.Write(mini.Max.X); w.Write(mini.Max.Y); // MiniMapData (MiniMapData.a(BinaryWriter) field order)
        w.Write(mini.HeightRangeEnabled); w.Write(mini.HeightRange.X); w.Write(mini.HeightRange.Y);
        w.Write(mini.BaseLevelEnabled); w.Write(mini.BaseLevel); S(mini.Preset);
        w.Write(Templates.Count);
        foreach (var t in Templates)                          // Dictionary<string, a9r> in insertion order
        {
            S(t.Slot); S(t.Template);
            w.Write(t.Position.X); w.Write(t.Y); w.Write(t.Position.Y);
            w.Write(t.Rotation); w.Write(false);
            w.Write(t.Layers.Count);
            foreach (var l in t.Layers) S(l);
            if (!rooms) continue;
            w.Write(t.Rooms?.Length ?? 0);                    // a9r.g, type 2 only: the MistDungeonRoomFlags of each room
            if (t.Rooms is { Length: > 0 } r) w.Write(r);
        }
    }

    /// <summary>The client's reader of the common part (a80.aq + a9r.g) after the type byte, for the self-tests.</summary>
    protected static (string Id, string Name, Vector2 Origin, Vector2 Size, MiniMap Mini, List<DungeonTemplate> Templates) ReadHead(BinaryReader r, bool rooms)
    {
        var id = r.ReadString();
        var name = r.ReadString();
        var origin = new Vector2(r.ReadSingle(), r.ReadSingle());
        var size = new Vector2(r.ReadSingle(), r.ReadSingle());
        for (var i = 0; i < 3; i++) r.ReadSingle();
        var min = new Vector2(r.ReadSingle(), r.ReadSingle());
        var max = new Vector2(r.ReadSingle(), r.ReadSingle());
        var heightOn = r.ReadBoolean();
        var height = new Vector2(r.ReadSingle(), r.ReadSingle());
        var baseOn = r.ReadBoolean();
        var mini = new MiniMap(min, max, heightOn, height, baseOn, r.ReadSingle(), r.ReadString());
        var templates = new List<DungeonTemplate>();
        for (var n = r.ReadInt32(); n > 0; n--)
        {
            var slot = r.ReadString();
            var template = r.ReadString();
            var (x, y, z) = (r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            var rot = r.ReadSingle();
            r.ReadBoolean();
            var layers = new List<string>();
            for (var m = r.ReadInt32(); m > 0; m--) layers.Add(r.ReadString());
            byte[]? flags = null;
            if (rooms && r.ReadInt32() is var count and > 0) flags = r.ReadBytes(count);
            templates.Add(new DungeonTemplate(slot, template, "", new Vector2(x, z), rot, layers, y, rooms ? flags ?? [] : null));
        }
        return (id, name, origin, size, mini, templates);
    }
}

/// <summary>The client's MiniMapData (Albion.Common.LandScape.MiniMap): bounds, height range, base level and the material preset ("Mists" in a mists zone).</summary>
public sealed record MiniMap(Vector2 Min, Vector2 Max, bool HeightRangeEnabled, Vector2 HeightRange, bool BaseLevelEnabled, float BaseLevel, string Preset)
{
    /// <summary>The client generator's defaults (a random dungeon floor's): all zero, the preset "" (null in the ctor, never written as null).</summary>
    public static readonly MiniMap Empty = new(Vector2.Zero, Vector2.Zero, false, Vector2.Zero, false, 0, "");
}

/// <summary>
/// The layout of one random dungeon floor (analysis/work/stageG/dungeons/SPEC.md, "SPEC" in this file, §3): the a80 of layout
/// type 1 (a9p) the client reads from ChangeCluster key 3 / SelectCharacter key 4 (a80.aq 0x180511780) and builds the floor
/// from, without world.xml. The a9p fields keep the client's names (w … al). <see cref="Write"/> is the client's writer
/// (a80.av 0x180511F70): BinaryWriter, UTF-8 strings with a 7-bit length, little-endian int32/float, 1-byte bools, the
/// MiniMapData left at its defaults, Guids as "D" strings (never "": the reader's Guid(string) throws on it).
/// Reference: rdgen.py write_a9p / read_a80 and golden_a9p.json (517 bytes).
/// </summary>
public sealed record DungeonLayout : A80Layout
{
    /// <summary>a80.d height of every a9r (d7.a = -40).</summary>
    public const float Height = -40;

    /// <summary>a9p.y floor (1-based), a9p.z floor count, a9p.aa.</summary>
    public byte Floor { get; init; } = 1;
    public byte Floors { get; init; } = 1;
    public bool Aa { get; init; }

    /// <summary>a9p.ad/ab/ac: the up exit's target exit Guid, target cluster and its file name ("" for a world.xml target).</summary>
    public Guid UpExit { get; init; }
    public required string UpCluster { get; init; }
    public string UpName { get; init; } = "";

    /// <summary>a9p.ag/ae/af: the same for the down exit.</summary>
    public Guid DownExit { get; init; }
    public required string DownCluster { get; init; }
    public string DownName { get; init; } = "";

    /// <summary>a9p.ah/ai: the entrance cluster and position.</summary>
    public required string Entrance { get; init; }
    public Vector2 EntrancePosition { get; init; }

    /// <summary>a9p.aj Subtypes (1 Solo, 2 Veteran, …), a9p.ak the generator name, a9p.al CustomClusterFlags.</summary>
    public byte Subtype { get; init; } = 1;
    public string Generator { get; init; } = "DEFAULT";
    public byte Flags { get; init; }

    /// <summary>The bytes the client reads (a80.av order). Throws on a value its writer would throw on (a Guid written as "" cannot happen: Guids are typed).</summary>
    public override byte[] Write()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, new UTF8Encoding(false), leaveOpen: true))
        {
            void S(string? s) => w.Write(s ?? throw new InvalidDataException("a layout string is null (BinaryWriter.Write(null) throws in the client's writer)"));
            // a80.a layout type 1 = a9p; the MiniMapData at the client generator's defaults; every a9r at Height (DungeonTemplate.Y).
            WriteHead(w, 1, MiniMap.Empty, rooms: false);
            S(DisplayName); S(ClusterType); w.Write(Floor); w.Write(Floors); w.Write(Aa); // a9p.ke: w, x, y, z, aa
            S(UpExit.ToString()); S(UpCluster); S(UpName);       // ad, ab, ac
            S(DownExit.ToString()); S(DownCluster); S(DownName); // ag, ae, af
            S(Entrance); w.Write(EntrancePosition.X); w.Write(EntrancePosition.Y); // ah, ai
            w.Write(Subtype); S(Generator); w.Write(Flags);       // aj, ak, al
        }
        return ms.ToArray();
    }

    /// <summary>
    /// The client's reader (a80.aq + a9r.g + a9p.kf, rdgen.read_a80), for the self-test and to rebuild a cached layout: type 1
    /// only. A Guid that does not parse, a truncated or a longer buffer throw <see cref="InvalidDataException"/>, as they would
    /// make the client's reader throw.
    /// </summary>
    public static DungeonLayout Read(byte[] bytes)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(bytes), new UTF8Encoding(false));
            if (r.ReadByte() != 1) throw new InvalidDataException("not a layout of type 1 (a9p)");
            var (id, name, origin, size, _, templates) = ReadHead(r, rooms: false);
            var layout = new DungeonLayout
            {
                Id = id, Name = name, Origin = origin, Size = size, Templates = templates,
                DisplayName = r.ReadString(), ClusterType = r.ReadString(), Floor = r.ReadByte(), Floors = r.ReadByte(), Aa = r.ReadBoolean(),
                UpExit = Guid.Parse(r.ReadString()), UpCluster = r.ReadString(), UpName = r.ReadString(),
                DownExit = Guid.Parse(r.ReadString()), DownCluster = r.ReadString(), DownName = r.ReadString(),
                Entrance = r.ReadString(), EntrancePosition = new Vector2(r.ReadSingle(), r.ReadSingle()), Subtype = r.ReadByte(), Generator = r.ReadString(),
            };
            if (r.BaseStream.Position < r.BaseStream.Length) layout = layout with { Flags = r.ReadByte() }; // a9p.al only when bytes remain
            if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("trailing bytes");
            return layout;
        }
        catch (Exception e) when (e is EndOfStreamException or FormatException or IOException)
        {
            throw new InvalidDataException($"layout does not parse: {e.Message}", e);
        }
    }

    /// <summary>The prefix of a random dungeon instance id (a4l: "@" + ClusterInstanceType 7 + "@").</summary>
    public const string InstancePrefix = "@RANDOMDUNGEON@";

    /// <summary>a4l.b(Guid) 0x180900CF0: "@RANDOMDUNGEON@" + the Guid (lower case, hyphens; no '_' or '.', which the name parse splits at).</summary>
    public static string InstanceId(Guid id) => InstancePrefix + id.ToString();

    public static bool IsInstance(string? cluster) => cluster?.StartsWith(InstancePrefix, StringComparison.Ordinal) == true;

    /// <summary>The faction tokens of the cluster name (SPEC §4).</summary>
    public static string FactionToken(string faction) => faction.ToUpperInvariant() switch
    {
        "HERETIC" => "HER", "KEEPER" => "KPR", "MORGANA" => "MOR", "UNDEAD" => "UND", "AVALON" => "AVA", _ => "NON",
    };

    /// <summary>a80.b (bgv ClusterNameInformation.b with ClusterType 17, continent 0): Forest/Green, or Dragon/none for the dragon templates.</summary>
    public static string ClusterName(string id, bool dragon, int tier, string faction) =>
        string.Create(CultureInfo.InvariantCulture, $"{id}_RDG_{(dragon ? "DR_NON" : "FR_GRN")}_T{tier}_{FactionToken(faction)}_NON");
}

/// <summary>
/// The random dungeon generator: a port of the client's own (Albion.Common d5/d6/d7, analysis/work/stageN/dgen/SPEC.md, "DGEN" in
/// this file; stageG SPEC §5.6). A depth-first walk with backtracking from the Start: Basic rooms (corners, straights, T and cross
/// junctions) spend the floor's path budget (DungeonLength pathlength minus the end room's pathsize), the floor's highlights are
/// spread along it by the client's spacing (budget left over the highlights left + 1), a dead-end highlight hangs off a T (or two off
/// a cross) as a side branch, a through highlight sits on the path; the floor ends in its Exit. The last floor's end-boss highlight
/// (endbosshighlight; the client's end room, which holds no exit) is its last highlight, next to the Exit. Templates of any size (multi-cell highlights and starts) by their pathdata
/// doors (TemplateMetaInfo.f), no two on one cell. Then backdrops (one info, random quarter turns) on every free 8-neighbour cell of a
/// room and the master at the Start, the layers of §5.4. Deterministic for a seed (System.Random, not the client's Mersenne Twister).
/// Throws <see cref="InvalidDataException"/> when the data has no template for a part or no layout fits.
/// </summary>
public static class DungeonGenerator
{
    /// <summary>TemplateMetaInfo.BUILDINGBLOCKSIZE: one cell is 80 m.</summary>
    public const float Block = 80;

    /// <summary>MetaDirectionFlag: Forward +Z, Right +X, Back -Z, Left -X.</summary>
    public const int F = 1, R = 2, B = 4, L = 8;

    private static int Opposite(int d) => d switch { F => B, B => F, R => L, _ => R };

    private static Vector2 DirVector(int d) => d switch { F => new(0, 1), R => new(1, 0), B => new(0, -1), _ => new(-1, 0) };

    /// <summary>SPEC §5.2: the mask turned r quarter turns clockwise (F → R → B → L).</summary>
    public static int RotateMask(int m, int r)
    {
        r = ((r % 4) + 4) % 4;
        return r == 0 ? m : ((m << r) | (m >> (4 - r))) & 15;
    }

    /// <summary>A local offset turned r quarter turns clockwise (x' = x cos a + z sin a, z' = −x sin a + z cos a; +Z → +X), exact.</summary>
    public static Vector2 Turn(Vector2 v, int r) => (((r % 4) + 4) % 4) switch
    {
        0 => v, 1 => new Vector2(v.Y, -v.X), 2 => new Vector2(-v.X, -v.Y), _ => new Vector2(-v.Y, v.X),
    };

    /// <summary>The quarter turns that take direction <paramref name="from"/> to <paramref name="to"/> (bh3.a_6), -1 when none.</summary>
    private static int TurnsTo(int from, int to)
    {
        for (var r = 0; r < 4; r++)
            if (RotateMask(from, r) == to) return r;
        return -1;
    }

    /// <summary>
    /// SPEC §5.3 (corrected): whether a Start or Exit info's dungeonlevelusage fits floor <paramref name="floor"/> of
    /// <paramref name="floors"/>: none = any; floor 1 → First/FirstAndOthers; a middle floor → Others/FirstAndOthers/LastAndOthers;
    /// the last floor (of more than one) → Last/LastAndOthers.
    /// </summary>
    public static bool UsageFits(string? usage, int floor, int floors)
    {
        if (string.IsNullOrEmpty(usage)) return true;
        string[] fits = floor == 1 ? ["First", "FirstAndOthers"] : floor == floors ? ["Last", "LastAndOthers"] : ["Others", "FirstAndOthers", "LastAndOthers"];
        return fits.Contains(usage, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// d6.ao_2 (0x1808C1480): the infos of a Start/Exit pool for a floor: the ones of templateusage Enforce whose dungeonlevelusage
    /// fits (<see cref="UsageFits"/>); without any, all of them but those of templateusage Exclude whose dungeonlevelusage fits.
    /// </summary>
    public static List<TemplateMetaInfo> ForFloor(IEnumerable<TemplateMetaInfo> infos, int floor, int floors)
    {
        var all = infos.ToList();
        bool Fits(TemplateMetaInfo i, string usage) => string.Equals(i.Usage, usage, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(i.LevelUsage)
            && UsageFits(i.LevelUsage, floor, floors);
        var enforce = all.Where(i => Fits(i, "Enforce")).ToList();
        return enforce.Count > 0 ? enforce : [.. all.Where(i => !Fits(i, "Exclude"))];
    }

    /// <summary>A weighted pick (rdgen wpick): the first item when every weight is 0.</summary>
    public static T Pick<T>(Random rng, IReadOnlyList<(T Item, double Weight)> items)
    {
        if (items.Count == 0) throw new InvalidDataException("nothing to pick from");
        var total = items.Sum(i => Math.Max(0, i.Weight));
        if (total <= 0) return items[0].Item;
        var v = rng.NextDouble() * total;
        foreach (var (item, weight) in items)
        {
            v -= Math.Max(0, weight);
            if (v < 0) return item;
        }
        return items[^1].Item;
    }

    /// <summary>What the generator draws once per dungeon (SPEC §5.3 steps 1-2): faction subset, environment and its subset, flag, floors and their path lengths.</summary>
    public sealed record Plan(DungeonGeneratorInfo Generator, string Subset, string Environment, string EnvironmentSubset, string Flag, IReadOnlyList<int> PathLengths)
    {
        /// <summary>The DungeonLength highlightCount of each floor: so many highlights the floor places (DGEN §3); empty = none.</summary>
        public IReadOnlyList<int> Highlights { get; init; } = [];
    }

    /// <summary>
    /// Steps 1-2 for <paramref name="generator"/>: the subset, environment and its subset by weight; the flag (the generator's,
    /// else solo for Subtypes Solo/Event_Solo, veteran otherwise); the Size by weight (its floors, or exactly <paramref name="floors"/>
    /// when that is not 0) and a DungeonLength per floor (the deepest Floor level up to it for deeper floors).
    /// </summary>
    public static Plan Draw(DungeonGeneratorInfo generator, byte subtype, Random rng, int floors = 0)
    {
        var subset = generator.Subsets.Count > 0 ? Pick(rng, generator.Subsets) : "";
        var env = generator.Environments.Count > 0 ? Pick(rng, [.. generator.Environments.Select(e => (e, e.Weight))]) : ("indoor", 1, []);
        var envSubset = env.Subsets.Count > 0 ? Pick(rng, env.Subsets) : "";
        var flag = generator.Flags.FirstOrDefault() ?? (subtype is 1 or 4 ? "solo" : "veteran");
        var (lengths, highlights) = (new List<int>(), new List<int>());
        if (generator.Sizes.Count > 0)
        {
            var size = Pick(rng, [.. generator.Sizes.Select(s => (s, s.Weight))]);
            var count = floors > 0 ? floors : size.Floors;
            for (var f = 1; f <= count; f++)
            {
                var levels = size.Lengths.Keys.Where(k => k <= f).DefaultIfEmpty(size.Lengths.Keys.DefaultIfEmpty(1).Min()).Max();
                var options = size.Lengths.GetValueOrDefault(levels) ?? [];
                var length = options.Count > 0 ? Pick(rng, [.. options.Select(o => (o, o.Weight))]) : (PathLength: 6, Highlights: 0, Weight: 1.0);
                lengths.Add(Math.Max(2, length.PathLength));
                highlights.Add(length.Highlights);
            }
        }
        while (lengths.Count < Math.Max(1, floors))
        {
            lengths.Add(lengths.Count > 0 ? lengths[^1] : 6);
            highlights.Add(0);
        }
        return new Plan(generator, subset, env.Name, envSubset, flag, lengths) { Highlights = highlights };
    }

    /// <summary>The links of a floor's two exits (a9p ab/ac/ad and ae/af/ag) and the entrance (ah/ai).</summary>
    public sealed record Links(string UpCluster, string UpName, Guid UpExit, string DownCluster, string DownName, Guid DownExit, string Entrance, Vector2 EntrancePosition);

    /// <summary>A door of a template info (bh4): its index, the offset from the template's centre to the door (an edge midpoint) and its side.</summary>
    public sealed record Door(int Index, Vector2 Offset, int Dir);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TemplateMetaInfo, DoorSet> DoorCache = new();

    private sealed record DoorSet(IReadOnlyList<Door> Doors, bool Symmetrical);

    /// <summary>
    /// TemplateMetaInfo.f (0x1806B1B80): the doors of an info from its pathdata (one MetaDirectionFlag byte per cell, row-major, row 0
    /// at -Z): per column the Back door of row 0 and the Forward door of the last row, then per row the Left door of column 0 and the
    /// Right door of the last column; inner sides are no doors.
    /// </summary>
    public static IReadOnlyList<Door> Doors(TemplateMetaInfo info) => DoorInfo(info).Doors;

    /// <summary>TemplateMetaInfo.Symmetrical: a 1x1 cross or straight, or a larger info whose doors sit only on the middle of odd sides in pairs.</summary>
    public static bool Symmetrical(TemplateMetaInfo info) => DoorInfo(info).Symmetrical;

    private static DoorSet DoorInfo(TemplateMetaInfo info) => DoorCache.GetValue(info, i =>
    {
        var (w, h, p) = (i.Width, i.Height, i.PathData);
        var doors = new List<Door>();
        if (w < 1 || h < 1 || p.Length < w * h) return new DoorSet(doors, false);
        var c = new Vector2(w, h) * (Block / 2);
        uint cols = 0;
        var rows = 0;
        for (var x = 0; x < w; x++)
        {
            var n = 0;
            if ((p[x] & B) != 0) { doors.Add(new Door(doors.Count, new Vector2((x + 0.5f) * Block, 0) - c, B)); n++; }
            if ((p[(h - 1) * w + x] & F) != 0) { doors.Add(new Door(doors.Count, new Vector2((x + 0.5f) * Block, h * Block) - c, F)); n++; }
            if (w > 1 && (w & 1) != 0 && n != 0) cols = x == w / 2 && n == 2 ? (cols == 0 ? 1 : cols) : uint.MaxValue;
        }
        for (var y = 0; y < h; y++)
        {
            var n = 0;
            if ((p[y * w] & L) != 0) { doors.Add(new Door(doors.Count, new Vector2(0, (y + 0.5f) * Block) - c, L)); n++; }
            if ((p[(y + 1) * w - 1] & R) != 0) { doors.Add(new Door(doors.Count, new Vector2(w * Block, (y + 0.5f) * Block) - c, R)); n++; }
            if (h > 1 && (h & 1) != 0 && n != 0) rows = y == h / 2 && n == 2 ? (rows == 0 ? 1 : rows) : -1;
        }
        var sym = w == 1 && h == 1 ? doors.Count == 4 || doors.Count == 2 && doors[0].Dir == Opposite(doors[1].Dir)
            : rows == 1 ? cols < 2 : rows == 0 && cols == 1;
        return new DoorSet(doors, sym);
    });

    /// <summary>d5.h: the chance a non-symmetrical Basic room is turned to lead north (d5.a_ta12) rather than at random.</summary>
    public const double NorthChance = 0.7;

    /// <summary>Rooms tried, and rooms on one floor, after which one run gives up (the client has no cap; its data never needs one).</summary>
    public const int MaxTries = 5000, MaxRooms = 64;

    /// <summary>One placed template of a floor (d4): info, centre, quarter turns, the door it was entered by.</summary>
    private sealed record Room(TemplateMetaInfo Info, Vector2 Center, int Turns, Vector2 Entry, bool Recorded);

    /// <summary>The cells of a placed info (keys on the 40 m lattice: neighbouring cell centres differ by 2).</summary>
    private static IEnumerable<(int X, int Z)> Cells(TemplateMetaInfo info, Vector2 center, int turns)
    {
        var size = (turns & 1) == 0 ? new Vector2(info.Width, info.Height) : new Vector2(info.Height, info.Width);
        var min = center - size * (Block / 2) + new Vector2(Block / 2);
        for (var x = 0; x < (int)size.X; x++)
            for (var z = 0; z < (int)size.Y; z++)
                yield return ((int)MathF.Round((min.X + x * Block) / (Block / 2)), (int)MathF.Round((min.Y + z * Block) / (Block / 2)));
    }

    private sealed class Abort : Exception;

    /// <summary>The client's path walk (d5): its state and steps; see the class summary and DGEN §2.</summary>
    private sealed class Walk(Random rng, IReadOnlyList<TemplateMetaInfo> basics, IReadOnlyList<TemplateMetaInfo> highlights, TemplateMetaInfo end, bool lastFloor, int budget)
    {
        public readonly List<Room> Rooms = [];
        private readonly HashSet<(int, int)> _taken = [];
        private readonly List<Vector2> _doors = [];                          // d5.d: the entry doors of the rooms on the path
        private readonly Stack<(int Spacing, int Walked)> _spacing = new();  // d5.c
        private int _placed;                                                  // d5.f: highlights placed
        private int _budget = budget;                                         // d5.g: path budget left
        private int _tries;

        private int HighlightsLeft => highlights.Count - _placed;

        private static bool DeadEnd(TemplateMetaInfo i) => i.Type == "Highlight" && Doors(i).Count == 1;

        /// <summary>
        /// d5.a_7: the path a highlight waits for: (budget − the highlights' own path) / (highlights left + 1), rounded up. An end boss
        /// among them (the server's last floor, DGEN §4) does not count: alone, it waits for the whole budget.
        /// </summary>
        private int Spacing()
        {
            var rest = _budget - highlights.Skip(_placed).Sum(h => h.PathSize);
            var n = highlights.Skip(_placed).Count(h => !h.EndBoss) + 1;
            return rest % n == 0 ? rest / n : rest / n + 1;
        }

        private bool Fits(TemplateMetaInfo info, Vector2 center, int turns) => Cells(info, center, turns).All(c => !_taken.Contains(c));

        /// <summary>d5.a_8 and the push of d5.a_4/a_5/a_ta16: the room taken, its entry door, the budget and the highlights' spacing.</summary>
        private Room Push(TemplateMetaInfo info, Vector2 center, int turns, Vector2 entry)
        {
            if (++_tries > MaxTries || Rooms.Count >= MaxRooms) throw new Abort();
            var room = new Room(info, center, turns, entry, !(end.EndBoss && ReferenceEquals(info, end)) && !DeadEnd(info));
            Rooms.Add(room);
            foreach (var c in Cells(info, center, turns)) _taken.Add(c);
            if (room.Recorded) _doors.Add(entry);
            if (info.Type is "Basic" or "Highlight") _budget -= info.PathSize;
            var (spacing, walked) = _spacing.Count > 0 ? _spacing.Peek() : (Spacing(), 0);
            if (info.Type == "Highlight" && !ReferenceEquals(info, end))
            {
                _placed++;
                (spacing, walked) = (Spacing(), 0);
            }
            else if (info.Type == "Basic") walked += info.PathSize;
            _spacing.Push((spacing, walked));
            return room;
        }

        /// <summary>d5.d: the last room taken back.</summary>
        private void Pop()
        {
            var room = Rooms[^1];
            Rooms.RemoveAt(Rooms.Count - 1);
            foreach (var c in Cells(room.Info, room.Center, room.Turns)) _taken.Remove(c);
            if (_doors.Count > 0 && !DeadEnd(room.Info)) _doors.RemoveAt(_doors.Count - 1);
            if (room.Info.Type is "Basic" or "Highlight") _budget += room.Info.PathSize;
            _spacing.Pop();
            if (room.Info.Type == "Highlight" && !ReferenceEquals(room.Info, end)) _placed--;
        }

        private static (Vector2 At, int Dir) Through(Room room, Door d) => (room.Center + Turn(d.Offset, room.Turns), RotateMask(d.Dir, room.Turns));

        /// <summary>d5.a: the Start at the origin, its door turned to the entrance direction, then the walk.</summary>
        public bool Run(TemplateMetaInfo start, int entrance)
        {
            var door = Doors(start)[0];
            var room = Push(start, Vector2.Zero, Math.Max(0, TurnsTo(door.Dir, entrance)), Vector2.Zero);
            var (at, dir) = Through(room, door);
            return Step(at, dir, room);
        }

        /// <summary>d5.a_ta16: what comes through the door at <paramref name="at"/> facing <paramref name="dir"/> after <paramref name="prev"/>.</summary>
        private bool Step(Vector2 at, int dir, Room prev)
        {
            var prevType = prev.Info.Type;
            if (_budget < 1)
            {
                if (HighlightsLeft < 1)
                {
                    if (prevType == "Exit") return true;
                    if (lastFloor && prevType == "Highlight") return Basic(at, dir);
                    return Single(end, at, dir);
                }
                return prevType is "Highlight" or "Start" ? Basic(at, dir) : Highlight(at, dir);
            }
            if (HighlightsLeft < 1) return Basic(at, dir);
            var (spacing, walked) = _spacing.Peek();
            return spacing - walked > 0 || prevType is "Highlight" or "Start" ? Basic(at, dir) : Highlight(at, dir);
        }

        /// <summary>
        /// The next highlight through the door (a dead end fails here as in the client, which then backtracks into a T). The server's
        /// end boss as a dead end: a T at once (boss on one side, the way on to the Exit on the other), else a Basic.
        /// </summary>
        private bool Highlight(Vector2 at, int dir)
        {
            var next = highlights[_placed];
            if (next.EndBoss && DeadEnd(next)) return From(at, dir, [.. basics.Where(b => Doors(b).Count == 3)]) || Basic(at, dir);
            return From(at, dir, [next]);
        }

        /// <summary>Whether the next highlight is due: the budget spent or its spacing walked.</summary>
        private bool Due => _budget < 1 || _spacing.Count > 0 && _spacing.Peek() is var (spacing, walked) && spacing - walked <= 0;

        /// <summary>d5.a_1: a Basic room; after a straight run (the door in line with the last entry) the turns and junctions first.</summary>
        private bool Basic(Vector2 at, int dir)
        {
            if (_doors.Count > 1 && (at - _doors[^1]) is var d && (d.X == 0 || d.Y == 0))
            {
                var straight = basics.Where(b => Symmetrical(b) && Doors(b).Count < 3).ToList();
                return From(at, dir, [.. basics.Except(straight)]) || From(at, dir, straight);
            }
            return From(at, dir, basics);
        }

        private List<T> Shuffled<T>(IEnumerable<T> items)
        {
            var list = items.ToList();
            if (list.Count > 1) rng.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list));
            return list;
        }

        /// <summary>d5.a_3: the infos in random order, each by its doors (one for a symmetrical info; a Basic leading north at 70 %).</summary>
        private bool From(Vector2 at, int dir, IReadOnlyList<TemplateMetaInfo> infos)
        {
            foreach (var info in Shuffled(infos))
            {
                var doors = Doors(info);
                if (doors.Count == 0) continue;
                IEnumerable<Door> entries = Symmetrical(info) ? [doors[rng.Next(doors.Count)]]
                    : info.Type == "Basic" && rng.NextDouble() < NorthChance ? North(doors, dir)
                    : Shuffled(doors);
                foreach (var e in entries)
                    if (Place(info, e, at, dir)) return true;
            }
            return false;
        }

        /// <summary>d5.a_ta12: the entry doors by how far north the room's other doors lead once turned (the most first).</summary>
        private static List<Door> North(IReadOnlyList<Door> doors, int dir) =>
        [
            .. doors.Select(e => (e, Score: TurnsTo(e.Dir, Opposite(dir)) is var r and >= 0
                    ? doors.Where(o => o != e).Sum(o => Vector2.Dot(new Vector2(0, 1), DirVector(RotateMask(o.Dir, r)))) : float.MinValue))
                .OrderByDescending(x => x.Score).Select(x => x.e),
        ];

        /// <summary>d5.a_5 / the end of d5.a_ta16: an info entered by its first door (a dead-end highlight, the end room); false when it does not fit.</summary>
        private bool Single(TemplateMetaInfo info, Vector2 at, int dir)
        {
            var doors = Doors(info);
            if (doors.Count == 0) return false;
            var r = TurnsTo(doors[0].Dir, Opposite(dir));
            if (r < 0) return false;
            var center = at - Turn(doors[0].Offset, r);
            if (!Fits(info, center, r)) return false;
            Push(info, center, r, at);
            return true;
        }

        /// <summary>
        /// d5.a_4: <paramref name="info"/> entered by <paramref name="entry"/>. A T takes the next highlight as a dead end on one side and
        /// goes on through the other, a cross two of them; a dead-end highlight cannot stand on the path; any other room goes on
        /// through its first other door. Every failure takes the room back.
        /// </summary>
        private bool Place(TemplateMetaInfo info, Door entry, Vector2 at, int dir)
        {
            var r = TurnsTo(entry.Dir, Opposite(dir));
            if (r < 0) return false;
            var center = at - Turn(entry.Offset, r);
            if (!Fits(info, center, r)) return false;
            var due = Due;
            var room = Push(info, center, r, at);
            var doors = Doors(info);
            bool Go(Door d)
            {
                var (a, dd) = Through(room, d);
                return HighlightsLeft < 1 ? Step(a, dd, room) : Basic(a, dd);
            }
            if (info.Type == "Basic" && doors.Count is 3 or 4)
            {
                var need = doors.Count - 2;
                // The server's end boss hangs only once it is due, so it comes last before the Exit.
                if (HighlightsLeft >= need && Enumerable.Range(_placed, need).All(k => Doors(highlights[k]).Count <= 1 && (!highlights[k].EndBoss || due)))
                {
                    var (first, second) = (highlights[_placed], need == 2 ? highlights[_placed + 1] : null);
                    foreach (var o1 in Shuffled(doors.Where(d => d != entry)))
                    {
                        var (a1, d1) = Through(room, o1);
                        if (!Single(first, a1, d1)) continue;
                        if (second is null)
                        {
                            if (Go(doors.First(d => d != entry && d != o1))) return true;
                        }
                        else
                            foreach (var o2 in doors.Where(d => d != entry && d != o1))
                            {
                                var (a2, d2) = Through(room, o2);
                                if (!Single(second, a2, d2)) continue;
                                if (Go(doors.First(d => d != entry && d != o1 && d != o2))) return true;
                                Pop();
                            }
                        Pop();
                    }
                }
                Pop();
                return false;
            }
            if (!DeadEnd(info) && doors.FirstOrDefault(d => d != entry) is { } next)
            {
                var (a, d) = Through(room, next);
                if (Step(a, d, room)) return true;
            }
            Pop();
            return false;
        }
    }

    /// <summary>
    /// d6 (au/ar/aq/ao_ta3f): the floor's highlights: <paramref name="count"/> drawn by the generator's HighlightCategories weights
    /// (the default category = the infos without one), each category's infos in a shuffled queue so none repeats before its category
    /// runs dry; the end-boss highlights stay for the last floor's end.
    /// </summary>
    private static List<TemplateMetaInfo> DrawHighlights(IReadOnlyList<TemplateMetaInfo> infos, DungeonGeneratorInfo generator, int count, Random rng)
    {
        var pool = infos.Where(i => i.Type == "Highlight" && !i.EndBoss && Doors(i).Count > 0).ToList();
        var result = new List<TemplateMetaInfo>();
        if (count <= 0 || pool.Count == 0) return result;
        var byCategory = pool.GroupBy(i => i.Category ?? "").ToDictionary(g => g.Key, g => g.ToList());
        var weights = generator.Categories.Select(c => (Key: c.Name ?? "", c.Weight)).Where(c => c.Weight > 0 && byCategory.ContainsKey(c.Key)).ToList();
        if (weights.Count == 0) weights = [.. byCategory.Keys.Select(k => (Key: k, Weight: 1.0))];
        var queues = new Dictionary<string, Queue<TemplateMetaInfo>>();
        for (var n = 0; n < count; n++)
        {
            var key = Pick(rng, weights);
            if (!queues.TryGetValue(key, out var q) || q.Count == 0)
            {
                var list = byCategory[key].ToList();
                rng.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list));
                queues[key] = q = new Queue<TemplateMetaInfo>(list);
            }
            result.Add(q.Dequeue());
        }
        return result;
    }

    /// <summary>
    /// Floor <paramref name="floor"/> (1-based) of <paramref name="plan"/>: the client's walk (<see cref="DungeonGenerator"/>) with the
    /// floor's path length and highlights, the templates in the order of placement (Start first, so the Start's exit is the client's
    /// exit #1, Guid …0001; the end room last of the path) → backdrops → master, the bounds over the templates' own extents, the
    /// layers of §5.4 (light Start, hard highlights and boss, standard otherwise), and the a9p fields. <paramref name="dragon"/>: the
    /// dungeon's templates are the Dragon/none ones (<see cref="IsDragon"/>), in templates/NONE, and its name says DR_NON.
    /// <paramref name="bossEnd"/>: the last floor ends in its end-boss highlight as the client's does (no exit down), not in the Exit
    /// with the boss as its last highlight (the server's default: every floor keeps …0001 and …0002).
    /// </summary>
    public static DungeonLayout Floor(RandomDungeonData data, Plan plan, int floor, string id, int tier, string clusterType, byte subtype, Links links, Random rng,
        bool dragon, bool bossEnd = false)
    {
        var floors = plan.PathLengths.Count;
        var index = Math.Clamp(floor - 1, 0, floors - 1);
        var pathLength = plan.PathLengths[index];
        var faction = plan.Generator.Faction;
        var lastFloor = floor >= floors;
        var infos = Infos(data, plan).Where(i => i.Biome.Equals("Dragon", StringComparison.OrdinalIgnoreCase) == dragon && i.Names.Count > 0).ToList();
        InvalidDataException Missing(string what) =>
            new($"{plan.Generator.Name}: no {what} template of {faction}/{plan.Subset}/{plan.Environment}/{plan.Flag} for floor {floor} of {floors}");

        var starts = ForFloor(infos.Where(i => i.Type == "Start" && Doors(i).Count > 0), floor, floors);
        var exits = ForFloor(infos.Where(i => i.Type == "Exit" && Doors(i).Count > 0), floor, floors);
        var bosses = lastFloor ? infos.Where(i => i.Type == "Highlight" && i.EndBoss && Doors(i).Count > 0).ToList() : [];
        var basics = infos.Where(i => i.Type == "Basic" && Doors(i).Count > 1).ToList();
        if (starts.Count == 0) throw Missing("Start");
        if (exits.Count == 0 && !(bossEnd && bosses.Count > 0)) throw Missing("Exit");
        if (basics.Count == 0) throw Missing("Basic");
        var start = starts[rng.Next(starts.Count)];
        var boss = bosses.Count > 0 ? bosses[rng.Next(bosses.Count)] : null;
        var highlights = DrawHighlights(infos, plan.Generator, plan.Highlights.ElementAtOrDefault(index), rng);
        // The client ends its last floor in the boss room (d6.i); the boss templates hold no exit, so the server's last floor keeps
        // its Exit (the way out, …0002) and takes the boss as its last highlight (DGEN §4).
        TemplateMetaInfo end;
        if (bossEnd && boss is not null) end = boss;
        else
        {
            end = exits[rng.Next(exits.Count)];
            if (boss is not null) highlights.Add(boss);
        }
        var entrance = rng.NextDouble() < 0.5 ? F : L; // d6 ctor: EntranceDirection Forward or Left at even odds

        // d7.a_2: three runs, each with one more cell of budget; a run that cannot place every highlight and the end fails. The
        // server's boss that finds no T to hang from (a faction without one) is left out after them.
        Walk? walk = null;
        for (var run = 0; run < 6 && walk is null; run++)
        {
            if (run == 3 && (boss is null || !highlights.Remove(boss))) break;
            var w = new Walk(rng, basics, highlights, end, lastFloor, Math.Max(0, pathLength - end.PathSize) + run % 3);
            try { if (w.Run(start, entrance)) walk = w; }
            catch (Abort) { }
        }
        if (walk is null) throw new InvalidDataException($"{plan.Generator.Name}: no layout of {pathLength} cells with {highlights.Count} highlight(s) for floor {floor} of {floors}");

        var rooms = walk.Rooms;
        var placed = rooms.Select((r, n) => (r.Info, Template: r.Info.Names[rng.Next(r.Info.Names.Count)], At: r.Center, Rotation: 90f * r.Turns,
            Encounter: n == 0 ? "light" : r.Info.Type == "Highlight" ? "hard" : "standard")).ToList();

        // d7.a_ta51: one Backdrop info, a random quarter turn each, on every free 8-neighbour cell of a room (rooms in order, cells, N … NW).
        var taken = rooms.SelectMany(r => Cells(r.Info, r.Center, r.Turns)).ToHashSet();
        var backdrops = infos.Where(i => i.Type == "Backdrop" && i.Width == 1 && i.Height == 1).ToList();
        if (backdrops.Count == 0) throw Missing("Backdrop");
        var backdrop = backdrops[rng.Next(backdrops.Count)];
        (int X, int Z)[] around = [(0, 2), (2, 2), (2, 0), (2, -2), (0, -2), (-2, -2), (-2, 0), (-2, 2)];
        foreach (var room in rooms)
            foreach (var (cx, cz) in Cells(room.Info, room.Center, room.Turns))
                foreach (var (dx, dz) in around)
                {
                    if (!taken.Add((cx + dx, cz + dz))) continue;
                    placed.Add((backdrop, backdrop.Names[rng.Next(backdrop.Names.Count)], new Vector2((cx + dx) * (Block / 2), (cz + dz) * (Block / 2)),
                        90f * rng.Next(4), "standard"));
                }
        // d7.a_2: the master at the Start.
        var masters = infos.Where(i => i.Type == "Master").ToList();
        if (masters.Count > 0)
            placed.Add((masters[0], masters[0].Names[0], rooms[0].Center, 0, "standard"));
        // Bounds over every template's own extent (a master's 10 x 10 cells too).
        var mn = new Vector2(float.MaxValue);
        var mx = new Vector2(float.MinValue);
        foreach (var p in placed)
        {
            var half = new Vector2(p.Info.Width, p.Info.Height) * (Block / 2);
            if (p.Rotation is 90 or 270) half = new Vector2(half.Y, half.X);
            mn = Vector2.Min(mn, p.At - half);
            mx = Vector2.Max(mx, p.At + half);
        }
        mn = new Vector2(MathF.Floor(mn.X), MathF.Floor(mn.Y));
        mx = new Vector2(MathF.Ceiling(mx.X), MathF.Ceiling(mx.Y));

        var folder = dragon ? "NONE" : "GREEN";
        var templates = placed.Select((p, n) => new DungeonTemplate(string.Create(CultureInfo.InvariantCulture, $"instanceslot_{n:00}"), p.Template.Name, folder, p.At, p.Rotation,
            Layers(p.Template, plan, p.Encounter, rng))).ToList();
        return new DungeonLayout
        {
            Id = id, Name = DungeonLayout.ClusterName(id, dragon, tier, faction), Origin = mn, Size = mx - mn, Templates = templates,
            DisplayName = data.DisplayNames.GetValueOrDefault(faction.ToUpperInvariant(), ""), ClusterType = clusterType,
            Floor = (byte)floor, Floors = (byte)floors,
            UpCluster = links.UpCluster, UpName = links.UpName, UpExit = links.UpExit,
            DownCluster = links.DownCluster, DownName = links.DownName, DownExit = links.DownExit,
            Entrance = links.Entrance, EntrancePosition = links.EntrancePosition, Subtype = subtype, Generator = plan.Generator.Name,
        };
    }

    /// <summary>The template infos of the plan's faction, subset, environment and flag (rdgen infos; an attribute left out lets any through).</summary>
    public static IEnumerable<TemplateMetaInfo> Infos(RandomDungeonData data, Plan plan) =>
        data.Templates.Where(i => string.Equals(i.Faction, plan.Generator.Faction, StringComparison.OrdinalIgnoreCase)
                                  && (i.FactionSubset is null || i.FactionSubset == plan.Subset)
                                  && (i.Environments is null || i.Environments.Contains(plan.Environment))
                                  && (i.Flags is null || i.Flags.Contains(plan.Flag)));

    /// <summary>Whether the plan's Start templates are the Dragon/none ones (templatemetadata Dragon/none/Keeper, SPEC §4), not Forest/Green.</summary>
    public static bool IsDragon(RandomDungeonData data, Plan plan)
    {
        var starts = Infos(data, plan).Where(i => i.Type == "Start").ToList();
        return starts.Count > 0 && starts.All(i => i.Biome.Equals("Dragon", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// SPEC §5.4: the active layers of a placed template. A layercontrol "all:…" or "&lt;flag&gt;:…" counts: without a qualifier
    /// (solo, veteran) or with the dungeon's "environment|env|subset" all its layers; with "encounter|&lt;level&gt;" one of them
    /// at random [inf]; "all:story" and other environments none. Each weighted layer block of no flag or the dungeon's adds
    /// one layer by weight.
    /// </summary>
    public static List<string> Layers(TemplateMeta template, Plan plan, string encounter, Random rng)
    {
        var layers = new List<string>();
        foreach (var (type, list) in template.LayerControls)
        {
            if (list.Length == 0) continue;
            var colon = type.IndexOf(':');
            var (who, what) = colon < 0 ? (type, "") : (type[..colon], type[(colon + 1)..]);
            if (who != "all" && who != plan.Flag) continue;
            if (what.Length == 0 || what == $"environment|{plan.Environment}|{plan.EnvironmentSubset}") layers.AddRange(list);
            else if (what == "encounter|" + encounter) layers.Add(list[rng.Next(list.Length)]);
        }
        foreach (var (flag, block) in template.WeightedBlocks)
            if ((flag is null || flag == plan.Flag) && block.Count > 0) layers.Add(Pick(rng, [.. block.Select(l => (l.Id, l.Weight))]));
        return layers;
    }
}
