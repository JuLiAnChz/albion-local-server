using System.Globalization;
using System.Xml.Linq;

namespace AlbionLocal.Server.Data;

/// <summary>
/// randomdungeons.xml and templatemetadata.xml as the random dungeons of stageG use them (analysis/work/stageG/dungeons/SPEC.md,
/// "SPEC" in this file, §5.1, §8, §9): the open-world exits (RandomExits and FixedExits, the distribution per continent,
/// subtype and tier, the visualizations, the level rerolls, the cluster type mapping), the generators, the display names,
/// the lifetimes and difficulty levels, and the template infos the simplified generator (<see cref="DungeonGenerator"/>)
/// picks from. Read with the first open-world cluster (off the world thread, <see cref="ClusterStore.Dungeons"/>); immutable.
/// </summary>
public sealed class RandomDungeonData
{
    /// <summary>DungeonLifeTimes maxlifetimeseconds (87000): a dungeon closes then even with players inside.</summary>
    public double MaxLifetimeSeconds { get; private init; } = 87000;

    /// <summary>DungeonLifeTimes lifetimeafteremptyseconds (300) and its exitsubtype overrides (SOLO 30, ELITE 7200).</summary>
    public double EmptyLifetimeSeconds { get; private init; } = 300;

    public IReadOnlyDictionary<string, double> EmptyLifetimeBySubtype { get; private init; } = new Dictionary<string, double>();

    /// <summary>DifficultyLevels difficultygroup "default" by level (1-4; level 0 has none).</summary>
    public IReadOnlyDictionary<int, DungeonDifficulty> Difficulties { get; private init; } = new Dictionary<int, DungeonDifficulty>();

    /// <summary>StageL mists (SPEC §2.6, §6.3 step 2): every DifficultyLevels difficultygroup by name ("default", "mists"), its levels by number.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<int, DungeonDifficulty>> DifficultyGroups { get; private init; } =
        new Dictionary<string, IReadOnlyDictionary<int, DungeonDifficulty>>();

    public IReadOnlyDictionary<string, LevelReroll> Rerolls { get; private init; } = new Dictionary<string, LevelReroll>();

    /// <summary>ClusterTypeMappings: world.xml cluster type → its dungeon clustertype and the per-generator overrides.</summary>
    public IReadOnlyDictionary<string, (string Type, IReadOnlyDictionary<string, string> Generators)> TypeMappings { get; private init; } =
        new Dictionary<string, (string, IReadOnlyDictionary<string, string>)>();

    /// <summary>ClusterTypeMappings defaultClustertype (DUNGEON_SAFEAREA).</summary>
    public string DefaultClusterType { get; private init; } = "DUNGEON_SAFEAREA";

    /// <summary>RandomDungeonNames Faction defaultname by faction name ("HERETIC" → "Hidden Mines"): a9p.w.</summary>
    public IReadOnlyDictionary<string, string> DisplayNames { get; private init; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, RandomExitInfo> Exits { get; private init; } = new Dictionary<string, RandomExitInfo>();

    /// <summary>
    /// RandomExitDistribution by (continent name, subtype name, tier): the ClusterTier without playersarelethal, else the first of a split
    /// one (stageL mists V5: Royal SOLO T6 has a non-lethal and a lethal entry, <see cref="Lethal"/>).
    /// </summary>
    public IReadOnlyDictionary<(string Continent, string Subtype, int Tier), ExitDistribution> Distribution { get; private init; } =
        new Dictionary<(string, string, int), ExitDistribution>();

    /// <summary>StageL mists (V5): the ClusterTier entries with playersarelethal by (continent, subtype, tier, lethal).</summary>
    public IReadOnlyDictionary<(string Continent, string Subtype, int Tier, bool Lethal), ExitDistribution> Lethal { get; private init; } =
        new Dictionary<(string, string, int, bool), ExitDistribution>();

    /// <summary>
    /// The distribution of a cluster of <paramref name="continent"/> and <paramref name="tier"/> for <paramref name="subtype"/> whose clustertype's
    /// players are <paramref name="lethal"/> (playersarelethal): the entry of that lethality, else the one without the attribute (stageL mists
    /// SPEC §2.6, V5; before it the lethal entry of Royal SOLO T6 overwrote the non-lethal one).
    /// </summary>
    public ExitDistribution? DistributionFor(string continent, string subtype, int tier, bool lethal) =>
        Lethal.GetValueOrDefault((continent, subtype, tier, lethal)) ?? Distribution.GetValueOrDefault((continent, subtype, tier));

    public IReadOnlyDictionary<string, ExitVisualization> Visualizations { get; private init; } = new Dictionary<string, ExitVisualization>();

    public IReadOnlyDictionary<string, DungeonGeneratorInfo> Generators { get; private init; } = new Dictionary<string, DungeonGeneratorInfo>();

    /// <summary>templatemetadata.xml: every info with its templates, in document order.</summary>
    public IReadOnlyList<TemplateMetaInfo> Templates { get; private init; } = [];

    /// <summary>The dungeon clustertype of an exit's dungeon in a cluster of world.xml type <paramref name="worldType"/> (SPEC §8.3): the mapping, the generator's override, else the default.</summary>
    public string ClusterTypeFor(string worldType, string? generator) =>
        TypeMappings.TryGetValue(worldType, out var m)
            ? generator is not null && m.Generators.TryGetValue(generator, out var g) ? g : m.Type
            : DefaultClusterType;

    /// <summary>The empty lifetime of a dungeon of the exit subtype <paramref name="subtype"/> (SOLO, VETERAN, …).</summary>
    public double EmptyLifetime(string subtype) => EmptyLifetimeBySubtype.GetValueOrDefault(subtype, EmptyLifetimeSeconds);

    public static RandomDungeonData Load(XElement randomDungeons, XElement templateMetadata)
    {
        static double D(XElement e, string a, double fallback = 0) =>
            double.TryParse((string?)e.Attribute(a), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        static int I(XElement e, string a, int fallback = 0) =>
            int.TryParse((string?)e.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        static string S(XElement e, string a) => (string?)e.Attribute(a) ?? "";
        var r = randomDungeons;

        var life = r.Element("DungeonLifeTimes");
        var bySubtype = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in life?.Elements("exitsubtype") ?? []) bySubtype[S(e, "type")] = D(e, "lifetimeafteremptyseconds", 300);

        var groups = new Dictionary<string, IReadOnlyDictionary<int, DungeonDifficulty>>(StringComparer.Ordinal);
        foreach (var g in r.Element("DifficultyLevels")?.Elements("difficultygroup") ?? [])
        {
            var levels = new Dictionary<int, DungeonDifficulty>();
            foreach (var d in g.Elements("difficulty"))
                levels[I(d, "level")] = new DungeonDifficulty((string?)d.Attribute("mobmodifier"), D(d, "silverfactor", 1), D(d, "lootfactor", 1), D(d, "mobfamefactor", 1));
            groups.TryAdd(S(g, "name"), levels);
        }
        var difficulties = groups.GetValueOrDefault("default") ?? new Dictionary<int, DungeonDifficulty>();

        var rerolls = new Dictionary<string, LevelReroll>(StringComparer.Ordinal);
        foreach (var e in r.Element("RandomLevelRerolls")?.Elements("RandomLevelReroll") ?? [])
            rerolls[S(e, "name")] = new LevelReroll(S(e, "name"), D(e, "rerolltimesecondsmin", 60), D(e, "rerolltimesecondsmax", 60),
                [.. e.Elements("level").Select(l => ((byte)I(l, "level"), D(l, "weight", 1)))]) { SingleRoll = S(e, "singlerollonly") == "true" };

        var mappings = new Dictionary<string, (string, IReadOnlyDictionary<string, string>)>(StringComparer.Ordinal);
        var map = r.Element("ClusterTypeMappings");
        foreach (var e in map?.Elements("ClusterTypeMapping") ?? [])
            mappings[S(e, "clustertype")] = (S(e, "dungeonclustertype"),
                e.Elements("Generator").ToDictionary(g => S(g, "name"), g => S(g, "dungeonclustertype"), StringComparer.Ordinal));

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in r.Element("RandomDungeonNames")?.Elements("Faction") ?? []) names.TryAdd(S(f, "name"), S(f, "defaultname"));

        var exits = new Dictionary<string, RandomExitInfo>(StringComparer.Ordinal);
        foreach (var e in r.Element("FixedExits")?.Elements("FixedExit") ?? [])
            exits.TryAdd(S(e, "name"), new RandomExitInfo
            {
                Name = S(e, "name"), Fixed = true, ActivationSeconds = D(e, "activationtime"), CooldownSeconds = D(e, "cooldown"), MaxFillRatio = D(e, "maxfillratio", 1),
                Reroll = (string?)e.Attribute("randomlevelrerollref"),
                Dungeons = S(e, "generatorName") is { Length: > 0 } gen ? [(gen, I(e, "tier", 1), 1.0)] : [],
            });
        foreach (var e in r.Element("RandomExits")?.Elements("RandomExit") ?? [])
            exits.TryAdd(S(e, "name"), new RandomExitInfo
            {
                Name = S(e, "name"), ActivationSeconds = D(e, "activationtime"), CooldownSeconds = D(e, "cooldown"),
                LifetimeAfterLastUseSeconds = D(e, "lifetimeafterlastuse"), Visualization = (string?)e.Element("Visualization")?.Attribute("ref"),
                Dungeons = [.. e.Element("RandomDungeonGenerators")?.Elements("GeneratedRandomDungeon").Select(g => (S(g, "generatorName"), I(g, "tier", 1), D(g, "weight", 1))) ?? []],
                Corrupted = e.Element("RandomDungeonGenerators")?.Element("CorruptedDungeonGenerator") is not null,
                MistsMode = (string?)e.Element("RandomDungeonGenerators")?.Element("MistsGenerator")?.Attribute("mistsmode"),
            });

        var distribution = new Dictionary<(string, string, int), ExitDistribution>();
        var lethal = new Dictionary<(string, string, int, bool), ExitDistribution>();
        foreach (var c in r.Element("RandomExitDistribution")?.Elements("Continent") ?? [])
            foreach (var s in c.Elements("Subtype"))
                foreach (var t in s.Elements("ClusterTier"))
                {
                    var entry = new ExitDistribution(D(t, "maxfillratio", 0),
                        [.. t.Elements("RandomExit").Select(x => new ExitChoice(S(x, "name"), D(x, "weight", 1), (string?)x.Attribute("randomlevelrerollref")))]);
                    var (continent, subtype, tier) = (S(c, "name"), S(s, "name"), I(t, "tier"));
                    // StageL mists V5: a playersarelethal entry belongs to that lethality; the plain table keeps the first of a split key.
                    if ((string?)t.Attribute("playersarelethal") is { } l)
                    {
                        lethal[(continent, subtype, tier, l.Equals("true", StringComparison.OrdinalIgnoreCase))] = entry;
                        distribution.TryAdd((continent, subtype, tier), entry);
                    }
                    else distribution[(continent, subtype, tier)] = entry;
                }

        // SPEC §2.1 key 7: the 1-based index of a <prefab> among all <prefab> elements of the file, in document order.
        var prefabIndex = new Dictionary<XElement, int>();
        var n = 0;
        foreach (var p in r.Descendants("prefab")) prefabIndex[p] = ++n;
        List<ExitPrefab> Prefabs(XElement? parent) =>
            [.. parent?.Elements("prefab").Select(p => new ExitPrefab(S(p, "name"), D(p, "weight", 1), prefabIndex[p])) ?? []];
        var visualizations = new Dictionary<string, ExitVisualization>(StringComparer.Ordinal);
        foreach (var v in r.Element("RandomExitVisualizations")?.Elements("Visualization") ?? [])
        {
            var biomes = new Dictionary<string, (IReadOnlyDictionary<string, IReadOnlyList<ExitPrefab>> Subbiomes, IReadOnlyList<ExitPrefab> Fallback)>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in v.Elements("biome"))
                biomes[S(b, "name")] = (b.Elements("subbiome").ToDictionary(sb => S(sb, "name"), sb => (IReadOnlyList<ExitPrefab>)Prefabs(sb), StringComparer.OrdinalIgnoreCase),
                    Prefabs(b.Element("fallback")));
            visualizations.TryAdd(S(v, "name"), new ExitVisualization(S(v, "name"), biomes, Prefabs(v.Element("fallback"))));
        }

        var generators = new Dictionary<string, DungeonGeneratorInfo>(StringComparer.Ordinal);
        foreach (var g in r.Element("RandomDungeonGenerators")?.Elements("RandomDungeonGenerator") ?? [])
        {
            var faction = g.Element("Faction");
            var sizes = new List<DungeonSize>();
            foreach (var s in g.Element("DungeonSizeAndDepth")?.Elements("Size") ?? [])
                sizes.Add(new DungeonSize(Math.Max(1, I(s, "floors", 1)), D(s, "weight", 1), s.Elements("Floor").ToDictionary(f => I(f, "level", 1),
                    f => (IReadOnlyList<(int PathLength, int Highlights, double Weight)>)[.. f.Elements("DungeonLength").Select(l => (I(l, "pathlength", 4), I(l, "highlightCount"), D(l, "weight", 1)))])));
            generators.TryAdd(S(g, "name"), new DungeonGeneratorInfo
            {
                Name = S(g, "name"), Faction = faction is null ? "" : S(faction, "name"),
                Subsets = [.. faction?.Elements("Subset").Select(s => (S(s, "name"), D(s, "weight", 1))) ?? []],
                Environments = [.. g.Element("Environments")?.Elements("Environment").Select(e => (S(e, "name"), D(e, "weight", 1),
                    (IReadOnlyList<(string, double)>)[.. e.Elements("Subset").Select(s => (S(s, "name"), D(s, "weight", 1)))])) ?? []],
                // The flags without a festivity condition (solo or veteran; christmas only with its festivity, which this server has not).
                Flags = [.. g.Element("Flags")?.Elements("Flag").Where(f => f.Attribute("requiresActiveFestivityFlag") is null).Select(f => S(f, "name")) ?? []],
                Sizes = sizes,
                // HighlightCategories: the category weights the highlights are drawn by (DefaultCategory = the infos without one, name null).
                Categories = [.. g.Element("HighlightCategories")?.Elements().Where(c => c.Name == "Category" || c.Name == "DefaultCategory")
                    .Select(c => (c.Name == "DefaultCategory" ? null : S(c, "name"), D(c, "weight", 1))) ?? []],
            });
        }

        var templates = new List<TemplateMetaInfo>();
        foreach (var b in templateMetadata.Elements("biome"))
            foreach (var sb in b.Elements("subbiome"))
                foreach (var f in sb.Elements("faction"))
                    foreach (var ty in f.Elements("type"))
                        foreach (var info in ty.Elements("info"))
                        {
                            var size = TileMath.V2((string?)info.Attribute("size")) ?? System.Numerics.Vector2.One;
                            byte[] path;
                            try { path = (string?)info.Attribute("pathdata") is { Length: > 0 } pd ? Convert.FromBase64String(pd) : []; }
                            catch (FormatException) { path = []; }
                            string[]? List(string a) => (string?)info.Attribute(a) is { } v ? v.Split('|', StringSplitOptions.RemoveEmptyEntries) : null;
                            templates.Add(new TemplateMetaInfo
                            {
                                Biome = S(b, "name"), SubBiome = S(sb, "name"), Faction = S(f, "name"), Type = S(ty, "name"),
                                Width = (int)size.X, Height = (int)size.Y, PathData = path, FactionSubset = (string?)info.Attribute("factionsubset"),
                                Environments = List("environment"), Flags = List("flags"), LevelUsage = (string?)info.Attribute("dungeonlevelusage"),
                                Usage = (string?)info.Attribute("templateusage"), Category = (string?)info.Attribute("category"),
                                EndBoss = string.Equals((string?)info.Attribute("endbosshighlight"), "true", StringComparison.OrdinalIgnoreCase),
                                PathSize = I(info, "pathsize", 1),
                                Names = [.. info.Elements("template").Select(t => new TemplateMeta(S(t, "name"),
                                    [.. t.Elements("layercontrol").Select(l => (S(l, "type"), S(l, "layers").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))],
                                    [.. t.Elements("weightedlayerblock").Select(w => ((string?)w.Attribute("flag") is { Length: > 0 } fl ? fl : null,
                                        (IReadOnlyList<(string, double)>)[.. w.Elements("layer").Select(l => (S(l, "id"), D(l, "weight", 1)))]))]))],
                            });
                        }

        return new RandomDungeonData
        {
            MaxLifetimeSeconds = life is null ? 87000 : D(life, "maxlifetimeseconds", 87000),
            EmptyLifetimeSeconds = life is null ? 300 : D(life, "lifetimeafteremptyseconds", 300),
            EmptyLifetimeBySubtype = bySubtype, Difficulties = difficulties, DifficultyGroups = groups, Rerolls = rerolls, TypeMappings = mappings,
            DefaultClusterType = map is null ? "DUNGEON_SAFEAREA" : S(map, "defaultClustertype") is { Length: > 0 } dt ? dt : "DUNGEON_SAFEAREA",
            DisplayNames = names, Exits = exits, Distribution = distribution, Lethal = lethal, Visualizations = visualizations, Generators = generators, Templates = templates,
        };
    }
}

/// <summary>DifficultyLevels default level n: the mob modifier spell and the factors on silver, loot and mob fame (SPEC §7).</summary>
public sealed record DungeonDifficulty(string? MobModifier, double Silver, double Loot, double MobFame);

/// <summary>
/// RandomLevelReroll: every min..max seconds a new level 0-4 by weight (SPEC §8.4); <see cref="SingleRoll"/> (singlerollonly, stageL roads
/// SPEC §7.1: ROADS_FIXED_DUNGEONS, the mists' modes): the level drawn once, when the exit spawns, and never again.
/// </summary>
public sealed record LevelReroll(string Name, double MinSeconds, double MaxSeconds, IReadOnlyList<(byte Level, double Weight)> Levels)
{
    public bool SingleRoll { get; init; }
}

/// <summary>
/// A RandomExit or a FixedExit (SPEC §5.1, §9): the timers, the visualization (random exits), and the weighted dungeons
/// beneath it (generator, tier). <see cref="MaxFillRatio"/> is the fixed exit's own (the random exits take the distribution's).
/// </summary>
public sealed record RandomExitInfo
{
    public required string Name { get; init; }
    public bool Fixed { get; init; }
    public double ActivationSeconds { get; init; }
    public double CooldownSeconds { get; init; }
    public double LifetimeAfterLastUseSeconds { get; init; }
    public double MaxFillRatio { get; init; } = 1;

    /// <summary>A FixedExit's own randomlevelrerollref (stageL roads SPEC §7.1: the road dungeons' ROADS_FIXED_DUNGEONS); the random exits take their distribution entry's.</summary>
    public string? Reroll { get; init; }

    public string? Visualization { get; init; }
    public IReadOnlyList<(string Generator, int Tier, double Weight)> Dungeons { get; init; } = [];

    /// <summary>StageL corrupted: a &lt;CorruptedDungeonGenerator/&gt; exit (CORRUPTED_SOLO_NONLETHAL / _LETHAL): a corrupted portal, no random dungeon beneath.</summary>
    public bool Corrupted { get; init; }

    /// <summary>StageL mists (SPEC §2.6): a &lt;MistsGenerator mistsmode&gt; exit's mode (MISTS_SOLO_YELLOW): a mists portal (event 324 key 16), entered by ENTER_MISTS.</summary>
    public string? MistsMode { get; init; }
}

/// <summary>RandomExitDistribution ClusterTier: how many markers of the subtype may hold an exit at once, and which exits by weight.</summary>
public sealed record ExitDistribution(double MaxFillRatio, IReadOnlyList<ExitChoice> Exits);

public sealed record ExitChoice(string Exit, double Weight, string? Reroll);

/// <summary>One &lt;prefab&gt; of a visualization: the tile name (event 324 key 6) and its document index (key 7).</summary>
public sealed record ExitPrefab(string Name, double Weight, int Index);

/// <summary>RandomExitVisualizations Visualization: prefabs by biome and subbiome, the biome's fallback, the visualization's fallback (SPEC §8.2).</summary>
public sealed record ExitVisualization(string Name,
    IReadOnlyDictionary<string, (IReadOnlyDictionary<string, IReadOnlyList<ExitPrefab>> Subbiomes, IReadOnlyList<ExitPrefab> Fallback)> Biomes,
    IReadOnlyList<ExitPrefab> Fallback)
{
    /// <summary>The prefabs a cluster of <paramref name="biome"/>/<paramref name="subBiome"/> picks from (SPEC §8.2 step 2).</summary>
    public IReadOnlyList<ExitPrefab> For(string biome, string subBiome) =>
        Biomes.TryGetValue(biome, out var b)
            ? b.Subbiomes.TryGetValue(subBiome, out var p) && p.Count > 0 ? p : b.Fallback.Count > 0 ? b.Fallback : Fallback
            : Fallback;
}

public sealed record DungeonSize(int Floors, double Weight, IReadOnlyDictionary<int, IReadOnlyList<(int PathLength, int Highlights, double Weight)>> Lengths);

/// <summary>RandomDungeonGenerator: the faction and its subsets, the environments with their subsets, the flags, the sizes (SPEC §5.3 step 1-2).</summary>
public sealed record DungeonGeneratorInfo
{
    public required string Name { get; init; }
    public required string Faction { get; init; }
    public IReadOnlyList<(string Name, double Weight)> Subsets { get; init; } = [];
    public IReadOnlyList<(string Name, double Weight, IReadOnlyList<(string Name, double Weight)> Subsets)> Environments { get; init; } = [];
    public IReadOnlyList<string> Flags { get; init; } = [];
    public IReadOnlyList<DungeonSize> Sizes { get; init; } = [];

    /// <summary>HighlightCategories: (category, weight); a null category is the DefaultCategory (the highlights without a category).</summary>
    public IReadOnlyList<(string? Name, double Weight)> Categories { get; init; } = [];
}

/// <summary>
/// A templatemetadata.xml info: biome/subbiome/faction/type, size in cells, the path data (one MetaDirectionFlag byte per
/// cell), the filters (null = any) and the templates (SPEC §5.1).
/// </summary>
public sealed record TemplateMetaInfo
{
    public required string Biome { get; init; }
    public required string SubBiome { get; init; }
    public required string Faction { get; init; }
    public required string Type { get; init; }
    public int Width { get; init; } = 1;
    public int Height { get; init; } = 1;
    public byte[] PathData { get; init; } = [];
    public string? FactionSubset { get; init; }
    public string[]? Environments { get; init; }
    public string[]? Flags { get; init; }
    public string? LevelUsage { get; init; }

    /// <summary>templateusage (TemplateUsageType Default/Enforce/Exclude; null = Default): with dungeonlevelusage, which floors take the info (d6.ao_2).</summary>
    public string? Usage { get; init; }

    /// <summary>category (HighlightCategories: silver, fame, statue, event; null = default), endbosshighlight (a last floor's boss room), pathsize (WalkablePathSize).</summary>
    public string? Category { get; init; }
    public bool EndBoss { get; init; }
    public int PathSize { get; init; } = 1;

    public IReadOnlyList<TemplateMeta> Names { get; init; } = [];
}

/// <summary>A template of an info: its layercontrols (type, layers) and weighted layer blocks (flag, weighted layer ids) (SPEC §5.4).</summary>
public sealed record TemplateMeta(string Name, IReadOnlyList<(string Type, string[] Layers)> LayerControls,
    IReadOnlyList<(string? Flag, IReadOnlyList<(string Id, double Weight)> Layers)> WeightedBlocks);
