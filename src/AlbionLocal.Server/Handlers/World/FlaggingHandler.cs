using AlbionLocal.Photon;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Network;

/// <summary>The flag change (stageE pvp SPEC §3.1; the rules in <see cref="Flagging"/>).</summary>
public sealed partial class WorldSession
{
    /// <summary>
    /// ChangeFlaggingPrepare (op 354, qd: 0 byte FlaggingStatus): rc 0 with no keys and the flag cast starts, or
    /// <see cref="Flagging.Refused"/> with CastCancel and a SystemMessage reason to the player, which ends the cast its client
    /// started on its own (the client ignores the response, cwg.ai7 0x1809E1180).
    /// </summary>
    [Handles(OperationCodes.ChangeFlaggingPrepare, Status = SessionStatus.InWorld)]
    private AlbionResponse? ChangeFlaggingPrepare(ChangeFlaggingPrepareRequest req) =>
        Player is { } player ? new GenericResponse { ReturnCode = Flagging.Prepare(player, req.eo) } : null;
}
