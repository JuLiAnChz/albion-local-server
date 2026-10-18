using AlbionLocal.Photon.Serialization;
using AlbionLocal.Server;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;
using static AlbionLocal.SelfTest.AlbionTests;
using static AlbionLocal.SelfTest.ChatTest;
using static AlbionLocal.SelfTest.CombatTest;

namespace AlbionLocal.SelfTest;

/// <summary>
/// The friends list and the mute list (analysis/work/stageJ/chat/SPEC.md §4 items 10-13): requests and answers between two or three
/// players through <see cref="FakePeer"/>s, the FriendInvite codes, the lists after a login (and none after an arrival), presence, a
/// restart, and the mute list with its filter on chat lines and whispers.
/// </summary>
public static class SocialTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("social: invite (RC 0 + 293), accept (296 key 10 true), after a restart both 295 lists with online flags, 297 at the other's login and logout", Friends),
        ("social: FriendInvite codes 10641 self, 10640 unknown, 10642 twice, 10649 crossed, 10648 friends, 10644 limit; cancel 298, remove 299", InviteCodes),
        ("social: a login's first Join sends 403, 294 (true), 294, 295 after JoinFinished, every array present; a cluster change's arrival none", LoginLists),
        ("social: a cluster change that never arrives: 297 {false} after Parties.OfflineGrace, whisper 7720; a late arrival 297 {true}", LostTransfer),
        ("social: mute 403 mode 1, again 3/5, self 3/4, unknown 3/2; muted lines and whispers not delivered; saved over a restart; unmute mode 2", Mutes),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static OperationResponse? Send(Client c, AlbionRequest req, int? id = 5)
    {
        var n = c.GamePeer.Responses.Count;
        c.Game.OnRequest(Request(req, id), encrypted: false);
        return c.GamePeer.Responses.Count > n ? c.GamePeer.Responses[^1] : null;
    }

    private static Guid G(object? v) => new((byte[])v!);

    private static Guid[] Gs(object? v) => [.. ((byte[])v!).Chunk(16).Select(b => new Guid(b))];

    private static void Friends()
    {
        using var town = new ChatTown();
        var a = town.Login("Anna");
        var b = town.Login("Boris");
        var mb = b.GamePeer.Events.Count;
        var r = Send(a, new FriendInviteRequest { l0 = "boris" });
        Check(r is { ReturnCode: 0 } && G(r.Parameters[0]) == b.Id && (string)r.Parameters[1]! == "Boris" && r.Parameters[ReservedKeys.RequestId] is 5
              && Of(b.GamePeer, EventCodes.FriendRequest, mb) is [var req] && G(req[0]) == a.Id && (string)req[1]! == "Anna",
              "A invites B: RC 0 with B's entry, 293 {A} to B");
        var ma = a.GamePeer.Events.Count;
        var ans = Send(b, new FriendAnswerInvitationRequest { lx = a.Id, ly = true });
        Check(ans is { ReturnCode: 0 } && ans.Parameters[10] is true && G(ans.Parameters[0]) == a.Id
              && Of(a.GamePeer, EventCodes.FriendRequestAnswered, ma) is [var done] && G(done[0]) == b.Id && done[10] is true && done[2] is true,
              "B accepts: key 10 true with A's entry; A gets 296 {B, online, 10 true}");

        b.GamePeer.Disconnect();
        a.GamePeer.Disconnect();
        town.Restart();
        a = town.Login(a.C);
        var list = Of(a.GamePeer, EventCodes.FriendInfos, 0);
        Check(list is [var fa] && Gs(fa[0]) is [var f] && f == b.Id && (bool[])fa[2]! is [false] && (long[])fa[9]! is [> 0],
              "after a restart A's 295 holds B, offline, with its last seen");
        ma = a.GamePeer.Events.Count;
        b = town.Login(b.C);
        Check(Of(b.GamePeer, EventCodes.FriendInfos, 0) is [var fb] && Gs(fb[0]) is [var g] && g == a.Id && (bool[])fb[2]! is [true]
              && Of(a.GamePeer, EventCodes.FriendOnlineStatus, ma) is [var on] && on[0] is true && G(on[1]) == b.Id,
              "B's 295 holds A online; A gets 297 {true, B} at B's login");
        ma = a.GamePeer.Events.Count;
        b.GamePeer.Disconnect();
        Check(Of(a.GamePeer, EventCodes.FriendOnlineStatus, ma) is [var off] && off[0] is false && G(off[1]) == b.Id, "B's logout: 297 {false, B}");
    }

    private static void InviteCodes()
    {
        using var town = new ChatTown(new ServerConfig { JoinByCharacterId = true, SocialFriendLimit = 1 });
        var a = town.Login("Anna");
        var b = town.Login("Boris");
        var c = town.Login("Cora");
        short Rc(Client from, string name) => Send(from, new FriendInviteRequest { l0 = name })!.ReturnCode;
        Check(Rc(a, "Anna") == Social.CantInviteYourself && Rc(a, "Nobody") == Social.PlayerNotFound, "oneself 10641, an unknown name 10640");
        Check(Rc(a, "Boris") == 0 && Rc(a, "Boris") == Social.AlreadyInvited, "twice 10642");
        var ma = a.GamePeer.Events.Count;
        Check(Rc(b, "Anna") == Social.AddedAsFriend && Of(a.GamePeer, EventCodes.FriendRequestAnswered, ma) is [var x] && x[10] is true
              && town.World.Social.FriendsOf(a.Id).Contains(b.Id), "crossed requests: 10649, friends at once, 296 {10 true} to A");
        Check(Rc(a, "Boris") == Social.AlreadyFriends && Rc(a, "Cora") == Social.FriendLimitReached, "friends already 10648; Social:FriendLimit 1 reached 10644");
        var mb = b.GamePeer.Events.Count;
        Check(Send(a, new FriendRemoveRequest { l1 = b.Id }) is { ReturnCode: 0 } && Of(b.GamePeer, EventCodes.FriendRemoved, mb) is [var rm] && G(rm[0]) == a.Id
              && town.World.Social.FriendsOf(b.Id).Count == 0, "remove: RC 0, 299 {A} to B, gone on both sides");
        var mc = c.GamePeer.Events.Count;
        Check(Rc(a, "Cora") == 0 && Send(a, new FriendCancelnvitationRequest { lz = c.Id }) is { ReturnCode: 0 } cancel && !cancel.Parameters.ContainsKey(0)
              && Of(c.GamePeer, EventCodes.FriendRequestCanceled, mc) is [var cn] && G(cn[0]) == a.Id, "cancel: the empty RC 0, 298 {A} to C");
        Check(Rc(c, "Anna") == 0 && Rc(a, "Boris") == 0 && town.World.Social.Answer(b.P, a.Id, true).arm
              && Send(a, new FriendAnswerInvitationRequest { lx = c.Id, ly = true }) is { ReturnCode: Social.Refused }
              && !town.World.Social.FriendsOf(a.Id).Contains(c.Id) && town.World.Social.RequestInfos(a.Id, incoming: true).cch is [var kept] && kept == c.Id,
              "accepting C with a full list (limit 1): RC 1, not friends, C's request kept");
    }

    private static void LoginLists()
    {
        _ = AlbionTests.Client.Clusters!.Get("0000");
        _ = AlbionTests.Client.Clusters!.Get("0007");
        using var town = new ChatTown(clusters: true);
        var a = town.Login("Anna");
        var events = Since(a.GamePeer, 0);
        var finished = events.FindIndex(e => e.Code == EventCodes.JoinFinished);
        var lists = events.Skip(finished + 1).Where(e => e.Code is EventCodes.MutePlayerUpdate or EventCodes.FriendRequestInfos or EventCodes.FriendInfos).ToList();
        Check(finished >= 0 && lists.Select(e => e.Code).ToArray() is [EventCodes.MutePlayerUpdate, EventCodes.FriendRequestInfos, EventCodes.FriendRequestInfos, EventCodes.FriendInfos]
              && events.Take(finished).All(e => e.Code is not (EventCodes.MutePlayerUpdate or EventCodes.FriendRequestInfos or EventCodes.FriendInfos)),
              "after JoinFinished: 403, 294, 294, 295");
        var (mute, incoming, outgoing, friends) = (lists[0].P, lists[1].P, lists[2].P, lists[3].P);
        Check(Convert.ToInt32(mute[1]) == 0 && Convert.ToInt32(mute[2]) == 0 && !mute.ContainsKey(0) && mute[3] is string[] { Length: 0 } && mute[4] is string[] { Length: 0 }
              && mute[5] is not null, "403 {1: 0, 2: 0, 3/4/5 empty}, no key 0");
        Check(incoming[0] is true && !outgoing.ContainsKey(0) && Enumerable.Range(1, 7).All(k => incoming.ContainsKey((byte)k) && outgoing.ContainsKey((byte)k))
              && Enumerable.Range(0, 10).All(k => friends.ContainsKey((byte)k)),
              "294 key 0 true then left out (false), all seven arrays present; 295 all ten arrays present");
        var mark = a.GamePeer.Events.Count;
        town.Tele(a, "0007", arrive: false);
        mark = 0; // the arrival's connection is new
        town.Rejoin(a);
        Check(Since(a.GamePeer, mark).All(e => e.Code is not (EventCodes.MutePlayerUpdate or EventCodes.FriendRequestInfos or EventCodes.FriendInfos)),
              "the arrival's Join: none of them");
    }

    private static void LostTransfer()
    {
        _ = AlbionTests.Client.Clusters!.Get("0000");
        _ = AlbionTests.Client.Clusters!.Get("0007");
        using var town = new ChatTown(clusters: true);
        var a = town.Login("Anna");
        var b = town.Login("Boris");
        Check(town.World.Social.Invite(a.P, "Boris").ReturnCode == 0 && town.World.Social.Answer(b.P, a.Id, true).arm, "A and B are friends");
        town.Tele(b, "0007", arrive: false);
        b.GamePeer.Disconnect(); // the connection left behind; the client never joins on the new one
        var ma = a.GamePeer.Events.Count;
        town.World.Social.Update(town.Clock.Now);
        town.Clock.Advance(Parties.OfflineGrace - TimeSpan.FromSeconds(2));
        town.World.Social.Update(town.Clock.Now);
        Check(Of(a.GamePeer, EventCodes.FriendOnlineStatus, ma).Count == 0, "within the grace B stays online for A");
        town.Clock.Advance(TimeSpan.FromSeconds(3));
        town.World.Social.Update(town.Clock.Now);
        Check(Of(a.GamePeer, EventCodes.FriendOnlineStatus, ma) is [var off] && off[0] is false && G(off[1]) == b.Id
              && Whisper(a, "Boris", "there?").ReturnCode == ChatHub.PlayerNotOnline, "after the grace: 297 {false, B} to A, a whisper to B 7720");
        ma = a.GamePeer.Events.Count;
        town.Rejoin(b);
        Check(Of(a.GamePeer, EventCodes.FriendOnlineStatus, ma) is [var on] && on[0] is true && G(on[1]) == b.Id, "B arrives late after all: 297 {true, B}");
    }

    private static void Mutes()
    {
        using var town = new ChatTown();
        var a = town.Login("Anna");
        var b = town.Login("Boris");
        MutePlayerUpdateEvent Mute(AlbionRequest req)
        {
            var n = a.GamePeer.Events.Count;
            Check(Send(a, req, null) is null, "no response to a mute operation");
            var e = new MutePlayerUpdateEvent();
            AlbionSerializer.FillFields(e, Of(a.GamePeer, EventCodes.MutePlayerUpdate, n).Single());
            return e;
        }
        var added = Mute(new AddPlayerToMuteListRequest { aj = "boris" });
        Check(added is { cs5: 1, cs7: ["Boris"], cs8: [""], cs9: [> 0] }, "mute: 403 mode 1 [Boris]");
        Check(Mute(new AddPlayerToMuteListRequest { aj = "Boris" }) is { cs5: 3, cs6: 5 } && Mute(new AddPlayerToMuteListRequest { aj = "ANNA" }) is { cs5: 3, cs6: 4 }
              && Mute(new AddPlayerToMuteListRequest { aj = "Nobody" }) is { cs5: 3, cs6: 2 }, "again 3/5, oneself 3/4, unknown 3/2");
        var area = IdOf(a, ChatChannelType.Area);
        JoinChannels(a, ChatChannelType.Area);
        JoinChannels(b, ChatChannelType.Area);
        var (ma, mb) = (a.ChatPeer.Events.Count, b.ChatPeer.Events.Count);
        Line(b, area, "spam");
        Check(Lines(a, ma).Count == 0 && Lines(b, mb) is [(_, "Boris", "spam")] && Whisper(b, "Anna", "psst").ReturnCode == 0
              && Of(a.ChatPeer, EventCodes.ChatWhisper, ma).Count == 0, "B's Local line and whisper do not reach A (B's whisper still RC 0)");

        a.GamePeer.Disconnect();
        b.GamePeer.Disconnect();
        town.Restart();
        a = town.Login(a.C);
        b = town.Login(b.C);
        var list = new MutePlayerUpdateEvent();
        AlbionSerializer.FillFields(list, Of(a.GamePeer, EventCodes.MutePlayerUpdate, 0).Single());
        Check(list is { cs5: 0, cs7: ["Boris"] }, "after a restart the login's 403 lists Boris");
        Check(Mute(new RemovePlayerFromMuteListRequest { zj = "Boris" }) is { cs5: 2, cs7: ["Boris"] } && Mute(new RemovePlayerFromMuteListRequest { zj = "Boris" }) is { cs5: 3, cs6: 3 },
              "unmute: 403 mode 2 [Boris]; again 3/3");
        area = IdOf(a, ChatChannelType.Area);
        JoinChannels(a, ChatChannelType.Area);
        JoinChannels(b, ChatChannelType.Area);
        ma = a.ChatPeer.Events.Count;
        Line(b, area, "hello again");
        Check(Lines(a, ma) is [(_, "Boris", "hello again")], "B's lines reach A again");
    }
}
