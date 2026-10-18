using System.Globalization;
using System.Xml.Linq;

namespace AlbionLocal.Server.Data;

/// <summary>
/// buffshrines.xml (analysis/work/stageN/shrines/SPEC.md, "SPEC" here, §1; client registry bdz, a definition bdz.a): the buff shrines the
/// templates place with a &lt;shrine shrinetype="…"/&gt; tile (GENERAL_SHRINE_COMBAT_BUFF in 159 solo dungeon rooms, the faction FAME/SILVER
/// shrines, the city and portal-cluster invisibility shrines). Read with the first cluster that places one (off the world thread,
/// <see cref="ClusterStore.BuffShrines"/>); immutable.
/// </summary>
public sealed class BuffShrineData
{
    /// <summary>bdz ShrineMode: Default 0, InfiniteCapacity 1, ExternallyControlled 2, Treasure 3.</summary>
    public enum ShrineMode { Default = 0, InfiniteCapacity = 1, ExternallyControlled = 2, Treasure = 3 }

    /// <summary>
    /// One &lt;Shrine&gt;: uniquename (NewShrine key 3: the client's bdz.abo look-up), type (buff, dungeonmapsolo, …), faction, alertradius (the
    /// mobs of that faction within it keep it locked), usespell (OPEN_BUFFSHRINE: the client's fake-slot cast once the server accepts UseShrine),
    /// effectspell (what the use applies), respawns and its time, maxcharges (uses before it is spent; 1 without), mode.
    /// </summary>
    public sealed record Definition(string Name, string Type, string Faction, double AlertRadius, string UseSpell, string EffectSpell, bool Respawns,
        double RespawnMin, double RespawnMax, int MaxCharges, ShrineMode Mode)
    {
        /// <summary>Whether this server runs it: a buff shrine with its own state (not ExternallyControlled or Treasure: mists dungeons, hell dungeons).</summary>
        public bool Supported => Type == "buff" && Mode is ShrineMode.Default or ShrineMode.InfiniteCapacity && EffectSpell.Length > 0;
    }

    private readonly Dictionary<string, Definition> _byName;

    private BuffShrineData(Dictionary<string, Definition> byName) => _byName = byName;

    public IReadOnlyCollection<Definition> All => _byName.Values;

    public Definition? Find(string? name) => name is not null && _byName.TryGetValue(name, out var d) ? d : null;

    public static BuffShrineData Load(XElement root)
    {
        static string S(XElement e, string a) => ((string?)e.Attribute(a) ?? "").Trim();
        static double D(XElement e, string a) => double.TryParse(S(e, a), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
        var byName = new Dictionary<string, Definition>(StringComparer.Ordinal);
        foreach (var e in root.Elements().Where(x => x.Name.LocalName.Equals("Shrine", StringComparison.OrdinalIgnoreCase)))
        {
            var name = S(e, "uniquename");
            if (name.Length == 0) continue;
            var mode = Enum.TryParse<ShrineMode>(S(e, "mode"), true, out var m) ? m : ShrineMode.Default;
            byName[name] = new Definition(name, S(e, "type").ToLowerInvariant(), S(e, "faction"), D(e, "alertradius"), S(e, "usespell"), S(e, "effectspell"),
                S(e, "respawns").Equals("true", StringComparison.OrdinalIgnoreCase), D(e, "respawntimesecondsmin"), D(e, "respawntimesecondsmax"),
                Math.Max(1, (int)D(e, "maxcharges")), mode);
        }
        return new BuffShrineData(byName);
    }
}
