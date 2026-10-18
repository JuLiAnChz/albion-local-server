using System.Globalization;
using System.Xml.Linq;
using AlbionLocal.Photon;

namespace AlbionLocal.Server.Data;

/// <summary>
/// One &lt;mob&gt; of a spawn behaviour block: the mobs.xml mob (null = not in mobs.xml), its weight (1 when missing; 22 are fractions),
/// whether it only comes as an upgrade, and its mobUpgradeLevelIndex (0 when missing; the boss points' CHAMPION_B 4, MINIBOSS 6, BOSS 8,
/// which pick a mob-upgrade chest, stageH bosses SPEC §4.3).
/// </summary>
public sealed record SpawnEntry(string Name, MobTemplate? Mob, double Weight, bool OnlyAsUpgrade, int UpgradeLevel = 0);

/// <summary>
/// One &lt;clustertype&gt; (or the &lt;default&gt;) of a spawn behaviour: the conditions it holds for (tier 0 = any; a
/// continents list, a clustertype; null = unrestricted) and what a point using it spawns and how long it waits to respawn.
/// </summary>
public sealed record SpawnBlock
{
    public required string Behavior { get; init; }

    /// <summary>The &lt;default&gt; block: the fallback when no &lt;clustertype&gt; matches.</summary>
    public bool IsDefault { get; init; }

    /// <summary>Its place in the behaviour (document order; the default after the clustertypes): for logs.</summary>
    public int Number { get; init; }

    public int Tier { get; init; }

    public IReadOnlyList<string>? Continents { get; init; }

    public string? ClusterType { get; init; }

    public float RespawnSecondsMin { get; init; }

    public float RespawnSecondsMax { get; init; }

    public IReadOnlyList<SpawnEntry> Mobs { get; init; } = [];

    /// <summary>faction (UNDEAD on the static points, UNDEAD_CAMP on the camps; null when missing): whose chests and camp quests its mobs count for.</summary>
    public string? Faction { get; init; }

    /// <summary>triggersmobupgradespawnpointrespawn: a mob of it that dies near a mob-upgrade chest spot spawns that chest (stageH bosses SPEC §4.3).</summary>
    public bool TriggersMobUpgrade { get; init; }

    /// <summary>isquestmobspawn (the camp points): its mobs count for the camp's local quest.</summary>
    public bool IsQuestMobSpawn { get; init; }

    /// <summary>
    /// allowUpgradeByChest (the road camps, faction ROAD): its point spawns the entries of the mobUpgradeLevelIndex of the road chest in reach
    /// (4 / 6 / 8, stageL roads SPEC §5.5 rule 1), the lowest without one (<see cref="PickAt"/>).
    /// </summary>
    public bool AllowUpgradeByChest { get; init; }

    /// <summary>The lowest mobUpgradeLevelIndex of its mobs (4 on the road camps; 0 without any).</summary>
    public int LowestLevel => Mobs.Where(e => e.Mob is not null).Select(e => e.UpgradeLevel).DefaultIfEmpty(0).Min();

    /// <summary>Whether it has <paramref name="mob"/> at mobUpgradeLevelIndex <paramref name="level"/> (a scout comes at 4, 6 and 8 alike).</summary>
    public bool HasAt(MobTemplate mob, int level) => Mobs.Any(e => e.Mob == mob && e.UpgradeLevel == level);

    /// <summary>A weighted pick of its mobs of mobUpgradeLevelIndex <paramref name="level"/> (onlyAsUpgrade too); the plain <see cref="Pick"/> when none has it.</summary>
    public MobTemplate? PickAt(Random random, int level, double eliteWeight = 1)
    {
        var at = Mobs.Where(e => e is { Mob: not null, Weight: > 0 } && e.UpgradeLevel == level).ToList();
        if (at.Count == 0) return Pick(random, eliteWeight);
        return DungeonGenerator.Pick(random, [.. at.Select(e => (e.Mob!, e.Mob!.IsElite ? e.Weight * eliteWeight : e.Weight))]);
    }

    /// <summary>The mobUpgradeLevelIndex of <paramref name="mob"/> in this block (0 when it has none or is not in it).</summary>
    public int UpgradeLevelOf(MobTemplate mob) => Mobs.FirstOrDefault(e => e.Mob == mob)?.UpgradeLevel ?? 0;

    /// <summary>Whether a cluster of <paramref name="tier"/>, <paramref name="continent"/> (Royal, Outlands, …) and world.xml type <paramref name="clusterType"/> may use it.</summary>
    public bool Fits(int tier, string continent, string? clusterType) =>
        (Tier == 0 || Tier == tier)
        && (Continents is null || Continents.Contains(continent, StringComparer.OrdinalIgnoreCase))
        && (ClusterType is null || string.Equals(ClusterType, clusterType, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A plain weighted pick over the mobs of mobs.xml that do not come only as an upgrade (SPEC §5a.2; the upgrade levels of
    /// the official points are open, §9 Q1); null when none is left. <paramref name="eliteWeight"/> (Rates:ChampionWeight, stageH bosses
    /// SPEC §6.2) multiplies the weight of the champion, miniboss and boss mobs (<see cref="MobTemplate.IsElite"/>): 3 makes a
    /// CHAMPION of weight 3 next to 62 and 35 count 9.
    /// </summary>
    public MobTemplate? Pick(Random random, double eliteWeight = 1)
    {
        double W(SpawnEntry e) => e.Mob!.IsElite ? e.Weight * eliteWeight : e.Weight;
        var total = 0.0;
        foreach (var e in Mobs)
            if (e is { Mob: not null, OnlyAsUpgrade: false, Weight: > 0 }) total += W(e);
        if (total <= 0) return null;
        var r = random.NextDouble() * total;
        foreach (var e in Mobs)
        {
            if (e is not { Mob: not null, OnlyAsUpgrade: false, Weight: > 0 }) continue;
            if (r < W(e)) return e.Mob;
            r -= W(e);
        }
        return Mobs.Last(e => e is { Mob: not null, OnlyAsUpgrade: false, Weight: > 0 }).Mob; // r rounded up to the total
    }

    public override string ToString() => IsDefault ? $"{Behavior} default"
        : string.Create(CultureInfo.InvariantCulture, $"{Behavior} #{Number} (tier {Tier}{(Continents is null ? "" : ", " + string.Join("/", Continents))}{(ClusterType is null ? "" : ", " + ClusterType)})");
}

/// <summary>
/// randomspawnbehaviors.xml (stage 5 SPEC §5a.2): the blocks of each &lt;spawnbehavior&gt; by name, its mob names bound to
/// mobs.xml. A RANDOM spawn point takes the first block in document order that fits its cluster (client bnx.abo
/// 0x1806ff490: tier 0 or the cluster's, continents unset or containing the cluster's, clustertype unset or equal; the
/// file sets no biome or pvp warning level, which the client also tests), else the default block. Immutable.
/// </summary>
public sealed class SpawnBehaviors
{
    private readonly Dictionary<string, IReadOnlyList<SpawnBlock>> _byName;

    private SpawnBehaviors(Dictionary<string, IReadOnlyList<SpawnBlock>> byName) => _byName = byName;

    public int Count => _byName.Count;

    public bool Contains(string behavior) => _byName.ContainsKey(behavior);

    /// <summary>The blocks of <paramref name="behavior"/> in document order, the default last; empty for an unknown name.</summary>
    public IReadOnlyList<SpawnBlock> Blocks(string behavior) => _byName.GetValueOrDefault(behavior) ?? [];

    /// <summary>The block a RANDOM point of <paramref name="behavior"/> uses in the cluster; null for an unknown behaviour or one with no fitting block.</summary>
    public SpawnBlock? Select(string behavior, int tier, string continent, string? clusterType)
    {
        var blocks = Blocks(behavior);
        return blocks.FirstOrDefault(b => !b.IsDefault && b.Fits(tier, continent, clusterType)) ?? blocks.FirstOrDefault(b => b.IsDefault);
    }

    /// <summary>randomspawnbehaviors.bin, with the mob names looked up in <paramref name="mobs"/> (a name it does not have stays, with Mob null, and is never picked).</summary>
    public static SpawnBehaviors Load(GameDataFile file, MobStore mobs)
    {
        var numbers = new NumberReader(file.Name);
        var byName = new Dictionary<string, IReadOnlyList<SpawnBlock>>(StringComparer.Ordinal);
        var unknown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var behavior in file.Elements())
        {
            if (behavior.Name.LocalName != "spawnbehavior" || (string?)behavior.Attribute("name") is not { } name) continue;
            var blocks = new List<SpawnBlock>();
            foreach (var b in behavior.Elements())
            {
                var isDefault = b.Name.LocalName == "default";
                if (!isDefault && b.Name.LocalName != "clustertype") continue;
                string? A(XElement e, string a) => (string?)e.Attribute(a) is { Length: > 0 } v ? v : null;
                var entries = b.Elements("mob").Select(m =>
                {
                    var mob = A(m, "name") ?? "";
                    var template = mobs.Find(mob);
                    if (template is null && mob.Length > 0) unknown.Add(mob);
                    return new SpawnEntry(mob, template, numbers.Num(A(m, "weight"), "weight", name, 1),
                        string.Equals(A(m, "onlyAsUpgrade"), "true", StringComparison.OrdinalIgnoreCase),
                        numbers.Int(A(m, "mobUpgradeLevelIndex"), "mobUpgradeLevelIndex", name));
                }).ToList();
                blocks.Add(new SpawnBlock
                {
                    Behavior = name, IsDefault = isDefault, Number = blocks.Count,
                    Tier = numbers.Int(A(b, "tier"), "tier", name),
                    Continents = A(b, "continents") is { } c ? c.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries) : null,
                    ClusterType = A(b, "clustertype"),
                    RespawnSecondsMin = (float)numbers.Num(A(b, "respawntimesecondsmin"), "respawntimesecondsmin", name),
                    RespawnSecondsMax = (float)numbers.Num(A(b, "respawntimesecondsmax"), "respawntimesecondsmax", name),
                    Mobs = entries,
                    Faction = A(b, "faction"),
                    TriggersMobUpgrade = string.Equals(A(b, "triggersmobupgradespawnpointrespawn"), "true", StringComparison.OrdinalIgnoreCase),
                    IsQuestMobSpawn = string.Equals(A(b, "isquestmobspawn"), "true", StringComparison.OrdinalIgnoreCase),
                    AllowUpgradeByChest = string.Equals(A(b, "allowUpgradeByChest"), "true", StringComparison.OrdinalIgnoreCase),
                });
            }
            // The default last, whatever its place: it is only the fallback.
            byName.TryAdd(name, [.. blocks.Where(x => !x.IsDefault), .. blocks.Where(x => x.IsDefault)]);
        }
        numbers.Report();
        if (unknown.Count > 0)
            Log.Debug("data", $"{file.Name}: {unknown.Count} mob name(s) not in mobs.xml, never spawned (first {unknown.Order(StringComparer.Ordinal).First()})");
        return new SpawnBehaviors(byName);
    }
}
