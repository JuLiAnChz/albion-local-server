using System.Globalization;
using System.Numerics;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Persistence;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>Cluster travel commands (SPEC stage 3 §6): .tele, .exits.</summary>
public static class TransferCommands
{
    /// <summary>
    /// Moves the player to <paramref name="cluster"/> through a loading screen, as an exit does: the same commit, then the
    /// ChangeCluster response pushed without a request id (WorldSession.Teleport). Position: (<paramref name="x"/>,
    /// <paramref name="z"/>) as given, else the cluster's spawn point (<see cref="Transfers.DefaultSpawn"/>); facing 0. Only
    /// world.xml clusters the server can resolve and the client can load without a layout: no "@" ids (islands, generated
    /// dungeons) and no island or expedition types until instancing exists. A cluster not resolved yet is resolved first
    /// (in the background: no file is read under the world lock), and the move follows when it is in. The same cluster is
    /// allowed (the client loads it again; untested in the client).
    /// </summary>
    [Command("tele", Level = AccountLevel.GameMaster,
        Help = "moves the player to <cluster> (world.xml id, e.g. 0006 = Thetford bank) at (x, z), or at its spawn point, through a loading screen")]
    public static void Tele(CommandContext ctx, string cluster, float? x = null, float? z = null)
    {
        var player = ctx.RequirePlayer();
        if (x.HasValue != z.HasValue) throw new CommandError("give both x and z, or neither");
        var world = ctx.World;
        if (world.Clusters is not { } store) throw new CommandError("this server runs without cluster data");
        if (cluster.Contains('@')) throw new CommandError($"{cluster}: instanced clusters (islands, generated dungeons) cannot be entered yet");
        if (store.WorldIfLoaded is { } info && info.Find(cluster) is null) throw new CommandError($"{cluster} is not a world.xml cluster");
        var at = x is { } gx && z is { } gz ? new Vector2(gx, gz) : (Vector2?)null;

        void Go(ClusterTemplate target)
        {
            if (target.Info.Type.Contains("ISLAND", StringComparison.Ordinal) || target.Info.Type.Contains("EXPEDITION", StringComparison.Ordinal))
                throw new CommandError($"{cluster} is a {target.Info.Type} cluster, which needs an instance: not yet");
            if (target.Instances.Count == 0) throw new CommandError($"{cluster} has no template the client could load");
            if (player.Map is null || player.Session.Player != player) throw new CommandError("the player has left the world meanwhile");
            var spot = at ?? Transfers.DefaultSpawn(target);
            ctx.Reply(string.Create(CultureInfo.InvariantCulture, $"teleporting to {target.Id} ({spot.X:0.##}, {spot.Y:0.##})"));
            player.Session.Teleport(new Arrival(target.Id, spot, 0));
        }

        if (world.CachedCluster(cluster) is { } cached)
        {
            Go(cached);
            return;
        }
        ctx.Reply($"resolving cluster {cluster}; the move follows when it is in");
        world.ResolveCluster(cluster)!.ContinueWith(task => world.Post(() =>
        {
            try
            {
                if (!task.IsCompletedSuccessfully)
                    throw new CommandError($"{cluster}: {task.Exception?.InnerException?.Message ?? "not resolved"}");
                Go(task.Result);
            }
            catch (CommandError e) { ctx.Error(e.Message); }
        }), TaskScheduler.Default);
    }

    /// <summary>
    /// The exits of the player's cluster, nearest first: object id (ChangeCluster key 0), world.xml exit Guid, position and
    /// trigger square, distance, target cluster and exit, and, once the target cluster is resolved, the arrival point and
    /// facing a player coming through gets (SPEC stage 3 §3). Unresolved targets are resolved in the background, so asking
    /// again shows them.
    /// </summary>
    [Command("exits", Level = AccountLevel.GameMaster, Help = "the exits of the player's cluster with where they lead, nearest first")]
    public static void Exits(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        if (player.Map is not { Template: not null } map)
            throw new CommandError($"cluster {player.Map?.Cluster ?? player.Character.Cluster} has no world objects (yet)");
        var here = player.PositionAt(DateTime.UtcNow);
        var exits = map.Objects.Values.OfType<Exit>().OrderBy(e => Vector2.Distance(e.Position, here)).ToList();
        var lines = new List<string> { $"{map.Cluster}: {exits.Count} exit(s)" };
        var pending = 0;
        foreach (var e in exits)
        {
            var w = e.World;
            var size = Transfers.Size(e.Spawn);
            var head = string.Create(CultureInfo.InvariantCulture,
                $"  #{e.Id} {WorldSession.Short(w.Id)} at ({e.Position.X:0.##}, {e.Position.Y:0.##}) {size.X:0.#}x{size.Y:0.#}, {Vector2.Distance(e.Position, here):0.0} m");
            string where;
            if (w.TargetCluster.Length == 0 || w.Target == Guid.Empty) where = "no target";
            else if (ctx.World.CachedCluster(w.TargetCluster) is not { } target)
            {
                ctx.World.PrefetchCluster(w.TargetCluster);
                pending++;
                where = $"{w.TargetCluster} (exit {WorldSession.Short(w.Target)}; not resolved yet)";
            }
            else where = Transfers.Through(w, target, ctx.World.CollisionRadius) is { } to
                ? string.Create(CultureInfo.InvariantCulture,
                    $"{w.TargetCluster} (exit {WorldSession.Short(w.Target)}): arrival ({to.Position.X:0.##}, {to.Position.Y:0.##}) facing {to.Direction:0.##}")
                : $"{w.TargetCluster}: target exit {WorldSession.Short(w.Target)} missing or without spawnpos (refused, 1641)";
            lines.Add($"{head} -> {where}");
        }
        if (pending > 0) lines.Add($"  ({pending} target cluster(s) are being resolved: ask again for their arrival points)");
        ctx.Reply(string.Join("\n", lines));
    }
}
