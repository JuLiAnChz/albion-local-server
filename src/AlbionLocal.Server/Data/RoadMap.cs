using System.Numerics;

namespace AlbionLocal.Server.Data;

/// <summary>
/// Stage I bots (bots SPEC §1.6, bots-world SPEC §1.7, §2.1): the paths of a cluster over its <see cref="WalkGrid"/>. A lattice of
/// nodes every <see cref="Step"/> m (Bots:Nav:LatticeStep, 4) on walkable cells (cell x and z multiples of the step, the node at the
/// cell centre), each joined to its 8 lattice neighbours when the straight line between them is <see cref="WalkGrid.Clear"/>
/// (0000: 4,797 nodes; 1207: 25,608). <see cref="Path"/> walks straight when it can, else attaches both ends to a near node, runs
/// A* (Euclidean heuristic) and pulls the string tight with Clear lines: 2-13 legs, 4-11 % longer than the straight line in the
/// research's Python port, a few ms in C#. <see cref="Routes"/> are the cluster's road graphs on walkable ground, the patrols' beats.
/// Immutable once built; any thread may read it.
/// </summary>
public sealed class RoadMap
{
    private readonly WalkGrid _grid;
    private readonly Dictionary<(int, int), int> _index = [];
    private readonly List<Vector2> _nodes = [];
    private readonly List<List<(int To, float Cost)>> _edges = [];

    /// <summary>The lattice step in metres.</summary>
    public int Step { get; }

    /// <summary>Lattice nodes and edges (each edge once).</summary>
    public int Nodes => _nodes.Count;

    public int Edges { get; private set; }

    /// <summary>The road graphs (<see cref="ClusterTemplate.Roads"/>) as walkable polylines, each with at least two points (none in cities).</summary>
    public IReadOnlyList<IReadOnlyList<Vector2>> Routes { get; private set; } = [];

    private RoadMap(WalkGrid grid, int step)
    {
        _grid = grid;
        Step = step;
    }

    /// <summary>The lattice of <paramref name="grid"/> every <paramref name="step"/> m, and the routes of <paramref name="roads"/>.</summary>
    public static RoadMap Build(WalkGrid grid, int step, IReadOnlyList<RoadLine> roads, int maxExpansions)
    {
        var map = new RoadMap(grid, Math.Max(1, step));
        step = map.Step;
        for (var z = grid.MinZ; z < grid.MaxZ; z++)
        {
            if (Mod(z, step) != 0) continue;
            for (var x = grid.MinX; x < grid.MaxX; x++)
            {
                if (Mod(x, step) != 0 || !grid.Walkable(x, z)) continue;
                map._index[(FloorDiv(x, step), FloorDiv(z, step))] = map._nodes.Count;
                map._nodes.Add(new Vector2(x + 0.5f, z + 0.5f));
                map._edges.Add([]);
            }
        }
        foreach (var ((kx, kz), i) in map._index)
            foreach (var (dx, dz) in (ReadOnlySpan<(int, int)>)[(1, 0), (0, 1), (1, 1), (1, -1)])
            {
                if (!map._index.TryGetValue((kx + dx, kz + dz), out var j) || !grid.Clear(map._nodes[i], map._nodes[j])) continue;
                var cost = Vector2.Distance(map._nodes[i], map._nodes[j]);
                map._edges[i].Add((j, cost));
                map._edges[j].Add((i, cost));
                map.Edges++;
            }
        map.Routes = [.. roads.Select(r => map.Route(r, maxExpansions)).Where(r => r.Count >= 2)];
        return map;
    }

    private static int Mod(int v, int m) => ((v % m) + m) % m;

    private static int FloorDiv(int v, int m) => (int)Math.Floor((double)v / m);

    /// <summary>
    /// A walkable way from <paramref name="from"/> to <paramref name="to"/> (both in the component), first point <paramref name="from"/>,
    /// last <paramref name="to"/>, every leg <see cref="WalkGrid.Clear"/>; null when there is none, an end attaches to no node, or A*
    /// takes more than <paramref name="maxExpansions"/> nodes (Bots:Nav:MaxExpansions, 20000).
    /// </summary>
    public IReadOnlyList<Vector2>? Path(Vector2 from, Vector2 to, int maxExpansions)
    {
        if (_grid.Clear(from, to)) return [from, to];
        if (Attach(from) is not { } start || Attach(to) is not { } goal) return null;
        var g = new Dictionary<int, float> { [start] = 0 };
        var previous = new Dictionary<int, int>();
        var open = new PriorityQueue<int, float>();
        open.Enqueue(start, Vector2.Distance(_nodes[start], _nodes[goal]));
        var expansions = 0;
        var found = false;
        var closed = new HashSet<int>();
        while (open.TryDequeue(out var u, out _))
        {
            if (!closed.Add(u)) continue; // a stale entry of a node reached again more cheaply
            if (u == goal)
            {
                found = true;
                break;
            }
            if (++expansions > maxExpansions) return null;
            foreach (var (v, cost) in _edges[u])
            {
                var through = g[u] + cost;
                if (g.TryGetValue(v, out var known) && known <= through) continue;
                g[v] = through;
                previous[v] = u;
                open.Enqueue(v, through + Vector2.Distance(_nodes[v], _nodes[goal]));
            }
        }
        if (!found) return null;
        var chain = new List<int> { goal };
        while (chain[^1] != start) chain.Add(previous[chain[^1]]);
        chain.Reverse();
        var points = new List<Vector2>(chain.Count + 2) { from };
        points.AddRange(chain.Select(i => _nodes[i]));
        points.Add(to);
        return Pull(points);
    }

    /// <summary>String pulling (roadmap.py): from each kept point, the farthest later point with a Clear line.</summary>
    private List<Vector2> Pull(List<Vector2> points)
    {
        var result = new List<Vector2> { points[0] };
        var i = 0;
        while (i < points.Count - 1)
        {
            var j = points.Count - 1;
            while (j > i + 1 && !_grid.Clear(points[i], points[j])) j--;
            result.Add(points[j]);
            i = j;
        }
        return result;
    }

    /// <summary>The node within 3 lattice steps of <paramref name="p"/> with a Clear line to it, nearest first (the first 8 tried); null when none.</summary>
    private int? Attach(Vector2 p)
    {
        var (kx, kz) = (FloorDiv((int)Math.Floor(p.X), Step), FloorDiv((int)Math.Floor(p.Y), Step));
        var near = new List<int>();
        for (var dz = -3; dz <= 3; dz++)
            for (var dx = -3; dx <= 3; dx++)
                if (_index.TryGetValue((kx + dx, kz + dz), out var i)) near.Add(i);
        foreach (var i in near.OrderBy(i => Vector2.DistanceSquared(p, _nodes[i])).Take(8))
            if (_grid.Clear(p, _nodes[i])) return i;
        return null;
    }

    /// <summary>
    /// Whether a path may start at <paramref name="p"/>: its cell walkable and a lattice node near it with a Clear line (what
    /// <see cref="Path"/> needs of its start when the straight line is not Clear). A failed path from such a start is the goal's fault.
    /// </summary>
    public bool CanAttach(Vector2 p) => _grid.Walkable(p) && Attach(p) is not null;

    /// <summary>A random lattice node (a spot on walkable ground); null when the lattice is empty.</summary>
    public Vector2? RandomNode(Random random) => _nodes.Count == 0 ? null : _nodes[random.Next(_nodes.Count)];

    /// <summary>
    /// A road graph as a walkable polyline: its nodes in a walk along its links from an end (depth first), each moved onto walkable
    /// ground (<see cref="WalkGrid.Nearest"/> within 6 m, else left out), a link that is not Clear bridged with <see cref="Path"/>.
    /// </summary>
    private List<Vector2> Route(RoadLine road, int maxExpansions)
    {
        var links = new List<int>[road.Nodes.Count];
        for (var i = 0; i < links.Length; i++) links[i] = [];
        foreach (var (a, b) in road.Links)
        {
            links[a].Add(b);
            links[b].Add(a);
        }
        var first = Enumerable.Range(0, links.Length).FirstOrDefault(i => links[i].Count == 1);
        var order = new List<int>();
        var seen = new bool[links.Length];
        var stack = new Stack<int>();
        stack.Push(first);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (seen[n]) continue;
            seen[n] = true;
            order.Add(n);
            foreach (var m in Enumerable.Reverse(links[n])) if (!seen[m]) stack.Push(m);
        }
        var route = new List<Vector2>();
        foreach (var n in order)
        {
            if (_grid.Nearest(road.Nodes[n], 6) is not { } p) continue;
            if (route.Count > 0 && route[^1] == p) continue;
            if (route.Count > 0 && !_grid.Clear(route[^1], p))
            {
                if (Path(route[^1], p, maxExpansions) is not { } bridge) continue;
                route.AddRange(bridge.Skip(1).Take(bridge.Count - 2));
            }
            route.Add(p);
        }
        return route;
    }
}
