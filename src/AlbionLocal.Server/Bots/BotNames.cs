using System.Text.RegularExpressions;
using AlbionLocal.Server.Content;

namespace AlbionLocal.Server.Bots;

/// <summary>
/// Stage I bots (bots-entity SPEC §2.7): names that look like players' ("Aldric", "Felwyn"): prefix + middle + suffix syllables of
/// content/bots.json, first letter upper and the rest lower, 5-12 letters, so that no name a client can type in its creation dialog is
/// unlike them. A drawn name is refused when <paramref name="taken"/> says so (a character's, a live bot's or human's, a reserved one);
/// after 50 tries there is none and the spawn is skipped.
/// </summary>
public static partial class BotNames
{
    /// <summary>The shape every bot name has.</summary>
    [GeneratedRegex("^[A-Z][a-z]{4,11}$")]
    public static partial Regex Shape();

    /// <summary>A free name, or null after <paramref name="tries"/> refused draws.</summary>
    public static string? Next(BotSettings settings, Random random, Func<string, bool> taken, int tries = 50)
    {
        for (var i = 0; i < tries; i++)
        {
            var raw = settings.NamePrefixes[random.Next(settings.NamePrefixes.Count)] + settings.NameMiddles[random.Next(settings.NameMiddles.Count)]
                + settings.NameSuffixes[random.Next(settings.NameSuffixes.Count)];
            var name = char.ToUpperInvariant(raw[0]) + raw[1..].ToLowerInvariant();
            if (Shape().IsMatch(name) && !taken(name)) return name;
        }
        return null;
    }
}
