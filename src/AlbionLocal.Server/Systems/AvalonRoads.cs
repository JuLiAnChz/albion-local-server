using System.Globalization;
using System.Numerics;
using AlbionLocal.Photon;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Persistence;

namespace AlbionLocal.Server.Systems;

/// <summary>The state of a road network exit spot (the official ce8 states, SPEC §3.3-3.4).</summary>
public enum RoadSlotState
{
    /// <summary>An INCOMING exit (outgoingConnectionChance 0) waiting for a partner.</summary>
    Incoming = 0,

    /// <summary>An outgoing exit that drew a keyword and looks for a free incoming exit of it.</summary>
    Searching = 1,

    /// <summary>An outgoing exit that drew no link this lifetime.</summary>
    Idle = 2,

    /// <summary>One end of a link.</summary>
    Linked = 3,
}

/// <summary>A cluster of the road network (ce7): its world.xml id and type, its tunnel preset and its exit spots in path order.</summary>
public sealed class RoadNode(string cluster, string type, RoadNetworkData.Preset preset)
{
    public string Cluster { get; } = cluster;

    /// <summary>The world.xml cluster type (TUNNEL_ROYAL, SAFEAREA, …).</summary>
    public string Type { get; } = type;

    public RoadNetworkData.Preset Preset { get; } = preset;

    /// <summary>A road (a TUNNEL_* preset), whose every spot shows a portal; else an open-world zone, whose spots show only their links.</summary>
    public bool IsRoad => Preset.Name.StartsWith("TUNNEL_", StringComparison.Ordinal);

    public List<RoadSlot> Slots { get; } = [];

    public override string ToString() => $"{Cluster} ({Preset.Name})";
}

/// <summary>An exit spot (ce8): its world.xml criticalObjects path and Guid, the exit preset the split gave it, its state, link and portal.</summary>
public sealed class RoadSlot(RoadNode node, string path, Guid guid, RoadNetworkData.ExitPreset preset)
{
    public RoadNode Node { get; } = node;
    public string Cluster => Node.Cluster;
    public string Path { get; } = path;
    public Guid Guid { get; } = guid;
    public RoadNetworkData.ExitPreset Preset { get; } = preset;
    public RoadSlotState State { get; internal set; }

    /// <summary>The keyword a searching exit looks for.</summary>
    public string? Keyword { get; internal set; }

    /// <summary>The end of an idle or searching exit's lifetime (GameTimeStamp): then it rolls again.</summary>
    public long Closes { get; internal set; }

    public RoadLink? Link { get; internal set; }

    /// <summary>Its portal on the loaded map of its cluster (none while that map is not loaded, or an open-world spot without a link).</summary>
    public TunnelExit? Portal { get; internal set; }

    public override string ToString() => $"{Cluster} {Path}";
}

/// <summary>A link between two spots (both ce8 in states 4 and 3): its type, its times (GameTimeStamps) and charges.</summary>
public sealed class RoadLink
{
    public required long Id { get; init; }
    public required RoadSlot Out { get; init; }
    public required RoadSlot In { get; init; }

    /// <summary>The tunnelexittype index (SOLO 0, GROUP 1, RAID 2), NewTunnelExit key 5.</summary>
    public required int Type { get; init; }

    public required long Opened { get; init; }
    public required long Closes { get; set; }
    public required short MaxCharges { get; init; }
    public short Charges { get; set; }

    /// <summary>When the next charge comes (0 while full).</summary>
    public long NextCharge { get; set; }

    /// <summary>The end of the recently-used window (0 = never used).</summary>
    public long RecentUntil { get; set; }

    /// <summary>Made by the rescue rule (SPEC §5.2) or a GM, not by the generator.</summary>
    public bool Special { get; init; }

    public RoadSlot Other(RoadSlot s) => s == Out ? In : Out;

    public override string ToString() => $"link {Id} {Out} <-> {In}";
}

/// <summary>
/// The Roads of Avalon (analysis/work/stageL/roads/SPEC.md, "SPEC" here): the portal network of the open world and the road clusters (TNL-*),
/// built from the client's own data as the official generator does (ce6-ce9, SPEC §3): every world.xml cluster with a clustertype tunnelpreset
/// and criticalObjects &lt;tunnelexit&gt; spots gets min(spots, maxExits) exits split over its preset's exit presets (§3.2); an outgoing one rolls
/// a keyword by weight with its outgoingConnectionChance and a lifetime of U(lifetimemin, lifetimemax) (U(0, max) at the first build, §3.3),
/// and links to a random free INCOMING exit of a cluster with that keyword, never its own cluster nor one it is linked to already, of the
/// target preset's type (open world: GROUP); one that finds none retries every Roads:RetrySeconds. A link starts with the type's maxcharges,
/// each pass takes one and starts the charge-up, and sets "recently used", in which the portal lets players in at 0 charges (§3.6, Q2); it
/// closes at the end of its outgoing exit's lifetime, which then rolls again (§3.5). The rescue rule (§5.2) gives a road with a human inside and
/// no link one at once. The portals are <see cref="TunnelExit"/> objects on the loaded maps (§5.3), the travel is WorldSession.RoadChange
/// (§5.4). The network is saved in the character database (<see cref="IRoadStore"/>, schema v26) in batches and taken up again at the next
/// start (expired rows roll again). Phase B (SPEC §7): the bots' portal rule (<see cref="BotMayUse"/>, Bots/BotManager.ExitsOf), the official
/// rules of a road (World.LogoutTimeIn, <see cref="ThrowOutIfStale"/>), the closing portal's NewTunnelExitTemp; the road dungeons are
/// Systems/RandomDungeons' fixed exits. Under <see cref="World.Sync"/>.
/// </summary>
public sealed class AvalonRoads(World world, IRoadStore? store)
{
    /// <summary>How often the changes go to the store (and at the end, <see cref="Flush"/>).</summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

    /// <summary>The lifetime of a GM's link (.roads link).</summary>
    public static readonly TimeSpan GmLinkLifetime = TimeSpan.FromMinutes(30);

    private readonly Dictionary<string, RoadNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<RoadNode>> _byKeyword = new(StringComparer.Ordinal);
    private readonly Dictionary<long, RoadLink> _links = [];
    private readonly HashSet<RoadLink> _dirtyLinks = [];
    private readonly List<long> _droppedLinks = [];
    private readonly HashSet<RoadSlot> _dirtySlots = [];
    private long _nextId;
    private Task? _reading;
    private TimeSpan _nextTick, _nextFlush, _readAgain;
    private long _nextRetry;

    private RoadsOptions Options => world.Config.Roads;

    /// <summary>The network's random numbers (Roads:Seed fixes them).</summary>
    private Random Rng => _rng ??= Options.Seed is { } seed ? new Random(seed) : new Random();

    private Random? _rng;

    /// <summary>tunnelsystem.xml; null until the network is built.</summary>
    public RoadNetworkData? Data { get; private set; }

    /// <summary>Whether the network is built (at the first tick that finds world.xml read, <see cref="Update"/>).</summary>
    public bool Ready => Data is not null;

    public IReadOnlyDictionary<string, RoadNode> Nodes => _nodes;

    public IReadOnlyCollection<RoadLink> Links => _links.Values;

    /// <summary>The network's node of <paramref name="cluster"/>; null when it has no exits.</summary>
    public RoadNode? NodeOf(string? cluster) => cluster is not null && _nodes.TryGetValue(cluster, out var n) ? n : null;

    /// <summary>The metres about NewTunnelExit key 1 a portal is used from (Roads:EnterRadius, else gamedata defaultexitusagedistance; SPEC §2.2, V4).</summary>
    public double EnterRadius => Options.EnterRadius ?? Data?.UsageDistance ?? 5;

    // ---- the build ----

    /// <summary>
    /// Builds the network now (the self-test; <see cref="Update"/> does it at the first tick with world.xml read): the nodes from world.xml and
    /// worldsettings, then the saved links and spots, else a first generation. Needs the world's cluster data; Roads:Enabled off: nothing.
    /// </summary>
    public void Start()
    {
        if (Ready || !Options.Enabled || world.Clusters is not { } clusters) return;
        Build(clusters);
    }

    private void Build(ClusterStore clusters)
    {
        RoadNetworkData data;
        WorldInfo info;
        WorldArchetypes archetypes;
        try { (data, info, archetypes) = (clusters.Roads, clusters.World, clusters.Archetypes); }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            Log.Warn("world", $"roads: {e.Message}; no Roads of Avalon");
            _readAgain = TimeSpan.MaxValue; // not again: the next start reads it
            return;
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var c in info.Clusters)
        {
            if (c.Attrs.GetValueOrDefault("enabled") == "false" || archetypes.TunnelPresetOf(c.Type) is not { } name
                || !data.Presets.TryGetValue(name, out var preset) || !Options.LayerOn(name)) continue;
            // SPEC §1.4, V1: the spots are world.xml's criticalObjects <tunnelexit>s (TNL-201…400 take those of the cluster their file is named after).
            var critical = c.Critical.Count > 0 ? c.Critical : info.CriticalOf(ClusterName.Parse(c.File).Id) ?? c.Critical;
            var spots = critical.Values.Where(o => o.Type == "tunnelexit" && !o.Unused).OrderBy(o => o.Path, StringComparer.Ordinal).ToList();
            if (spots.Count == 0) continue;
            var split = preset.Split(Math.Min(spots.Count, preset.MaxExits));
            if (split.Count == 0) continue;
            var node = new RoadNode(c.Id, c.Type, preset);
            for (var i = 0; i < split.Count; i++)
                if (data.ExitPresets.TryGetValue(split[i], out var exit)) node.Slots.Add(new RoadSlot(node, spots[i].Path, spots[i].Guid, exit));
            _nodes[c.Id] = node;
            if (!_byKeyword.TryGetValue(preset.Keyword, out var list)) _byKeyword[preset.Keyword] = list = [];
            list.Add(node);
        }
        Data = data;
        var now = world.GameNow;
        var restored = Restore(now);
        if (!restored) Generate(now, first: true);
        // the maps loaded before the build get their portals now
        foreach (var map in world.Maps.Maps.ToList())
            if (map.Template is { } template && map.Grid is not null)
                foreach (var portal in Populate(map, template)) map.AddObject(portal);
        _nextFlush = world.Now + FlushInterval;
        watch.Stop();
        Log.Info("world", string.Create(CultureInfo.InvariantCulture,
            $"roads: {_nodes.Count} clusters, {_nodes.Values.Sum(n => n.Slots.Count)} exits ({_nodes.Values.Count(n => n.IsRoad)} roads), {_links.Count} links "
            + $"{(restored ? "taken up from the database" : "generated")} in {watch.Elapsed.TotalMilliseconds:0} ms"));
    }

    /// <summary>The first build (or .roads regen): every exit rolls (outgoing ones with a staggered lifetime U(0, max)), then the searching ones link up.</summary>
    private void Generate(long now, bool first)
    {
        foreach (var slot in _nodes.Values.SelectMany(n => n.Slots)) Roll(slot, now, first, connect: false);
        ConnectAll(now);
    }

    /// <summary>
    /// The saved network (SPEC §5.7): a link of two known, free spots that has not expired comes back with its id, times and charges; an idle
    /// or searching spot keeps its row's state until its lifetime ends. Every outgoing spot left rolls; then the searching ones link. False
    /// (nothing saved) = generate.
    /// </summary>
    private bool Restore(long now)
    {
        if (store is null) return false;
        IReadOnlyList<RoadLinkRow> links;
        IReadOnlyList<RoadSlotRow> slots;
        try { (links, slots) = store.LoadRoads(); }
        catch (Exception e)
        {
            Log.Error("db", $"roads: the saved network could not be read ({e.Message}); generated anew");
            return false;
        }
        if (links.Count == 0 && slots.Count == 0) return false;
        var set = new HashSet<RoadSlot>();
        foreach (var row in links)
        {
            _nextId = Math.Max(_nextId, row.Id);
            if (Find(row.OutCluster, row.OutPath) is not { Link: null } a || Find(row.InCluster, row.InPath) is not { Link: null } b || a == b || row.Closes <= now
                || Data!.Types.ElementAtOrDefault(row.Type) is null)
            {
                _droppedLinks.Add(row.Id);
                continue;
            }
            var max = (short)MaxChargesOf(row.Type);
            var link = new RoadLink
            {
                Id = row.Id, Out = a, In = b, Type = row.Type, Opened = row.Opened, Closes = row.Closes, MaxCharges = max, Charges = (short)Math.Clamp(row.Charges, 0, max),
                NextCharge = row.Charges < max ? Math.Max(row.NextCharge, now) : 0, RecentUntil = row.RecentUntil,
            };
            (a.Link, b.Link, a.State, b.State) = (link, link, RoadSlotState.Linked, RoadSlotState.Linked);
            _links[link.Id] = link;
            set.Add(a);
            set.Add(b);
        }
        foreach (var row in slots)
        {
            if (Find(row.Cluster, row.Path) is not { } s || s.Preset.Incoming || set.Contains(s) || row.Closes <= now || row.State is not (1 or 2))
            {
                if (Find(row.Cluster, row.Path) is { } stale) _dirtySlots.Add(stale); // its row goes (or is rewritten)
                continue;
            }
            (s.State, s.Keyword, s.Closes) = ((RoadSlotState)row.State, row.State == 1 ? row.Keyword : null, row.Closes);
            set.Add(s);
        }
        foreach (var slot in _nodes.Values.SelectMany(n => n.Slots).Where(s => !set.Contains(s))) Roll(slot, now, first: false, connect: false);
        ConnectAll(now);
        return true;
    }

    private RoadSlot? Find(string cluster, string path) => NodeOf(cluster)?.Slots.FirstOrDefault(s => s.Path == path);

    /// <summary>.roads regen: every link closes and the network is generated anew (the store emptied first).</summary>
    public void Regenerate()
    {
        if (!Ready) return;
        var now = world.GameNow;
        foreach (var link in _links.Values.ToList()) Unlink(link, now, "regenerated", roll: false);
        try { store?.ClearRoads(); }
        catch (Exception e) { Log.Error("db", $"roads: the saved network could not be removed: {e.Message}"); }
        _dirtyLinks.Clear();
        _droppedLinks.Clear();
        _dirtySlots.Clear();
        Generate(now, first: true);
        Log.Info("world", $"roads: regenerated, {_links.Count} links");
    }

    // ---- the rules (SPEC §3) ----

    /// <summary>The lifetime of a link in seconds: U(lifetimemin, lifetimemax), or Roads:LifetimeMinutes; U(0, max) at the first build (to stagger).</summary>
    private double Lifetime(bool first)
    {
        var (min, max) = Options.LifetimeMinutes is { } m ? (m.Min * 60, m.Max * 60) : (Data!.LifetimeMin, Data.LifetimeMax);
        if (first) min = 0;
        return min + Rng.NextDouble() * Math.Max(0, max - min);
    }

    private static long Ticks(double seconds) => (long)(seconds * TimeSpan.TicksPerSecond);

    /// <summary>
    /// ce8.p (SPEC §3.3): an INCOMING exit waits; an outgoing one draws, with its outgoingConnectionChance, a keyword by weight (searching), else
    /// stays idle; both for a new lifetime. A searching one looks for its partner at once (<paramref name="connect"/>).
    /// </summary>
    private void Roll(RoadSlot slot, long now, bool first, bool connect = true)
    {
        _dirtySlots.Add(slot);
        if (slot.Preset.Incoming)
        {
            (slot.State, slot.Keyword, slot.Closes) = (RoadSlotState.Incoming, null, 0);
            return;
        }
        slot.Closes = now + Ticks(Lifetime(first));
        var weights = slot.Preset.Weights.Where(w => w.Weight > 0).ToList();
        if (weights.Count > 0 && Rng.NextDouble() < slot.Preset.Chance)
        {
            var keyword = DungeonGenerator.Pick(Rng, [.. weights.Select(w => (w.Keyword, w.Weight))]);
            // An exit whose keyword has no cluster in the network (Roads:Layers) stays idle.
            (slot.State, slot.Keyword) = _byKeyword.ContainsKey(keyword) ? (RoadSlotState.Searching, keyword) : (RoadSlotState.Idle, null);
        }
        else (slot.State, slot.Keyword) = (RoadSlotState.Idle, null);
        if (connect && slot.State == RoadSlotState.Searching) Connect(slot, now);
    }

    /// <summary>Every searching exit in random order tries to link (the first build, a restart, the retry).</summary>
    private void ConnectAll(long now)
    {
        foreach (var slot in _nodes.Values.SelectMany(n => n.Slots).Where(s => s.State == RoadSlotState.Searching).OrderBy(_ => Rng.Next()).ToList())
            Connect(slot, now);
        _nextRetry = now + Ticks(Options.RetrySeconds);
    }

    /// <summary>Whether a link joins <paramref name="a"/> and <paramref name="b"/> already.</summary>
    private static bool Joined(RoadNode a, RoadNode b) => a.Slots.Any(s => s.Link is { } l && l.Other(s).Node == b);

    private static bool Free(RoadSlot s) => s is { State: RoadSlotState.Incoming, Link: null };

    /// <summary>
    /// ce8.o_5 (SPEC §3.4, §5.2): a searching exit takes a random free INCOMING exit of the clusters of its keyword (never its own cluster nor one
    /// it is linked to), with the type of the target's preset; its lifetime is the link's. Nothing free: it keeps searching.
    /// </summary>
    private bool Connect(RoadSlot slot, long now)
    {
        if (slot.State != RoadSlotState.Searching || slot.Keyword is not { } keyword || !_byKeyword.TryGetValue(keyword, out var nodes)) return false;
        var free = nodes.Where(n => n != slot.Node && !Joined(slot.Node, n)).SelectMany(n => n.Slots).Where(Free).ToList();
        if (free.Count == 0) return false;
        Link(slot, free[Rng.Next(free.Count)], now, slot.Closes);
        return true;
    }

    /// <summary>
    /// The exit type of a link between <paramref name="a"/> and <paramref name="b"/> (the target, its incoming side): the open-world side's
    /// preset when there is one (GROUP: open-world portals are group portals, SPEC §3.7), else the target preset's incomingConnectionWeightings.
    /// </summary>
    private int TypeFor(RoadSlot a, RoadSlot b)
    {
        var preset = !b.Node.IsRoad ? b.Node.Preset : !a.Node.IsRoad ? a.Node.Preset : b.Node.Preset;
        var types = preset.IncomingTypes.Where(t => t.Weight > 0 && Data!.TypeNamed(t.Type) is not null).ToList();
        if (types.Count == 0) return Data!.TypeNamed("SOLO")?.Index ?? 0;
        return Data!.TypeNamed(DungeonGenerator.Pick(Rng, [.. types.Select(t => (t.Type, t.Weight))]))!.Index;
    }

    /// <summary>A type's numbers (Roads:Charges over tunnelsystem.xml): its maxcharges, a charge-up's seconds and the recently-used window's.</summary>
    private int MaxChargesOf(int type) => Data!.Types[type] is var t && Options.Charges.GetValueOrDefault(t.Name)?.MaxCharges is { } max ? max : t.MaxCharges;

    private double ChargeSeconds(int type)
    {
        var t = Data!.Types[type];
        return Options.Charges.GetValueOrDefault(t.Name)?.ChargeSeconds ?? t.ChargeMin + Rng.NextDouble() * Math.Max(0, t.ChargeMax - t.ChargeMin);
    }

    private double UsedSeconds(int type) => Data!.Types[type] is var t && Options.Charges.GetValueOrDefault(t.Name)?.UsedSeconds is { } used ? used : t.UsedSeconds;

    /// <summary>The link of <paramref name="a"/> (outgoing, whose lifetime it is) and <paramref name="b"/>, full, until <paramref name="closes"/>; the portals show it.</summary>
    private RoadLink Link(RoadSlot a, RoadSlot b, long now, long closes, bool special = false)
    {
        var type = TypeFor(a, b);
        var max = (short)MaxChargesOf(type);
        var link = new RoadLink { Id = ++_nextId, Out = a, In = b, Type = type, Opened = now, Closes = closes, MaxCharges = max, Charges = max, Special = special };
        (a.Link, b.Link, a.State, b.State, a.Keyword, b.Keyword) = (link, link, RoadSlotState.Linked, RoadSlotState.Linked, null, null);
        _links[link.Id] = link;
        _dirtyLinks.Add(link);
        _dirtySlots.Add(a);
        _dirtySlots.Add(b);
        Shown(a);
        Shown(b);
        Log.Debug("world", $"roads: {link} ({Data!.Types[type].Name}, {max} charges)");
        return link;
    }

    /// <summary>ce8.s (SPEC §3.5): the link closes; both ends are free, an outgoing one rolls again at once (<paramref name="roll"/>), an incoming one waits.</summary>
    private void Unlink(RoadLink link, long now, string why, bool roll = true)
    {
        if (!_links.Remove(link.Id)) return;
        _dirtyLinks.Remove(link);
        _droppedLinks.Add(link.Id);
        foreach (var s in new[] { link.Out, link.In })
        {
            s.Link = null;
            (s.State, s.Keyword, s.Closes) = (RoadSlotState.Incoming, null, 0);
            _dirtySlots.Add(s);
            Hidden(s);
        }
        Log.Debug("world", $"roads: {link} closed ({why})");
        if (!roll) return;
        foreach (var s in new[] { link.Out, link.In }) Roll(s, now, first: false);
    }

    /// <summary>A GM's close of <paramref name="link"/> (.roads close): its ends roll again.</summary>
    public void Close(RoadLink link) => Unlink(link, world.GameNow, "gm");

    // ---- the tick ----

    /// <summary>
    /// One world tick: the build when world.xml is in (its read started on a pool thread at the first tick that lacks it), then every
    /// Roads:UpdateSeconds the expiries (the link closes, its outgoing exit rolls at once), the idle and searching exits whose lifetime ended,
    /// the charge-ups (a resend: the client never counts key 9 up itself; the end of the recently-used window needs none, the client times key
    /// 12 out), the searching exits' retry, the rescue rule, and the batched save.
    /// </summary>
    public void Update()
    {
        if (!Options.Enabled || world.Clusters is not { } clusters) return;
        if (!Ready)
        {
            if (_reading is { IsCompleted: false } || _readAgain == TimeSpan.MaxValue) return; // reading, or the data is bad (Build)
            if (clusters.WorldIfLoaded is null || clusters.RoadsIfLoaded is null) // read off the world thread
            {
                if (_reading is not null) // the read is over and failed (a file being rewritten): tried again a minute later
                {
                    _reading = null;
                    _readAgain = world.Now + TimeSpan.FromMinutes(1);
                    return;
                }
                if (world.Now < _readAgain) return;
                _reading = Task.Run(() => { _ = clusters.World; _ = clusters.Archetypes; _ = clusters.Roads; });
                _reading.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                return;
            }
            _reading = null; // a good read builds at the next tick, no wait
            Build(clusters);
            if (!Ready) return;
        }
        var clock = world.Now;
        if (clock < _nextTick) return;
        _nextTick = clock + TimeSpan.FromSeconds(Options.UpdateSeconds);
        var now = world.GameNow;
        foreach (var link in _links.Values.ToList())
        {
            if (now >= link.Closes)
            {
                Unlink(link, now, "expired");
                continue;
            }
            if (link.Charges >= link.MaxCharges || link.NextCharge <= 0 || now < link.NextCharge) continue;
            link.Charges++;
            link.NextCharge = link.Charges < link.MaxCharges ? link.NextCharge + Ticks(ChargeSeconds(link.Type)) : 0;
            _dirtyLinks.Add(link);
            Resend(link.Out);
            Resend(link.In);
        }
        foreach (var slot in _nodes.Values.SelectMany(n => n.Slots))
            if (slot.State is RoadSlotState.Idle or RoadSlotState.Searching && now >= slot.Closes) Roll(slot, now, first: false);
        if (now >= _nextRetry) ConnectAll(now);
        if (Options.Rescue)
            foreach (var map in world.Maps.Maps)
                if (map.HumanCount > 0 && NodeOf(map.Cluster) is { IsRoad: true } node && !node.Slots.Any(s => s.Link is not null)) Rescue(node, now);
        if (clock >= _nextFlush)
        {
            _nextFlush = clock + FlushInterval;
            Flush();
        }
    }

    /// <summary>
    /// The rescue rule (SPEC §5.2, V6): a road with a human inside and no link gets one of Roads:RescueMinutes at once: a free INCOMING exit there
    /// from an idle or searching exit of a cluster whose keyword weights name this road's keyword, else from a free open-world exit of the royal
    /// layer; a road without a free incoming exit (TUNNEL_DEEP) links one of its own idle or searching exits the same way. False = nothing free.
    /// </summary>
    public bool Rescue(RoadNode node, long now)
    {
        var closes = now + Ticks(Options.RescueMinutes * 60);
        var royal = _nodes.Values.Where(n => n.Preset.Name is "SAFEZONE" or "YELLOW" && n != node && !Joined(node, n)).SelectMany(n => n.Slots).Where(Free).ToList();
        if (node.Slots.FirstOrDefault(Free) is { } free)
        {
            var sources = _nodes.Values.Where(n => n != node && !Joined(node, n)).SelectMany(n => n.Slots)
                .Where(s => s.State is RoadSlotState.Idle or RoadSlotState.Searching && s.Preset.Weights.Any(w => w.Keyword == node.Preset.Keyword)).ToList();
            var from = sources.Count > 0 ? sources[Rng.Next(sources.Count)] : royal.Count > 0 ? royal[Rng.Next(royal.Count)] : null;
            if (from is null) return false;
            Log.Info("world", $"roads: rescue link for {node}");
            Link(from, free, now, closes, special: true);
            return true;
        }
        if (node.Slots.FirstOrDefault(s => s.State is RoadSlotState.Idle or RoadSlotState.Searching) is not { } own) return false;
        var keywords = own.Preset.Weights.Select(w => w.Keyword).ToHashSet(StringComparer.Ordinal);
        var partners = _nodes.Values.Where(n => n != node && keywords.Contains(n.Preset.Keyword) && !Joined(node, n)).SelectMany(n => n.Slots).Where(Free).ToList();
        var to = partners.Count > 0 ? partners[Rng.Next(partners.Count)] : royal.Count > 0 ? royal[Rng.Next(royal.Count)] : null;
        if (to is null) return false;
        Log.Info("world", $"roads: rescue link for {node}");
        Link(own, to, now, closes, special: true);
        return true;
    }

    /// <summary>
    /// .roads link: the nearest free spot of <paramref name="here"/> to <paramref name="at"/> linked to a free spot of cluster or preset
    /// <paramref name="target"/> (a random cluster of it), for <see cref="GmLinkLifetime"/>; an outgoing spot of here searching or idle counts as free.
    /// </summary>
    public RoadLink GmLink(Map here, Vector2 at, string target)
    {
        if (!Ready) throw new InvalidOperationException("the road network is not built yet");
        if (NodeOf(here.Cluster) is not { } node) throw new InvalidOperationException($"{here.Cluster} has no road exits");
        var mine = node.Slots.Where(s => s.Link is null).OrderBy(s => SpotOf(here, s) is { } m ? Vector2.Distance(m.Position, at) : float.MaxValue).FirstOrDefault()
                   ?? throw new InvalidOperationException($"every exit of {here.Cluster} is linked");
        var candidates = ((NodeOf(target) ?? NodeOf(target.ToUpperInvariant())) is { } one ? [one] : _nodes.Values.Where(n => n.Preset.Name.Equals(target, StringComparison.OrdinalIgnoreCase)).ToList())
            .Where(n => n != node && !Joined(node, n)).SelectMany(n => n.Slots).Where(s => s.Link is null).ToList();
        if (candidates.Count == 0) throw new InvalidOperationException($"{target}: no cluster or preset of the network with a free exit not linked here");
        var other = candidates[Rng.Next(candidates.Count)];
        var now = world.GameNow;
        return Link(mine, other, now, now + GmLinkLifetime.Ticks, special: true);
    }

    /// <summary>.roads charges: the link's charges now (0..max), the charge-up started below the maximum.</summary>
    public void SetCharges(RoadLink link, int charges)
    {
        link.Charges = (short)Math.Clamp(charges, 0, link.MaxCharges);
        link.NextCharge = link.Charges < link.MaxCharges ? world.GameNow + Ticks(ChargeSeconds(link.Type)) : 0;
        _dirtyLinks.Add(link);
        Resend(link.Out);
        Resend(link.In);
    }

    // ---- the travel (SPEC §5.4) ----

    /// <summary>Whether <paramref name="link"/> lets a player in now: a charge left, or its recently-used window open (CanBeEnteredByState).</summary>
    public static bool Enterable(RoadLink link, long now) => link.Charges > 0 || link.RecentUntil > now;

    /// <summary>
    /// Whether a bot may pass through <paramref name="link"/> now (phase B, SPEC §7.2; Bots:Roads:Portals): with two charges or more, or free inside
    /// the recently-used window at 0 [server choice: a bot never takes the last charge, it leaves that to the players].
    /// </summary>
    public static bool BotMayUse(RoadLink link, long now) => link.Charges > 1 || link.Charges == 0 && link.RecentUntil > now;

    /// <summary>
    /// A player passes through <paramref name="link"/>: with a charge left it takes one (the charge-up starts when it was full) and opens the
    /// recently-used window (Q2: a paying pass; a pass inside the window at 0 charges is free); both portals are sent again.
    /// </summary>
    public void Pass(RoadLink link, long now)
    {
        if (link.Charges > 0)
        {
            if (link.Charges >= link.MaxCharges) link.NextCharge = now + Ticks(ChargeSeconds(link.Type));
            link.Charges--;
            link.RecentUntil = now + Ticks(UsedSeconds(link.Type));
            _dirtyLinks.Add(link);
        }
        Resend(link.Out);
        Resend(link.In);
    }

    /// <summary>The template member of <paramref name="slot"/> on a map of its cluster (the active tunnelexit member of its path).</summary>
    public static CriticalMember? MemberOf(ClusterTemplate template, RoadSlot slot) =>
        template.Critical.FirstOrDefault(m => m.Type == "tunnelexit" && m.Path == slot.Path);

    private static CriticalMember? SpotOf(Map map, RoadSlot slot) => map.Template is { } t ? MemberOf(t, slot) : null;

    /// <summary>The tile's tunnelexit spawnpos (tiles.xml Tunnel_Exit 4 0, Tunnel_Exit_Inside 5.53 0).</summary>
    private Vector2 SpawnPosOf(CriticalMember member) =>
        TileMath.V2((string?)member.Components.Concat(world.Clusters?.Tiles.Components(member.Tile) ?? []).FirstOrDefault(c => c.Name.LocalName == "tunnelexit")
            ?.Attribute("spawnpos")) ?? Vector2.Zero;

    /// <summary>Where a player coming through <paramref name="slot"/>'s portal arrives in <paramref name="template"/> (its cluster); null when the spot is not there.</summary>
    public Arrival? ArrivalAt(RoadSlot slot, ClusterTemplate template) =>
        MemberOf(template, slot) is { } m ? TunnelExit.ArrivalAt(slot.Cluster, m.Position, m.Direction, SpawnPosOf(m)) : null;

    // ---- the portals (SPEC §5.3) ----

    /// <summary>
    /// At the map's fill (MapSpawner, next to the dungeon exits): a road's every spot as a portal (unlinked ones inactive), an open-world
    /// map's linked spots; nothing before the build, off the network, or for a spot whose member the layout lacks.
    /// </summary>
    internal IEnumerable<TunnelExit> Populate(Map map, ClusterTemplate template)
    {
        if (!Ready || NodeOf(map.Cluster) is not { } node) return [];
        var made = new List<TunnelExit>();
        foreach (var slot in node.Slots)
        {
            if (!node.IsRoad && slot.Link is null || Live(slot) is not null) continue; // a map loaded before the build: its linked spots got theirs at the link (Shown)
            if (Make(map, template, slot) is { } portal) made.Add(portal);
        }
        return made;
    }

    private TunnelExit? Make(Map map, ClusterTemplate template, RoadSlot slot)
    {
        if (MemberOf(template, slot) is not { } member)
        {
            Combat.LogOnce($"roads:{slot}", $"roads: {slot} has no active tunnelexit member in {template.Id}, no portal");
            return null;
        }
        var portal = new TunnelExit(world.ObjectIds.Next(), member, slot) { SpawnPos = SpawnPosOf(member) };
        slot.Portal = portal;
        return portal;
    }

    /// <summary>The portal of <paramref name="slot"/> when it stands on a live map.</summary>
    private static TunnelExit? Live(RoadSlot slot) =>
        slot.Portal is { Map: { Unloaded: false } map } p && map.Objects.TryGetValue(p.Id, out var o) && o == p ? p : null;

    /// <summary>The spot's portal shows its link: resent where it stands, else made on its loaded map.</summary>
    private void Shown(RoadSlot slot)
    {
        if (Live(slot) is not null)
        {
            Resend(slot);
            return;
        }
        if (world.Maps.Find(slot.Cluster) is { Template: { } template, Grid: not null, Unloaded: false } map && Make(map, template, slot) is { } portal) map.AddObject(portal);
    }

    /// <summary>
    /// The spot lost its link: a road's portal is resent inactive (key 4 false, no key 6), an open-world one leaves; one a player saw leaves the
    /// short-lived visual NewTunnelExitTemp on its spot for Roads:TempPortalSeconds (phase B, SPEC §7.5 [inference]).
    /// </summary>
    private void Hidden(RoadSlot slot)
    {
        if (Live(slot) is not { } portal) return;
        if (slot.Node.IsRoad) Resend(slot);
        else
        {
            var map = portal.Map!;
            var seen = map.ObserversOf(portal).Any();
            map.RemoveObject(portal);
            slot.Portal = null;
            if (!seen || Options.TempPortalSeconds <= 0 || portal.Spawn is not CriticalMember member) return;
            var temp = new TunnelExitTemp(world.ObjectIds.Next(), member);
            map.AddObject(temp);
            map.Timers.At(world.Now + TimeSpan.FromSeconds(Options.TempPortalSeconds), () => map.RemoveObject(temp));
        }
    }

    /// <summary>The whole NewTunnelExit again (the same id: cjb.av updates in place) to whoever sees the spot's portal.</summary>
    private static void Resend(RoadSlot slot)
    {
        if (Live(slot) is not { } portal) return;
        foreach (var p in portal.Map!.ObserversOf(portal).ToList())
            foreach (var e in portal.BuildCreate(p)) p.Session.Send(e);
    }

    // ---- the official rules of a road (phase B, SPEC §5.1, §7.3) ----

    /// <summary>The characters <see cref="ThrowOutIfStale"/> moved, who get its chat line at their Join (<see cref="Joined"/>).</summary>
    private readonly HashSet<Guid> _thrownOut = [];

    /// <summary>The characters a SelectCharacter kept in their road: the Join that follows does not decide again (its key 8 must stay key 1).</summary>
    private readonly HashSet<Guid> _kept = [];

    /// <summary>Whether <paramref name="cluster"/> is a road (world.xml type TUNNEL_*; before world.xml is read, a TNL-nnn id).</summary>
    public bool IsRoad(string cluster) => world.Clusters?.WorldIfLoaded?.Find(cluster) is { } c
        ? c.Type.StartsWith("TUNNEL_", StringComparison.Ordinal) : cluster.StartsWith("TNL-", StringComparison.Ordinal);

    /// <summary>
    /// The thrown-out rule (Roads:ThrowOutAfterMinutes, official 60 = the road types' graceperiodthreshold; pve 0 = never; Q6 [inference]: counted
    /// from the logout): a character that logs in to a road it left at least that long ago (Character.LastLeftAt) comes back in its respawn city
    /// (Pvp:RespawnCluster, else its last city, else 0000) with gamedata's ItemLossEvent ThrownOutAvalonRoads on its saved items: 20-40 % of the
    /// maximum durability of everything, a third of each stack destroyed (rounded at random), no equipment item, weapon or mount, every trash item
    /// (<see cref="PvpDeathSettings.ThrownOutAvalonRoads"/>). Saved at once; the Join tells it (<see cref="Joined"/>). At SelectCharacter; at the
    /// Join (<paramref name="atJoin"/>) only for a Join no SelectCharacter decided for (a Join by character id), so that its key 8 stays the
    /// SelectCharacter's key 1. Never for a character a map still holds: LastLeftAt is also stamped at every cluster change, so an online one's
    /// is its road entry (a reconnect within the old connection's 30 s); World.Dispose stamps the online ones [server limit: after a crash
    /// the saved LastLeftAt can still be the road entry]. False when the rule does not apply.
    /// </summary>
    public bool ThrowOutIfStale(Character c, bool atJoin = false)
    {
        var minutes = Options.ThrowOutAfterMinutes;
        if (minutes <= 0 || c.LastLeftAt <= 0 || !IsRoad(c.Cluster)) return false;
        if (!atJoin) _kept.Add(c.Id);
        else if (_kept.Remove(c.Id)) return false; // its SelectCharacter kept it in the road
        if (world.Maps.Maps.Any(m => m.Players.Values.Any(p => p.Character.Id == c.Id))) return false; // online: not a logout
        if (world.GameNow - c.LastLeftAt < TimeSpan.FromMinutes(minutes).Ticks) return false;
        if (Death.RespawnPoint(world, Death.RespawnCluster(world, c), c.Name) is not { } point) return false;
        _kept.Remove(c.Id);
        var from = c.Cluster;
        var lost = Lose(c, Death.Settings(world).ThrownOutAvalonRoads);
        (c.Cluster, c.PositionX, c.PositionY, c.Direction) = (point.Cluster, point.At.X, point.At.Y, 0);
        c.PendingTransfer = null;
        world.Saves.SaveNow(c);
        _thrownOut.Add(c.Id);
        Log.Info("world", string.Create(CultureInfo.InvariantCulture, $"'{c.Name}' was offline in road {from} for {TimeSpan.FromTicks(world.GameNow - c.LastLeftAt).TotalMinutes:0} min: "
            + $"thrown out to {point.Cluster} ({point.At.X:0.##}, {point.At.Y:0.##}){(lost.Count > 0 ? $", destroyed {string.Join(", ", lost)}" : "")}"));
        return true;
    }

    /// <summary>An item loss <paramref name="rule"/> on the saved items of offline <paramref name="c"/> (nothing drops): what was destroyed.</summary>
    private List<string> Lose(Character c, ItemLossRule rule)
    {
        var lost = new List<string>();
        InventoryItem? One(InventoryItem? saved, DurabilityLossRule? wear, StackLossRule? stack)
        {
            if (saved is null || world.Items.Find(saved.Name) is not { } type) return saved;
            var item = saved;
            if (wear is not null)
            {
                var raw = saved.Durability ?? type.FullDurabilityRaw;
                if (Death.Worn(type, raw, wear, 1, Rng) is var next && next != raw) item = item with { Durability = next == type.FullDurabilityRaw ? null : next };
            }
            var gone = stack is null ? 0 : Death.RandomRound(item.Count * stack.For(type.Kind), Rng);
            if (gone <= 0) return item;
            lost.Add($"{type.Name} x{Math.Min(gone, item.Count)}");
            return gone >= item.Count ? null : item with { Count = item.Count - gone };
        }
        for (var i = 0; i < c.Equipment.Length; i++) c.Equipment[i] = One(c.Equipment[i], rule.EquipmentWear, rule.EquipmentStack);
        for (var i = 0; i < c.Inventory.Count; i++) c.Inventory[i] = One(c.Inventory[i], rule.InventoryWear, rule.InventoryStack);
        return lost;
    }

    /// <summary>The Join after a throw-out (WorldSession.AfterResponse): its chat line.</summary>
    public void Joined(Player p)
    {
        if (!_thrownOut.Remove(p.Character.Id)) return;
        Expeditions.Tell(p, "You were away from the Roads of Avalon too long and have been thrown out: your gear is worn and a third of your stacks is lost.");
    }

    // ---- the store ----

    /// <summary>Writes the changes since the last batch (one transaction); also at the end (World.Dispose).</summary>
    public void Flush()
    {
        if (_dirtyLinks.Count == 0 && _droppedLinks.Count == 0 && _dirtySlots.Count == 0) return;
        if (store is null)
        {
            _dirtyLinks.Clear();
            _droppedLinks.Clear();
            _dirtySlots.Clear();
            return;
        }
        var links = _dirtyLinks.Where(l => _links.ContainsKey(l.Id)).Select(l => new RoadLinkRow(l.Id, l.Out.Cluster, l.Out.Path, l.In.Cluster, l.In.Path, l.Type, l.Opened,
            l.Closes, l.Charges, l.NextCharge, l.RecentUntil)).ToList();
        var slots = _dirtySlots.Where(s => s.State is RoadSlotState.Idle or RoadSlotState.Searching)
            .Select(s => new RoadSlotRow(s.Cluster, s.Path, (int)s.State, s.Keyword, s.Closes)).ToList();
        var dropped = _dirtySlots.Where(s => s.State is not (RoadSlotState.Idle or RoadSlotState.Searching)).Select(s => (s.Cluster, s.Path)).ToList();
        try { store.SaveRoads(links, [.. _droppedLinks], slots, dropped); }
        catch (Exception e)
        {
            Log.Error("db", $"roads: {links.Count} link(s) and {slots.Count + dropped.Count} spot(s) not saved: {e.Message}");
            return; // kept for the next batch
        }
        _dirtyLinks.Clear();
        _droppedLinks.Clear();
        _dirtySlots.Clear();
    }

    // ---- the GM's and the players' views (.roads) ----

    /// <summary>"in 3h 12m", "in 4:05" for a GameTimeStamp from now.</summary>
    public static string In(long when, long now)
    {
        var left = TimeSpan.FromTicks(Math.Max(0, when - now));
        return left.TotalHours >= 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)left.TotalHours}h {left.Minutes:00}m")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)left.TotalMinutes}:{left.Seconds:00}");
    }

    /// <summary>The link's portal line: type, target, charges, the next charge or the window, the close.</summary>
    public string Describe(RoadSlot slot, long now)
    {
        if (slot.Link is not { } l)
            return slot.State switch
            {
                RoadSlotState.Searching => $"{slot.Path}: no link (looking for {slot.Keyword}, for {In(slot.Closes, now)})",
                RoadSlotState.Idle => $"{slot.Path}: no link (idle for {In(slot.Closes, now)})",
                _ => $"{slot.Path}: no link (waiting for one)",
            };
        var other = l.Other(slot);
        var name = world.Clusters?.WorldIfLoaded?.Find(other.Cluster)?.DisplayName is { Length: > 0 } n ? $" {n}" : "";
        var charge = l.Charges < l.MaxCharges ? $", next charge in {In(l.NextCharge, now)}" : "";
        var recent = l.RecentUntil > now ? $", recently used ({In(l.RecentUntil, now)} left)" : "";
        return $"{slot.Path}: {Data!.Types[l.Type].Name} to {other.Cluster}{name} ({other.Node.Preset.Name}), {l.Charges}/{l.MaxCharges} charges{charge}{recent}, closes in {In(l.Closes, now)}";
    }

    /// <summary>
    /// .roads route: the shortest way over the links from <paramref name="from"/> to <paramref name="to"/> (a breadth-first search as the client's
    /// ce6.w, SPEC §3.4 V5), as the clusters on it; null when none leads there.
    /// </summary>
    public IReadOnlyList<string>? Route(string from, string to)
    {
        if (!_nodes.ContainsKey(from) || !_nodes.ContainsKey(to)) return null;
        var previous = new Dictionary<string, string>(StringComparer.Ordinal) { [from] = "" };
        var queue = new Queue<string>([from]);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            if (c == to) break;
            foreach (var s in _nodes[c].Slots)
                if (s.Link is { } l && l.Other(s).Cluster is var next && previous.TryAdd(next, c)) queue.Enqueue(next);
        }
        if (!previous.ContainsKey(to)) return null;
        var path = new List<string>();
        for (var c = to; c.Length > 0; c = previous[c]) path.Add(c);
        path.Reverse();
        return path;
    }

    /// <summary>
    /// The royal open-world spots that lead into a road now (SPEC §5.10; the black zones' too for a player in the black layer), the nearest to
    /// <paramref name="cluster"/> first by world-map distance (world.xml worldmapposition; those of an unplaced cluster last): .roads in the open world.
    /// </summary>
    public IEnumerable<(RoadSlot Slot, double Distance)> Entrances(string cluster)
    {
        var info = world.Clusters?.WorldIfLoaded;
        Vector2? Place(string id) => TileMath.V2(info?.Find(id)?.Attrs.GetValueOrDefault("worldmapposition"));
        var here = Place(cluster);
        bool In(string layer, RoadNode? n) => n is not null && RoadsOptions.LayerNames[layer].Contains(n.Preset.Name, StringComparer.OrdinalIgnoreCase);
        var black = In("BLACK", NodeOf(cluster));
        return _nodes.Values.Where(n => !n.IsRoad && (In("ROYAL", n) || black && In("BLACK", n))).SelectMany(n => n.Slots).Where(s => s.Link is { } l && l.Other(s).Node.IsRoad)
            .Select(s => (s, here is { } h && Place(s.Cluster) is { } p ? (double)Vector2.Distance(h, p) : double.MaxValue))
            .OrderBy(x => x.Item2).ThenBy(x => x.s.Cluster, StringComparer.Ordinal);
    }

    /// <summary>.roads info: per preset its clusters, exits by state and links.</summary>
    public IEnumerable<string> Summary()
    {
        foreach (var g in _nodes.Values.GroupBy(n => n.Preset.Name).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var slots = g.SelectMany(n => n.Slots).ToList();
            string Count(RoadSlotState s) => slots.Count(x => x.State == s).ToString(CultureInfo.InvariantCulture);
            yield return $"  {g.Key}: {g.Count()} cluster(s), {slots.Count} exits: {Count(RoadSlotState.Linked)} linked, {Count(RoadSlotState.Searching)} searching, "
                + $"{Count(RoadSlotState.Idle)} idle, {Count(RoadSlotState.Incoming)} waiting";
        }
    }
}
