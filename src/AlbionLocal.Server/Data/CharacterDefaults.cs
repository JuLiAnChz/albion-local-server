using System.Globalization;
using System.Xml.Linq;

namespace AlbionLocal.Server.Data;

/// <summary>
/// The focus, learning point and premium values of characters.xml &lt;Characters&gt;&lt;DefaultValues&gt; (analysis/work/stageJ/focus-lp/SPEC.md
/// §1.1): the focus cap and regeneration per day (without / with premium), the learning point cap, start and seconds per point (without /
/// with premium), the auto-learn fame factor (PROGRESSION.md §4: A5), and the premium factors. The client parses only the learning point
/// and premium* attributes (b3a.ms 0x1807E3C90); it knows the focus numbers from the Join keys. <see cref="Default"/> = this build's values,
/// used when the file or an attribute is missing.
/// </summary>
public sealed record CharacterDefaults(
    double FocusMax, double FocusMaxPremium, double FocusAtStart, double FocusRegenPerDay, double FocusRegenPerDayPremium,
    double LearningPointsMax, double LearningPointsMaxPremium, double LearningPointsAtStart, double SecondsPerPoint, double SecondsPerPointPremium,
    double LearningPointFameFactor, double PremiumFameFactor, double PremiumSilverFactor, double PremiumMobLootFactor, double PremiumGatherBonusChance,
    double PremiumCropYieldFactor, double PremiumAnimalGrowthFactor, double PremiumMarketFeeFactor)
{
    public static CharacterDefaults Default { get; } = new(30000, 30000, 0, 0, 10000, 999999, 999999, 0, 0, 2880, 5, 1.5, 1.5, 1.5, 0.5, 2, 0.5, 0.5);

    /// <summary>From &lt;DefaultValues&gt; (null = none: <see cref="Default"/>); an attribute that is missing or does not parse keeps its default.</summary>
    public static CharacterDefaults From(XElement? dv)
    {
        var d = Default;
        if (dv is null) return d;
        double Num(string name, double fallback) =>
            double.TryParse(((string?)dv.Attribute(name))?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        var max = Num("craftingfocusmax", d.FocusMax);
        return new CharacterDefaults(
            max, Num("premiumcraftingfocusmax", max), Num("craftingfocusatstart", d.FocusAtStart),
            Num("craftingfocusregenerationperday", d.FocusRegenPerDay), Num("premiumcraftingfocusregenerationperday", d.FocusRegenPerDayPremium),
            Num("learningpointsmax", d.LearningPointsMax), Num("learningpointsmaxpremium", d.LearningPointsMaxPremium),
            Num("learningpointsatstart", d.LearningPointsAtStart),
            Num("learningpointsecondsperpoint", d.SecondsPerPoint), Num("learningpointsecondsperpointpremium", d.SecondsPerPointPremium),
            Num("learningpointfamefactor", d.LearningPointFameFactor), Num("premiumdestinyboardprogressionfactor", d.PremiumFameFactor),
            Num("premiumsilverlootfactor", d.PremiumSilverFactor), Num("premiummoblootfactor", d.PremiumMobLootFactor),
            Num("premiumgatheringyieldbonuschance", d.PremiumGatherBonusChance), Num("premiumcropyieldfactor", d.PremiumCropYieldFactor),
            Num("premiumanimalgrowthfactor", d.PremiumAnimalGrowthFactor), Num("premiummarkettransactionfeefactor", d.PremiumMarketFeeFactor));
    }
}
