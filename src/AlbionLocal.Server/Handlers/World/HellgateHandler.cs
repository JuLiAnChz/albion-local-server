using AlbionLocal.Photon;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Network;

/// <summary>
/// The hellgate operations (analysis/work/stageL/hellgates/SPEC.md, "SPEC" here, §3): UseHellgateShrine and the way out of an instance. The
/// state and the rules are in <see cref="Systems.Hellgates"/>.
/// </summary>
public sealed partial class WorldSession
{
    /// <summary>
    /// UseHellgateShrine (op 255, acg {0 time, 1 shrine, 2 cancel, 3 mode}; c32.i_48 0x180A65C60, sent by the shrine's Join, its auto-use and the
    /// channel state's reset): no response class of its own, so a GenericResponse: RC 0 (also for a cancel that changes nothing), or RC 1 with
    /// the reason in chat (cz4.u(g7q): the client's channel ends, no text of its own).
    /// </summary>
    [Handles(OperationCodes.UseHellgateShrine, Status = SessionStatus.InWorld)]
    private AlbionResponse? UseHellgateShrine(UseHellgateShrineRequest req)
    {
        if (Player is not { } player) return new GenericResponse { ReturnCode = 1 };
        if (world.Hellgates.Use(player, req.aic, req.aid, req.aie) is not { } refused) return new GenericResponse();
        Log.Info(Name, $"'{player.Character.Name}' hellgate shrine refused: {refused}");
        Systems.Expeditions.Tell(player, world.HellgateSettings.Text("hg.refused", ("why", refused)));
        return new GenericResponse { ReturnCode = 1 };
    }

    /// <summary>
    /// The escape inside a hellgate (ChangeCluster {0: 0, 3: true}, SPEC §3.7, §5.7 a): the client's own cast of slot 21 (ESCAPE_HELLGATE 900 s
    /// during the run, ESCAPE_DUNGEON 7 s from state 3 and after the exit portal's confirm), in every state: out to the member's way back,
    /// answered {0: the return cluster}; refused knocked down. Phase B (SPEC §7): the walk-in of the chain exit {0: its id} (in its square, at least
    /// Dungeons:MinSquare, ± Transfer:PositionTolerance) into the chain's next hellgate, answered {0: its id, 1: its map}. False outside a hellgate,
    /// and for any other object (a made-up id gets the exit checks' 1642).
    /// </summary>
    private bool HellgateEscape(Player player, ChangeClusterRequest req, out ChangeClusterResponse? response)
    {
        response = null;
        if (player.Map is not { } map || world.Hellgates.Find(map.Cluster) is not { } instance) return false;
        if (!req.eh && (instance.ChainExit is not { } chain || chain.Id != req.ee)) return false;
        if (player.Combat.Down is { } down)
        {
            response = Refuse(player, req.eh ? 0 : req.ee, ChangeClusterRefusal.NotNearAnExit, $"knocked down ({down.Kind})");
            return true;
        }
        if (!req.eh)
        {
            if (Reach(player, map, instance.ChainExit!.Position, System.Numerics.Vector2.Max(instance.ChainExit.Size, new System.Numerics.Vector2(world.DungeonSettings.MinSquare)),
                    "the hellgate chain exit") is { } far)
            {
                response = Refuse(player, req.ee, far.Code, far.Why);
                return true;
            }
            if (world.Hellgates.ChainInto(player, instance, out var into) is { } why)
            {
                Systems.Expeditions.Tell(player, world.HellgateSettings.Text("hg.refused", ("why", why)));
                response = Refuse(player, req.ee, ChangeClusterRefusal.ExitNotConnected, why);
                return true;
            }
            var next = into!.Value;
            Commit(player, next, $"hellgate:{instance.Id}:chain");
            response = new ChangeClusterResponse { anu = next.Cluster, anv = world.Hellgates.WorldOf(next.Cluster) }; // [0] the next id, [1] its map (the push's keys)
            return true;
        }
        var to = world.Hellgates.Leave(player, instance, "escape", answer: true);
        Commit(player, to, $"hellgate:{instance.Id}:escape");
        response = new ChangeClusterResponse { anu = to.Cluster };
        return true;
    }
}
