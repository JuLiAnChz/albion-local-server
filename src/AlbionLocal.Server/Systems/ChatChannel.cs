using AlbionLocal.Server.Network;

namespace AlbionLocal.Server.Systems;

/// <summary>
/// The client's ChatChannelType (Albion.Common.Chat, stageJ chat SPEC §1.3), the values the server uses: the chat server's
/// channels, the game server's Say/Party/System lines only as tab entries (UpdateChatSettings 217).
/// </summary>
public enum ChatChannelType
{
    Global = 0, Trade = 1, Recruit = 2, LookingForGroup = 3, HardCoreExpedition = 4, Help = 5, Staff = 6, Announcement = 7,
    English = 8, Arabic = 22, LanguageProxy = 23, Guild = 24, Alliance = 25, Party = 26, Area = 27, Private = 28,
    Faction = 29, FactionArea = 30, Surrounding = 31, System = 33,
}

/// <summary>
/// One chat channel of the chat server (stageJ chat SPEC §2.3): its id (NewChatChannels key 1, the id every later message names),
/// its type, its scope (the globals', a map's cluster for Area, a guild's Guid for Guild) and the chat connections that joined it
/// (JoinChatChannel 191 → JoinedChatChannel 206). Not saved: the ids hold for the server run.
/// </summary>
public sealed class ChatChannel(int id, ChatChannelType type, string scope)
{
    public int Id { get; } = id;

    public ChatChannelType Type { get; } = type;

    /// <summary>"global" for the 22 globals and Staff, the map's cluster for Area, the guild's Guid for Guild.</summary>
    public string Scope { get; } = scope;

    /// <summary>The joined chat sessions (the line's receivers).</summary>
    public HashSet<ChatSession> Members { get; } = [];

    /// <summary>One of the global types that have the client's cooldown (0-5, 8-22; SPEC §2.3).</summary>
    public bool Global => Type is >= ChatChannelType.Global and <= ChatChannelType.Help || Language;

    /// <summary>One of the 15 language channels (English 8 … Arabic 22).</summary>
    public bool Language => Type is >= ChatChannelType.English and <= ChatChannelType.Arabic;

    public override string ToString() => $"{Type} {Id}{(Scope == "global" ? "" : $" ({Scope})")}";
}
