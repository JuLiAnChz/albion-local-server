using AlbionLocal.Photon;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Network;

/// <summary>
/// Duels (stageE pvp SPEC §2.2, §2.3; the rules in <see cref="Duels"/>): challenge, accept and deny. Each carries a request
/// id and gets a response: op 110 the echo {0 target, 1 wager} with the return code, ops 111 and 112 the return code alone.
/// </summary>
public sealed partial class WorldSession
{
    /// <summary>DuellingChallengePlayer (op 110, akc: 0 target object id, 1 wager a7m): rc 0 or 4400-4410, with {0 target, 1 wager} (aet).</summary>
    [Handles(OperationCodes.DuellingChallengePlayer, Status = SessionStatus.InWorld)]
    private AlbionResponse? DuellingChallengePlayer(DuellingChallengePlayerRequest req)
    {
        if (Player is not { } player) return null;
        var rc = world.Duels.Challenge(player, req.bqx, req.bqy);
        if (rc != 0) Log.Info(Name, $"'{player.Character.Name}' DuellingChallengePlayer on object {req.bqx} refused: {rc}");
        return new DuellingChallengePlayerResponse { aq3 = req.bqx, aq4 = req.bqy, ReturnCode = rc };
    }

    /// <summary>DuellingAcceptChallenge (op 111, no keys): rc 0 (the duel starts) or 4441-4446.</summary>
    [Handles(OperationCodes.DuellingAcceptChallenge, Status = SessionStatus.InWorld)]
    private AlbionResponse? DuellingAcceptChallenge(DuellingAcceptChallengeRequest req)
    {
        if (Player is not { } player) return null;
        var rc = world.Duels.Accept(player);
        if (rc != 0) Log.Info(Name, $"'{player.Character.Name}' DuellingAcceptChallenge refused: {rc}");
        return new GenericResponse { ReturnCode = rc };
    }

    /// <summary>DuellingDenyChallenge (op 112, no keys): rc 0 (DuelDenied to the challenger) or 4445 with no challenge open.</summary>
    [Handles(OperationCodes.DuellingDenyChallenge, Status = SessionStatus.InWorld)]
    private AlbionResponse? DuellingDenyChallenge(DuellingDenyChallengeRequest req) =>
        Player is { } player ? new GenericResponse { ReturnCode = world.Duels.Deny(player) } : null;
}
