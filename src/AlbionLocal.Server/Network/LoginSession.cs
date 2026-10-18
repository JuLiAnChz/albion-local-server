using AlbionLocal.Server.Content;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Persistence;

namespace AlbionLocal.Server.Network;

/// <summary>
/// Login server (client peer c33). Sequence: Login → (CreateCharacter) → SelectCharacter, whose
/// response hands the client the game server address and the start cluster; the connection then stays up for the whole
/// game session and answers GetGameServerByCluster at every cluster change (SPEC stage 3 §4.3).
/// See analysis/NOTES-protocol.md for where each field was traced. One per login connection; the
/// character repository is shared with the game side (it hands out copies, so the list never sees a character
/// the game is changing); new characters start with the starter kit. Handlers: Handlers/Login (status
/// query and Login in LoginHandler, the character list operations in CharacterListHandler, the referral window's season
/// in ReferralHandler, GetGameServerByCluster in ClusterHandler).
/// </summary>
public sealed partial class LoginSession(IPeer peer, ServerConfig config, ICharacterRepository characters, StarterKit starter)
    : AlbionSession(peer, "login")
{
    /// <summary>
    /// The world of the game connections (<see cref="ServerHost"/>; its listeners share one lock, so the login handlers run
    /// under <see cref="World.Sync"/> too): the account state a Join needs (<see cref="World.Accounts"/>: the selected
    /// character, login time), the newest saved state of a character (a snapshot still queued after a cluster change), and
    /// the background resolve of the cluster the client is about to load (<see cref="World.PrefetchCluster"/>), so the Join
    /// can send the world objects in view before its items. Null = none (the self-test's login-only sessions): the
    /// repository is read directly and nothing is remembered or prefetched.
    /// </summary>
    public World? World { get; init; }

    /// <summary>The kit of a new character under Progression:NewCharacters = FairStart (content/starter-fair.json).</summary>
    public StarterKit FairStarter { get; init; } = StarterKit.FairDefault;

    /// <summary>The password checks per address (<see cref="LoginThrottle"/>); the host shares one between its connections.</summary>
    public LoginThrottle Throttle { get; init; } = new();

    /// <summary>
    /// The account this connection logged in to (Login key 6, the login name: one account per name since stage 7); the
    /// local account before a successful Login and after a refused one (attached to no account then).
    /// </summary>
    private Account _account = characters.Account;

    /// <summary>The connection is gone: it no longer counts as one of its account's (<see cref="AccountStates.Detach"/>).</summary>
    public override void OnDisconnected(string reason)
    {
        base.OnDisconnected(reason);
        World?.Accounts.Detach(_account.Id, this);
    }
}
