using AlbionLocal.Photon;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Network;

/// <summary>The buff shrines' operation (analysis/work/stageN/shrines/SPEC.md §4); the rules are in <see cref="Systems.BuffShrines"/>.</summary>
public sealed partial class WorldSession
{
    /// <summary>
    /// UseShrine (op 254, acj {0 time, 1 shrine id, 2 the state the client shows}; c32.j 0x180A77060, sent by ShrineObjectView.Use with a response
    /// callback): rc 0 for a Ready shrine in reach (the client then casts the usespell in slot 25), <see cref="Systems.BuffShrines.NotUsable"/>
    /// for a Locked or Spent one (the client shows its state message), 1 otherwise.
    /// </summary>
    [Handles(OperationCodes.UseShrine, Status = SessionStatus.InWorld)]
    private AlbionResponse? UseShrine(UseShrineRequest req)
    {
        if (Player is not { } player) return new GenericResponse { ReturnCode = 1 };
        var (code, why) = world.Shrines.Use(player, req.aij);
        if (why is not null) Log.Info(Name, $"'{player.Character.Name}' UseShrine #{req.aij} (state {req.aik}) refused: {why}");
        return new GenericResponse { ReturnCode = code };
    }
}
