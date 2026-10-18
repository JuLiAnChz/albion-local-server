using System.Numerics;
using System.Runtime.CompilerServices;
using AlbionLocal.Photon;
using AlbionLocal.Server.Data;

namespace AlbionLocal.Server.AI;

/// <summary>
/// StageN mobnav (analysis/work/stageN/mobnav/SPEC.md): the mobs' walk grid, paths and line of sight.
/// <list type="bullet">
/// <item><b>Grid</b>: the bots' <see cref="WalkGrid"/> of the map's <see cref="ClusterTemplate"/> (tile collision bit 0, 1 m cells),
/// one per template object, shared by every map of it and every mob on it; built once on a pool thread (tens of ms for an open-world
/// cluster, ~1 byte per cell), taken by the world thread on its next tick (<see cref="World.Post"/>). Until then, and on a map without
/// a template or colliders, the mobs walk straight as before. A random dungeon floor's template comes from its layout with the same
/// colliders, so it gets its grid the same way.</item>
/// <item><b>Paths</b>: A* over the free cells (8 neighbours, no corner cutting), inside a window around both ends of at most
/// <see cref="MaxWindow"/> cells a side, at most <see cref="MaxExpansions"/> cells expanded, then pulled tight with
/// <see cref="WalkGrid.ClearFree"/> lines; when the goal is not reached the path to the expanded cell nearest to it (a mob gets as
/// close as it can; the leash does the rest). At most <see cref="BudgetPerTick"/> searches a world tick over all mobs; a mob over
/// the budget keeps its walk and asks again next tick.</item>
/// <item><b>Sight</b>: <see cref="WalkGrid.Sight"/> on the un-inflated grid.</item>
/// </list>
/// Touched only under <see cref="World.Sync"/> (but the build).
/// </summary>
public static class MobNav
{
    /// <summary>Cells A* may expand per search.</summary>
    public const int MaxExpansions = 4000;

    /// <summary>The search window's largest side in cells (and the margin around the two ends).</summary>
    public const int MaxWindow = 160, Margin = 24;

    /// <summary>Searches a world tick at most, over all mobs.</summary>
    public const int BudgetPerTick = 24;

    /// <summary>Whether mobs use the grid at all (a switch for tests and a GM).</summary>
    public static bool Enabled { get; set; } = true;

    private sealed class Slot
    {
        public WalkGrid? Grid;
        public bool Building;
        public bool Failed;
    }

    private static readonly ConditionalWeakTable<ClusterTemplate, Slot> Grids = new();
    private static readonly ConditionalWeakTable<Map, WalkGrid> Overrides = new();

    [ThreadStatic] private static World? _budgetWorld;
    [ThreadStatic] private static TimeSpan _budgetTick;
    [ThreadStatic] private static int _budgetUsed;

    /// <summary>Searches run and their total time, for .mob nav / the log.</summary>
    public static long Searches { get; private set; }

    public static double SearchMs { get; private set; }

    /// <summary>A hand-made grid for <paramref name="map"/> (the self-test), or null to drop it.</summary>
    public static void Use(Map map, WalkGrid? grid)
    {
        Overrides.Remove(map);
        if (grid is not null) Overrides.Add(map, grid);
    }

    /// <summary>The grid of <paramref name="map"/> when built; else null and, unless one runs or failed, a build is started.</summary>
    public static WalkGrid? Grid(World world, Map map)
    {
        if (!Enabled) return null;
        if (Overrides.TryGetValue(map, out var own)) return own;
        if (map.Template is not { Colliders.Count: > 0 } template) return null;
        var slot = Grids.GetValue(template, _ => new Slot());
        if (slot.Grid is not null || slot.Building || slot.Failed) return slot.Grid;
        slot.Building = true;
        Task.Run(() => WalkGrid.Build(template)).ContinueWith(task => world.Post(() =>
        {
            slot.Building = false;
            if (task.IsCompletedSuccessfully) slot.Grid = task.Result;
            else
            {
                slot.Failed = true;
                Log.Warn("ai", $"mob walk grid of {template.Id} not built: {task.Exception?.InnerException?.Message}; its mobs walk straight");
            }
        }), TaskScheduler.Default);
        return null;
    }

    /// <summary>Whether <paramref name="b"/> is in sight of <paramref name="a"/> on <paramref name="map"/> (no grid: true).</summary>
    public static bool Sees(World world, Map map, Vector2 a, Vector2 b) => Grid(world, map) is not { } g || g.Sight(a, b);

    /// <summary>Takes one search from this tick's budget; false when it is spent.</summary>
    private static bool Budget(World world)
    {
        if (world != _budgetWorld || world.Now != _budgetTick) (_budgetWorld, _budgetTick, _budgetUsed) = (world, world.Now, 0);
        if (_budgetUsed >= BudgetPerTick) return false;
        _budgetUsed++;
        return true;
    }

    /// <summary>
    /// The way from <paramref name="from"/> to <paramref name="to"/> on <paramref name="grid"/>, first point <paramref name="from"/>:
    /// straight when the line is clear; else the A* path (<paramref name="partial"/>: to the nearest reachable point when the goal is
    /// not). Null when there is none, or the tick's budget is spent (<paramref name="overBudget"/>).
    /// </summary>
    public static List<Vector2>? Path(World world, WalkGrid grid, Vector2 from, Vector2 to, bool partial, out bool overBudget)
    {
        overBudget = false;
        if (grid.ClearFree(from, to)) return [from, to];
        if (!Budget(world))
        {
            overBudget = true;
            return null;
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var path = Search(grid, from, to, partial);
        Searches++;
        SearchMs += watch.Elapsed.TotalMilliseconds;
        return path;
    }

    // ---- A* ----

    // Buffers reused by every search of the thread (the world thread; the self-test's worlds may run on others).
    [ThreadStatic] private static float[]? _g;
    [ThreadStatic] private static int[]? _parent;
    [ThreadStatic] private static byte[]? _state; // 0 unseen, 1 open, 2 closed
    [ThreadStatic] private static PriorityQueue<int, float>? _open;

    /// <summary>The A* itself (no budget), public for the self-test.</summary>
    public static List<Vector2>? Search(WalkGrid grid, Vector2 from, Vector2 to, bool partial)
    {
        // A start or goal off the free cells (a mob by a wall, a player hugging one): the nearest free cell within 3 m.
        if (grid.NearestFree(from, 3) is not { } start) return null;
        var goalPoint = grid.NearestFree(to, 3);
        if (goalPoint is null && !partial) return null;
        var goalAim = goalPoint ?? to;
        int sx = (int)Math.Floor(start.X), sz = (int)Math.Floor(start.Y);
        int gx = (int)Math.Floor(goalAim.X), gz = (int)Math.Floor(goalAim.Y);
        int x0 = Math.Min(sx, gx) - Margin, z0 = Math.Min(sz, gz) - Margin;
        int x1 = Math.Max(sx, gx) + Margin, z1 = Math.Max(sz, gz) + Margin;
        // A window wider than MaxWindow: centred on the start (the goal may lie outside: partial at best).
        if (x1 - x0 > MaxWindow) (x0, x1) = (sx - MaxWindow / 2, sx + MaxWindow / 2);
        if (z1 - z0 > MaxWindow) (z0, z1) = (sz - MaxWindow / 2, sz + MaxWindow / 2);
        int w = x1 - x0 + 1, h = z1 - z0 + 1, n = w * h;
        if (_g is null || _g.Length < n) (_g, _parent, _state) = (new float[n], new int[n], new byte[n]);
        var (g, parent, state) = (_g, _parent!, _state!);
        var open = _open ??= new PriorityQueue<int, float>();
        Array.Clear(state, 0, n);
        open.Clear();
        int Index(int x, int z) => (x - x0) + (z - z0) * w;
        bool Inside(int x, int z) => x >= x0 && x <= x1 && z >= z0 && z <= z1;
        float H(int x, int z)
        {
            int dx = Math.Abs(x - gx), dz = Math.Abs(z - gz);
            return Math.Max(dx, dz) + 0.41421356f * Math.Min(dx, dz);
        }
        var s = Index(sx, sz);
        g[s] = 0;
        parent[s] = -1;
        state[s] = 1;
        open.Enqueue(s, H(sx, sz));
        int best = s, found = -1;
        var bestH = H(sx, sz);
        var expansions = 0;
        while (open.TryDequeue(out var u, out _))
        {
            if (state[u] == 2) continue;
            state[u] = 2;
            int ux = u % w + x0, uz = u / w + z0;
            if (ux == gx && uz == gz)
            {
                found = u;
                break;
            }
            var hu = H(ux, uz);
            if (hu < bestH) (best, bestH) = (u, hu);
            if (++expansions > MaxExpansions) break;
            for (var dz = -1; dz <= 1; dz++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int vx = ux + dx, vz = uz + dz;
                    if (!Inside(vx, vz) || !grid.Free(vx, vz)) continue;
                    if (dx != 0 && dz != 0 && (!grid.Free(ux + dx, uz) || !grid.Free(ux, uz + dz))) continue; // no corner cutting
                    var v = Index(vx, vz);
                    if (state[v] == 2) continue;
                    var through = g[u] + (dx != 0 && dz != 0 ? 1.41421356f : 1f);
                    if (state[v] == 1 && g[v] <= through) continue;
                    g[v] = through;
                    parent[v] = u;
                    state[v] = 1;
                    open.Enqueue(v, through + H(vx, vz));
                }
        }
        if (found < 0 && (!partial || best == s)) return null;
        var end = found >= 0 ? found : best;
        var cells = new List<Vector2>();
        for (var c = end; c >= 0; c = parent[c]) cells.Add(new Vector2(c % w + x0 + 0.5f, c / w + z0 + 0.5f));
        cells.Reverse();
        var points = new List<Vector2>(cells.Count + 2) { from };
        if (start != from) points.Add(start);
        points.AddRange(cells);
        if (found >= 0) points.Add(goalAim);
        return Pull(grid, points);
    }

    /// <summary>String pulling: from each kept point the farthest later one with a <see cref="WalkGrid.ClearFree"/> line (the first leg off-grid allowed).</summary>
    private static List<Vector2> Pull(WalkGrid grid, List<Vector2> points)
    {
        var result = new List<Vector2> { points[0] };
        var i = 0;
        while (i < points.Count - 1)
        {
            var j = points.Count - 1;
            while (j > i + 1 && !grid.ClearFree(points[i], points[j])) j--;
            if (points[j] != result[^1]) result.Add(points[j]);
            i = j;
        }
        return result;
    }
}
