using AlbionLocal.Photon;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Network;

/// <summary>StageN qol (analysis/work/stageN/qol/SPEC.md §1): player emotes, the rules in <see cref="Emotes"/>.</summary>
public sealed partial class WorldSession
{
    /// <summary>
    /// PlayEmote (op 195, y9: 0 int emote index into emotes.xml, 1 long target object id or 0). Played: PlayEmote 77 to the player and its
    /// observers and an empty RC-0 answer (cyz.ai7 reads only RC 7807); an unknown or locked emote: RC 7807, which the client shows as
    /// "@PLAY_EMOTE_ERROR_NOT_UNLOCKED"; otherwise (knocked down, on a mount bar, within the 1 s cooldown) a bare RC 0 and nothing shown.
    /// </summary>
    [Handles(OperationCodes.PlayEmote, Status = SessionStatus.InWorld)]
    private AlbionResponse? PlayEmote(PlayEmoteRequest req)
    {
        if (Player is not { Map: not null } player) return null;
        var refused = Emotes.Play(world, player, req.yb, req.yc);
        if (refused is { } rc) Log.Info(Name, $"'{player.Character.Name}' emote {req.yb} refused (rc {rc})");
        return new PlayEmoteResponse { ReturnCode = refused ?? 0 };
    }

    /// <summary>StopEmote (op 196, abj, no fields; no response class): the player's looping emote ends; StopEmote 78 to its observers.</summary>
    [Handles(OperationCodes.StopEmote, Status = SessionStatus.InWorld)]
    private AlbionResponse? StopEmote(StopEmoteRequest req)
    {
        if (Player is { } player) Emotes.Stop(player, "stopped by the client");
        return null;
    }
}
