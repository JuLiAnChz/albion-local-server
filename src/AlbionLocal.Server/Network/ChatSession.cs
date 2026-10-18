using AlbionLocal.Server.Game;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Network;

/// <summary>
/// The chat server (client peer c31, app "Albion.ChatServer"; analysis/work/stageJ/chat/SPEC.md): one session per chat connection,
/// which the client opens right after SelectCharacter when its key 7 names this server (Chat:Enabled) and keeps for the whole login.
/// RegisterChatPeer (188) with the token of SelectCharacter key 8 binds it to the account's selected character (Authed); then come the
/// channels the hub offers it (NewChatChannels 205), the joins and leaves the client asks for (191/192, answered by 206/207), its lines
/// (189/190 → ChatMessage 73 to the channel's members) and its whispers (193 → ChatWhisper 75). What a channel holds and how a line
/// travels is <see cref="ChatHub"/>'s; the session keeps what this connection was offered and has joined. Handlers:
/// Handlers/Chat/ChatServerHandler.cs. The client sends no keepalive op here, only Photon pings (SPEC §1.1), so the 30 s ENet
/// timeout ends a chat connection only when its client is gone.
/// </summary>
public sealed partial class ChatSession(IPeer peer, World world) : AlbionSession(peer, "chat")
{
    /// <summary>The account of the token (Guid.Empty before RegisterChatPeer).</summary>
    public Guid Account { get; private set; }

    /// <summary>The character the account selected for this login; Guid.Empty before RegisterChatPeer.</summary>
    public Guid CharacterId { get; private set; }

    /// <summary>The character's name: the sender of its lines, the name whispers find it by.</summary>
    public string CharacterName { get; private set; } = "";

    /// <summary>The account's level at the register (GameMaster and Admin may post on Announcement and Staff, and carry the [GM] role).</summary>
    public AccountLevel Level { get; private set; }

    /// <summary>A GameMaster or Admin account.</summary>
    public bool Staff => Level >= AccountLevel.GameMaster;

    /// <summary>The channels offered to this connection (205), by id; one per type (the client keeps one, SPEC §1.2).</summary>
    internal Dictionary<int, ChatChannel> Offered { get; } = [];

    /// <summary>The channels this connection has joined (206), by id.</summary>
    internal Dictionary<int, ChatChannel> Joined { get; } = [];

    /// <summary>
    /// Whether its character plays (from the register or its Join until its logout, <see cref="ChatHub.LoggedOut"/>): only then do
    /// whispers reach it.
    /// </summary>
    internal bool Active { get; set; }

    /// <summary>The character this connection speaks for (its register; <see cref="ChatHub.Selected"/> when the account selects another).</summary>
    internal void Bind(Character c)
    {
        CharacterId = c.Id;
        CharacterName = c.Name;
    }

    /// <summary>An event to this connection's client (unencrypted, as the game server's).</summary>
    public void Send(AlbionEvent ev) => SendEvent(ev);

    /// <summary>The connection is gone: off every channel and out of the hub, no longer one of its account's.</summary>
    public override void OnDisconnected(string reason)
    {
        base.OnDisconnected(reason);
        world.Chat.Unregister(this);
        if (Account != Guid.Empty) world.Accounts.Detach(Account, this);
    }

    public override string ToString() => CharacterName.Length > 0 ? $"'{CharacterName}' (chat peer {Peer.PeerId})" : $"chat peer {Peer.PeerId}";
}
