using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AlbionLocal.Photon;

namespace AlbionLocal.Server.Content;

/// <summary>
/// The goals of PVE-DESIGN.md step 11 (analysis/work/stageH/journals/SPEC.md §B, §C, §E): content/goals.json with four parts, the
/// Adventurer's Challenge (<see cref="SeasonGoals"/>), the Albion Journal (<see cref="JournalGoals"/>), the server bounties
/// (<see cref="BountyGoals"/>), the PvP goals (<see cref="PvpGoalSettings"/>, stage I) and the guild goals (<see cref="GuildGoalSettings"/>,
/// stage I). Every key is optional: one the file leaves out keeps the built-in value (<see cref="Default"/>, the official rules for the
/// season and the journal, no bounties; a guild key marked "preset" the server preset's); a missing file or one that does not parse gives the built-in values
/// with a log line, an unknown key or a value out of range is refused. Immutable.
/// </summary>
public sealed record GoalSettings
{
    public static GoalSettings Default { get; } = new();

    public SeasonGoals Season { get; init; } = new();

    public JournalGoals Journal { get; init; } = new();

    public BountyGoals Bounties { get; init; } = new();

    public PvpGoalSettings Pvp { get; init; } = new();

    public GuildGoalSettings Guild { get; init; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static GoalSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("content", $"{path} missing: built-in goal settings (official season factors, no bounties)");
            return Default;
        }
        try
        {
            var s = Parse(File.ReadAllText(path));
            Log.Info("content", string.Create(CultureInfo.InvariantCulture,
                $"goals: season {(s.Season.Enabled ? $"on (pve x{s.Season.Pve?.ToString(CultureInfo.InvariantCulture) ?? "data"}, boss +{s.Season.BossBonus})" : "off")}, "
                + $"journal {(s.Journal.Enabled ? "on" : "off")}, bounties {(s.Bounties.Enabled ? $"{s.Bounties.Daily}+{s.Bounties.PvpDaily} pvp daily + {s.Bounties.Weekly}+{s.Bounties.PvpWeekly} pvp weekly of {s.Bounties.Pool.Count}" : "off")}, "
                + $"pvp goals {(s.Pvp.Enabled ? $"on ({s.Pvp.KillPoints}+{s.Pvp.KillPointsPerTier}/tier points, cap {s.Pvp.DailyPointsCap})" : "off")}, "
                + $"guild goals {(s.Guild.Enabled ? $"on (region {(s.Guild.Region.Length == 0 ? "west" : s.Guild.Region)}, {s.Guild.Pools.Values.Sum(p => p.Count)} weekly kinds)" : "off")}"));
            return s;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            Log.Error("content", $"{path}: {e.Message}; built-in goal settings used");
            return Default;
        }
    }

    /// <summary>The file's text, checked (<see cref="InvalidDataException"/> for a value out of range or a bounty without its keys).</summary>
    public static GoalSettings Parse(string text)
    {
        var s = JsonSerializer.Deserialize<GoalSettings>(text, Options) ?? throw new InvalidDataException("not a JSON object");
        s.Season.Check();
        s.Bounties.Check();
        s.Pvp.Check();
        s.Guild.Check();
        return s;
    }
}

/// <summary>
/// The Adventurer's Challenge (SPEC §C, §E.1, §E.2, §E.5). Points per gain = factor × (whole fame)^exponent of the month's season
/// (premium factor 1: no premium here), × <see cref="PointsFactor"/>. A factor left null keeps the season's own (official: pve 1.5,
/// gathering 3, fishing 3); crafting, PvP and tomes give none, as officially.
/// </summary>
public sealed record SeasonGoals
{
    /// <summary>Whether gains give season points and the tracker answers with them (false: the empty tracker of before).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The factor of a PvE kill's fame; null = the season's pve (1.5).</summary>
    public double? Pve { get; init; }

    /// <summary>The factor of a harvest's fame; null = the season's gathering (3).</summary>
    public double? Gathering { get; init; }

    /// <summary>The factor of a catch's fame; null = the season's fishing (3).</summary>
    public double? Fishing { get; init; }

    /// <summary>The factor of a kill inside a random dungeon or an expedition (§E.1); null = the PvE one.</summary>
    public double? Dungeon { get; init; }

    /// <summary>Flat points for a boss kill (a world boss, or a mob of category boss / miniboss), to the killer and its party near it; 0 = none.</summary>
    public double BossBonus { get; init; }

    /// <summary>All points × this (the server's "Rates:SeasonPoints").</summary>
    public double PointsFactor { get; init; } = 1;

    /// <summary>
    /// §E.2: the main and secondary trackers' rewards are "boughtpremium" only, which the client never lets a character without premium
    /// claim. True = the server gives them itself when they unlock (into the inventory, a system line; CLAIMED in the tracker); false = they
    /// stay UNLOCKED_NOT_CLAIMABLE, as officially for a character without premium.
    /// </summary>
    public bool GrantPremiumRewards { get; init; }

    /// <summary>§E.5: extra items given with the daily goal (1200 points), straight into the inventory with a system line (the client does not know them).</summary>
    public IReadOnlyList<string> DailyExtra { get; init; } = [];

    internal void Check()
    {
        foreach (var (name, v) in new[] { ("Pve", Pve), ("Gathering", Gathering), ("Fishing", Fishing), ("Dungeon", Dungeon), ("BossBonus", (double?)BossBonus), ("PointsFactor", (double?)PointsFactor) })
            if (v is < 0 or > 1000 || v is double d && double.IsNaN(d)) throw new InvalidDataException($"Season:{name}: {v} is not 0..1000");
    }
}

/// <summary>The Albion Journal (SPEC §B, §E.4): which categories' achievements progress (empty = all; the rest stays at 0).</summary>
public sealed record JournalGoals
{
    /// <summary>Whether achievements progress, complete and can be claimed (false: the journal shows nothing done, as before).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The albionjournal.xml categories that progress (PVE, CREATURES, GATHERING …); empty = every category.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>
    /// Whether the achievements that mirror a Steam achievement (steamachievementname: SA_*, STEAMONLY) progress and complete. Off by
    /// default: whether the unmodified client passes their completion (362) on to the Steam API is not traced, and it must not unlock
    /// real achievements on the player's account.
    /// </summary>
    public bool SteamMirrors { get; init; }
}

/// <summary>
/// The server bounties (PVE-DESIGN.md §2.I, step 11): a few goals a day and a week picked from <see cref="Pool"/>, the same for every
/// player (seeded by the period), progress and completion as system lines in chat, the reward straight into the inventory. .bounty lists
/// them. The client has no window for them (no quest board), so chat is all there is.
/// </summary>
public sealed record BountyGoals
{
    public bool Enabled { get; init; } = true;

    /// <summary>The UTC time of day the daily (and on Mondays the weekly) bounties change.</summary>
    public string ResetUtc { get; init; } = "10:00";

    /// <summary>How many daily bounties are picked from the pool's daily entries.</summary>
    public int Daily { get; init; } = 3;

    /// <summary>How many weekly bounties are picked from the pool's weekly entries.</summary>
    public int Weekly { get; init; } = 1;

    /// <summary>
    /// Stage I (pvp-goals SPEC §2.5): daily slots for the pool's PvP entries (kinds pvpkill / duel) on top of <see cref="Daily"/>, which
    /// then draws from the PvE entries only: a PvP goal every day that takes no PvE goal's place (a solo player keeps all of those).
    /// 0 = one mixed draw (as before). Without Pvp:Enabled the PvP entries are never drawn.
    /// </summary>
    public int PvpDaily { get; init; }

    /// <summary>Stage I: the same for the weekly entries on top of <see cref="Weekly"/>; 0 = one mixed draw.</summary>
    public int PvpWeekly { get; init; }

    /// <summary>A system line at every quarter of a bounty's count (and at its completion, always).</summary>
    public bool AnnounceProgress { get; init; } = true;

    /// <summary>A system line to every player in game with the new bounties when they change.</summary>
    public bool AnnounceReset { get; init; } = true;

    public IReadOnlyList<Bounty> Pool { get; init; } = [];

    /// <summary>The reset time of day.</summary>
    [JsonIgnore]
    public TimeSpan Reset => TimeSpan.TryParseExact(ResetUtc, @"hh\:mm", CultureInfo.InvariantCulture, out var t) ? t : TimeSpan.FromHours(10);

    internal void Check()
    {
        if (!TimeSpan.TryParseExact(ResetUtc, @"hh\:mm", CultureInfo.InvariantCulture, out var t) || t >= TimeSpan.FromDays(1))
            throw new InvalidDataException($"Bounties:ResetUtc: {ResetUtc} is not hh:mm");
        if (Daily is < 0 or > 20 || Weekly is < 0 or > 20) throw new InvalidDataException("Bounties:Daily / Weekly: not 0..20");
        if (PvpDaily is < 0 or > 20 || PvpWeekly is < 0 or > 20) throw new InvalidDataException("Bounties:PvpDaily / PvpWeekly: not 0..20");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in Pool)
        {
            if (string.IsNullOrWhiteSpace(b.Id) || !ids.Add(b.Id)) throw new InvalidDataException($"Bounties:Pool: an entry without an Id, or Id {b.Id} twice");
            if (!Bounty.Kinds.Contains(b.Kind)) throw new InvalidDataException($"Bounties:Pool {b.Id}: Kind {b.Kind} is not {string.Join(" / ", Bounty.Kinds)}");
            if (b.Count is < 1 or > 100000 || b.Silver < 0 || b.Fame < 0) throw new InvalidDataException($"Bounties:Pool {b.Id}: Count 1..100000, Silver and Fame >= 0");
            if (b.Bots is not ("any" or "none" or "only")) throw new InvalidDataException($"Bounties:Pool {b.Id}: Bots {b.Bots} is not any / none / only");
            if (b.Zone is not null && !PvpGoalSettings.WarningTypes.Contains(b.Zone))
                throw new InvalidDataException($"Bounties:Pool {b.Id}: Zone {b.Zone} is not {string.Join(" / ", PvpGoalSettings.WarningTypes)}");
        }
    }
}

/// <summary>
/// One bounty of the pool: kill (mobs, with MinTier / MaxTier / Mob, a part of the mob name / Category, mobs.xml mobtypecategory /
/// Cluster), boss (a world boss), dungeon (a boss or miniboss inside a random dungeon), expedition (one finished), gather (items
/// harvested, MinTier / MaxTier), fish (catches), stageL's corrupted (a corrupted dungeon completed, MinTier its rank's tier 5 / 6 / 8),
/// roadchest (an Avalonian road chest opened, MinTier / MaxTier the road's tier 4 / 6 / 8) and portal (a road portal passed, MinTier / MaxTier
/// the tier of the cluster it leads to), and stage I's pvpkill (a credited PvP kill or knockdown, <see cref="Systems.PvpGoals"/>:
/// MinTier / MaxTier the victim's, Zone a pvpwarningtype, Bots any / none / only, Assists whether an assist counts) or duel (a credited
/// duel win). Text is the chat line ({count} = Count). The reward: Silver and Fame (whole, Rates
/// not applied) and Items (uniquenames, one each; "NAME:n" for n).
/// </summary>
public sealed record Bounty
{
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal) { "kill", "boss", "dungeon", "expedition", "gather", "fish", "pvpkill", "duel", "corrupted", "roadchest", "portal" };

    /// <summary>The PvP kinds (<see cref="BountyGoals.PvpDaily"/>).</summary>
    [JsonIgnore]
    public bool IsPvp => Kind is "pvpkill" or "duel";

    public string Id { get; init; } = "";
    public string Kind { get; init; } = "kill";
    public string Text { get; init; } = "";
    public int Count { get; init; } = 1;
    public bool Weekly { get; init; }
    public int MinTier { get; init; }
    public int MaxTier { get; init; } = 8;
    public string? Mob { get; init; }
    public string? Category { get; init; }
    public string? Cluster { get; init; }

    /// <summary>pvpkill: the zone's pvpwarningtype (safe, yellow, orange, red, black); null = any whose Pvp:ZoneFactors is above 0.</summary>
    public string? Zone { get; init; }

    /// <summary>pvpkill: whether player-bot victims count, "any" (default), "none" or "only".</summary>
    public string Bots { get; init; } = "any";

    /// <summary>pvpkill: an assist (Pvp:AssistMinShare of the damage) counts too.</summary>
    public bool Assists { get; init; }

    public long Silver { get; init; }
    public long Fame { get; init; }
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>The chat text: Text with {count} filled, or a made-up one.</summary>
    public string Describe() => (Text.Length > 0 ? Text : $"{Kind} x{{count}}").Replace("{count}", Count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}

/// <summary>
/// The PvP goals (stage I, analysis/work/stageI/pvp-goals/SPEC.md §2.6; <see cref="Systems.PvpGoals"/>): what a credited PvP kill, knockdown
/// or duel win gives (Adventurer's Challenge points, bounty counts, the journal's PVP and GUILD achievements) and the anti-farming rules
/// that refuse a credit. The built-in values are the official rules where there are any (no season points for PvP, a journal killplayer
/// only for a death); content/goals.json ships the server's (points, a knockdown counts as a kill).
/// </summary>
public sealed record PvpGoalSettings
{
    /// <summary>The zone classes of worldsettings pvpwarningtype (<see cref="Data.ClusterPvp.WarningType"/>).</summary>
    public static readonly IReadOnlySet<string> WarningTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "safe", "yellow", "orange", "red", "black" };

    /// <summary>Whether PvP results feed any goal (false: none, the journal's PvP achievements included).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>A non-lethal PvP knockdown counts as a kill for the journal's killplayer (the pve preset has no PvP deaths); built-in false.</summary>
    public bool KnockdownIsKill { get; init; }

    /// <summary>The share of the kill fame a non-lethal knockdown gives (UpdateFame only, no KilledPlayer); 0 = none (official KillRewardedNoFame).</summary>
    public double KnockdownFameFactor { get; init; }

    /// <summary>Season points of a credited kill: KillPoints + KillPointsPerTier × the victim's tier (whole points; built-in 0, officially none).</summary>
    public double KillPoints { get; init; }

    public double KillPointsPerTier { get; init; }

    /// <summary>Season points of a credited duel win (whole points; built-in 0).</summary>
    public double DuelWinPoints { get; init; }

    /// <summary>An assist's share of the kill's points.</summary>
    public double AssistFactor { get; init; } = 0.5;

    /// <summary>The least share of the damage on the victim (in the combat timeout) that makes an attacker an assist.</summary>
    public double AssistMinShare { get; init; } = 0.15;

    /// <summary>The points factor of a player-bot victim.</summary>
    public double BotFactor { get; init; } = 0.5;

    /// <summary>
    /// The points and bounty factor by the zone's pvpwarningtype (safe, yellow, orange, red, black); a type left out is 0: no points and
    /// no bounty there (the journal judges by its own filters). Duels ignore it. A file's map replaces this one whole.
    /// </summary>
    public IReadOnlyDictionary<string, double> ZoneFactors { get; init; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["red"] = 1, ["black"] = 1.5 };

    /// <summary>The least tier (the highest of the victim's equipped items) a credit needs.</summary>
    public int MinVictimTier { get; init; } = 4;

    /// <summary>The same victim credits the same player again only after this many minutes …</summary>
    public double SameVictimCooldownMinutes { get; init; } = 30;

    /// <summary>… and at most this often in a bounty day.</summary>
    public int SameVictimPerDay { get; init; } = 3;

    /// <summary>A duel win credits only after this many seconds since the duel's start.</summary>
    public double DuelMinSeconds { get; init; } = 20;

    /// <summary>The same two duelists credit at most this many wins a bounty day.</summary>
    public int DuelSamePairPerDay { get; init; } = 2;

    /// <summary>Season points from PvP per player and bounty day (whole points).</summary>
    public double DailyPointsCap { get; init; } = 600;

    /// <summary>Whether a crime (a Red attacking a non-Red: Pvp:Mode Official only) still credits.</summary>
    public bool CrimesCount { get; init; }

    /// <summary>Whether a player-bot victim counts for the journal's killplayer / knockdownplayer (the anti-farming rules still apply).</summary>
    public bool JournalCountsBots { get; init; } = true;

    /// <summary>A system line for each PvP season gain.</summary>
    public bool Announce { get; init; } = true;

    /// <summary>The factor of zone class <paramref name="warningType"/> (0 when not listed).</summary>
    public double ZoneFactor(string? warningType)
    {
        if (warningType is null) return 0;
        foreach (var (k, v) in ZoneFactors)
            if (k.Equals(warningType, StringComparison.OrdinalIgnoreCase)) return v;
        return 0;
    }

    internal void Check()
    {
        foreach (var (name, v) in new[] { ("KnockdownFameFactor", KnockdownFameFactor), ("KillPoints", KillPoints), ("KillPointsPerTier", KillPointsPerTier),
                     ("DuelWinPoints", DuelWinPoints), ("AssistFactor", AssistFactor), ("BotFactor", BotFactor),
                     ("SameVictimCooldownMinutes", SameVictimCooldownMinutes), ("DuelMinSeconds", DuelMinSeconds), ("DailyPointsCap", DailyPointsCap) })
            if (v is < 0 or > 100000 || double.IsNaN(v)) throw new InvalidDataException($"Pvp:{name}: {v} is not 0..100000");
        if (AssistMinShare is < 0 or > 1 || double.IsNaN(AssistMinShare)) throw new InvalidDataException($"Pvp:AssistMinShare: {AssistMinShare} is not 0..1");
        if (KnockdownFameFactor > 10) throw new InvalidDataException($"Pvp:KnockdownFameFactor: {KnockdownFameFactor} is not 0..10");
        if (MinVictimTier is < 0 or > 10 || SameVictimPerDay is < 1 or > 1000 || DuelSamePairPerDay is < 1 or > 1000)
            throw new InvalidDataException("Pvp:MinVictimTier 0..10, SameVictimPerDay and DuelSamePairPerDay 1..1000");
        foreach (var (k, v) in ZoneFactors)
            if (!WarningTypes.Contains(k) || v is < 0 or > 100 || double.IsNaN(v))
                throw new InvalidDataException($"Pvp:ZoneFactors: {k} {v} (keys {string.Join(" / ", WarningTypes)}, values 0..100)");
    }
}

/// <summary>
/// The guild goals (stage I, analysis/work/stageI/guild-goals/SPEC.md §2.9; <see cref="Systems.GuildGoals"/>): the Guild Challenge of the
/// client's Guild Season window (the members' Adventurer's Challenge points × <see cref="ChallengeFactor"/>, levels from the client's own
/// table, their season points), the server's weekly guild goals with the guild blessing, the season points of boss kills, the season-end
/// rewards by bracket and the guild might (× <see cref="MightFactor"/>). The keys marked "preset" are null by default: null takes the
/// server preset's value (official / pve), a number or true / false wins over it.
/// </summary>
public sealed record GuildGoalSettings
{
    /// <summary>The goal kinds of a pool entry (<see cref="GuildGoalEntry"/>).</summary>
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "killmobs", "bosses", "dungeons", "pvpkills", "pvpfame", "gather", "fish", "craftfame",
    };

    /// <summary>Whether the Guild Challenge and the weekly goals run (false: the old stub answers of 390 and 152, no hooks, no 351).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The client's season files: gvgseasons_&lt;Region&gt; / times_&lt;Region&gt; ("europe"; "" = the West's, "asia"), as its Login region.</summary>
    public string Region { get; init; } = "europe";

    /// <summary>The length of a local season (after the last client season, 2026-11-09 11:00 UTC) in days.</summary>
    public int LocalSeasonDays { get; init; } = 28;

    /// <summary>Preset (official 1, pve 20): guild challenge points per Adventurer's Challenge point a member earns.</summary>
    public double? ChallengeFactor { get; init; }

    /// <summary>Guild challenge points per craft fame point, both presets (SPEC §2.2; not × ChallengeFactor; the personal season gives none for crafts).</summary>
    public double CraftChallengeFactor { get; init; } = 0.002;

    /// <summary>Preset (official 1, pve 5): the factor of every guild season point gain but a challenge level's (the client's popup shows the table's value).</summary>
    public double? SeasonPointsFactor { get; init; }

    /// <summary>Preset (official false, pve true): the weekly guild goals, their rewards and the blessing.</summary>
    public bool? Weekly { get; init; }

    /// <summary>The goals rolled per guild and week (from Monday at Bounties:ResetUtc), one per pool while there are pools left.</summary>
    public int GoalsPerWeek { get; init; } = 3;

    /// <summary>A goal's target = the entry's per-member Target × (active members)^TargetExponent, two significant digits.</summary>
    public double TargetExponent { get; init; } = 0.8;

    /// <summary>A member seen within this many days (or in game) is active for the targets; at least 1 counts.</summary>
    public int ActiveDays { get; init; } = 7;

    /// <summary>The pools (pve, pvp, gathering, crafting …) with their entries: Kind, Target per active member, MinTier. A file's map replaces this one whole.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<GuildGoalEntry>> Pools { get; init; } = DefaultPools;

    /// <summary>A finished goal's silver into the guild account (whole silver × the goal's tier / 4).</summary>
    public long GoalSilver { get; init; } = 100_000;

    /// <summary>A finished goal's guild season points (× SeasonPointsFactor; booked under ChallengeLevelUp, the "Guild Challenge" row).</summary>
    public long GoalSeasonPoints { get; init; } = 150;

    /// <summary>A finished goal's guild experience (Guilds:Levels and their perks).</summary>
    public long GoalXp { get; init; } = 5_000;

    /// <summary>Per goal finished last week, this week's blessing step for every member: + PvE fame, + mob silver, + gathering and fishing yield.</summary>
    public double BlessingFame { get; init; } = 0.05;

    public double BlessingSilver { get; init; } = 0.05;

    public double BlessingYield { get; init; } = 0.03;

    /// <summary>The most blessing steps (a week with more goals finished gives no more).</summary>
    public int MaxBlessing { get; init; } = 3;

    /// <summary>Whether a credited PvP kill of a player-bot counts for pvpkills (the PvP goals' rules decide the credit).</summary>
    public bool CountBotKills { get; init; } = true;

    /// <summary>A system line to the members at every quarter of a goal (the completion is always told).</summary>
    public bool AnnounceProgress { get; init; } = true;

    /// <summary>
    /// The guild season points of a boss kill (× SeasonPointsFactor, activity KillMob, the client's "+N Season Points" and chat line): by the
    /// mob's kind (worldboss: a world boss; boss, miniboss: the template's category), split over the guilds credited.
    /// </summary>
    public IReadOnlyDictionary<string, long> BossSeasonPoints { get; init; } = new Dictionary<string, long>(StringComparer.Ordinal) { ["worldboss"] = 20, ["boss"] = 5, ["miniboss"] = 2 };

    /// <summary>
    /// Preset (official 1, pve 10): guild might per might the client data gives (PvE kill fame × 0.0016, a third of it in a solo random
    /// dungeon; gathering and fishing fame × 0.017; a corrupted dungeon's objective might; nothing in an orange-warning zone). 0 = no might.
    /// </summary>
    public double? MightFactor { get; init; }

    /// <summary>
    /// At the season's end a member gets the bracket's items with a contribution above 0 of at least this share of the members' average, in
    /// challenge points or in guild might (the client's minimumparticipationpercentage 51).
    /// </summary>
    public double MinParticipation { get; init; } = 0.51;

    /// <summary>The season-end silver into the guild account by the guild's bracket (iron … crystal, lowercase; whole silver; a bracket left out pays none).</summary>
    public IReadOnlyDictionary<string, long> BracketSilver { get; init; } = new Dictionary<string, long>(StringComparer.Ordinal)
    {
        ["iron"] = 500_000, ["bronze"] = 1_000_000, ["silver"] = 2_000_000, ["gold"] = 4_000_000, ["crystal"] = 8_000_000,
    };

    /// <summary>
    /// The season-end items of every participating member by the guild's bracket (lowercase): uniquename → count (the journal's bracket
    /// achievements' rewards as the model; an item the item table does not know is skipped with a warning at the start).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> BracketItems { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal)
    {
        ["iron"] = new Dictionary<string, int> { ["T4_SHARD_CRYSTAL"] = 10 },
        ["bronze"] = new Dictionary<string, int> { ["T4_SHARD_CRYSTAL"] = 20 },
        ["silver"] = new Dictionary<string, int> { ["T5_SHARD_CRYSTAL"] = 20 },
        ["gold"] = new Dictionary<string, int> { ["T5_SHARD_CRYSTAL"] = 30 },
        ["crystal"] = new Dictionary<string, int> { ["T6_SHARD_CRYSTAL"] = 50 },
    };

    /// <summary>The built-in pools (SPEC §2.3).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<GuildGoalEntry>> DefaultPools = new Dictionary<string, IReadOnlyList<GuildGoalEntry>>(StringComparer.Ordinal)
    {
        ["pve"] = [new() { Kind = "killmobs", Target = 300, MinTier = 4 }, new() { Kind = "bosses", Target = 3 }, new() { Kind = "dungeons", Target = 2 }],
        ["pvp"] = [new() { Kind = "pvpkills", Target = 5 }, new() { Kind = "pvpfame", Target = 20_000 }],
        ["gathering"] = [new() { Kind = "gather", Target = 1500, MinTier = 4 }, new() { Kind = "fish", Target = 30 }],
        ["crafting"] = [new() { Kind = "craftfame", Target = 50_000 }],
    };

    internal void Check()
    {
        if (Region is not ("" or "europe" or "asia")) throw new InvalidDataException($"Guild:Region: '{Region}' is not \"\", europe or asia");
        if (LocalSeasonDays is < 1 or > 366) throw new InvalidDataException($"Guild:LocalSeasonDays: {LocalSeasonDays} is not 1..366");
        foreach (var (name, v) in new[] { ("ChallengeFactor", ChallengeFactor), ("CraftChallengeFactor", (double?)CraftChallengeFactor), ("SeasonPointsFactor", SeasonPointsFactor),
                     ("TargetExponent", (double?)TargetExponent), ("BlessingFame", BlessingFame), ("BlessingSilver", BlessingSilver), ("BlessingYield", BlessingYield),
                     ("MightFactor", MightFactor) })
            if (v is < 0 or > 1000 || v is double d && double.IsNaN(d)) throw new InvalidDataException($"Guild:{name}: {v} is not 0..1000");
        if (GoalsPerWeek is < 0 or > 10 || ActiveDays is < 1 or > 365 || MaxBlessing is < 0 or > 100)
            throw new InvalidDataException("Guild:GoalsPerWeek 0..10, ActiveDays 1..365, MaxBlessing 0..100");
        if (GoalSilver < 0 || GoalSeasonPoints < 0 || GoalXp < 0) throw new InvalidDataException("Guild:GoalSilver, GoalSeasonPoints and GoalXp >= 0");
        if (MinParticipation is < 0 or > 1 || double.IsNaN(MinParticipation)) throw new InvalidDataException($"Guild:MinParticipation: {MinParticipation} is not 0..1");
        foreach (var (kind, points) in BossSeasonPoints)
            if (kind is not ("worldboss" or "boss" or "miniboss") || points is < 0 or > 1_000_000)
                throw new InvalidDataException($"Guild:BossSeasonPoints {kind}: worldboss / boss / miniboss, 0..1000000");
        foreach (var (bracket, silver) in BracketSilver)
            if (!Data.SeasonBracket.Order.Contains(bracket) || silver is < 0 or > 10_000_000_000) // exact: the client data's lowercase names
                throw new InvalidDataException($"Guild:BracketSilver {bracket}: {string.Join(" / ", Data.SeasonBracket.Order)} (lowercase), 0..1e10 silver");
        foreach (var (bracket, items) in BracketItems)
            if (!Data.SeasonBracket.Order.Contains(bracket) || items.Any(i => i.Key.Length == 0 || i.Value is < 1 or > 10_000))
                throw new InvalidDataException($"Guild:BracketItems {bracket}: {string.Join(" / ", Data.SeasonBracket.Order)} (lowercase), items of 1..10000");
        foreach (var (pool, entries) in Pools)
            foreach (var e in entries)
            {
                if (!Kinds.Contains(e.Kind)) throw new InvalidDataException($"Guild:Pools {pool}: Kind {e.Kind} is not {string.Join(" / ", Kinds)}");
                if (e.Target is <= 0 or > 1e9 || double.IsNaN(e.Target) || e.MinTier is < 0 or > 8)
                    throw new InvalidDataException($"Guild:Pools {pool} {e.Kind}: Target above 0 (at most 1e9), MinTier 0..8");
            }
    }
}

/// <summary>
/// One entry of a guild goal pool: <see cref="Kind"/> (killmobs: mobs of the goal's tier or more, each member's kill or share counted;
/// bosses: a boss, miniboss or world boss, once per guild; dungeons: a random dungeon cleared (a boss or miniboss, once per guild and run,
/// whatever its floors), an expedition completed, a world event won or a corrupted dungeon finished; pvpkills: a credited PvP kill or
/// knockdown (no assist, no duel) in a zone with PvP points; pvpfame: credited PvP kill fame with pvpkills' filter (never rolled when no
/// PvP fame can be credited: Pvp:LethalDeath false and Pvp:KnockdownFameFactor 0, the pve preset); gather: resources of the goal's tier or more; fish: catches; craftfame:
/// crafting fame), <see cref="Target"/> per active member (the fame kinds in whole fame), <see cref="MinTier"/> (0 = any; killmobs and
/// gather take the guild's tier when it is higher).
/// </summary>
public sealed record GuildGoalEntry
{
    public string Kind { get; init; } = "killmobs";
    public double Target { get; init; } = 1;
    public int MinTier { get; init; }
}
