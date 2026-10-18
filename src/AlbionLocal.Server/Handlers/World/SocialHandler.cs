using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Network;

/// <summary>
/// The friends window and the mute list (stageJ chat SPEC §1.5, §1.6, §2.6; <see cref="Systems.Social"/>): game-server operations, all
/// four friend ones with a request id, the two mute ones without a callback (their answer is MutePlayerUpdate 403).
/// </summary>
public sealed partial class WorldSession
{
    /// <summary>FriendInvite (266, s6 {0 name}) → aez: RC 0 / 10649 with the target's entry, else an RcOpFriendInvite code.</summary>
    [Handles(OperationCodes.FriendInvite, Status = SessionStatus.InWorld)]
    private AlbionResponse? FriendInvite(FriendInviteRequest req) => Player is { } player ? world.Social.Invite(player, req.l0) : null;

    /// <summary>FriendAnswerInvitation (267, s4 {0 requester, 1 accept}) → aey: key 10 whether they are friends now.</summary>
    [Handles(OperationCodes.FriendAnswerInvitation, Status = SessionStatus.InWorld)]
    private AlbionResponse? FriendAnswer(FriendAnswerInvitationRequest req) => Player is { } player ? world.Social.Answer(player, req.lx, req.ly) : null;

    /// <summary>FriendCancelnvitation (268, s5 {0 target}): the empty RC 0 (the client's dispatcher also reads it as aey, which an empty one leaves alone).</summary>
    [Handles(OperationCodes.FriendCancelnvitation, Status = SessionStatus.InWorld)]
    private AlbionResponse? FriendCancel(FriendCancelnvitationRequest req)
    {
        if (Player is not { } player) return null;
        world.Social.Cancel(player, req.lz);
        return new GenericResponse();
    }

    /// <summary>FriendRemove (269, s7 {0 friend}): RC 0, the client drops the entry.</summary>
    [Handles(OperationCodes.FriendRemove, Status = SessionStatus.InWorld)]
    private AlbionResponse? FriendRemove(FriendRemoveRequest req)
    {
        if (Player is not { } player) return null;
        world.Social.Remove(player, req.l1);
        return new GenericResponse();
    }

    /// <summary>AddPlayerToMuteList (380, om {0 name}; /mute, the friends window): no response; MutePlayerUpdate 403 tells the client.</summary>
    [Handles(OperationCodes.AddPlayerToMuteList, Status = SessionStatus.InWorld)]
    private AlbionResponse? MutePlayer(AddPlayerToMuteListRequest req)
    {
        if (Player is { } player) SendEvent(world.Social.Mute(player, req.aj));
        return null;
    }

    /// <summary>RemovePlayerFromMuteList (381, zw {0 name}; /unmute): no response; 403 mode 2, or mode 3 reason 3 when not muted.</summary>
    [Handles(OperationCodes.RemovePlayerFromMuteList, Status = SessionStatus.InWorld)]
    private AlbionResponse? UnmutePlayer(RemovePlayerFromMuteListRequest req)
    {
        if (Player is { } player) SendEvent(world.Social.Unmute(player, req.zj));
        return null;
    }
}
