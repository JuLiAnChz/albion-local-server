using System.Globalization;
using System.Xml.Linq;

namespace AlbionLocal.Server.Data;

/// <summary>
/// tunnelsystem.xml (analysis/work/stageL/roads/SPEC.md, "SPEC" here, §1.2; client parser bbf.c8 0x180578920 "AvalonRoadData"): the exit
/// lifetimes and warning time, the three exit types in file order (their index is NewTunnelExit key 5: SOLO 0, GROUP 1, RAID 2; b6k), the
/// exit presets (outgoing chance and keyword weights, b6j) and the tunnel presets (incoming keyword, exit counts, the exit split, b6l), plus
/// gamedata.xml ExitSettings/@defaultexitusagedistance, the client's portal range (bgf+0x6D0, SPEC §2.2, V4). Read with the road network's
/// build (off the world thread, <see cref="ClusterStore.Roads"/>); immutable.
/// </summary>
public sealed class RoadNetworkData
{
    /// <summary>One &lt;tunnelexittype&gt; (b6k): its index (file order), name, charges and times in seconds.</summary>
    public sealed record ExitType(int Index, string Name, int MaxCharges, double ChargeMin, double ChargeMax, double UsedSeconds);

    /// <summary>One &lt;tunnelexitpreset&gt; (b6j): outgoingConnectionChance (0 = INCOMING, it waits) and the keyword weights of its outgoing links.</summary>
    public sealed record ExitPreset(string Name, double Chance, IReadOnlyList<(string Keyword, double Weight)> Weights)
    {
        public bool Incoming => Chance <= 0;
    }

    /// <summary>
    /// One &lt;tunnelpreset&gt; (b6l, a clustertype's tunnelpreset): its incoming keyword, exit counts, dial symbol, the exit types its incoming
    /// links take by weight and the exit split (weight, exit preset) in list order.
    /// </summary>
    public sealed record Preset(string Name, string Keyword, int MinExits, int MaxExits, string Dial, IReadOnlyList<(string Type, double Weight)> IncomingTypes,
        IReadOnlyList<(double Weight, string ExitPreset)> Exits)
    {
        /// <summary>
        /// The exit presets of <paramref name="count"/> exits in slot order (ce7.f 0x1808A00B0, SPEC §3.2): each entry in list order gets
        /// min(round-half-even(weight × count / total), remaining) of them. TUNNEL_ROYAL 6 → OUT_ROYAL_SAFE, OUT_ROYAL_YELLOW, OUT_TUNNEL_ROYAL, 3 × INCOMING.
        /// </summary>
        public IReadOnlyList<string> Split(int count)
        {
            var total = Exits.Sum(e => e.Weight);
            var list = new List<string>();
            if (total <= 0) return list;
            foreach (var (weight, preset) in Exits)
            {
                var n = Math.Min((int)Math.Round(weight * count / total, MidpointRounding.ToEven), count - list.Count);
                for (var i = 0; i < n; i++) list.Add(preset);
            }
            return list;
        }
    }

    /// <summary>tunnelexitsettings lifetimemin / lifetimemax / shutdownwarningtime (43200 / 86400 / 3600 s).</summary>
    public double LifetimeMin { get; private init; } = 43200;
    public double LifetimeMax { get; private init; } = 86400;
    public double WarningSeconds { get; private init; } = 3600;

    /// <summary>The exit types in file order (index = NewTunnelExit key 5).</summary>
    public IReadOnlyList<ExitType> Types { get; private init; } = [];

    public IReadOnlyDictionary<string, ExitPreset> ExitPresets { get; private init; } = new Dictionary<string, ExitPreset>();

    public IReadOnlyDictionary<string, Preset> Presets { get; private init; } = new Dictionary<string, Preset>();

    /// <summary>gamedata.xml &lt;ExitSettings defaultexitusagedistance&gt; (5 m; the client parser's default 5.0 without it).</summary>
    public double UsageDistance { get; private init; } = 5;

    public ExitType? TypeNamed(string name) => Types.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static RoadNetworkData Load(XElement root, XElement? gamedata)
    {
        static double D(XElement? e, string a, double fallback) =>
            double.TryParse((string?)e?.Attribute(a), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        var settings = root.Element("tunnelexitsettings");
        var types = new List<ExitType>();
        foreach (var t in root.Element("tunnelexittypes")?.Elements("tunnelexittype") ?? [])
            types.Add(new ExitType(types.Count, (string?)t.Attribute("name") ?? "", (int)D(t, "maxcharges", 0), D(t, "timeperchargeupmin", 0), D(t, "timeperchargeupmax", 0),
                D(t, "usedconnectionduration", 0)));
        var exitPresets = new Dictionary<string, ExitPreset>(StringComparer.Ordinal);
        foreach (var p in root.Element("tunnelexitpresets")?.Elements("tunnelexitpreset") ?? [])
            if ((string?)p.Attribute("name") is { Length: > 0 } name)
                exitPresets.TryAdd(name, new ExitPreset(name, D(p, "outgoingConnectionChance", 0),
                    [.. p.Elements("outgoingConnectionWeighting").Select(w => ((string?)w.Attribute("outgoingConnectionKeyword") ?? "", D(w, "weight", 0))).Where(w => w.Item1.Length > 0)]));
        var presets = new Dictionary<string, Preset>(StringComparer.Ordinal);
        foreach (var p in root.Element("tunnelpresets")?.Elements("tunnelpreset") ?? [])
            if ((string?)p.Attribute("name") is { Length: > 0 } name)
                presets.TryAdd(name, new Preset(name, (string?)p.Attribute("incomingConnectionKeyword") ?? "", (int)D(p, "minExits", 0), (int)D(p, "maxExits", 0),
                    (string?)p.Attribute("dialsymbol") ?? "",
                    [.. (p.Element("incomingConnectionWeightings")?.Elements("type") ?? []).Select(t => ((string?)t.Attribute("name") ?? "", D(t, "weight", 1)))],
                    [.. (p.Element("exits")?.Elements("exit") ?? []).Select(e => (D(e, "weight", 0), (string?)e.Attribute("tunnelexitpreset") ?? ""))]));
        if (types.Count == 0 || presets.Count == 0) throw new InvalidDataException("tunnelsystem.bin: no <tunnelexittype> or <tunnelpreset>");
        return new RoadNetworkData
        {
            LifetimeMin = D(settings, "lifetimemin", 43200), LifetimeMax = D(settings, "lifetimemax", 86400), WarningSeconds = D(settings, "shutdownwarningtime", 3600),
            Types = types, ExitPresets = exitPresets, Presets = presets,
            UsageDistance = D(gamedata?.Descendants("ExitSettings").FirstOrDefault(), "defaultexitusagedistance", 5),
        };
    }
}
