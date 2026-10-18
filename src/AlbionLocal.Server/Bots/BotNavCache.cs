using AlbionLocal.Photon;
using AlbionLocal.Server.Data;

namespace AlbionLocal.Server.Bots;

/// <summary>
/// Stage I bots (analysis/work/stageI/bots/SPEC.md §1.6, D13): the navigation of the clusters that get bots, by cluster id, the
/// Bots:Nav:CacheClusters (32) used last. A miss starts one build on a pool thread (<see cref="ClusterNav.Build"/>, tens of ms to a few
/// hundred for a big open-world cluster) whose result the world thread takes in its next tick (<see cref="World.Post"/>), so the world
/// lock never waits for it; until then the map gets no bots. The cache is not the <see cref="ClusterTemplate"/>'s (the store keeps
/// those for the life of the process); a map with bots holds its nav itself, so an eviction never takes it from under them. A build
/// that fails is tried again only <see cref="RetryAfter"/> later (one Warn per try, not one per population pass). Touched only under
/// <see cref="World.Sync"/>, but <see cref="Preload"/>.
/// </summary>
public sealed class BotNavCache(World world)
{
    private readonly Dictionary<string, (ClusterNav Nav, long Used)> _built = new(StringComparer.Ordinal);
    private readonly HashSet<string> _building = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TimeSpan> _failed = new(StringComparer.Ordinal);
    private long _uses;

    /// <summary>Walk grids kept, and builds under way.</summary>
    public int Count => _built.Count;

    public int Building => _building.Count;

    /// <summary>How long after a failed build of a cluster the next one may start (its data does not change while the server runs).</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);

    /// <summary>The nav of <paramref name="cluster"/> when built, without starting a build (.bot).</summary>
    public ClusterNav? Find(string cluster) => _built.TryGetValue(cluster, out var e) ? e.Nav : null;

    /// <summary>The nav of <paramref name="map"/>'s cluster when built; else false and, unless one runs, a build is started.</summary>
    public bool TryGet(Map map, out ClusterNav nav)
    {
        nav = null!;
        return map.Template is { } template && TryGet(template, out nav);
    }

    /// <summary>The nav of <paramref name="template"/> when built; else false and, unless one runs, a build is started (stageL hellgates: before its map exists).</summary>
    public bool TryGet(ClusterTemplate template, out ClusterNav nav)
    {
        nav = null!;
        if (_built.TryGetValue(template.Id, out var entry))
        {
            _built[template.Id] = (entry.Nav, ++_uses);
            nav = entry.Nav;
            return true;
        }
        if (_failed.TryGetValue(template.Id, out var failed) && world.Clock.Now - failed < RetryAfter) return false;
        if (!_building.Add(template.Id)) return false;
        var options = world.Config.Bots;
        Task.Run(() => ClusterNav.Build(template, options.LatticeStep, options.MaxExpansions)).ContinueWith(task => world.Post(() =>
        {
            _building.Remove(template.Id);
            if (task.IsCompletedSuccessfully)
            {
                _failed.Remove(template.Id);
                Store(task.Result);
                Log.Info("bots", $"walk grid of {template.Id}: {task.Result.Summary()}");
            }
            else
            {
                _failed[template.Id] = world.Clock.Now;
                Log.Warn("bots", $"walk grid of {template.Id} not built: {task.Exception?.InnerException?.Message}; no bots there, next try in {RetryAfter.TotalMinutes:0} min");
            }
        }), TaskScheduler.Default);
        return false;
    }

    /// <summary>
    /// Builds the nav of <paramref name="template"/> now on the calling thread, outside the world lock, and keeps it (the self-test, a
    /// console that wants a cluster ready); the store step takes <see cref="World.Sync"/>.
    /// </summary>
    public ClusterNav Preload(ClusterTemplate template)
    {
        var options = world.Config.Bots;
        var nav = ClusterNav.Build(template, options.LatticeStep, options.MaxExpansions);
        lock (world.Sync) Store(nav);
        return nav;
    }

    private void Store(ClusterNav nav)
    {
        _built[nav.Cluster] = (nav, ++_uses);
        while (_built.Count > Math.Max(1, world.Config.Bots.CacheClusters))
            _built.Remove(_built.MinBy(e => e.Value.Used).Key);
    }
}
