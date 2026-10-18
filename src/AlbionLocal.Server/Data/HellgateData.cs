using System.Globalization;
using System.Xml.Linq;

namespace AlbionLocal.Server.Data;

/// <summary>
/// hellgate.xml (analysis/work/stageL/hellgates/SPEC.md, "SPEC" here, §2.1; client parser bcr.c8 0x1805EAB40): the modes with the client's
/// ids (the list index: bcr.c8 first adds hellgate_generic as 0, then each &lt;hellgatemode&gt; in document order; bcr.abp(int) indexes the
/// list), the mapsets with their weights and the global settings. Plus the instance ids "@HELLCLUSTER@&lt;guid&gt;" (ClusterInstanceType 2,
/// SPEC §3.5), the named markers of the Hellgate_NN templates (SPEC §2.4) and the guardian bosses' spawn behaviours. Read with the first
/// cluster that has a hellgate shrine (a Hell Den) or is a hellgate (off the world thread, <see cref="ClusterStore.Hellgates"/>); immutable.
/// </summary>
public sealed class HellgateData
{
    /// <summary>"@HELLCLUSTER@": a4l builds every instance id as "@" + ClusterInstanceType + "@" + Guid (HELLCLUSTER = 2, SPEC §3.5 [inference on the name]).</summary>
    public const string InstancePrefix = "@HELLCLUSTER@";

    /// <summary>The instance id of <paramref name="guid"/> ("@HELLCLUSTER@&lt;guid&gt;", Guid "D" form, lower case).</summary>
    public static string InstanceId(Guid guid) => InstancePrefix + guid.ToString();

    /// <summary>Whether <paramref name="cluster"/> is a hellgate instance id.</summary>
    public static bool IsInstance(string? cluster) => cluster?.StartsWith(InstancePrefix, StringComparison.Ordinal) == true;

    /// <summary>Whether a worldsettings cluster type is a hellgate's (DUNGEON_HELL_2V2_NON_LETHAL …; SPEC §2.2).</summary>
    public static bool IsHellgateType(string? type) => type?.StartsWith("DUNGEON_HELL_", StringComparison.Ordinal) == true;

    /// <summary>The namedmarkerpoint names of a Hellgate_NN template (SPEC §2.4): the objective chest, the exit portal, the chain exit (phase B).</summary>
    public const string ObjectiveChest = "HellgateObjectiveChest", ExitPortal = "EXIT_PORTAL", ChainExit = "CHAIN_EXIT";

    public static readonly IReadOnlySet<string> Markers = new HashSet<string>(StringComparer.Ordinal) { ObjectiveChest, ExitPortal, ChainExit };

    /// <summary>Whether <paramref name="behaviour"/> is a guardian boss's (HG_DEMON_*_BOSS of maps 06 and 10, not a *_MINIBOSS): held back at the fill (SPEC §5.4).</summary>
    public static bool IsBoss(string? behaviour) => behaviour is not null && behaviour.StartsWith("HG_DEMON_", StringComparison.Ordinal)
        && behaviour.EndsWith("_BOSS", StringComparison.Ordinal) && !behaviour.EndsWith("_MINIBOSS", StringComparison.Ordinal);

    /// <summary>Whether <paramref name="behaviour"/> is a miniboss's (HG_DEMON_*_MINIBOSS of maps 03, 04, 07): its death opens the REWARD layer's CHEST.</summary>
    public static bool IsMiniboss(string? behaviour) => behaviour?.EndsWith("_MINIBOSS", StringComparison.Ordinal) == true && behaviour.StartsWith("HG_DEMON_", StringComparison.Ordinal);

    /// <summary>
    /// One &lt;hellgatemode&gt; (bct, parser bct.aa(XmlElement) 0x1805EC540): its id (the list index), name, team size and count, mapset, the
    /// points of objective 1 (the client's tracker bar divides by them: SPEC §3.6), the item power the client checks, the loot chest spawnpoints
    /// of the objective chest and the might reward (phase B).
    /// </summary>
    public sealed record Mode(int Id, string Name, int TeamSize, int TeamAmount, string MapSet, int PointsRequirement, double ItemPower,
        string PvpChest, string PveChest, string MightReward)
    {
        /// <summary>Whether its players are lethal (the HRD mapsets of hellgate_2v2 and hellgate_5v5; the name has no "_non_lethal").</summary>
        public bool Lethal => !Name.EndsWith("_non_lethal", StringComparison.Ordinal);

        /// <summary>
        /// Phase B (SPEC §2.1, §3.8): the restricted area (bct +0x40..0x70): arearestrictiondelay (90 s after the match's start), the radius's
        /// velocity (0.30 m/s), the final phase's velocity (0.2) and the break before it (120 s), the warning and damage spells' names.
        /// </summary>
        public double AreaDelay { get; init; } = 90;
        public double AreaVelocity { get; init; } = 0.30;
        public double AreaFinalVelocity { get; init; } = 0.2;
        public double AreaFinalBreak { get; init; } = 120;
        public string AreaWarningSpell { get; init; } = "HELLGATE_WARNING";
        public string AreaDamageSpell { get; init; } = "HELLGATE_DAMAGE";

        /// <summary>Phase B: the chain exit's visualization (chainexitvisualization, randomdungeons.xml: HELLGATE_CHAIN_EXIT → SHARED_HELLGATE_EXIT_LETHAL).</summary>
        public string ChainVisualization { get; init; } = "HELLGATE_CHAIN_EXIT";

        /// <summary>Phase B: rewardconstant, rewardceiling, rewardscaling (bct +0x90/94/98) of <see cref="ChainFactor"/>.</summary>
        public double RewardConstant { get; init; } = 43.44769151;
        public double RewardCeiling { get; init; } = 0.2;
        public double RewardScaling { get; init; } = 0.4;

        /// <summary>
        /// bct.af(int n) 0x1805ED020 (SPEC §2.1): 1 + rewardceiling · (1 − e^(−n^rewardscaling / rewardconstant)); this server's n is the chain (the
        /// hellgates before this one in a row [inference]): 1 for the first, 1.0045 for the second of a non-lethal 2v2.
        /// </summary>
        public double ChainFactor(int n) => n <= 0 || RewardConstant <= 0 ? 1 : 1 + RewardCeiling * (1 - Math.Exp(-Math.Pow(n, RewardScaling) / RewardConstant));

        /// <summary>Its InfamyType (bct +0x80, bct.aa: teamsize 2 → Hell2v2 = 2, 5 → Hell5v5 = 3, 10 → Hell10v10 = 4; 0 for the generic mode).</summary>
        public int InfamyType => TeamSize switch { 2 => 2, 5 => 3, 10 => 4, _ => 0 };

        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Name} ({TeamSize}v{TeamSize}, IP {ItemPower:0}, {PointsRequirement} points)");
    }

    /// <summary>A map of a mapset: its world.xml cluster (HELLGATE-NN-&lt;mode&gt;-01[-HRD]) and weight (32 / 10 miniboss / 5 boss; the layer attribute is not used).</summary>
    public sealed record MapEntry(string Id, double Weight);

    private HellgateData(IReadOnlyList<Mode> modes, IReadOnlyDictionary<string, IReadOnlyList<MapEntry>> mapSets, IReadOnlyDictionary<string, string> globals,
        IReadOnlyDictionary<int, IReadOnlyList<long>> milestones)
    {
        Modes = modes;
        MapSets = mapSets;
        Globals = globals;
        Milestones = milestones;
    }

    /// <summary>Phase B: the infamymilestones per InfamyType (mode H1 → 1 is the 1v1's and unused, H2 → 2, H5 → 3, H10 → 4), ascending.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<long>> Milestones { get; }

    /// <summary>The milestones of InfamyType <paramref name="type"/> passed from <paramref name="before"/> to <paramref name="after"/>.</summary>
    public IEnumerable<long> Crossed(int type, long before, long after) => (Milestones.GetValueOrDefault(type) ?? []).Where(m => m > before && m <= after);

    /// <summary>The modes by id: 0 hellgate_generic (no team, no mapset), then the four of the data (1 2v2_non_lethal, 2 5v5_non_lethal, 3 2v2, 4 5v5).</summary>
    public IReadOnlyList<Mode> Modes { get; }

    /// <summary>The mapsets by name (2v2_non_lethal, 5v5_non_lethal, 2v2, 5v5), each with its ten maps.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<MapEntry>> MapSets { get; }

    /// <summary>The globalsettings attributes (opengatedistance, secondstoenter, individualchanneltime, mobdespawndelay, …).</summary>
    public IReadOnlyDictionary<string, string> Globals { get; }

    /// <summary>A number of the globalsettings; <paramref name="fallback"/> without it (the client's own defaults, bcr.c8).</summary>
    public double Global(string name, double fallback) =>
        double.TryParse(Globals.GetValueOrDefault(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>The spells of the match start (full health and energy, cooldowns reset) and of the mobs' despawn (globalsettings).</summary>
    public string MatchStartSpell => Globals.GetValueOrDefault("pvpmatchstartspell") ?? "HELLGATE_MATCH_START_SPELL";

    /// <summary>The mode of id <paramref name="id"/>; null out of range (bcr.abp's bounds).</summary>
    public Mode? At(int id) => id >= 0 && id < Modes.Count ? Modes[id] : null;

    /// <summary>The mode called <paramref name="name"/> (hellgate_2v2 …, case-insensitive); null for any other.</summary>
    public Mode? Find(string? name) => Modes.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The maps of <paramref name="mode"/>'s mapset; empty for the generic mode.</summary>
    public IReadOnlyList<MapEntry> MapsOf(Mode mode) => MapSets.GetValueOrDefault(mode.MapSet) ?? [];

    /// <summary>The mode whose mapset holds world cluster <paramref name="map"/>; null for any other cluster.</summary>
    public Mode? OfMap(string map) => Modes.FirstOrDefault(m => MapsOf(m).Any(e => e.Id == map));

    /// <summary>Reads hellgate.xml's root.</summary>
    public static HellgateData Load(XElement root)
    {
        static string S(XElement e, string a) => ((string?)e.Attribute(a) ?? "").Trim();
        static double D(XElement e, string a) => double.TryParse(S(e, a), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
        var modes = new List<Mode> { new(0, "hellgate_generic", 0, 0, "", 0, 0, "", "", "") };
        static double Or(XElement e, string a, double fallback) => e.Attribute(a) is null ? fallback : D(e, a);
        foreach (var e in root.Element("hellgatemodes")?.Elements("hellgatemode") ?? [])
            modes.Add(new Mode(modes.Count, S(e, "name"), (int)D(e, "teamsize"), (int)D(e, "teamamount"), S(e, "mapset"), (int)D(e, "pointsrequirement"),
                D(e, "itempowerrequirement"), S(e, "pvplootchestspawnpoint"), S(e, "pvelootchestspawnpoint"), S(e, "mightreward"))
            {
                AreaDelay = Or(e, "arearestrictiondelay", 90), AreaVelocity = Or(e, "arearestrictionvelocity", 0.30),
                AreaFinalVelocity = Or(e, "finalarearestrictionvelocity", 0.2), AreaFinalBreak = Or(e, "finalarearestrictionbreak", 120),
                AreaWarningSpell = S(e, "arearestrictionspellwarningarea") is { Length: > 0 } w ? w : "HELLGATE_WARNING",
                AreaDamageSpell = S(e, "arearestrictionspelldamagearea") is { Length: > 0 } dmg ? dmg : "HELLGATE_DAMAGE",
                ChainVisualization = S(e, "chainexitvisualization") is { Length: > 0 } v ? v : "HELLGATE_CHAIN_EXIT",
                RewardConstant = Or(e, "rewardconstant", 43.44769151), RewardCeiling = Or(e, "rewardceiling", 0.2), RewardScaling = Or(e, "rewardscaling", 0.4),
            });
        var sets = new Dictionary<string, IReadOnlyList<MapEntry>>(StringComparer.Ordinal);
        foreach (var set in root.Element("hellgatemaps")?.Elements("mapset") ?? [])
            sets[S(set, "name")] = [.. set.Elements("map").Select(m => new MapEntry(S(m, "id"), m.Attribute("weight") is null ? 1 : D(m, "weight"))).Where(m => m.Id.Length > 0)];
        var globals = root.Element("globalsettings")?.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value.Trim(), StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var milestones = new Dictionary<int, IReadOnlyList<long>>();
        foreach (var m in root.Element("infamymilestonetypes")?.Elements("infamymilestones") ?? [])
            if (S(m, "mode") switch { "H1" => 1, "H2" => 2, "H5" => 3, "H10" => 4, _ => 0 } is > 0 and var type)
                milestones[type] = [.. m.Elements("milestone").Select(x => (long)D(x, "value")).Where(v => v > 0).Order()];
        return new HellgateData(modes, sets, globals, milestones);
    }
}
