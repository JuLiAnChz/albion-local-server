using System.Diagnostics;
using System.Numerics;

namespace AlbionLocal.Server.Data;

/// <summary>
/// Stage I bots (bots SPEC §1.6): everything a bot needs to walk a cluster: its <see cref="WalkGrid"/>, the <see cref="RoadMap"/> over
/// it, the city <see cref="Pois"/> and the <see cref="Exits"/> bots arrive and leave by. Built once per cluster off the world thread
/// (Bots/BotNavCache, about 1.2 MB for a big open-world cluster) and only for clusters that get bots; immutable.
/// </summary>
public sealed class ClusterNav
{
    public required string Cluster { get; init; }

    public required WalkGrid Grid { get; init; }

    public required RoadMap Roads { get; init; }

    public required IReadOnlyList<BotPoi> Pois { get; init; }

    public required IReadOnlyList<BotExit> Exits { get; init; }

    /// <summary>Objects that got no POI (no walkable approach within 25 m).</summary>
    public int DroppedPois { get; init; }

    /// <summary>How long the build took.</summary>
    public TimeSpan BuildTime { get; init; }

    /// <summary>Whether a bot can walk here at all: a walkable component with at least one lattice node.</summary>
    public bool Usable => Grid.ComponentCells > 0 && Roads.Nodes > 0;

    /// <summary>The grid, the lattice of <paramref name="latticeStep"/> m with the routes, the POIs and exits of <paramref name="t"/>.</summary>
    public static ClusterNav Build(ClusterTemplate t, int latticeStep, int maxExpansions)
    {
        var watch = Stopwatch.StartNew();
        var grid = WalkGrid.Build(t);
        var roads = RoadMap.Build(grid, latticeStep, t.Roads, maxExpansions);
        var pois = BotPois.Of(t, grid, out var dropped);
        var exits = BotPois.Exits(t, grid);
        watch.Stop();
        return new ClusterNav { Cluster = t.Id, Grid = grid, Roads = roads, Pois = pois, Exits = exits, DroppedPois = dropped, BuildTime = watch.Elapsed };
    }

    /// <summary>"cells 360000, blocked 20109, walkable 76354; lattice 4797 nodes / 16942 edges; 7 routes; 40 POIs; 9 exits; 85 ms".</summary>
    public string Summary() => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"cells {Grid.Cells}, blocked {Grid.BlockedCells}, walkable {Grid.ComponentCells}; lattice {Roads.Nodes} nodes / {Roads.Edges} edges; {Roads.Routes.Count} routes; {Pois.Count} POIs ({DroppedPois} without an approach); {Exits.Count} exits; {BuildTime.TotalMilliseconds:0} ms");

    /// <summary>The walkable cell centre nearest to <paramref name="p"/> within <paramref name="radius"/> m (a GM's spot, a door).</summary>
    public Vector2? Nearest(Vector2 p, float radius) => Grid.Nearest(p, radius);
}
