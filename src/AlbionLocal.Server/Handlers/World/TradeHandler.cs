using AlbionLocal.Photon;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Network;

/// <summary>
/// The player trade window (stageJ trade SPEC §2.2; the rules in <see cref="Trades"/>): invite, cancel, accept the invitation, add and
/// remove an item, set the silver, accept. Every one gets a response: the client clears its pending invite only on the answer to op 161.
/// </summary>
public sealed partial class WorldSession
{
    /// <summary>
    /// InviteToPlayerTrade (op 161, wr: 0 target object id): rc 0 with the target {0 id, 1 name, 2 "" (guild), 3 avatar 0, 4 flag,
    /// 5 ring 0, 6 trade id} (ahf; the client opens its window at once), or 6441-6448 alone (the client reads key 6 only).
    /// </summary>
    [Handles(OperationCodes.InviteToPlayerTrade, Status = SessionStatus.InWorld)]
    private AlbionResponse? InviteToPlayerTrade(InviteToPlayerTradeRequest req)
    {
        if (Player is not { } player) return null;
        var rc = world.Trades.Invite(player, req.sr, out var trade);
        if (trade is null)
        {
            Log.Info(Name, $"'{player.Character.Name}' InviteToPlayerTrade on object {req.sr} refused: {rc}");
            return new InviteToPlayerTradeResponse { ReturnCode = rc };
        }
        var b = trade.B.Player;
        return new InviteToPlayerTradeResponse { a70 = b.Id, a71 = b.Character.Name, a72 = "", a74 = 0, a76 = trade.Id };
    }

    /// <summary>PlayerTradeCancel (op 162, no keys): the window closed, the invitation declined; the sender's trade ends (177 to both). Always rc 0.</summary>
    [Handles(OperationCodes.PlayerTradeCancel, Status = SessionStatus.InWorld)]
    private AlbionResponse? PlayerTradeCancel(PlayerTradeCancelRequest req)
    {
        if (Player is not { } player) return null;
        world.Trades.Cancel(player, "cancelled by the player");
        return new GenericResponse();
    }

    /// <summary>PlayerTradeInvitationAccept (op 163, zd: 0 trade id): rc 0 (both windows open) or 1.</summary>
    [Handles(OperationCodes.PlayerTradeInvitationAccept, Status = SessionStatus.InWorld)]
    private AlbionResponse? PlayerTradeInvitationAccept(PlayerTradeInvitationAcceptRequest req) =>
        Player is { } player ? new GenericResponse { ReturnCode = world.Trades.AcceptInvitation(player, req.yj) } : null;

    /// <summary>PlayerTradeAddItem (op 164, zb: 0 item object id, 1 trade id, 2 trade slot): rc 0 or 1, key 0 the item (ah9; never read).</summary>
    [Handles(OperationCodes.PlayerTradeAddItem, Status = SessionStatus.InWorld)]
    private AlbionResponse? PlayerTradeAddItem(PlayerTradeAddItemRequest req)
    {
        if (Player is not { } player) return null;
        var rc = world.Trades.AddItem(player, req.yh, req.yg, req.yi);
        if (rc != 0) Log.Info(Name, $"'{player.Character.Name}' PlayerTradeAddItem of object {req.yg} into slot {req.yi} refused");
        return new PlayerTradeAddItemResponse { bgz = req.yg, ReturnCode = rc };
    }

    /// <summary>PlayerTradeRemoveItem (op 165, ze: 0 trade id, 1 trade slot): rc 0 or 1, key 0 the removed item (ah8; never read).</summary>
    [Handles(OperationCodes.PlayerTradeRemoveItem, Status = SessionStatus.InWorld)]
    private AlbionResponse? PlayerTradeRemoveItem(PlayerTradeRemoveItemRequest req)
    {
        if (Player is not { } player) return null;
        var rc = world.Trades.RemoveItem(player, req.yk, req.yl, out var item);
        return new PlayerTradeRemoveItemResponse { bgy = item, ReturnCode = rc };
    }

    /// <summary>PlayerTradeAcceptTrade (op 166, za: 0 trade id, 1 revision, 2 accept): rc 0 or 1 (the client reads neither; AcceptChange tells it).</summary>
    [Handles(OperationCodes.PlayerTradeAcceptTrade, Status = SessionStatus.InWorld)]
    private AlbionResponse? PlayerTradeAcceptTrade(PlayerTradeAcceptTradeRequest req) =>
        Player is { } player ? new GenericResponse { ReturnCode = world.Trades.Accept(player, req.yd, req.ye, req.yf) } : null;

    /// <summary>PlayerTradeSetSilverOrGold (op 167, zf: 0 trade id, 1 amount a7m, 2 gold): rc 0 or 1 (no client handler; a refusal resyncs).</summary>
    [Handles(OperationCodes.PlayerTradeSetSilverOrGold, Status = SessionStatus.InWorld)]
    private AlbionResponse? PlayerTradeSetSilverOrGold(PlayerTradeSetSilverOrGoldRequest req) =>
        Player is { } player ? new GenericResponse { ReturnCode = world.Trades.SetSilver(player, req.ym, req.yn, req.yo) } : null;
}
