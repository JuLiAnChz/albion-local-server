using System.Numerics;
using AlbionLocal.Photon;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Network;

/// <summary>
/// The corrupted dungeon operations (analysis/work/stageL/corrupted/SPEC.md, "SPEC" here, §2): the entrance window's GetInfamyInfo, the
/// ways out of an instance and (phase B) the Demonic Shrine. The enter cast is a CastStart on slot 25 (Handlers/World/CombatHandler); the
/// state and the rules are in <see cref="Systems.CorruptedDungeons"/>.
/// </summary>
public sealed partial class WorldSession
{
    /// <summary>
    /// GetInfamyInfo (op 419, t3; the entrance window asks it on opening, OnInfamyInfoResponse 0x1813946C0): the infamy per type and its peak,
    /// key 8 whether Slayer is open (<see cref="Systems.CorruptedDungeons.InfamyInfo"/>).
    /// </summary>
    [Handles(OperationCodes.GetInfamyInfo, Status = SessionStatus.InWorld)]
    private AlbionResponse? GetInfamyInfo(GetInfamyInfoRequest req) => Player is { } player ? world.Corrupted.InfamyInfo(player) : null;

    /// <summary>
    /// A ChangeCluster inside a corrupted dungeon (SPEC §4.2 step 8): the escape spell's {0: 0, 3: true} (the client's cast, as the expeditions')
    /// or the walk-in of the exit portal {0: its id} (in the square of its size, at least Dungeons:MinSquare, ± Transfer:PositionTolerance;
    /// the exit cooldown): out to the way back, answered {0: the return cluster}. False outside a run, and for any other object (a made-up id
    /// gets the exit checks' 1642).
    /// </summary>
    private bool CorruptedChange(Player player, ChangeClusterRequest req, out ChangeClusterResponse? response)
    {
        response = null;
        if (player.Map is not { } map || world.Corrupted.Find(map.Cluster) is not { } run) return false;
        if (!req.eh && (!map.Objects.TryGetValue(req.ee, out var o) || o is not CorruptedExitPortal portal || !ReferenceEquals(run.Portal, portal))) return false;
        if (player.Combat.Down is { } down)
        {
            response = Refuse(player, req.ee, ChangeClusterRefusal.NotNearAnExit, $"knocked down ({down.Kind})");
            return true;
        }
        if (!req.eh && Reach(player, map, run.Portal!.Position, Vector2.Max(run.Portal.Size, new Vector2(world.DungeonSettings.MinSquare)), "the corrupted exit portal") is { } refused)
        {
            response = Refuse(player, req.ee, refused.Code, refused.Why);
            return true;
        }
        var why = req.eh ? "escape" : "exit portal";
        var to = world.Corrupted.Leave(player, run, why, answer: true);
        var (map0, type) = world.Corrupted.KeysOf(to.Cluster); // phase B: a player invader goes back into its own run (keys 1 and 4)
        Commit(player, to, $"corrupted:{run.Id}:{why}");
        response = new ChangeClusterResponse { anu = to.Cluster, anv = map0, any = type };
        return true;
    }

    /// <summary>
    /// UseCorruptedShrine (op 416 {0 time, 1 shrine id, 2 false}; sent with a response callback, only when the shrine shows state 0): the owner's
    /// consent to invasions (<see cref="Systems.CorruptedDungeons.UseShrine"/>: 460 now, its effect after the use time). Always answered: rc 0,
    /// or 1 with the reason in chat.
    /// </summary>
    [Handles(OperationCodes.UseCorruptedShrine, Status = SessionStatus.InWorld)]
    private AlbionResponse? UseCorruptedShrine(UseCorruptedShrineRequest req)
    {
        if (Player is not { } player) return new GenericResponse { ReturnCode = 1 };
        if (world.Corrupted.UseShrine(player, req.ah7) is not { } refused) return new GenericResponse();
        Log.Info(Name, $"'{player.Character.Name}' Demonic Shrine use refused: {refused}");
        Systems.Expeditions.Tell(player, world.CorruptedSettings.Text("cd.refused", ("reason", refused)));
        return new GenericResponse { ReturnCode = 1 };
    }
}
