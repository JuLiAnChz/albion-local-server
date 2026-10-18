using System.Globalization;
using System.Xml.Linq;

namespace AlbionLocal.Server.Data;

/// <summary>
/// worldsettings.xml &lt;islands&gt; (stageH islands SPEC §0.1, client parser bgz.j 0x180693D10): the 13 island types, numbered 1-based in
/// document order as the client's static counter does (2 PLAYER_ISLAND_LYMHURST, 9 GUILD_ISLAND_LYMHURST; GetIslandInfos key 2), each
/// with its city, premium requirement, showroom and the six level templates (level 0-5 = world.xml cluster ISLAND-…-0001a…f) with
/// their prices. Plus the island instance ids: "@ISLAND@" + the island Guid (SPEC §0.3, a4l prefix list ISLAND = 1).
/// </summary>
public sealed class IslandData
{
    /// <summary>"@ISLAND@": a4l.f(Guid) = "@" + ClusterInstanceType 1 + "@" + guid (SPEC §0.3).</summary>
    public const string InstancePrefix = "@ISLAND@";

    private IslandData(IReadOnlyList<IslandType> types) => Types = types;

    /// <summary>The island types in document order (<see cref="IslandType.Id"/> = index + 1).</summary>
    public IReadOnlyList<IslandType> Types { get; }

    /// <summary>The type of GetIslandInfos key 2 <paramref name="id"/>; null when there is none.</summary>
    public IslandType? TypeAt(int id) => id >= 1 && id <= Types.Count ? Types[id - 1] : null;

    /// <summary>The personal (kind 0) island type of <paramref name="city"/> (its world.xml id, "1000"); null when the city has none.</summary>
    public IslandType? PersonalOf(string city) => Types.FirstOrDefault(t => t.Kind == 0 && t.City == city);

    /// <summary>The instance id of island <paramref name="guid"/> ("@ISLAND@&lt;guid&gt;", Guid "D" form, lower case: 44 characters, as captured in 103829).</summary>
    public static string InstanceId(Guid guid) => InstancePrefix + guid.ToString("D");

    /// <summary>Whether <paramref name="cluster"/> is an island instance id.</summary>
    public static bool IsInstance(string? cluster) => cluster?.StartsWith(InstancePrefix, StringComparison.Ordinal) == true;

    /// <summary>The island Guid of instance id <paramref name="cluster"/>; null when it is none (Join key 82).</summary>
    public static Guid? GuidOf(string? cluster) =>
        IsInstance(cluster) && Guid.TryParse(cluster![InstancePrefix.Length..], out var g) ? g : null;

    /// <summary>Reads the &lt;island&gt; elements of worldsettings.xml's &lt;islands&gt;; none = no types (no island can be given).</summary>
    public static IslandData Load(XElement root)
    {
        var types = new List<IslandType>();
        foreach (var e in root.Descendants("islands").Elements("island"))
        {
            var name = (string?)e.Attribute("name") ?? "";
            var levels = e.Elements("cluster").Select(c => new IslandLevel(
                    Int((string?)c.Attribute("level")), (string?)c.Attribute("name") ?? "",
                    Long((string?)c.Attribute("silverprice")), Long((string?)c.Attribute("firstsilverprice"))))
                .OrderBy(l => l.Level).ToList();
            types.Add(new IslandType(types.Count + 1, name, (string?)e.Attribute("city") ?? "",
                name.StartsWith("GUILD_", StringComparison.Ordinal) ? 1 : 0, Int((string?)e.Attribute("premiumrequirement")),
                (string?)e.Attribute("showroomcluster") is { Length: > 0 } showroom ? showroom : null, levels));
        }
        return new IslandData(types);
    }

    private static int Int(string? text) => int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0;

    private static long Long(string? text) => long.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0;
}

/// <summary>
/// One island type (bgz): <see cref="Id"/> the 1-based document position, <see cref="Kind"/> 0 personal (PLAYER_*) or 1 guild (GUILD_*,
/// bgz+0x28), <see cref="City"/> the city's world.xml id, the premium days the buy window asks for and the showroom cluster (none for
/// Caerleon and the Mists).
/// </summary>
public sealed record IslandType(int Id, string Name, string City, int Kind, int PremiumRequirement, string? Showroom, IReadOnlyList<IslandLevel> Levels)
{
    /// <summary>The highest level (bgz.t(): 5).</summary>
    public int MaxLevel => Levels.Count == 0 ? 0 : Levels.Max(l => l.Level);

    /// <summary>Level <paramref name="level"/> (bgz.l); null when the type has none.</summary>
    public IslandLevel? LevelAt(int level) => Levels.FirstOrDefault(l => l.Level == level);
}

/// <summary>One &lt;cluster&gt; of an island type: the level (0-5), its world.xml template cluster and the prices in silver (§0.1).</summary>
public sealed record IslandLevel(int Level, string Cluster, long SilverPrice, long FirstSilverPrice);
