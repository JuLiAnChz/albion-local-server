using AlbionLocal.Photon;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Network;

/// <summary>
/// The chat server's operations (stageJ chat SPEC §1, §2.3): RegisterChatPeer, the channel joins and leaves, the lines and the
/// whispers. Only RegisterChatPeer, a refused line and a whisper get a response (the official chat flow has one operation response,
/// the register's; 189, 191 and 192 come without a request id).
/// </summary>
public sealed partial class ChatSession
{
    /// <summary>RegisterChatPeer refused (an unknown or old token): the client shows nothing and stays without channels [choice].</summary>
    public const short RegisterRefused = 1;

    /// <summary>
    /// RegisterChatPeer (188, zs {0 Guid token}; encrypted, with a request id; df9.ae sends it at the chat peer's EncryptionEstablished):
    /// the token of the account's last SelectCharacter (<see cref="AccountStates.Select"/>) names the account and character. RC 0
    /// (encrypted as the request; the client then clears its joined channels) and, after it, the channels
    /// (<see cref="Systems.ChatHub.Registered"/>); an unknown token RC 1 and nothing else.
    /// </summary>
    [Handles(OperationCodes.RegisterChatPeer)]
    private AlbionResponse Register(RegisterChatPeerRequest req)
    {
        if (world.Accounts.FindByChatToken(req.zc) is not { } found || world.LoadCharacter(found.Character) is not { } c)
        {
            Log.Warn(Name, $"peer {Peer.PeerId}: register refused, the chat token is unknown or void (a SelectCharacter or Login came since)");
            return new GenericResponse { ReturnCode = RegisterRefused };
        }
        if (Account != Guid.Empty && Account != found.Account) world.Accounts.Detach(Account, this);
        Account = found.Account;
        Bind(c);
        Level = world.Characters.FindAccount(found.Account)?.Level ?? AccountLevel.Player;
        world.Accounts.Attach(Account, this); // the account's; a re-Login keeps it (LoginHandler), the next Select may move it (ChatHub.Selected)
        Status = SessionStatus.Authed;
        Log.Info(Name, $"peer {Peer.PeerId} registers for '{c.Name}'");
        return new GenericResponse();
    }

    /// <summary>After the RegisterChatPeer response: the globals and the character's own channels (<see cref="Systems.ChatHub.Registered"/>).</summary>
    protected override void AfterResponse(OperationCodes code, AlbionResponse resp)
    {
        if (code == OperationCodes.RegisterChatPeer && resp.ReturnCode == 0) world.Chat.Registered(this);
    }

    /// <summary>JoinChatChannel (191, wu {0 id}; no request id, no response): the join sync of the client's chat manager (<see cref="Systems.ChatHub.Join"/>).</summary>
    [Handles(OperationCodes.JoinChatChannel, Status = SessionStatus.Authed)]
    private AlbionResponse? JoinChannel(JoinChatChannelRequest req)
    {
        world.Chat.Join(this, req.sx);
        return null;
    }

    /// <summary>LeaveChatChannel (192, w6 {0 id}; no request id, no response): off the channel, and always LeftChatChannel 207 (<see cref="Systems.ChatHub.Leave"/>).</summary>
    [Handles(OperationCodes.LeaveChatChannel, Status = SessionStatus.Authed)]
    private AlbionResponse? LeaveChannel(LeaveChatChannelRequest req)
    {
        world.Chat.Leave(this, req.tf);
        return null;
    }

    /// <summary>SendChatMessage (189, aar {0 channel id, 1 text}; no callback): a line without an item link (<see cref="Line"/>).</summary>
    [Handles(OperationCodes.SendChatMessage, Status = SessionStatus.Authed)]
    private AlbionResponse? ChatLine(SendChatMessageRequest req) => Line(req.abj, req.abk);

    /// <summary>SendModeratorMessage (190, aax: the keys of 189), the client's choice for moderators: handled as 189 (staff rights come from the account, not the operation).</summary>
    [Handles(OperationCodes.SendModeratorMessage, Status = SessionStatus.Authed)]
    private AlbionResponse? ModeratorLine(SendModeratorMessageRequest req) => Line(req.abj, req.abk);

    /// <summary>
    /// A line: one starting with '.' and a letter is a GM command, run as from Say on the character's game connection, whose
    /// SystemMessages carry the replies, and never relayed (with a chat server the input box may be on Local or Global, and a command
    /// line can hold a password, SPEC §2.3); without a game connection it is dropped. Any other line goes to <see cref="Systems.ChatHub.Line"/>.
    /// </summary>
    private AlbionResponse? Line(int channel, string? text)
    {
        if (WorldSession.IsCommand(text))
        {
            if (world.Parties.Online(CharacterId)?.Session is { } game) game.RunChatCommand(text!);
            else Log.Info(Name, $"{this}: a command line without a game connection, dropped");
            return null;
        }
        return world.Chat.Line(this, channel, text);
    }

    /// <summary>
    /// SendWhisperMessage (193, aa2 {0 target name, 1 text}; with a request id): RC 0 = the client writes the "to &lt;target&gt;" line
    /// itself, 7720 not online, 7722 too soon (<see cref="Systems.ChatHub.Whisper"/>).
    /// </summary>
    [Handles(OperationCodes.SendWhisperMessage, Status = SessionStatus.Authed)]
    private AlbionResponse Whisper(SendWhisperMessageRequest req) =>
        new SendWhisperMessageResponse { ReturnCode = world.Chat.Whisper(CharacterId, CharacterName, Level, req.afo, req.afp) };
}
