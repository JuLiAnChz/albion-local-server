using System.Buffers.Binary;
using AlbionLocal.Photon;
using AlbionLocal.Photon.Serialization;
using AlbionLocal.Server;
using AlbionLocal.Server.Commands;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;
using static AlbionLocal.SelfTest.AlbionTests;
using static AlbionLocal.SelfTest.CombatTest;

namespace AlbionLocal.SelfTest;

/// <summary>
/// The chat server (analysis/work/stageJ/chat/SPEC.md §4): the SelectCharacter keys and RegisterChatPeer over real UDP, then two or
/// three players in one World through <see cref="FakePeer"/>s, each with a game and a chat connection: the Area channel per map and
/// its lines, a cluster change and a stale join, logout and a reconnect, the pve world channel and merged languages, the guild
/// channel and .g, whispers, '.'-commands on a channel and the Announcement channel.
/// </summary>
public static class ChatTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("chat: SelectCharacter key 7 = the bound chat port, key 8 a new token per select (Chat:Enabled false: \"\"); UDP RegisterChatPeer: an old token RC 1, the new one RC 0 and the 22 globals", RegisterUdp),
        ("chat: register RC 0 encrypted, Area 205 at the Join, 191 → 206 (unknown id: nothing), a Local line to the map's members only (keys 0-3), 7561 on a second Help line", AreaLines),
        ("chat: cluster change 207 + 208 of the old Area before the new Join's 205, a stale 191 → 206 + 207, 192 → 207, the old Area id again back home", ClusterChange),
        ("chat: a reconnect's old connection closing keeps the channels and sends no 297; logout 207 per joined id, 208 for Area, whisper → 7720", LogoutAndReconnect),
        ("chat: a re-Login keeps the token (a reconnecting chat peer RC 0); a Select while it is open: the same token, the connection moves to the new character", Relogin),
        ("chat: pve preset: GENERAL tab with Global 0, an English line reaches Russian (id 6); official: not", PveWorldChannel),
        ("chat: guild channel 205 on create and accept, .g on it instead of the system line, a kicked member 207 + 208 and no later line", GuildChannel),
        ("chat: whisper to any case of the name (75 to the target only, RC 0), 7722 within 100 ms, oneself 7720", Whispers),
        ("chat: a '.'-command on Local runs (SystemMessage) and is relayed to nobody, a player's line on Announcement relays nothing, a world announcement on 7", CommandsAndAnnouncements),
    ];

    /// <summary>A player with its game connection and its chat connection (registered before the Join, as the client does).</summary>
    internal sealed class Client(Character c, ChatSession chat, FakePeer chatPeer)
    {
        public Character C { get; } = c;
        public ChatSession Chat { get; } = chat;
        public FakePeer ChatPeer { get; } = chatPeer;
        public WorldSession Game { get; set; } = null!;
        public FakePeer GamePeer { get; set; } = null!;
        public AlbionLocal.Server.Entities.Player P => Game.Player!;
        public Guid Id => C.Id;
    }

    /// <summary>One World on a test database, its characters each in an account of its own (one chat token per account).</summary>
    internal sealed class ChatTown : IDisposable
    {
        public readonly string Dir = NewDir();
        public readonly SqliteCharacterRepository Characters;
        public readonly ManualClock Clock = new();
        public readonly ServerConfig Config;
        private readonly bool _clusters;
        public World World;

        public ChatTown(ServerConfig? config = null, bool clusters = false)
        {
            Config = config ?? new() { JoinByCharacterId = true, GuildCreateCost = 0, GuildRejoinCooldownMinutes = 0 };
            _clusters = clusters;
            Characters = Repository(Dir);
            World = NewWorld();
        }

        private World NewWorld() => new(Config, Characters, Items()) { Sync = new object(), Clock = Clock, Clusters = _clusters ? AlbionTests.Client.Clusters : null };

        /// <summary>The server restarts on the same database (the old world saved first).</summary>
        public void Restart()
        {
            World.Dispose();
            World = NewWorld();
        }

        public Character New(string name, string cluster = "0000", AccountLevel level = AccountLevel.Player)
        {
            var account = Characters.CreateAccount(name, null, level)!;
            var c = StarterKit.Default.NewCharacter(name);
            (c.Account, c.Cluster, c.PositionX, c.PositionY, c.Equipment, c.Inventory) = (account.Id, cluster, 5, 5, [], []);
            Check(Characters.Create(c), $"character {name} created");
            return c;
        }

        /// <summary>SelectCharacter's token, the chat connection's RegisterChatPeer (encrypted, as the client), then the game Join.</summary>
        public Client Login(Character c)
        {
            var peer = new FakePeer();
            var chat = new ChatSession(peer, World);
            peer.Disconnected = chat.OnDisconnected;
            var token = World.Accounts.Select(c.Account, c.Id);
            chat.OnRequest(Request(new RegisterChatPeerRequest { zc = token }, 1), encrypted: true);
            var client = new Client(c, chat, peer);
            Rejoin(client);
            return client;
        }

        public Client Login(string name, string cluster = "0000", AccountLevel level = AccountLevel.Player) => Login(New(name, cluster, level));

        /// <summary>A new game connection joins with the client's character (a reconnect, a cluster change's arrival).</summary>
        public void Rejoin(Client client)
        {
            var peer = new FakePeer();
            var session = new WorldSession(peer, World);
            peer.Disconnected = session.OnDisconnected;
            session.OnRequest(Request(new JoinRequest { ss = client.Id }, 1), encrypted: false);
            Check(session.Player is not null, $"{client.C.Name} joins");
            (client.Game, client.GamePeer) = (session, peer);
        }

        /// <summary>A GM's .tele of <paramref name="client"/> to <paramref name="cluster"/>, then the arrival's Join.</summary>
        public void Tele(Client client, string cluster, bool arrive = true)
        {
            World.Commands.Execute(new CommandContext(World, client.P, AccountLevel.Admin, (_, _) => { }), $".tele {cluster}");
            Check(client.Game.Status == SessionStatus.Transferring, $"{client.C.Name} leaves for {cluster}");
            if (arrive) Rejoin(client);
        }

        public void Dispose()
        {
            World.Dispose();
            Characters.Dispose();
            try { Directory.Delete(Dir, true); } catch (IOException) { }
        }
    }

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    /// <summary>An int array as it comes off the wire: a packed enum byte[] (4 bytes each) or a plain int[].</summary>
    internal static int[] Ints(object? v) => v switch
    {
        byte[] b => [.. Enumerable.Range(0, b.Length / 4).Select(i => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(4 * i)))],
        int[] i => i,
        short[] s => [.. s.Select(x => (int)x)],
        _ => [],
    };

    internal static List<Dictionary<byte, object?>> Of(FakePeer peer, EventCodes code, int mark) => [.. Since(peer, mark).Where(e => e.Code == code).Select(e => e.P)];

    /// <summary>The channels the client holds as offered after the chat events so far: id → type (one per type; 208 removes).</summary>
    internal static Dictionary<int, int> Offered(FakePeer chat)
    {
        var offered = new Dictionary<int, int>();
        foreach (var (code, p) in Since(chat, 0))
            if (code == EventCodes.NewChatChannels)
                foreach (var (type, id) in Ints(p[0]).Zip(Ints(p[1])))
                {
                    foreach (var old in offered.Where(o => o.Value == type).Select(o => o.Key).ToList()) offered.Remove(old);
                    offered[id] = type;
                }
            else if (code == EventCodes.RemovedChatChannel) offered.Remove(Convert.ToInt32(p[0]));
        return offered;
    }

    /// <summary>The offered id of <paramref name="type"/>; -1 = none.</summary>
    internal static int IdOf(Client c, ChatChannelType type) => Offered(c.ChatPeer).Where(o => o.Value == (int)type).Select(o => o.Key).DefaultIfEmpty(-1).First();

    /// <summary>The fixed id of a global channel (its place in the official 205, from 1).</summary>
    internal static int GlobalId(ChatChannelType type) => Array.IndexOf(ChatHub.GlobalOrder, type) + 1;

    internal static void JoinChannel(Client c, int id) => c.Chat.OnRequest(Request(new JoinChatChannelRequest { sx = id }, null), encrypted: false);

    /// <summary>JoinChatChannel for the offered channels of <paramref name="types"/>, as the client's join sync.</summary>
    internal static void JoinChannels(Client c, params ChatChannelType[] types)
    {
        foreach (var t in types) JoinChannel(c, IdOf(c, t));
    }

    /// <summary>A line on channel <paramref name="id"/>; the response, null = none.</summary>
    internal static OperationResponse? Line(Client c, int id, string text)
    {
        var n = c.ChatPeer.Responses.Count;
        c.Chat.OnRequest(Request(new SendChatMessageRequest { abj = id, abk = text }, null), encrypted: false);
        return c.ChatPeer.Responses.Count > n ? c.ChatPeer.Responses[^1] : null;
    }

    internal static OperationResponse Whisper(Client c, string to, string text)
    {
        c.Chat.OnRequest(Request(new SendWhisperMessageRequest { afo = to, afp = text }, 9), encrypted: false);
        return c.ChatPeer.Responses[^1];
    }

    /// <summary>The ChatMessage 73 lines a client got since <paramref name="mark"/> as (channel id, sender, text).</summary>
    internal static List<(int Id, string Sender, string Text)> Lines(Client c, int mark) =>
        [.. Of(c.ChatPeer, EventCodes.ChatMessage, mark).Select(p => (Convert.ToInt32(p[0]), (string)p[1]!, (string)p[2]!))];

    private static void RegisterUdp()
    {
        var dir = NewDir();
        using var host = Host(dir, spells: false);
        using var login = new TestPhotonClient(host.LoginServer.EndPoint);
        login.Connect();
        Check(login.EncryptionDone.Task.Wait(10000), "login encryption");
        Check(Call(login, TestLogin(), 1).ReturnCode == 0, "login");
        SelectCharacterResponse Select()
        {
            var r = new SelectCharacterResponse();
            AlbionSerializer.FillFields(r, Call(login, new SelectCharacterRequest { abc = "LocalHero" }, null).Parameters);
            return r;
        }
        var (first, second) = (Select(), Select());
        Check(first.blu == $"127.0.0.1:{host.ChatServer!.EndPoint.Port}" && host.ChatServer.EndPoint.Port != 0 && first.blv != Guid.Empty && second.blv != first.blv,
              $"key 7 names the bound chat port, key 8 a new token per SelectCharacter (got {first.blu})");
        using var chat = new TestPhotonClient(host.ChatServer.EndPoint, appId: "Albion.ChatServer");
        chat.Connect();
        Check(chat.EncryptionDone.Task.Wait(10000), "chat encryption");
        var level = Log.Level;
        Log.Level = LogLevel.Error; // the refused register's warning
        var refused = Call(chat, new RegisterChatPeerRequest { zc = first.blv }, 1);
        Log.Level = level;
        Check(refused.ReturnCode == ChatSession.RegisterRefused && !chat.Events.TryTake(out _, 300), "the first select's token is void: RC 1, no channels");
        var ok = Call(chat, new RegisterChatPeerRequest { zc = second.blv }, 2);
        Check(ok.ReturnCode == 0 && chat.Events.TryTake(out var e, 5000) && Convert.ToInt32(e!.Parameters[ReservedKeys.EventCode]) == (int)EventCodes.NewChatChannels
              && Ints(e.Parameters[0]).SequenceEqual(ChatHub.GlobalOrder.Select(t => (int)t)) && Ints(e.Parameters[1]).SequenceEqual(Enumerable.Range(1, 22)),
              "the current token: RC 0, then one 205 with the 22 globals in the official order, ids 1..22");

        // Chat:Enabled false: key 7 stays empty (the client opens no chat peer).
        var off = NewDir();
        var characters = Repository(off);
        var world = new World(new ServerConfig { ChatEnabled = false }, characters, Items()) { Sync = new object() };
        var peer = new FakePeer();
        var session = new LoginSession(peer, world.Config, characters, StarterKit.Default) { World = world };
        session.OnRequest(Request(TestLogin(), 1), encrypted: false);
        session.OnRequest(Request(new SelectCharacterRequest { abc = "LocalHero" }, null), encrypted: false);
        var sr = new SelectCharacterResponse();
        AlbionSerializer.FillFields(sr, peer.Responses[^1].Parameters);
        Check(sr.blu == "" && sr.bln is { Length: > 0 }, "Chat:Enabled false: SelectCharacter key 7 \"\"");
        world.Dispose();
        characters.Dispose();
        try { Directory.Delete(off, true); } catch (IOException) { }
    }

    private static void AreaLines()
    {
        using var town = new ChatTown();
        var a = town.Login("Anna");
        var b = town.Login("Boris");
        var c = town.Login("Cora", cluster: "0007");
        var area = IdOf(a, ChatChannelType.Area);
        Check(a.ChatPeer.Responses is [{ ReturnCode: 0 }] && a.ChatPeer.ResponseEncrypted is [true]
              && Since(a.ChatPeer, 0).Select(x => x.Code).ToArray() is [EventCodes.NewChatChannels, EventCodes.NewChatChannels]
              && area >= ChatHub.FirstDynamicId && IdOf(b, ChatChannelType.Area) == area && IdOf(c, ChatChannelType.Area) is var other && other != area && other > 0,
              "register: RC 0 encrypted as the request, the globals; the Join: 205 {27, the map's id}, one id per map");
        var mark = a.ChatPeer.Events.Count;
        JoinChannel(a, area);
        JoinChannel(a, 9999);
        Check(Since(a.ChatPeer, mark) is [(EventCodes.JoinedChatChannel, var j)] && Convert.ToInt32(j[0]) == 27 && Convert.ToInt32(j[1]) == area,
              "191 {area} → 206 {27, area}; 191 {9999} → nothing");
        JoinChannel(b, area);
        JoinChannels(c, ChatChannelType.Area);
        var (ma, mb, mc) = (a.ChatPeer.Events.Count, b.ChatPeer.Events.Count, c.ChatPeer.Events.Count);
        Check(Line(a, area, "  hello all ") is null, "a line gets no response");
        var got = Of(b.ChatPeer, EventCodes.ChatMessage, mb);
        Check(Lines(a, ma) is [(var i1, "Anna", "hello all")] && i1 == area && got is [var line] && line.Keys.Where(k => k < 250).Order().SequenceEqual(new byte[] { 0, 1, 2, 3 })
              && Convert.ToInt32(line[3]) == 0 && Lines(c, mc).Count == 0,
              "a Local line: 73 {0 area, 1 sender, 2 text, 3 role 0} to the sender and the map's other member, not to another map");
        var help = GlobalId(ChatChannelType.Help);
        JoinChannel(a, help);
        Check(Line(a, help, "lf healer") is null && Line(a, help, "again") is { ReturnCode: ChatHub.LineCooldown } r && Convert.ToInt32(r.Parameters[0]) == 5
              && Lines(a, ma).Count(l => l.Id == help) == 1,
              "a second Help line within 2 s: 7561 {0: 5}, not relayed");
        town.Clock.Advance(TimeSpan.FromSeconds(2));
        Check(Line(a, help, "now") is null && Lines(a, ma).Count(l => l.Id == help) == 2, "after the cooldown it goes out");
    }

    private static void ClusterChange()
    {
        _ = AlbionTests.Client.Clusters!.Get("0000");
        _ = AlbionTests.Client.Clusters!.Get("0007");
        using var town = new ChatTown(clusters: true);
        var a = town.Login("Anna");
        var b = town.Login("Boris");
        var home = IdOf(b, ChatChannelType.Area);
        JoinChannels(a, ChatChannelType.Area);
        JoinChannels(b, ChatChannelType.Area);
        var mark = b.ChatPeer.Events.Count;
        town.Tele(b, "0007", arrive: false);
        Check(Since(b.ChatPeer, mark) is [(EventCodes.LeftChatChannel, var l), (EventCodes.RemovedChatChannel, var r)]
              && Convert.ToInt32(l[0]) == home && Convert.ToInt32(r[0]) == home,
              "the change's commit: 207 + 208 of the old Area at once");
        mark = b.ChatPeer.Events.Count;
        JoinChannel(b, home); // a 191 that crossed them
        b.Chat.OnRequest(Request(new LeaveChatChannelRequest { tf = 4242 }, null), encrypted: false);
        var ma = a.ChatPeer.Events.Count;
        Line(a, home, "still here?");
        Check(Since(b.ChatPeer, mark) is [(EventCodes.JoinedChatChannel, var j), (EventCodes.LeftChatChannel, var jl), (EventCodes.LeftChatChannel, var any)]
              && Convert.ToInt32(j[1]) == home && Convert.ToInt32(jl[0]) == home && Convert.ToInt32(any[0]) == 4242 && Lines(a, ma).Count == 1,
              "a stale 191 → 206 + 207 without membership (the line reaches A only); 192 of any id → 207");
        mark = b.ChatPeer.Events.Count;
        town.Rejoin(b);
        var there = IdOf(b, ChatChannelType.Area);
        Check(Since(b.ChatPeer, mark) is [(EventCodes.NewChatChannels, var n)] && Ints(n[0]) is [27] && there != home && there > 0,
              "the arrival's Join: 205 {27} of the new map");
        town.Tele(b, "0000");
        Check(IdOf(b, ChatChannelType.Area) == home, "back home: the old Area id again (stable per cluster)");
    }

    private static void LogoutAndReconnect()
    {
        using var town = new ChatTown();
        var a = town.Login("Anna");
        var b = town.Login("Boris");
        Check(town.World.Social.Invite(a.P, "Boris").ReturnCode == 0 && town.World.Social.Answer(b.P, a.Id, true).arm, "A and B are friends");
        var area = IdOf(b, ChatChannelType.Area);
        var help = GlobalId(ChatChannelType.Help);
        JoinChannels(a, ChatChannelType.Area);
        JoinChannels(b, ChatChannelType.Area, ChatChannelType.Help);

        // A reconnect: a second game connection of B (its 207/208 + 205 of the Area, and the client joins again), then the old one closes.
        var oldPeer = b.GamePeer;
        town.Rejoin(b);
        JoinChannels(b, ChatChannelType.Area);
        var (mb, ma) = (b.ChatPeer.Events.Count, a.GamePeer.Events.Count);
        oldPeer.Disconnect();
        var mal = a.ChatPeer.Events.Count;
        Line(b, area, "back");
        Check(Since(b.ChatPeer, mb) is [(EventCodes.ChatMessage, _)] && Of(a.GamePeer, EventCodes.FriendOnlineStatus, ma).Count == 0 && Lines(a, mal).Count == 1,
              "the old connection closing: B keeps its channels (its line reaches A), no 297 {false} to A");

        mb = b.ChatPeer.Events.Count;
        ma = a.GamePeer.Events.Count;
        b.GamePeer.Disconnect();
        var left = Of(b.ChatPeer, EventCodes.LeftChatChannel, mb).Select(p => Convert.ToInt32(p[0])).Order().ToArray();
        var removed = Of(b.ChatPeer, EventCodes.RemovedChatChannel, mb).Select(p => Convert.ToInt32(p[0])).ToArray();
        Check(left.SequenceEqual(new[] { help, area }.Order()) && removed is [var gone] && gone == area,
              $"logout: 207 for every joined id, 208 only for the Area (got 207 [{string.Join(",", left)}], 208 [{string.Join(",", removed)}])");
        Check(Of(a.GamePeer, EventCodes.FriendOnlineStatus, ma) is [var off] && off[0] is false && new Guid((byte[])off[1]!) == b.Id
              && Whisper(a, "Boris", "hello?").ReturnCode == ChatHub.PlayerNotOnline,
              "A gets 297 {false, B}; a whisper to B → 7720");
    }

    private static void Relogin()
    {
        using var town = new ChatTown();
        var a = town.Login("Anna");
        var account = a.C.Account;
        var token = town.World.Accounts.ChatToken(account)!.Value;
        town.World.Accounts.Login(account, DateTime.UtcNow.Ticks); // the login peer reconnected
        var peer = new FakePeer();
        var again = new ChatSession(peer, town.World);
        peer.Disconnected = again.OnDisconnected;
        again.OnRequest(Request(new RegisterChatPeerRequest { zc = token }, 1), encrypted: true);
        Check(peer.Responses is [{ ReturnCode: 0 }] && town.World.Chat.SessionOf(a.Id) == again && a.ChatPeer.DisconnectReason is not null,
              "after a re-Login the stored token still registers (RC 0); the old chat connection is replaced");

        var alt = StarterKit.Default.NewCharacter("Alma");
        (alt.Account, alt.Cluster, alt.PositionX, alt.PositionY, alt.Equipment, alt.Inventory) = (account, "0000", 5, 5, [], []);
        Check(town.Characters.Create(alt), "a second character of the account");
        a.GamePeer.Disconnect();
        var mark = peer.Events.Count;
        Check(town.World.Accounts.Select(account, alt.Id) == token, "a Select while the chat connection is open keeps its token");
        town.World.Chat.Selected(account, alt);
        var events = Since(peer, mark);
        Check(again.CharacterId == alt.Id && town.World.Chat.SessionOf(alt.Id) == again && town.World.Chat.SessionOf(a.Id) is null
              && events.Count(e => e.Code == EventCodes.RemovedChatChannel) == 22 && events[^1].Code == EventCodes.NewChatChannels && Ints(events[^1].P[1]).Length == 22,
              "the connection moves to Alma: 208 for the 22 globals, then the register's 205");
    }

    private static void PveWorldChannel()
    {
        foreach (var pve in new[] { true, false })
        {
            using var town = new ChatTown(pve ? ServerConfig.PveDefaults with { JoinByCharacterId = true } : null);
            var a = town.Login("Anna");
            var b = town.Login("Boris");
            var tabs = Of(a.GamePeer, EventCodes.UpdateChatSettings, 0)[0][1] as int[][];
            JoinChannel(a, 2); // English 8
            JoinChannel(b, 6); // Russian 13
            var mb = b.ChatPeer.Events.Count;
            Line(a, 2, "hello world");
            var heard = Lines(b, mb);
            Check(pve ? tabs?[0].Contains(0) == true && heard is [(6, "Anna", "hello world")] : tabs?[0].Contains(0) == false && heard.Count == 0,
                  pve ? "pve: GENERAL holds Global 0; an English line reaches the Russian channel's member with id 6"
                      : "official: no Global in GENERAL, the languages apart");
        }
    }

    private static void GuildChannel()
    {
        using var town = new ChatTown();
        var a = town.Login("Anna");
        var b = town.Login("Boris");
        var ma = a.ChatPeer.Events.Count;
        Check(town.World.Guilds.Create(a.P, "Hunters", GuildLogo.Default, "", out _) == 0, "A founds Hunters");
        var gid = IdOf(a, ChatChannelType.Guild);
        Check(Of(a.ChatPeer, EventCodes.NewChatChannels, ma) is [var n] && Ints(n[0]) is [24] && gid >= ChatHub.FirstDynamicId, "the founder: 205 {24, id}");
        Check(town.World.Guilds.Invite(a.P, "Boris", out _, out _) == 0 && town.World.Guilds.Accept(b.P, out _) == 0 && IdOf(b, ChatChannelType.Guild) == gid,
              "the invitee on accept: 205 {24, the same id}");
        JoinChannels(a, ChatChannelType.Guild);
        JoinChannels(b, ChatChannelType.Guild);
        void G(string text) => town.World.Commands.Execute(new CommandContext(town.World, a.P, AccountLevel.Player, (_, _) => { }), $".g {text}");
        var (mal, mbl, mag, mbg) = (a.ChatPeer.Events.Count, b.ChatPeer.Events.Count, a.GamePeer.Events.Count, b.GamePeer.Events.Count);
        G("rally at the bank");
        Check(Lines(a, mal) is [(var i, "Anna", "rally at the bank")] && i == gid && Lines(b, mbl).Count == 1
              && Of(a.GamePeer, EventCodes.SystemMessage, mag).Count == 0 && Of(b.GamePeer, EventCodes.SystemMessage, mbg).Count == 0,
              ".g: on the Guild channel to both, no '[Guild]' system line besides");
        var mb = b.ChatPeer.Events.Count;
        Check(town.World.Guilds.Kick(a.P, b.Id, null) == 0, "A kicks B");
        mbl = b.ChatPeer.Events.Count;
        G("bye");
        Check(Since(b.ChatPeer, mb) is [(EventCodes.LeftChatChannel, var l), (EventCodes.RemovedChatChannel, var r)] && Convert.ToInt32(l[0]) == gid
              && Convert.ToInt32(r[0]) == gid && Lines(b, mbl).Count == 0,
              "the kicked member: 207 + 208 of the guild channel, no later guild line");
    }

    private static void Whispers()
    {
        using var town = new ChatTown();
        var a = town.Login("Anna");
        var b = town.Login("Boris");
        var (ma, mb) = (a.ChatPeer.Events.Count, b.ChatPeer.Events.Count);
        var r = Whisper(a, "bORIS", " psst ");
        Check(r.ReturnCode == 0 && r.Parameters[ReservedKeys.RequestId] is 9 && Of(b.ChatPeer, EventCodes.ChatWhisper, mb) is [var w] && (string)w[0]! == "Anna"
              && (string)w[3]! == "psst" && Of(a.ChatPeer, EventCodes.ChatWhisper, ma).Count == 0,
              "a whisper by name in any case: 75 {0 Anna, 3 text} to B, RC 0 to A (its client writes its own line), no 75 to A");
        Check(Whisper(a, "Boris", "again").ReturnCode == ChatHub.WhisperCooldown, "within 100 ms: 7722");
        town.Clock.Advance(TimeSpan.FromMilliseconds(150));
        Check(Whisper(a, "Anna", "me").ReturnCode == ChatHub.PlayerNotOnline && Whisper(a, "Nobody", "x").ReturnCode == ChatHub.PlayerNotOnline,
              "oneself or an unknown name: 7720");
    }

    private static void CommandsAndAnnouncements()
    {
        using var town = new ChatTown();
        var gm = town.Login("Gina", level: AccountLevel.GameMaster);
        var b = town.Login("Boris");
        var area = IdOf(b, ChatChannelType.Area);
        JoinChannels(gm, ChatChannelType.Area);
        JoinChannels(b, ChatChannelType.Area, ChatChannelType.Announcement);
        var (mg, mb, mgs) = (gm.ChatPeer.Events.Count, b.ChatPeer.Events.Count, gm.GamePeer.Events.Count);
        Check(Line(gm, area, ".help") is null && Of(gm.GamePeer, EventCodes.SystemMessage, mgs).Count > 0 && Lines(gm, mg).Count == 0 && Lines(b, mb).Count == 0,
              "a '.'-command on Local: run, replies as SystemMessage on the game connection, relayed to nobody");
        var announcement = GlobalId(ChatChannelType.Announcement);
        Line(b, announcement, "everyone look at me");
        Check(Lines(b, mb).Count == 0, "a player's line on Announcement 7 relays nothing");
        var rest = town.World.Chat.Announce("The world boss is up", [gm.P, b.P]).ToList();
        Check(Lines(b, mb) is [(var id, "", "The world boss is up")] && id == announcement && rest is [var other] && other == gm.P,
              "a server announcement: 73 on 7 to the members of 7, the others left for the SystemMessage");
    }
}
