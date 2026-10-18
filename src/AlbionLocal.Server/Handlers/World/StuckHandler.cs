using AlbionLocal.Photon;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Network;

/// <summary>StageN qol (analysis/work/stageN/qol/SPEC.md §3): /stuck, the rules in <see cref="Systems.Stuck"/>.</summary>
public sealed partial class WorldSession
{
    /// <summary>Stuck (op 169, abk, no fields, no response class): the wait starts, or StuckCancel 466 and a chat line say why not. No answer either way.</summary>
    [Handles(OperationCodes.Stuck, Status = SessionStatus.InWorld)]
    private AlbionResponse? Stuck(StuckRequest req)
    {
        if (Player is not { } player) return null;
        var why = Systems.Stuck.Start(world, player);
        Log.Info(Name, why is null ? $"'{player.Character.Name}' /stuck: moved in {world.GameData?.StuckDelaySeconds ?? 180:0} s" : $"'{player.Character.Name}' /stuck refused: {why}");
        return null;
    }
}
