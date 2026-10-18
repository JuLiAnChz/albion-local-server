using AlbionLocal.Photon;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Network;

/// <summary>The death screen of a lethal cluster (stageE pvp SPEC §4.1, §4.3; the rules in <see cref="Death"/>).</summary>
public sealed partial class WorldSession
{
    /// <summary>
    /// Suicide (op 102, abm, no keys: the die button of a knocked-down player): the death where <see cref="Death.Suicide"/> allows it.
    /// No response either way [infer: none was captured; a refusal is only logged].
    /// </summary>
    [Handles(OperationCodes.Suicide, Status = SessionStatus.InWorld)]
    private AlbionResponse? Suicide(SuicideRequest req)
    {
        if (Player is { } player && Death.Suicide(player) is { } why) Log.Info(Name, $"'{player.Character.Name}' Suicide refused: {why}");
        return null;
    }

    /// <summary>
    /// ResurrectionReply (op 207, aam: 0 ts, 1 accept): the answer to a holy resurrection's offer (<see cref="Revive.Reply"/>). No response
    /// (the client's handler for it, cy9.ai7, does nothing).
    /// </summary>
    [Handles(OperationCodes.ResurrectionReply, Status = SessionStatus.InWorld)]
    private AlbionResponse? ResurrectionReply(ResurrectionReplyRequest req)
    {
        if (Player is { } player && Revive.Reply(player, req.aa7) is { } why) Log.Info(Name, $"'{player.Character.Name}' ResurrectionReply ignored: {why}");
        return null;
    }

    /// <summary>GetRespawnInfos (op 204, uw): the respawn screen's options, {0 bool[9]} (<see cref="Death.RespawnInfos"/>).</summary>
    [Handles(OperationCodes.GetRespawnInfos, Status = SessionStatus.InWorld)]
    private AlbionResponse GetRespawnInfos(GetRespawnInfosRequest req) => new GetRespawnInfosResponse { a4h = Death.RespawnInfos() };

    /// <summary>
    /// Respawn (op 101, aai: 0 the a0.b option): a dead player respawns (<see cref="Death.Respawn"/>: the cluster change to the respawn
    /// city, whatever the option). No response [infer]: the pushed ChangeCluster response moves the client.
    /// </summary>
    [Handles(OperationCodes.Respawn, Status = SessionStatus.InWorld)]
    private AlbionResponse? Respawn(RespawnRequest req)
    {
        if (Player is { } player && !Death.Respawn(player, $"option {req.aax}")) Log.Info(Name, $"'{player.Character.Name}' Respawn refused: not dead");
        return null;
    }
}
