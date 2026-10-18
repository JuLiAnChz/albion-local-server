using System.Globalization;
using System.Xml.Linq;

namespace AlbionLocal.Server.Data;

/// <summary>
/// emotes.xml (analysis/work/stageN/qol/SPEC.md §1): the player emotes in file order. The index of an &lt;Emote&gt; is what PlayEmote (op 195
/// key 0) and the PlayEmote event (77 key 1) carry (the client's bcb.abq look-up). The root's cooldown_ms (1000) spaces two emotes of a
/// player. Every active emote of the current client has unlocklocation="OneServer" and no state="Locked" (the locked ones are commented
/// out), so all are free; a state="Locked" one would answer the client's NOT_UNLOCKED (7807). Immutable.
/// </summary>
public sealed class EmoteData
{
    /// <summary>One &lt;Emote&gt;: uniquename, loop (dance, sit, sleep: shown to every new observer until stopped), state Locked.</summary>
    public sealed record Emote(string Name, bool Loops, bool Locked);

    public IReadOnlyList<Emote> All { get; }

    /// <summary>Root cooldown_ms (1000 when missing).</summary>
    public double CooldownMs { get; }

    private EmoteData(IReadOnlyList<Emote> all, double cooldownMs)
    {
        All = all;
        CooldownMs = cooldownMs;
    }

    /// <summary>The 345402 list (emotes.xml, 16 emotes; loops 6 dance, 12 sit, 13 sleep): when emotes.bin cannot be read (tests without the client).</summary>
    public static readonly EmoteData Default = new(
        [.. new[] { "APPLAUD", "BEG", "BOW", "CHARGE", "CHEER", "CRY", "DANCE", "FLEX", "LAUGH", "NO", "POINT", "SALUTE", "SIT", "SLEEP", "WAVE", "YES" }
            .Select((n, i) => new Emote("EMOTE_" + n, i is 6 or 12 or 13, false))], 1000);

    public Emote? Find(int index) => index >= 0 && index < All.Count ? All[index] : null;

    public static EmoteData Load(XElement root)
    {
        var list = root.Elements().Where(e => e.Name.LocalName == "Emote")
            .Select(e => new Emote(((string?)e.Attribute("uniquename") ?? "").Trim(),
                string.Equals((string?)e.Attribute("loop"), "true", StringComparison.OrdinalIgnoreCase),
                string.Equals((string?)e.Attribute("state"), "Locked", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var cooldown = double.TryParse((string?)root.Attribute("cooldown_ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out var c) ? c : 1000;
        return list.Count == 0 ? Default : new EmoteData(list, cooldown);
    }
}
