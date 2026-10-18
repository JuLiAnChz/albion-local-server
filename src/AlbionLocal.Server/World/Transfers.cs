using System.Numerics;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Game;

namespace AlbionLocal.Server;

/// <summary>Where a cluster change leads: the cluster (Join key 8), the arrival point (key 9) and the facing in degrees (key 10).</summary>
public readonly record struct Arrival(string Cluster, Vector2 Position, float Direction);

/// <summary>
/// A travel-point journey as the client prices it (<see cref="Transfers.Route"/>): the summed world.xml costmodifiers,
/// whether it needs an empty inventory (only no-luggage connections reach the target), the clusters on the way.
/// </summary>
public sealed record JourneyRoute(float Cost, bool NoLuggage, IReadOnlyList<string> Clusters);

/// <summary>
/// The client-data rules of a cluster change (SPEC stage 3, "SPEC" below; analysis/work/stage3/spec/arrivals.py is the
/// reference): where an exit leads, whether the player stands in its trigger square, and how long after a Join an exit may
/// be used. Pure functions of the resolved clusters, so any thread may call them.
/// </summary>
public static class Transfers
{
    /// <summary>The exit cooldown of a city cluster (ctp.auf 0x1809a8bc0 uses c4.l(1), one second, for ClusterType 6 = City).</summary>
    public static readonly TimeSpan CityExitCooldown = TimeSpan.FromSeconds(1);

    /// <summary>
    /// SPEC §3: the arrival through <paramref name="exit"/> (a world.xml exit whose targetid is "&lt;exit guid&gt;@&lt;cluster&gt;")
    /// in its resolved target cluster <paramref name="target"/>: the target exit's spawnpos turned with its tile into world
    /// coordinates (<see cref="ExitSpawn.SpawnPosition"/>), facing away from that exit (<see cref="Facing"/>; every captured
    /// case to 0.001°), pushed clear of the exit tile's collision by <paramref name="collisionRadius"/>
    /// (<see cref="Clear"/>; 0 = no push). Null when the target exit is not in the cluster or has no spawnpos: the server
    /// then refuses the exit rather than send the player where the client cannot put it (no fallback to the exit's own
    /// position, which lies inside its trigger, §3.4).
    /// </summary>
    public static Arrival? Through(WorldExit exit, ClusterTemplate target, float collisionRadius = 0)
    {
        if (exit.Target == Guid.Empty || target.Exits.FirstOrDefault(e => e.Guid == exit.Target) is not { SpawnPosition: { } at } door) return null;
        var from = door.World?.Position ?? door.Position;
        return new Arrival(target.Id, Clear(door, door.Collision, from, at, collisionRadius), Facing(from, at));
    }

    /// <summary>At most this many pushes (<see cref="Clear"/>); every captured push took one.</summary>
    public const int MaxClearSteps = 4;

    /// <summary>
    /// The arrival <paramref name="at"/> made clear of the walk-blocking cells of <paramref name="tile"/> (<paramref name="grid"/>,
    /// its collision): while the character's circle (<paramref name="radius"/>, characters.xml collisionradius 0.75)
    /// there overlaps a blocked cell, the point moves on by the radius, away from <paramref name="from"/> (the exit's
    /// position: along the facing), at most <see cref="MaxClearSteps"/> times. This is the bank-door offset of SPEC §3/§13:
    /// both bank entrances (SWAMP_GREEN_T1_BANK_ENTRANCE in Thetford, FOREST_GREEN_T1_BANK_ENTRANCE in Lymhurst, exit kind
    /// DungeonSandbox) have spawnpos (0, -10) on the edge of a pillar (cells x -2..2, z -10..-7 of their 30 x 30 grid),
    /// and the official server put the player 0.75 m further, (-10, -20.750141) and (-20, -20.75) (captures 002835 and
    /// 103829): one step, a push by exactly the radius (not to the edge plus the radius, which would give -20.75 in
    /// Thetford). No other exit of 0000, 0006, 0007, 1000, 1001, 1002, 1207 has a blocked cell within 0.75 m of its
    /// spawnpos, and the other 14 captured exit arrivals (9 in 103829/114814, 5 in 002835) are exact. Only the arrival
    /// tile's own grid is looked at, not its neighbours'.
    /// </summary>
    public static Vector2 Clear(ClusterObject tile, TileCollision? grid, Vector2 from, Vector2 at, float radius)
    {
        if (grid is null || radius <= 0 || at == from) return at;
        var step = Vector2.Normalize(at - from) * radius;
        for (var i = 0; i < MaxClearSteps && grid.Blocks(Local(tile, at), radius); i++) at += step;
        return at;
    }

    /// <summary><paramref name="world"/> in the frame of <paramref name="tile"/> (the inverse of the template placement: back by its yaw).</summary>
    private static Vector2 Local(ClusterObject tile, Vector2 world)
    {
        var (x, z) = TileMath.Rotate(world.X - tile.Position.X, world.Y - tile.Position.Y, -tile.Direction);
        return new Vector2((float)x, (float)z);
    }

    /// <summary>
    /// SPEC §3.3, Join key 10: (atan2(arrival.x − exit.x, arrival.z − exit.z) · 180/π) mod 360, from the full float values
    /// (3-decimal inputs would be off by up to 0.003°).
    /// </summary>
    public static float Facing(Vector2 exitPosition, Vector2 arrival) =>
        (float)TileMath.Mod360(Math.Atan2((double)arrival.X - exitPosition.X, (double)arrival.Y - exitPosition.Y) * 180 / Math.PI);

    /// <summary>The exit component's size attribute ("9 9" for the Thetford bank door, 15 15 for city gates), 1 × 1 when it has none.</summary>
    public static Vector2 Size(ExitSpawn exit) => TileMath.V2(exit.Attr("size")) ?? Vector2.One;

    /// <summary>
    /// Whether <paramref name="position"/> is in the exit's trigger square, centre ± size/2 per axis, axis-aligned as the
    /// client's (ExitObjectView.OnTriggerStay; every exit size of the step is square), with <paramref name="tolerance"/>
    /// metres to spare on each side.
    /// </summary>
    public static bool InSquare(Vector2 centre, Vector2 size, Vector2 position, float tolerance = 0) =>
        Math.Abs(position.X - centre.X) <= size.X / 2 + tolerance && Math.Abs(position.Y - centre.Y) <= size.Y / 2 + tolerance;

    /// <summary>
    /// SPEC §2.1: how long after a Join (key 68) the client lets the player use an exit of <paramref name="cluster"/>: 1 s in
    /// a city (ClusterType City = the CTY token of the .cluster file name, bgu.bx: 0000, its bank 0006 and market 0007, 1000,
    /// 1001, 1002 …), else gamedata.xml ClusterChange cooldownseconds (10 s; 1207 and 0204 are WRL).
    /// </summary>
    public static TimeSpan ExitCooldown(ClusterName cluster, GameData? data) =>
        cluster.Type == "CTY" ? CityExitCooldown : TimeSpan.FromSeconds(data?.ClusterChangeCooldownSeconds ?? 10);

    /// <summary>
    /// The journey from travel point <paramref name="from"/> to <paramref name="to"/> ("&lt;guid&gt;@&lt;cluster&gt;", as BuyJourney
    /// key 1 and world.xml connection/@to name them), as the client works it out: a0.abo(ax, bool) 0x18062fa50 runs
    /// Dijkstra from every travel point over world.xml travelpoints/connections (edge cost = costmodifier, a0.c nodes) once
    /// with the noluggage="true" edges left out (table a0+0x98) and once with all (a0+0xa0, built in a0.db 0x180635690); the
    /// price, a0.abo(string, string, out …) 0x18062ce50, takes the first table that reaches the target and says "no luggage"
    /// only for the second. So: the cheapest route with luggage when there is one, else the cheapest one at all, which
    /// then needs an empty inventory (the client refuses it itself while the player carries anything, ctp.atx
    /// 0x1809a7f00). Routes run over several hops: Lymhurst 1000 has no connection to Thetford 0000, the captured journey
    /// 1000 → 0000 is 1000 → 4000 → 0000, cost 10 (5 + 5; without luggage 1000 → 3003 → 0000, 8). Null = no route.
    /// </summary>
    public static JourneyRoute? Route(WorldInfo world, string from, string to)
    {
        var points = new Dictionary<string, WorldTravelpoint>(StringComparer.Ordinal);
        foreach (var cluster in world.Clusters)
            foreach (var t in cluster.Travelpoints)
                points.TryAdd(TravelpointKey(t.Id, cluster.Id), t);
        if (!points.ContainsKey(from) || !points.ContainsKey(to)) return null;
        foreach (var noLuggage in new[] { false, true })
        {
            var cost = new Dictionary<string, float>(StringComparer.Ordinal) { [from] = 0 };
            var previous = new Dictionary<string, string>(StringComparer.Ordinal);
            var queue = new PriorityQueue<string, float>();
            queue.Enqueue(from, 0);
            var done = new HashSet<string>(StringComparer.Ordinal);
            while (queue.TryDequeue(out var at, out var c))
            {
                if (!done.Add(at)) continue;
                if (at == to) break;
                foreach (var edge in points[at].Connections)
                {
                    var next = TravelpointKey(edge.Target, edge.TargetCluster);
                    if (edge.NoLuggage && !noLuggage || !points.ContainsKey(next) || done.Contains(next)) continue;
                    var through = c + edge.CostModifier;
                    if (cost.TryGetValue(next, out var known) && known <= through) continue;
                    cost[next] = through;
                    previous[next] = at;
                    queue.Enqueue(next, through);
                }
            }
            if (!done.Contains(to)) continue;
            var path = new List<string> { to };
            while (previous.TryGetValue(path[^1], out var before)) path.Add(before);
            path.Reverse();
            return new JourneyRoute(cost[to], noLuggage, [.. path.Select(p => p[(p.IndexOf('@') + 1)..])]);
        }
        return null;
    }

    /// <summary>"&lt;guid&gt;@&lt;cluster&gt;", the client's name of a travel point (ax.h(Guid, aa) 0x1806431d0).</summary>
    public static string TravelpointKey(Guid id, string cluster) => $"{id}@{cluster}";

    /// <summary>
    /// Where .tele puts a player who gave no position (SPEC §6): the cluster's first respawn point, else the spawnpos of its
    /// first travel point, else that of its first exit (where players coming through that exit arrive; added here so that an
    /// interior without the other two does not drop the player in a wall), else the centre of the cluster.
    /// </summary>
    public static Vector2 DefaultSpawn(ClusterTemplate cluster) =>
        cluster.RespawnPoints.Select(r => (Vector2?)r.Position).FirstOrDefault()
        ?? cluster.Travelpoints.Select(t => t.SpawnPosition).FirstOrDefault(p => p is not null)
        ?? cluster.Exits.Select(e => e.SpawnPosition).FirstOrDefault(p => p is not null)
        ?? cluster.Origin + cluster.Size / 2;
}
