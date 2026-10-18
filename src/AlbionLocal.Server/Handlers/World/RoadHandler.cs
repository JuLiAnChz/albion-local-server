using System.Globalization;
using System.Numerics;
using AlbionLocal.Photon;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Network;

/// <summary>
/// The Roads of Avalon portals (analysis/work/stageL/roads/SPEC.md, "SPEC" here, §2.2, §5.4): the client's click on a portal
/// (TunnelExitObjectView.EnterExit, its own checks first) is a plain ChangeCluster {0: portal id, 1: ""}, answered as any exit's with
/// {0: target cluster}, a world.xml road or zone (no layer bytes, no instance id); the arrival is the paired spot's portal + its tile's
/// spawnpos. A target not resolved yet is read in the background and answered with the kept request id, as a plain exit's.
/// </summary>
public sealed partial class WorldSession
{
    /// <summary>Whether the request is for a <see cref="TunnelExit"/> of the player's map, and its answer (null = deferred).</summary>
    private bool RoadChange(Player player, ChangeClusterRequest req, out ChangeClusterResponse? response)
    {
        response = null;
        if (player.Map is not { } map || !map.Objects.TryGetValue(req.ee, out var o) || o is not TunnelExit portal) return false;
        response = EnterPortal(player, portal, null, RequestId, RequestEncrypted);
        return true;
    }

    /// <summary>
    /// SPEC §5.4, in order: the portal still on the map (1642); not knocked down (1640); linked (1641, "leads nowhere now"); enterable, a charge
    /// left or the recently-used window open (1641, the next charge's time); within Roads:EnterRadius + Transfer:PositionTolerance of key 1
    /// (1640); the exit cooldown (1647); then, the target known, its PvP entry rules (1652, 1655). The pass takes its charge and the commit
    /// sends the player to the paired spot, facing away from its portal. <paramref name="resolved"/> (a deferred pass) counts only for the cluster
    /// it was read for: a link that closed and re-rolled elsewhere meanwhile (Unlink -> Roll -> Connect in one tick) takes the new target's own.
    /// </summary>
    private ChangeClusterResponse? EnterPortal(Player player, TunnelExit portal, (string Cluster, ClusterTemplate Template)? resolved, int? requestId, bool encrypted)
    {
        var map = player.Map!;
        if (!map.Objects.TryGetValue(portal.Id, out var live) || !ReferenceEquals(live, portal))
            return Refuse(player, portal.Id, ChangeClusterRefusal.ExitInfoNotFound, $"tunnel exit {portal.Slot} is gone");
        if (player.Combat.Down is { } down) return Refuse(player, portal.Id, ChangeClusterRefusal.NotNearAnExit, $"knocked down ({down.Kind})");
        var roads = world.Roads;
        var now = world.GameNow;
        if (portal.Slot.Link is not { } link)
        {
            Expeditions.Tell(player, "This portal leads nowhere now.");
            return Refuse(player, portal.Id, ChangeClusterRefusal.ExitNotConnected, $"tunnel exit {portal.Slot} has no link");
        }
        if (!AvalonRoads.Enterable(link, now))
        {
            Expeditions.Tell(player, $"This portal has no charges left; next in {AvalonRoads.In(link.NextCharge, now)}.");
            return Refuse(player, portal.Id, ChangeClusterRefusal.ExitNotConnected, $"{link}: 0 charges, not recently used");
        }
        var at = player.PositionAt(DateTime.UtcNow);
        var reach = roads.EnterRadius + world.Config.Transfer.PositionTolerance;
        if (Vector2.Distance(at, portal.Position) > reach)
            return Refuse(player, portal.Id, ChangeClusterRefusal.NotNearAnExit, string.Create(CultureInfo.InvariantCulture,
                $"at ({at.X:0.##}, {at.Y:0.##}), {Vector2.Distance(at, portal.Position):0.#} m from tunnel exit {portal.Slot} (at most {reach:0.#})"));
        if (Cooldown(map) is { } early) return Refuse(player, portal.Id, early.Code, early.Why);
        var target = link.Other(portal.Slot);
        var template = resolved is { } r && r.Cluster == target.Cluster ? r.Template : world.CachedCluster(target.Cluster);
        if (template is null)
        {
            if (world.ResolveCluster(target.Cluster) is not { } load)
                return Refuse(player, portal.Id, ChangeClusterRefusal.ExitNotConnected, "this server has no cluster data");
            var pending = new PendingChange(player, map, portal.Id, requestId, encrypted);
            _changing = pending;
            Log.Info(Name, $"'{player.Character.Name}' takes {link}: resolving {target.Cluster}, the answer follows");
            var cluster = target.Cluster;
            load.ContinueWith(t => world.Post(() => ResumePortal(pending, portal, cluster, t)), TaskScheduler.Default);
            return null;
        }
        if (Flagging.EntryRefusal(player, template.Pvp) is { } barred) // stageE: the target's PvP entry rules
            return Refuse(player, portal.Id, (ChangeClusterRefusal)barred.Code, barred.Why);
        if (roads.ArrivalAt(target, template) is not { } to)
            return Refuse(player, portal.Id, ChangeClusterRefusal.ExitNotConnected, $"{target} has no tunnelexit member in {target.Cluster}");
        roads.Pass(link, now);
        world.Bounties.PortalPassed(player, template.Name.Tier ?? 0); // stageL roads phase B: the portal bounties
        Commit(player, to, $"road:{link.Id}");
        return new ChangeClusterResponse { anu = target.Cluster }; // [0] the target, byte-identical to the new Join's key 8
    }

    /// <summary>The target of a deferred pass is in (World.Post): if the session still waits on the same map, the pass runs again (all its checks) and is answered with the kept request id.</summary>
    private void ResumePortal(PendingChange pending, TunnelExit portal, string cluster, Task<ClusterTemplate> load)
    {
        if (_changing != pending) return;
        _changing = null;
        if (_closed || Status != SessionStatus.InWorld || Player != pending.Player || pending.Player.Map != pending.Map)
        {
            Log.Info(Name, $"tunnel exit {portal.Slot}: the player has left {pending.Map.Cluster} meanwhile, no answer");
            return;
        }
        var resp = load.IsCompletedSuccessfully ? EnterPortal(pending.Player, portal, (cluster, load.Result), pending.RequestId, pending.Encrypted)
            : Refuse(pending.Player, pending.ExitId, ChangeClusterRefusal.ExitNotConnected, "target cluster not resolved");
        if (resp is not null) SendResponse(OperationCodes.ChangeCluster, resp, pending.RequestId, pending.Encrypted);
    }
}
