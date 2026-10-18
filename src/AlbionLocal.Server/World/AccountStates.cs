namespace AlbionLocal.Server;

/// <summary>
/// What the server keeps of a logged-in account between its connections, in memory only (SPEC stage 3 §4.4, §4.6): the
/// character SelectCharacter picked, which a Join plays, because the real client's Join key 0 is the account Guid it got in
/// Login key 51 (ci7.a9 copies it to ci7+0x120, ci7.xl 0x418 sends it), never the character's, and the game connection is
/// new after every cluster change; when the account logged in (Join key 102) and how many Joins that login has had (key
/// 103: 1, 2, 3, …). Login and SelectCharacter set it on the login connection, Join reads it on the game connection; in
/// the server both run under the one lock of <see cref="ServerHost"/>, and its own lock keeps the self-test's separately
/// driven sessions safe as well. It also knows the open connections of each account (<see cref="Attach"/>: the login
/// connection after a successful Login, the game connection after its Join), so that a second Login of the account closes
/// the first one's (<see cref="Sessions"/>) and the console can tell whether an account is online. Since stageJ also the chat
/// token of each SelectCharacter (<see cref="ChatToken"/>), which the client's chat peer presents in RegisterChatPeer.
/// </summary>
public sealed class AccountStates
{
    private sealed class State
    {
        public Guid? Selected;
        public long LoginAt;
        public int Joins;
        public Guid? ChatToken;
        public readonly List<Network.AlbionSession> Sessions = [];
    }

    private readonly object _sync = new();
    private readonly Dictionary<Guid, State> _accounts = new();

    private State Of(Guid account)
    {
        if (!_accounts.TryGetValue(account, out var s)) _accounts[account] = s = new State();
        return s;
    }

    /// <summary>Login: the login session starts (<paramref name="utcTicks"/>, Join key 102) and its Join count starts again.</summary>
    public void Login(Guid account, long utcTicks)
    {
        lock (_sync)
        {
            var s = Of(account);
            s.LoginAt = utcTicks;
            s.Joins = 0;
            // The chat token stays (until the next Select): a second Login of the same client (its login peer reconnected, LoginResponse
            // key 85) keeps its chat peer, whose own reconnects (ci7.x) present the token it stored at the Select (stageJ chat SPEC §1.1).
        }
    }

    /// <summary>
    /// SelectCharacter: the character the account's next Joins play, and a new chat token for it (SelectCharacter key 8, stageJ chat SPEC
    /// §2.2): valid until the next Select of the account, so the client's chat reconnects (ci7.x, with the stored token) keep working
    /// over every cluster change and a re-Login. While a chat connection of the account is open the token stays the same: the client's
    /// c31.i stores key 8 only when its Connect succeeds, which a chat peer still connected makes fail, so it keeps the old token
    /// (the open connection moves to the new character, <see cref="Systems.ChatHub.Selected"/>).
    /// </summary>
    public Guid Select(Guid account, Guid character)
    {
        lock (_sync)
        {
            var s = Of(account);
            s.Selected = character;
            if (s.ChatToken is { } kept && s.Sessions.Any(x => x is Network.ChatSession)) return kept;
            return (s.ChatToken = Guid.NewGuid()).Value;
        }
    }

    /// <summary>The chat token of the account's last SelectCharacter; null = none (no Select since its Login).</summary>
    public Guid? ChatToken(Guid account)
    {
        lock (_sync) return _accounts.GetValueOrDefault(account)?.ChatToken;
    }

    /// <summary>The account and character a RegisterChatPeer token belongs to (<see cref="Select"/>); null = unknown or void.</summary>
    public (Guid Account, Guid Character)? FindByChatToken(Guid token)
    {
        if (token == Guid.Empty) return null;
        lock (_sync)
            foreach (var (account, s) in _accounts)
                if (s.ChatToken == token && s.Selected is { } character) return (account, character);
        return null;
    }

    /// <summary>The character SelectCharacter last picked for <paramref name="account"/>; null = none since the server started.</summary>
    public Guid? Selected(Guid account)
    {
        lock (_sync) return _accounts.GetValueOrDefault(account)?.Selected;
    }

    /// <summary>A Join of <paramref name="account"/>: the login start (0 = no Login seen) and this Join's number in the login.</summary>
    public (long LoginAt, int Join) Joined(Guid account)
    {
        lock (_sync)
        {
            var s = Of(account);
            return (s.LoginAt, ++s.Joins);
        }
    }

    /// <summary>A connection of <paramref name="account"/> is open (a successful Login, a Join, a chat register); <see cref="Detach"/> when it closes.</summary>
    public void Attach(Guid account, Network.AlbionSession session)
    {
        lock (_sync)
        {
            var list = Of(account).Sessions;
            if (!list.Contains(session)) list.Add(session);
        }
    }

    /// <summary>The connection is closed (or logged in to another account).</summary>
    public void Detach(Guid account, Network.AlbionSession session)
    {
        lock (_sync) _accounts.GetValueOrDefault(account)?.Sessions.Remove(session);
    }

    /// <summary>The open connections of <paramref name="account"/>, login, game and chat (a copy).</summary>
    public IReadOnlyList<Network.AlbionSession> Sessions(Guid account)
    {
        lock (_sync) return _accounts.GetValueOrDefault(account)?.Sessions.ToList() ?? [];
    }
}
