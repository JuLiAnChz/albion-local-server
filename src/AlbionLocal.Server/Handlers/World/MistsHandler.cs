using System.Numerics;
using AlbionLocal.Photon;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Network;

/// <summary>
/// The ways out of a mists zone (analysis/work/stageL/mists/SPEC.md, "SPEC" here, §3.6, §6.5): ChangeCluster {0: object id} on a border exit
/// (537: the next zone of the mode, answered {0 id, 3 layout, 4 clustertype, 5 tier}), on the Brecilien entrance (527: {0: "5001"}) and on the
/// open-world exit (523: {0: its target}), each with the exit square and cooldown checks of a world exit; a mists world cluster's city entrance
/// (5100-5103) the same; and op 476 on the return portal (RC 0, then the push to the way back). A next zone not built yet is answered once it
/// is, with the kept request id, as a random dungeon floor. Phase B: Knightfall Abbey's exits (538: a zone of exitsleadtomode, as a border),
/// Brecilien's static entrance (op 477) and its roads entrance (op 478, refused).
/// </summary>
public sealed partial class WorldSession
{
    /// <summary>Whether the request is a mists change and its answer (null = deferred). False = not a mists object.</summary>
    private bool MistsChange(Player player, ChangeClusterRequest req, out ChangeClusterResponse? response)
    {
        response = null;
        if (player.Map is not { } map || !map.Objects.TryGetValue(req.ee, out var o) || o is not (MistsBorderExit or MistsCityEntrance or MistsOpenWorldExit or MistsDungeonExit)) return false;
        if (player.Combat.Down is { } down)
        {
            response = Refuse(player, req.ee, ChangeClusterRefusal.NotNearAnExit, $"knocked down ({down.Kind})");
            return true;
        }
        var size = o is MistsBorderExit border ? border.Size : o is MistsDungeonExit door ? door.Size : new Vector2(world.DungeonSettings.MinSquare);
        if (Reach(player, map, o.Position, Vector2.Max(size, new Vector2(world.DungeonSettings.MinSquare)), o.Kind) is { } refused)
        {
            response = Refuse(player, req.ee, refused.Code, refused.Why);
            return true;
        }
        var zone = world.Mists.Find(map.Cluster);
        switch (o)
        {
            case MistsCityEntrance:
                // SPEC §6.5: to Brecilien's entrance hub, out of the mists (a world cluster's entrance as well).
                var city = world.Mists.CityArrival();
                if (zone is not null) world.Mists.Out(player);
                Commit(player, city, $"mists:{map.Cluster}:city");
                response = new ChangeClusterResponse { anu = city.Cluster };
                return true;
            case MistsOpenWorldExit exit when zone is not null:
                var target = exit.TargetOf(player);
                var back = player.Character.DungeonReturn;
                var to = world.Mists.Out(player);
                if (target != to.Cluster)
                {
                    // Mists:OpenWorldExit Random: the drawn world cluster at its default spawn (prefetched at the zone's fill).
                    if (world.CachedCluster(target) is not { } t)
                    {
                        player.Character.DungeonReturn = back;
                        response = Refuse(player, req.ee, ChangeClusterRefusal.ExitNotConnected, $"{target} is still being read");
                        return true;
                    }
                    to = new Arrival(target, Transfers.DefaultSpawn(t), 0);
                }
                Commit(player, to, $"mists:{zone.Id}:openworld");
                response = new ChangeClusterResponse { anu = to.Cluster };
                return true;
            case MistsBorderExit or MistsDungeonExit when zone is not null:
                // Phase B: a Knightfall Abbey exit (538) as a border into a zone of the mode's exitsleadtomode (Mists.LeadsTo), a resource zone's border too.
                response = ThroughBorder(player, zone, o, RequestId, RequestEncrypted);
                return true;
        }
        return false;
    }

    /// <summary>
    /// SPEC §6.5 "Border exit": the next zone of the mode (Private: the team's behind this border, else a new one of a rarity rolled anew; Shared:
    /// an open one of the mode and tier with room, else a new one; never the zone itself), arriving at its opposite border with a return
    /// portal, answered {0 id, 3 layout, 4 clustertype, 5 tier}. A new zone is built first (1669 meanwhile; teammates taking the border
    /// meanwhile wait for the same build, Mists.Pending); the answer follows with the kept request id.
    /// </summary>
    private ChangeClusterResponse? ThroughBorder(Player player, MistsZone from, WorldObject border, int? requestId, bool encrypted,
        Task<MistsBuild>? built = null, string? builtKey = null)
    {
        var mists = world.Mists;
        var place = $"border:{from.Id}:{border.Id}";
        var teamKey = builtKey ?? "";
        var next = built is not null ? mists.Resolve(player, built, teamKey, from.Reroll, from.Entrance) : mists.NextZone(player, from, place, out teamKey);
        if (next is null && built is not null) return Refuse(player, border.Id, ChangeClusterRefusal.ExitNotConnected, "the next zone closed meanwhile");
        if (next is null)
        {
            if (mists.Pending(player, teamKey, place, () => mists.BuildNext(from)) is not { } build)
                return Refuse(player, border.Id, ChangeClusterRefusal.ExitNotConnected, $"no map of {from.Mode.Mapset} to build");
            var pending = new PendingChange(player, player.Map!, border.Id, requestId, encrypted);
            _changing = pending;
            Log.Info(Name, $"'{player.Character.Name}' takes the {border.Kind} {border.TypeName} of {from.Id}: building the next zone, the answer follows");
            var key = teamKey;
            build.ContinueWith(t => world.Post(() =>
            {
                if (_changing != pending || _closed || Status != SessionStatus.InWorld || Player != pending.Player || pending.Player.Map != pending.Map || from.Closed)
                {
                    mists.Abandon(player, build);
                    if (_changing == pending) _changing = null;
                    return;
                }
                _changing = null;
                var resp = t.IsCompletedSuccessfully ? ThroughBorder(player, from, border, requestId, encrypted, t, key)
                    : Refuse(player, border.Id, ChangeClusterRefusal.ExitNotConnected, $"the next zone was not built: {t.Exception?.InnerException?.Message}");
                if (!t.IsCompletedSuccessfully) mists.Abandon(player, build);
                if (resp is not null) SendResponse(OperationCodes.ChangeCluster, resp, requestId, encrypted);
            }), TaskScheduler.Default);
            return null;
        }
        // Into a zone of another mode (Knightfall Abbey's way out, a resource zone's border) the arrival is a random border's, not the opposite one.
        var to = mists.Send(player, next, mists.ArrivalFor(next, next.Mode == from.Mode ? border.Position : null), answer: true);
        Commit(player, to, $"mists:{next.Id}:border");
        return new ChangeClusterResponse { anu = next.Id, anx = next.Bytes, any = next.Mode.ClusterType, anz = next.Tier }; // [0] the id (= Join key 8), [3] a9i, [4], [5]
    }

    /// <summary>
    /// MistsUseImmediateReturnExit (op 476, xv) {0 GameTimeStamp, 1 the return portal}: the player's own portal before its end (SPEC §3.7: the client
    /// sends it with a response callback) → a bare RC 0, then the push to the way back; after its end, or another's, refused with a chat line.
    /// </summary>
    [Handles(OperationCodes.MistsUseImmediateReturnExit, Status = SessionStatus.InWorld)]
    private AlbionResponse? MistsUseImmediateReturnExit(MistsUseImmediateReturnExitRequest req)
    {
        if (Player is not { Map: { } map } player) return null;
        if (world.Mists.Find(map.Cluster) is not { } zone || zone.Returns.GetValueOrDefault(player.Character.Id) is not { } portal || portal.Id != req.vw
            || world.GameNow > portal.End || player.Combat.Down is not null)
        {
            Log.Info(Name, $"'{player.Character.Name}' mists return portal {req.vw} refused");
            Expeditions.Tell(player, world.MistsSettings.Text("ReturnExpired"));
            return new GenericResponse { ReturnCode = 1 };
        }
        SendResponse(OperationCodes.MistsUseImmediateReturnExit, new GenericResponse(), RequestId, RequestEncrypted);
        Teleport(world.Mists.Out(player), $"mists:{zone.Id}:return");
        return null;
    }

    /// <summary>
    /// MistsUseStaticEntrance (op 477, xw) {0 GameTimeStamp, 1 the entrance (522), 2 the mode's id}: phase B (SPEC §6.7) Brecilien's static
    /// entrance; accepted → aji {0: the stamp the player goes in at} (Mists.UseStatic), refused → RC 1 and a chat line [the official RCs are not
    /// traced, SPEC Q6].
    /// </summary>
    [Handles(OperationCodes.MistsUseStaticEntrance, Status = SessionStatus.InWorld)]
    private AlbionResponse? MistsUseStaticEntrance(MistsUseStaticEntranceRequest req)
    {
        if (Player is not { } player) return null;
        var (stamp, refused) = world.Mists.UseStatic(player, req.vx, req.vy);
        if (refused is null) return new MistsUseStaticEntranceResponse { bnl = stamp };
        Log.Info(Name, $"'{player.Character.Name}' static entrance {req.vx} (mode {req.vy}) refused: {refused}");
        if (!refused.StartsWith("blocked", StringComparison.Ordinal)) Expeditions.Tell(player, world.MistsSettings.Text("Refused", ("why", refused)));
        return new GenericResponse { ReturnCode = 1 };
    }

    /// <summary>MistsUseCityRoadsEntrance (op 478, xu) {0 GameTimeStamp, 1 the entrance (528)}: refused with the chat line Mists.RoadsClosed (the roads spec's party binding).</summary>
    [Handles(OperationCodes.MistsUseCityRoadsEntrance, Status = SessionStatus.InWorld)]
    private AlbionResponse? MistsUseCityRoadsEntrance(MistsUseCityRoadsEntranceRequest req)
    {
        if (Player is not { } player) return null;
        Log.Info(Name, $"'{player.Character.Name}' Brecilien roads entrance {req.vv}: refused (not served)");
        Expeditions.Tell(player, world.MistsSettings.Text("RoadsClosed"));
        return new GenericResponse { ReturnCode = 1 };
    }

}
