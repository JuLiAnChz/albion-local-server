using System.Globalization;
using System.Xml.Linq;

namespace AlbionLocal.Server.Data;

/// <summary>
/// corrupteddungeons.xml (analysis/work/stageL/corrupted/SPEC.md, "SPEC" here, §1.1; client parser bbt.a.ab(XmlReader) 0x18057F120): the
/// three difficulties in the client's list order (index 0-2 = CorruptedDungeonStatus key 2), the 20 maps with their weights, the 26 infamy
/// milestones and the global settings. Plus the instance ids "@CORRUPTEDDUNGEON@&lt;guid&gt;" (ClusterInstanceType 9, SPEC §2.1) and the
/// spawn behaviours of the CORRUPT-0nn templates the server places itself (SPEC §1.2). Read with the first cluster that has a corrupted
/// entrance or is a corrupted dungeon (off the world thread, <see cref="ClusterStore.Corrupted"/>); immutable.
/// </summary>
public sealed class CorruptedData
{
    /// <summary>"@CORRUPTEDDUNGEON@": a4l.f(Guid) = "@" + ClusterInstanceType 9 + "@" + guid (SPEC §2.3).</summary>
    public const string InstancePrefix = "@CORRUPTEDDUNGEON@";

    /// <summary>The instance id of <paramref name="guid"/> ("@CORRUPTEDDUNGEON@&lt;guid&gt;", Guid "D" form, lower case).</summary>
    public static string InstanceId(Guid guid) => InstancePrefix + guid.ToString();

    /// <summary>Whether <paramref name="cluster"/> is a corrupted dungeon instance id.</summary>
    public static bool IsInstance(string? cluster) => cluster?.StartsWith(InstancePrefix, StringComparison.Ordinal) == true;

    /// <summary>The InfamyType of the corrupted dungeons (Albion.Common.Infamy.InfamyType 1; GetInfamyInfo keys 0 and 4).</summary>
    public const int InfamyType = 1;

    /// <summary>The spawn behaviour of the demon (the end boss): spawned by the server once the points are in (SPEC §4.2 step 6).</summary>
    public const string EndBoss = "CD_DEMON_ENDBOSS";

    /// <summary>The Demonic Shards an owner destroys to banish an invader (SPEC §4.4: objectiveclearrequirement of them at an invasion's start).</summary>
    public const string Statue = "CD_STATUE";

    /// <summary>The anti-reset imps (SPEC §6 phase B 5): spawned by the server during an invasion (Corrupted:AntiReset).</summary>
    public const string AntiResetImp = "CD_DEMON_IMP_ANTIRESET";

    /// <summary>The tile name of a template's invader spawn points (tiles.xml InvaderSpawnpoint, a namedmarkerpoint; 20 over the 48 templates).</summary>
    public const string InvaderSpawnTile = "InvaderSpawnpoint";

    /// <summary>
    /// The spawn behaviours with initialspawntimeseconds of 3.5-3.8 M (SPEC §1.2): never spawned by timer, the server places them by script
    /// (the demon; the shards and the anti-reset imps of phase B), so the map's spawning leaves them out.
    /// </summary>
    public static readonly IReadOnlySet<string> ScriptedBehaviours = new HashSet<string>(StringComparer.Ordinal) { EndBoss, Statue, AntiResetImp };

    /// <summary>A spawn behaviour with no tier rows at all: the sorcerer's teleport marker, never a mob (SPEC §1.2 [V]); dropped without a warning.</summary>
    public static readonly IReadOnlySet<string> MarkerBehaviours = new HashSet<string>(StringComparer.Ordinal) { "CD_DEMON_SORCERER_TELEPORT_TARGET" };

    /// <summary>
    /// Whether a mob of spawn behaviour <paramref name="behaviour"/> counts for the points (SPEC §4.3): not the traps, hazards and watchers
    /// (TRAP_*, HAZARD_*, PATROL_WATCHER) nor a scripted one.
    /// </summary>
    public static bool Counts(string? behaviour) => behaviour is null
        || !ScriptedBehaviours.Contains(behaviour) && !behaviour.StartsWith("TRAP_", StringComparison.Ordinal)
        && !behaviour.StartsWith("HAZARD_", StringComparison.Ordinal) && !behaviour.StartsWith("PATROL_WATCHER", StringComparison.Ordinal);

    /// <summary>The fake-slot spell of the entrance window's difficulty <paramref name="index"/> (bbt.a.ax 0x18057F9C0; the "CURRUPTED" typo is the data's).</summary>
    public static string EnterSpell(int index) => "ENTER_CURRUPTED_DUNGEON_SPELL_" + (index + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// One difficultylevel (bbt.a, SPEC §1.1): its index (the client's list order), name (beginner / intermediate / pro) and rank text, tier,
    /// cluster type (ChangeCluster key 4), the lethal-zone rule of the entrance and the cluster types it is open from in any zone, the gates,
    /// the reward curve, the points the demon needs and the infamy bonus of an invasion's win (here also the demon's, SPEC §4.3).
    /// </summary>
    public sealed record Difficulty(int Index, string Name, string DisplayName, int Tier, string ClusterType, bool LethalEntrance,
        IReadOnlySet<string> AccessibleFrom, double ItemPower, long InfamyRequirement, double RewardConstant, double RewardCeiling, double RewardScaling,
        long InfamyKillBonus, int PointsRequirement, int ObjectiveClear, double ExitDungeonTime, string MightReward)
    {
        /// <summary>The client's rank name (Hunter, Stalker, Slayer: the localisation of displayname).</summary>
        public string Rank => Index switch { 0 => "Hunter", 1 => "Stalker", 2 => "Slayer", _ => Name };

        /// <summary>Phase B (SPEC §1.1): the invasion rolls' chances without and with the shrine (chanceperrollpve / chanceperrollpvp).</summary>
        public double ChancePve { get; init; }
        public double ChancePvp { get; init; }

        /// <summary>The infamy shares of an invasion's loser and winner (pvpinfamyloss, pvpinfamygain) and the boss loot share of an invader's win (percentbosslootonkill, in %).</summary>
        public double PvpInfamyLoss { get; init; }
        public double PvpInfamyGain { get; init; }
        public double BossLootPercent { get; init; }

        /// <summary>The anti-reset imps (timeuntilantiresetmobspawninseconds 180, …despawn… 45, antiresetmobrequiredplayerdistanceforspawn 11).</summary>
        public double AntiResetSpawnSeconds { get; init; }
        public double AntiResetDespawnSeconds { get; init; }
        public double AntiResetDistance { get; init; }

        /// <summary>
        /// The reward bonus of <paramref name="infamy"/> (bbt.a.ab(long) 0x18057F7A0, SPEC §2.5: what the infamy tooltip shows):
        /// rewardceiling × (1 − e^(−infamy^rewardscaling / rewardconstant)); 0 for no infamy, approaching the ceiling.
        /// </summary>
        public double Bonus(long infamy) =>
            infamy <= 0 || RewardConstant <= 0 ? 0 : RewardCeiling * (1 - Math.Exp(-Math.Pow(infamy, RewardScaling) / RewardConstant));
    }

    private CorruptedData(IReadOnlyList<Difficulty> difficulties, IReadOnlyList<(string Id, double Weight)> maps, IReadOnlyList<long> milestones,
        IReadOnlyDictionary<string, string> globals)
    {
        Difficulties = difficulties;
        Maps = maps;
        Milestones = milestones;
        Globals = globals;
    }

    /// <summary>The difficulties in file order (index = CorruptedDungeonStatus key 2 = the entrance window's choice).</summary>
    public IReadOnlyList<Difficulty> Difficulties { get; }

    /// <summary>The corrupteddungeonmaps (CORRUPT-001..020, weight 1 each).</summary>
    public IReadOnlyList<(string Id, double Weight)> Maps { get; }

    /// <summary>The infamymilestones in rising order (50 … 10 000 000).</summary>
    public IReadOnlyList<long> Milestones { get; }

    /// <summary>The globalsettings attributes (safeperiod, rollinterval, chainexitvisualization, …).</summary>
    public IReadOnlyDictionary<string, string> Globals { get; }

    /// <summary>The visualization of the chain exit (globalsettings chainexitvisualization; CORRUPTED_DUNGEON_CHAIN_EXIT).</summary>
    public string ChainExitVisualization => Globals.GetValueOrDefault("chainexitvisualization") ?? "CORRUPTED_DUNGEON_CHAIN_EXIT";

    /// <summary>A number of the globalsettings (safeperiod 60, rollinterval 30, minimuminvaderspawndistance 120, expeltimeafterinvasionend 30, shrinetimelimit 45); <paramref name="fallback"/> without it.</summary>
    public double Global(string name, double fallback) =>
        double.TryParse(Globals.GetValueOrDefault(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>The spells of an invasion's start and end on both sides (pvpmatchstartspell, pvpmatchendspell: full health and energy, cooldowns reset).</summary>
    public string MatchStartSpell => Globals.GetValueOrDefault("pvpmatchstartspell") ?? "CORRUPTED_MATCH_START_SPELL";
    public string MatchEndSpell => Globals.GetValueOrDefault("pvpmatchendspell") ?? "CORRUPTED_MATCH_END_SPELL";

    /// <summary>The difficulty of index <paramref name="index"/>; null out of range.</summary>
    public Difficulty? At(int index) => index >= 0 && index < Difficulties.Count ? Difficulties[index] : null;

    /// <summary>The difficulty of cluster type <paramref name="clusterType"/> (CORRUPTED_DUNGEON_PRO …); null for any other.</summary>
    public Difficulty? OfClusterType(string? clusterType) => Difficulties.FirstOrDefault(d => d.ClusterType == clusterType);

    /// <summary>The index of entrance spell <paramref name="spell"/> (ENTER_CURRUPTED_DUNGEON_SPELL_1..n → 0..n−1); null for any other.</summary>
    public int? IndexOfSpell(string? spell) => Difficulties.FirstOrDefault(d => EnterSpell(d.Index) == spell)?.Index;

    /// <summary>The milestones above <paramref name="before"/> and at most <paramref name="after"/> (crossed by a gain).</summary>
    public IEnumerable<long> Crossed(long before, long after) => Milestones.Where(m => m > before && m <= after);

    /// <summary>Reads corrupteddungeons.xml's root.</summary>
    public static CorruptedData Load(XElement root)
    {
        static string S(XElement e, string a) => ((string?)e.Attribute(a) ?? "").Trim();
        static double D(XElement e, string a) => double.TryParse(S(e, a), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
        static long L(XElement e, string a) => long.TryParse(S(e, a), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0;
        var difficulties = new List<Difficulty>();
        foreach (var e in root.Element("difficulty")?.Elements("difficultylevel") ?? [])
            difficulties.Add(new Difficulty(difficulties.Count, S(e, "name"), S(e, "displayname"), (int)L(e, "tier"), S(e, "clustertype"),
                S(e, "playersarelethalinentranceclusterrequirement").Equals("true", StringComparison.OrdinalIgnoreCase),
                S(e, "accessiblefromarchetype").Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal),
                D(e, "itempowerrequirement"), L(e, "infamyrequirement"), D(e, "rewardconstant"), D(e, "rewardceiling"), D(e, "rewardscaling"),
                L(e, "infamykillbonus"), (int)L(e, "pointsrequirement"), (int)L(e, "objectiveclearrequirement"), D(e, "exitdungeontime"), S(e, "mightreward"))
            {
                ChancePve = D(e, "chanceperrollpve"), ChancePvp = D(e, "chanceperrollpvp"), PvpInfamyLoss = D(e, "pvpinfamyloss"), PvpInfamyGain = D(e, "pvpinfamygain"),
                BossLootPercent = D(e, "percentbosslootonkill"), AntiResetSpawnSeconds = D(e, "timeuntilantiresetmobspawninseconds"),
                AntiResetDespawnSeconds = D(e, "timeuntilantiresetmobdespawninseconds"), AntiResetDistance = D(e, "antiresetmobrequiredplayerdistanceforspawn"),
            });
        var maps = (root.Element("corrupteddungeonmaps")?.Elements("map") ?? []).Select(m => (S(m, "id"), m.Attribute("weight") is null ? 1 : D(m, "weight")))
            .Where(m => m.Item1.Length > 0).ToList();
        var milestones = (root.Element("infamymilestones")?.Elements("milestone") ?? []).Select(m => L(m, "value")).Where(v => v > 0).Order().ToList();
        var globals = root.Element("globalsettings")?.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value.Trim(), StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        return new CorruptedData(difficulties, maps, milestones, globals);
    }
}
