using System.Globalization;
using System.Xml.Linq;

namespace AlbionLocal.Server.Data;

/// <summary>
/// One boss of worldbosses.xml (analysis/work/stageH/bosses/SPEC.md §1.1): its name (the worldboss attribute of a WORLDBOSS spawn
/// point), its mobs.xml mob and its spawn times. &lt;SpawnTimes startdate blockeduntil&gt; holds &lt;day number&gt; lists of
/// &lt;SpawnTime utctime&gt;; the day of a date is floor((date − startdate) / 1 day) mod the day count + 1, so the base file's one day
/// of twelve odd hours is every day's. Immutable.
/// </summary>
public sealed record WorldBossType(string Name, string Mob, DateTime StartDate, DateTime? BlockedUntil, IReadOnlyDictionary<int, TimeSpan[]> Days)
{
    /// <summary>The number of days of the cycle (the highest day number; 0 = no spawn time at all).</summary>
    public int DayCount => Days.Count == 0 ? 0 : Days.Keys.Max();

    /// <summary>The day number (1..<see cref="DayCount"/>) of the UTC date of <paramref name="utc"/>.</summary>
    public int DayOf(DateTime utc)
    {
        var n = DayCount;
        if (n == 0) return 0;
        var days = (long)Math.Floor((utc.Date - StartDate.Date).TotalDays);
        return (int)(((days % n) + n) % n) + 1;
    }

    /// <summary>
    /// The first data spawn time strictly after <paramref name="after"/> (UTC), not before blockeduntil; null when the data has no
    /// spawn time (a day list without times is skipped, so a cycle whose days are all empty gives null).
    /// </summary>
    public DateTime? NextSpawn(DateTime after)
    {
        if (DayCount == 0) return null;
        if (BlockedUntil is { } blocked && blocked > after) after = blocked.AddTicks(-1);
        for (var k = 0; k <= DayCount + 1; k++)
        {
            var date = after.Date.AddDays(k);
            if (!Days.TryGetValue(DayOf(date), out var times)) continue;
            foreach (var t in times)
                if (date + t > after) return DateTime.SpecifyKind(date + t, DateTimeKind.Utc);
        }
        return null;
    }
}

/// <summary>
/// worldbosses.xml (or the regional worldbosses_asia.xml, SPEC §10 Q8: the even hours) by boss name. Read at the server start
/// (a file of under 1 KB). Immutable.
/// </summary>
public sealed class WorldBossData
{
    public static WorldBossData Empty { get; } = new([]);

    private WorldBossData(Dictionary<string, WorldBossType> bosses) => Bosses = bosses;

    public IReadOnlyDictionary<string, WorldBossType> Bosses { get; }

    /// <summary>The boss named <paramref name="name"/> (WORLD_BOSS_T5_KEEPER; any case), or null.</summary>
    public WorldBossType? Find(string name) =>
        Bosses.TryGetValue(name, out var b) ? b : Bosses.Values.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static WorldBossData Load(XElement root)
    {
        var bosses = new Dictionary<string, WorldBossType>(StringComparer.Ordinal);
        static DateTime Date(string? s) => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : DateTime.MinValue;
        foreach (var b in root.Elements("boss"))
        {
            if ((string?)b.Attribute("name") is not { Length: > 0 } name || (string?)b.Attribute("mob") is not { Length: > 0 } mob) continue;
            var times = b.Element("SpawnTimes");
            var days = new Dictionary<int, TimeSpan[]>();
            foreach (var day in times?.Elements("day") ?? [])
            {
                if (!int.TryParse((string?)day.Attribute("number"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < 1) continue;
                days[number] = [.. day.Elements("SpawnTime")
                    .Select(t => TimeSpan.TryParse((string?)t.Attribute("utctime"), CultureInfo.InvariantCulture, out var at) ? at : (TimeSpan?)null)
                    .OfType<TimeSpan>().Where(t => t >= TimeSpan.Zero && t < TimeSpan.FromDays(1)).Order()];
            }
            var blocked = (string?)times?.Attribute("blockeduntil") is { Length: > 0 } u ? Date(u) : (DateTime?)null;
            bosses[name] = new WorldBossType(name, mob, Date((string?)times?.Attribute("startdate")), blocked, days);
        }
        return new WorldBossData(bosses);
    }
}
