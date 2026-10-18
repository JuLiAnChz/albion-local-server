namespace AlbionLocal.Server.Game;

/// <summary>
/// The guild goals' state of a guild (stageI guild-goals SPEC §2.7, §2.8; the guilds row's progress column, JSON, saved with the account
/// every 30 s): its guild seasons (the current one and a few before it), the weekly goals of this week and the last, the members'
/// weekly logs of the Member Logs window (309) and the season-end member rewards not given yet (§2.6).
/// </summary>
public sealed class GuildProgress
{
    /// <summary>The guild's seasons, the newest last (at most <see cref="Systems.GuildGoals.SeasonsKept"/>).</summary>
    public List<GuildSeasonState> Seasons { get; set; } = [];

    /// <summary>This week's goals; null = none rolled yet.</summary>
    public GuildWeek? Week { get; set; }

    /// <summary>The week before (the blessing's source and .guild goals' "last week").</summary>
    public GuildWeek? LastWeek { get; set; }

    /// <summary>The Member Logs per member (QueryGuildPlayerStats, op 309).</summary>
    public Dictionary<Guid, GuildMemberLog> Logs { get; set; } = [];

    /// <summary>The season-end member rewards per member not given yet (an offline member's, given at its next login; paid once).</summary>
    public Dictionary<Guid, List<GuildMemberReward>> Owed { get; set; } = [];
}

/// <summary>A season-end member reward (SPEC §2.6): the season it closes and the guild's bracket (iron … crystal: the items and the journal's gotguildmemberreward).</summary>
public sealed class GuildMemberReward
{
    public string Season { get; set; } = "";

    public string Bracket { get; set; } = "";
}

/// <summary>
/// The guild might of one category in a season (SPEC §1.7, phase 4): the members' might summed (a7m raw; a member who left keeps nothing
/// in the list, the total keeps his might), the levels reached (the client's level: progression rows reached) and each member's might.
/// </summary>
public sealed class GuildMight
{
    public long Total { get; set; }

    public int Level { get; set; }

    public Dictionary<Guid, long> Members { get; set; } = [];
}

/// <summary>
/// The Guild Challenge and season points of a guild in one season (SPEC §2.2, §2.4): the challenge points (whole points, the 390 total),
/// the level reached, the guild season points with their activity breakdown (GvgSeasonActivity names, 447), the challenge points each
/// member gave (390's rows; a member who left keeps nothing in the list, the total keeps his points), the fraction of a challenge point
/// not yet whole (small gains add up), the GameTimeStamp of the last change (390 key 1: the client refills its bar only when it changes)
/// and the one of the last season points gain (152's ties: the guild that reached a score first ranks higher); phase 4 the guild might per
/// category (450 / 449), and whether the season's end was paid (<see cref="Closed"/>, once).
/// </summary>
public sealed class GuildSeasonState
{
    public string Season { get; set; } = "";

    public long Challenge { get; set; }

    public int Level { get; set; } = 1;

    public long Points { get; set; }

    public Dictionary<string, long> Activity { get; set; } = [];

    public Dictionary<Guid, long> Members { get; set; } = [];

    public double Carry { get; set; }

    public long Updated { get; set; }

    public long PointsAt { get; set; }

    public Dictionary<string, GuildMight> Might { get; set; } = [];

    public bool Closed { get; set; }
}

/// <summary>
/// A guild's goals of one week (SPEC §2.3): the week's start (UTC ticks of Monday at the bounties' reset), its goals, the fame (a7m)
/// below a whole point per fame kind not yet counted, and the blessing steps it holds.
/// </summary>
public sealed class GuildWeek
{
    public long Start { get; set; }

    public List<GuildGoal> Goals { get; set; } = [];

    public Dictionary<string, long> Carry { get; set; } = [];

    /// <summary>The blessing of this week: the goals finished the week before (at most Guild:MaxBlessing).</summary>
    public int Blessing { get; set; }
}

/// <summary>One weekly guild goal: its pool and kind, the least tier it counts (0 = any), the target, the progress and whether it is done (paid once).</summary>
public sealed class GuildGoal
{
    public string Pool { get; set; } = "";

    public string Kind { get; set; } = "";

    public int Tier { get; set; }

    public long Target { get; set; }

    public long Progress { get; set; }

    public bool Done { get; set; }

    /// <summary>"kill 600 T5+ mobs" (the .guild goals line and the announcements).</summary>
    public string Describe() => Kind switch
    {
        "killmobs" => $"kill {Target} T{Tier}+ mobs",
        "bosses" => $"defeat {Target} bosses",
        "dungeons" => $"clear {Target} dungeons, expeditions or events",
        "pvpkills" => $"defeat {Target} enemies in PvP",
        "pvpfame" => $"earn {Target} PvP fame",
        "gather" => Tier > 0 ? $"gather {Target} T{Tier}+ resources" : $"gather {Target} resources",
        "fish" => $"catch {Target} fish",
        "craftfame" => $"earn {Target} crafting fame",
        _ => $"{Kind} {Target}",
    };
}

/// <summary>
/// A member's Member Logs (309, SPEC §2.5): the values of the 10 stats (bew 0-9, a7m raw) this week, the week before and all time, the
/// week they belong to (a gain in a later week moves them) and the highest map tier it played at this week and the week before (the
/// weekly goals' tier).
/// </summary>
public sealed class GuildMemberLog
{
    /// <summary>The bew stats of the window (GuildMemberLogSearchBar.Init order).</summary>
    public const int TaxPaid = 0, SilverBalance = 1, GatheringFame = 4, CraftingFame = 5, PveFame = 6, PvpFame = 7, Count = 10;

    public long Week { get; set; }

    public long[] This { get; set; } = new long[Count];

    public long[] Last { get; set; } = new long[Count];

    public long[] All { get; set; } = new long[Count];

    public int Tier { get; set; }

    public int LastTier { get; set; }
}
