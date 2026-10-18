using AlbionLocal.Photon;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Network;

/// <summary>StageN qol (analysis/work/stageN/qol/SPEC.md §4): the minimap ping shared with the party (<see cref="Systems.Parties.Ping"/>).</summary>
public sealed partial class WorldSession
{
    /// <summary>SendMiniMapPing (op 168, aaw: 0 Vector2, 1 type; no response class): MiniMapPing 181 to the party on the map; no answer.</summary>
    [Handles(OperationCodes.SendMiniMapPing, Status = SessionStatus.InWorld, Quiet = true)]
    private AlbionResponse? SendMiniMapPing(SendMiniMapPingRequest req)
    {
        if (Player is { } player) world.Parties.Ping(player, req.aco, req.acp);
        return null;
    }
}
