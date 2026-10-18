using System.Globalization;
using System.Xml.Linq;

namespace AlbionLocal.Server.Data;

/// <summary>
/// personalseasons.xml, the Adventurer's Challenge (analysis/work/stageH/journals/SPEC.md §C.1): one season per calendar month
/// (activemonth 1-12, uniquename ADCSEASON_01.._12), each with its point factors per activity, the flattening exponent and three
/// trackers (daily, main, secondary), whose reward triggers unlock at a point total (psp, whole points) from an unlock day of the
/// season. The season id on the wire is "&lt;uniquename&gt;@&lt;year&gt;" (ADCSEASON_10@2026), the season of the UTC month; it starts on day 1
/// of its month at 00:00 UTC (rewardresettime 0UTC) and ends with the month. Read with the journal (<see cref="ClusterStore.Seasons"/>);
/// immutable.
/// </summary>
public sealed class PersonalSeasonData
{
    /// <summary>A reward of a trigger: itemreward (an item × amount), pspreward (points to the main and secondary trackers) or learningpointsreward.</summary>
    public sealed record Reward(string Id, string Kind, string? Item, long Amount, bool BoughtPremium, bool Premium, bool NonPremium)
    {
        /// <summary>
        /// Whether a character may claim it (brj.c.h 0x18072dea0, shared client code, SPEC §C.1): nonpremium and not premium, or premium
        /// and premium, or boughtpremium with premium for more than 6 days.
        /// </summary>
        public bool Eligible(bool isPremium, int premiumDays) =>
            (NonPremium && !isPremium) || (Premium && isPremium) || (BoughtPremium && isPremium && premiumDays > 6);
    }

    /// <summary>A rewardtrigger: its point total (a7m raw), the season day from which it counts, its rewards.</summary>
    public sealed record Trigger(long Psp, int UnlockDay, IReadOnlyList<Reward> Rewards);

    /// <summary>A psptracker: name, type (daily, main, secondary), its reset interval in days (0 = never) and its triggers.</summary>
    public sealed record Tracker(string Name, string Type, int ResetDays, IReadOnlyList<Trigger> Triggers)
    {
        /// <summary>The tracker's cap on season day <paramref name="day"/> (a35, SPEC §C.4): the highest psp of the triggers whose unlockday has come; 0 outside the season.</summary>
        public long Cap(int day) => day < 0 ? 0 : Triggers.Where(t => t.UnlockDay <= day).Select(t => t.Psp).DefaultIfEmpty(0).Max();
    }

    /// <summary>A personalseason: its uniquename, month, factors (pve, gathering, fishing, farming), exponent and trackers.</summary>
    public sealed record Season(string Name, int Month, double Pve, double Gathering, double Fishing, double Farming, double Exponent,
        IReadOnlyList<Tracker> Trackers)
    {
        /// <summary>The reward of id <paramref name="id"/> with its tracker and trigger; null = none of this season.</summary>
        public (Tracker Tracker, Trigger Trigger, Reward Reward)? Find(string id)
        {
            foreach (var tracker in Trackers)
                foreach (var trigger in tracker.Triggers)
                    if (trigger.Rewards.FirstOrDefault(r => r.Id == id) is { } r) return (tracker, trigger, r);
            return null;
        }

        /// <summary>Every reward of the season, tracker by tracker in file order.</summary>
        public IEnumerable<Reward> Rewards => Trackers.SelectMany(t => t.Triggers).SelectMany(t => t.Rewards);
    }

    private PersonalSeasonData(IReadOnlyList<Season> seasons) => Seasons = seasons;

    /// <summary>The seasons in file order (one per month).</summary>
    public IReadOnlyList<Season> Seasons { get; }

    /// <summary>The season of the month of <paramref name="utc"/>; null when the file has none for it.</summary>
    public Season? Of(DateTime utc) => Seasons.FirstOrDefault(s => s.Month == utc.Month);

    /// <summary>The wire id of <paramref name="utc"/>'s season: "ADCSEASON_10@2026"; null when there is none.</summary>
    public string? IdOf(DateTime utc) => Of(utc) is { } s ? $"{s.Name}@{utc.Year}" : null;

    /// <summary>The season and its first moment (UTC) for a wire id "&lt;name&gt;@&lt;year&gt;"; null for an unknown one.</summary>
    public (Season Season, DateTime Start)? Parse(string? id)
    {
        if (id?.Split('@') is not [var name, var y] || !int.TryParse(y, NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year is < 2000 or > 9999) return null;
        return Seasons.FirstOrDefault(s => s.Name == name) is { } s ? (s, new DateTime(year, s.Month, 1, 0, 0, 0, DateTimeKind.Utc)) : null;
    }

    /// <summary>The first moment of <paramref name="utc"/>'s season: day 1 of its month, 00:00 UTC.</summary>
    public static DateTime StartOf(DateTime utc) => new(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The season day of <paramref name="utc"/> in the season that starts at <paramref name="start"/> (whole days since its start); -1 outside it.</summary>
    public static int DayOf(DateTime start, DateTime utc) => utc < start || utc >= start.AddMonths(1) ? -1 : (int)(utc - start).TotalDays;

    public static PersonalSeasonData Load(XElement root)
    {
        static double D(XElement e, string a, double fallback) =>
            double.TryParse((string?)e.Attribute(a), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        static bool B(XElement e, string a, bool fallback) => (string?)e.Attribute(a) is { } v ? v.Equals("true", StringComparison.OrdinalIgnoreCase) : fallback;
        var seasons = new List<Season>();
        foreach (var s in root.Elements("personalseason"))
        {
            var trackers = new List<Tracker>();
            foreach (var t in s.Element("psptrackers")?.Elements("psptracker") ?? [])
            {
                var triggers = new List<Trigger>();
                foreach (var g in t.Elements("rewardtrigger"))
                {
                    // The reward flags' parser defaults (0x18072ded0): boughtpremium and premium true, nonpremium false.
                    var rewards = (g.Element("rewards")?.Elements() ?? []).Where(r => (string?)r.Attribute("id") is { Length: > 0 }).Select(r => new Reward(
                        (string)r.Attribute("id")!, r.Name.LocalName, (string?)r.Attribute("itemref"), (long)D(r, "amount", 1),
                        B(r, "boughtpremium", true), B(r, "premium", true), B(r, "nonpremium", false))).ToList();
                    triggers.Add(new Trigger((long)Math.Round(D(g, "psp", 0) * 10000), (int)D(g, "unlockday", 0), rewards));
                }
                trackers.Add(new Tracker(XmlAttr.Str(t, "uniquename"), XmlAttr.Str(t, "type"), (int)D(t, "resetintervalindays", 0), triggers));
            }
            seasons.Add(new Season(XmlAttr.Str(s, "uniquename"), (int)D(s, "activemonth", 0), D(s, "pve", 1.5), D(s, "gathering", 3), D(s, "fishing", 3),
                D(s, "farming", 1.3), D(s, "pspflatteningcurveexponent", 0.5), trackers));
        }
        return new PersonalSeasonData(seasons);
    }
}
