using System.Globalization;
using AlbionLocal.Photon;

namespace AlbionLocal.Server.Data;

/// <summary>
/// The numbers of one client XML file, read as the analysis tools read them (gen_gamedata.py float() and int(); missing
/// or empty = the default). Where Python stops on a value that does not parse, this reads the default and keeps the
/// value: <see cref="Report"/> then logs one warning per attribute with the number of elements and the first one
/// (principle 6: a bad data line is logged and skipped), so that a number format changed by an official update is named
/// at startup instead of turning into silent zeros.
/// </summary>
internal sealed class NumberReader(string file)
{
    private readonly Dictionary<string, (string Where, string Value, string Outcome, HashSet<string> Elements)> _bad = new(StringComparer.Ordinal);

    /// <summary>A float() number of <paramref name="where"/> (item name, achievement id, element); missing or empty = <paramref name="fallback"/>.</summary>
    public double Num(string? value, string attribute, string where, double fallback = 0)
    {
        if (string.IsNullOrEmpty(value)) return fallback;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        Note(attribute, where, value, string.Create(CultureInfo.InvariantCulture, $"read as {fallback}"));
        return fallback;
    }

    /// <summary>An int() number of <paramref name="where"/>; missing or empty = <paramref name="fallback"/>.</summary>
    public int Int(string? value, string attribute, string where, int fallback = 0)
    {
        if (string.IsNullOrEmpty(value)) return fallback;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        Note(attribute, where, value, string.Create(CultureInfo.InvariantCulture, $"read as {fallback}"));
        return fallback;
    }

    /// <summary>Keeps a value that cannot be used, for <see cref="Report"/>; <paramref name="outcome"/> says what was done instead.</summary>
    public void Note(string attribute, string where, string value, string outcome)
    {
        if (_bad.TryGetValue(attribute, out var bad)) bad.Elements.Add(where);
        else _bad[attribute] = (where, value.Length > 40 ? value[..40] + "..." : value, outcome, [where]);
    }

    /// <summary>Logs the kept values, one warning per attribute.</summary>
    public void Report()
    {
        foreach (var (attribute, bad) in _bad)
            Log.Warn("data", $"{file}: {attribute} unreadable in {bad.Elements.Count} element(s), {bad.Outcome} (first {bad.Where}: \"{bad.Value}\")");
    }
}
