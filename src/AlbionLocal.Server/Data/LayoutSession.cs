using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Data;

/// <summary>
/// What the client lays a cluster's open world out by, besides its data files (stage 4b, analysis/work/stage4b/SPEC.md §2,
/// "SPEC" in this file): <see cref="Key57"/> = Login key 57 (only its UTC date counts), <see cref="Flags"/> = Login key 79,
/// <see cref="DynamicTemplates"/> = Login key 99[112]. The resolver (<see cref="ClusterTemplate.Resolve"/>) rolls the
/// dynamic templates and places the resource nodes with them; they must be exactly what the Login sent
/// (<see cref="Login"/>). A different date gives a different layout, which the client would not bind to.
/// </summary>
public sealed record LayoutSession(long Key57, IReadOnlyList<string> Flags, bool DynamicTemplates)
{
    /// <summary>The values our Login sends (<see cref="LoginWorld"/>): the server start, no flags, feature 112 as in the table.</summary>
    public static LayoutSession Login => new(LoginWorld.Key57, LoginWorld.Flags, LoginWorld.Features()[FeatureSwitches.DynamicTemplates]);

    /// <summary>The flags lower-cased, as the client compares them (null when none).</summary>
    public IReadOnlySet<string>? LowerFlags => Flags.Count == 0 ? null : Flags.Select(f => f.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

    /// <summary>The UTC date of <see cref="Key57"/>, for logs.</summary>
    public string Day => new DateTime(DaySeed.DayTicks(Key57), DateTimeKind.Utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>
/// d0.a(GameTimeStamp, string) 0x1808B8260 (SPEC §4.1): seed = BitConverter.ToInt32(MD5(UTF8(s + day.Ticks.ToString())), 0),
/// day = the GameTimeStamp clamped to the DateTime range, cut to its date (UTC ticks, no time zone).
/// </summary>
public static class DaySeed
{
    public static long DayTicks(long ticks)
    {
        var t = Math.Clamp(ticks, 0, DateTime.MaxValue.Ticks);
        return t - t % TimeSpan.TicksPerDay;
    }

    public static string Text(string s, long ticks) => s + DayTicks(ticks).ToString(CultureInfo.InvariantCulture);

    public static int Of(string s, long ticks) => BitConverter.ToInt32(MD5.HashData(Encoding.UTF8.GetBytes(Text(s, ticks))), 0);
}

/// <summary>
/// The string order of the client's culture ignore-case comparer as far as the data needs it (SPEC §5.6.4; port sort_key):
/// '-' and '\'' ignored, punctuation &lt; digits &lt; letters, letters without case, then an ordinal upper-case tie break.
/// Not the .NET culture (ICU and NLS differ). The prefab lists and preset entries are sorted with it.
/// </summary>
public sealed class ClientCompare : IComparer<string>
{
    public static readonly ClientCompare Instance = new();

    public int Compare(string? x, string? y)
    {
        x ??= "";
        y ??= "";
        int i = 0, j = 0;
        while (true)
        {
            while (i < x.Length && x[i] is '-' or '\'') i++;
            while (j < y.Length && y[j] is '-' or '\'') j++;
            if (i == x.Length || j == y.Length) break;
            var c = Key(x[i]).CompareTo(Key(y[j]));
            if (c != 0) return c;
            i++;
            j++;
        }
        var rest = (i < x.Length).CompareTo(j < y.Length); // a shorter key first
        return rest != 0 ? rest : string.CompareOrdinal(x.ToUpperInvariant(), y.ToUpperInvariant());
    }

    private static (int Class, int Code) Key(char ch) =>
        char.IsDigit(ch) ? (2, ch) : char.IsLetter(ch) ? (3, char.ToUpperInvariant(ch)) : (1, ch);
}

/// <summary>
/// bgv.ClusterNameInformation (bgv.a(string, out CNI, bool) 0x18068E280 with the subbiome rule on; SPEC §4.1, port
/// parse_cni): the fields of a .cluster file name as the enum values the layout keys use. <see cref="ShortName"/> is the
/// dynamic-template seed string (ClusterNameInformation.b() 0x18067C4F0).
/// </summary>
public sealed record ClusterNameInfo(string Id, int Type, int Biome, int SubBiome, int Tier, int Faction, int Continent, int Quality)
{
    // bgv cctor 0x18068BD20: the alias tables, full name and short alias.
    internal static readonly Dictionary<int, string[]> Biomes = new()
    {
        [0] = ["Steppe", "ST"], [1] = ["Swamp", "SW"], [2] = ["Highland", "HL"], [3] = ["Mountain", "MN"], [4] = ["Forest", "FR"],
        [5] = ["Hell", "HE"], [6] = ["Crystal", "CL"], [7] = ["Roads", "RO"], [8] = ["Mists", "MI"], [9] = ["Dragon", "DR"],
    };
    internal static readonly Dictionary<int, string[]> Factions = new()
    {
        [0] = ["None", "NON"], [4] = ["Heretic", "HER"], [1] = ["Keeper", "KPR"], [2] = ["Morgana", "MOR"], [3] = ["Undead", "UND"], [5] = ["Avalon", "AVA"],
    };
    internal static readonly Dictionary<int, string[]> Types = new()
    {
        [0] = ["World", "WRL"], [1] = ["Dungeon", "DNG"], [2] = ["Expedition", "EXP"], [3] = ["Arena", "ARE"], [4] = ["Island", "ISL"],
        [5] = ["Hellgate", "HEL"], [6] = ["City", "CTY"], [7] = ["StartArea", "STA"], [8] = ["StartTown", "STT"], [9] = ["TutorialCluster", "TUT"],
        [10] = ["Harbor", "HRB"], [11] = ["HomeBase", "HBS"], [12] = ["Test", "TST"], [13] = ["Debug", "DBG"], [14] = ["PassageOverground", "PGO"],
        [15] = ["PassageUnderground", "PGU"], [16] = ["CharacterSelectArea", "CHA"], [17] = ["RandomDungeon", "RDG"], [18] = ["CorruptedDungeon", "COR"],
        [19] = ["Hideout", "HDO"], [20] = ["AvalonianRoads", "RDS"], [21] = ["Mists", "MIS"], [22] = ["MistsDungeon", "MID"], [23] = ["ResourceMists", "RMI"],
        [24] = ["HellDungeon", "HED"], [25] = ["DragonArea", "DRA"],
    };
    internal static readonly Dictionary<int, string[]> SubBiomes = new() { [0] = ["Green", "GRN"], [1] = ["Red", "RED"], [2] = ["Dead", "DED"], [-1] = ["None", "NON"] };
    internal static readonly Dictionary<int, string[]> Continents = new()
    {
        [1] = ["Royal", "ROY"], [2] = ["Outlands", "OUT"], [3] = ["Avalon", "AVA"], [4] = ["Mists", "MIS"], [5] = ["DragonLands", "DRL"], [0] = ["None", "NON"],
    };
    internal static readonly Dictionary<int, string[]> Qualities = new()
    {
        [1] = ["Quality01", "Q1"], [2] = ["Quality02", "Q2"], [3] = ["Quality03", "Q3"], [4] = ["Quality04", "Q4"], [5] = ["Quality05", "Q5"], [6] = ["Quality06", "Q6"],
    };

    // The enum names (Enum.ToString) the preset filters compare with, and Enum.TryParse's lower-case names.
    internal static readonly Dictionary<int, string> BiomeNames = new() { [-1] = "Invalid", [0] = "Steppe", [1] = "Swamp", [2] = "Highland", [3] = "Mountain", [4] = "Forest", [5] = "Hell", [6] = "Crystal", [7] = "Roads", [8] = "Mists", [9] = "Dragon" };
    internal static readonly Dictionary<int, string> ContinentNames = new() { [0] = "None", [1] = "Royal", [2] = "Outlands", [3] = "Avalon", [4] = "Mists", [5] = "DragonLands" };
    internal static readonly Dictionary<int, string> QualityNames = new() { [0] = "None", [1] = "Q1", [2] = "Q2", [3] = "Q3", [4] = "Q4", [5] = "Q5", [6] = "Q6" };
    internal static readonly Dictionary<int, string> PvpRuleNames = new() { [0] = "PvpDisabled", [1] = "FactionPvpOnly", [2] = "PvpAllowed", [3] = "PvpForced", [4] = "None" };
    internal static readonly Dictionary<int, string> FactionNames = new() { [0] = "None", [1] = "Keeper", [2] = "Morgana", [3] = "Undead", [4] = "Heretic", [5] = "Avalon" };

    /// <summary>bgv.a&lt;T&gt;(Dictionary&lt;T, string[]&gt;, string, T): an alias or full name, ignoring case; -1 for none.</summary>
    internal static int Lookup(Dictionary<int, string[]> table, string? token)
    {
        if (string.IsNullOrEmpty(token)) return -1;
        foreach (var (k, names) in table)
            if (names.Any(n => string.Equals(n, token, StringComparison.OrdinalIgnoreCase))) return k;
        return -1;
    }

    /// <summary>Enum.TryParse(s, ignoreCase: true) over <paramref name="names"/> (value → name); a number is taken as is.</summary>
    internal static int EnumParse(IReadOnlyDictionary<int, string> names, string? s, int fallback)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        var t = s.Trim();
        if (int.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) return n;
        foreach (var (k, name) in names)
            if (string.Equals(name, t, StringComparison.OrdinalIgnoreCase)) return k;
        return fallback;
    }

    /// <summary>Splits at any of <paramref name="separators"/>, dropping empty parts (the port's _split).</summary>
    internal static string[] Split(string s, string separators) => s.Split(separators.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

    /// <summary>"Replace(" ", "") then Split([',', ' '], RemoveEmpty)" (the port's split_list).</summary>
    internal static string[] SplitList(string s) => Split(s.Replace(" ", ""), ", ");

    /// <summary>The template folder of the subbiome (GREEN, RED, DEAD; NONE without one).</summary>
    public string Folder => SubBiome switch { 0 => "GREEN", 1 => "RED", 2 => "DEAD", _ => "NONE" };

    /// <summary>"{Id}_{type}_{biome}_{subbiome}_T{tier}_{faction}_{continent}" (+ "_{quality}" in the Outlands), short aliases ("Invalid" for none).</summary>
    public string ShortName
    {
        get
        {
            static string Alias(Dictionary<int, string[]> table, int v) => table.TryGetValue(v, out var names) ? names[1] : "Invalid";
            var s = string.Create(CultureInfo.InvariantCulture,
                $"{Id}_{Alias(Types, Type)}_{Alias(Biomes, Biome)}_{Alias(SubBiomes, SubBiome)}_T{Tier}_{Alias(Factions, Faction)}_{Alias(Continents, Continent)}");
            return Continent == 2 ? s + "_" + Alias(Qualities, Quality) : s;
        }
    }

    /// <summary>The faction's name upper-case ("UNDEAD"): the tag a rolled dynamic template gets (SPEC §4.3 step 6).</summary>
    public string FactionTag => FactionNames.GetValueOrDefault(Faction, Faction.ToString(CultureInfo.InvariantCulture)).ToUpperInvariant();

    /// <summary>bgv.a 0x18068E280: null when the name has too few parts or an unknown type, biome, tier, continent or quality.</summary>
    public static ClusterNameInfo? Parse(string file)
    {
        var t = Split(file, "_.");
        if (t.Length < 6) return null;
        var type = Lookup(Types, t[1]);
        if (type == -1) return null;
        var biome = Lookup(Biomes, t[2]);
        if (biome == -1) return null;
        if (!t[4].StartsWith('T') || !int.TryParse(t[4][1..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var tier)) return null;
        var sub = t[3] == "AUTO" ? tier <= 4 ? 0 : tier <= 6 ? 1 : 2 : Lookup(SubBiomes, t[3]);
        if (biome is 5 or 6 or 7 or 9) sub = -1;
        else if (sub is > 2 or < 0) sub = 0;
        var faction = Math.Max(Lookup(Factions, t[5]), 0);
        int continent = 0, quality = 0;
        if (type is 0 or 20 or 11 or 1 or 21 or 22 or 23 or 24 or 25)
        {
            if (t.Length <= 6) return null;
            continent = Math.Max(Lookup(Continents, t[6]), 0);
            if (continent == 0) return null;
            if (continent == 2)
            {
                if (t.Length <= 7) return null;
                quality = Math.Max(Lookup(Qualities, t[7]), 0);
                if (quality == 0) return null;
            }
        }
        return new ClusterNameInfo(t[0], type, biome, sub, tier, faction, continent, quality);
    }
}
