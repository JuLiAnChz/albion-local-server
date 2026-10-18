using System.Globalization;
using System.Text.Json;
using AlbionLocal.Photon;

namespace AlbionLocal.Server.Content;

/// <summary>How often a character gets an expedition category's bonus rewards (stageH expeditions SPEC §5.1, §12.4).</summary>
public enum ExpeditionBonus
{
    /// <summary>Every completion within the bonus time.</summary>
    Off,

    /// <summary>Once per day (the gamedata reset time) per expedition: the PvE default.</summary>
    Expedition,

    /// <summary>Once per day per character, as the official game.</summary>
    Character,
}

/// <summary>
/// The expeditions (analysis/work/stageH/expeditions/SPEC.md §8, §12; PVE-DESIGN.md §2.G, §3 steps 8 and 9): content/expeditions.json, as
/// content/journals.json. The times are gamedata's &lt;Expedition&gt; values (enteringduration 10, startrequesttimeout 60,
/// useexittimeinsec 5, invitationrequesttimeout 30, penaltyduration 180); the rest are [design] tunables. A key the file leaves out
/// keeps the built-in value (<see cref="Default"/>, the shipped file's), a missing file or one that does not parse gives the built-in
/// values with a log line. Immutable.
/// </summary>
public sealed record ExpeditionSettings
{
    public static ExpeditionSettings Default { get; } = new();

    /// <summary>Whether ExpeditionRegister is accepted at all (off: every registration is refused with 15).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The distance to the agent within which ExpeditionRegister is accepted (m; else a62 2, SPEC §3.2 step 1).</summary>
    public float RegisterRange { get; init; } = 8;

    /// <summary>The distance to the agent within which EnteringExpeditionStart (278) is accepted (m, SPEC §2.5).</summary>
    public float EnterRange { get; init; } = 8;

    /// <summary>The Join channel (gamedata enteringduration, captured 10.0 s): 278 to the push into the instance.</summary>
    public float EnteringSeconds { get; init; } = 10;

    /// <summary>Ready without a Join click for this long (gamedata startrequesttimeout): the registration ends with a62 19.</summary>
    public float ReadyTimeoutSeconds { get; init; } = 60;

    /// <summary>The channel of an expedition exit (gamedata useexittimeinsec): ExitEnterStart (237) to the push out.</summary>
    public float ExitUseSeconds { get; init; } = 5;

    /// <summary>The distance to an exit within which ExitEnterStart is accepted (m, SPEC §2.10: 5 + 2).</summary>
    public float ExitRange { get; init; } = 7;

    /// <summary>A channel (Join or exit) is cancelled when the player moves farther than this from where it started (m).</summary>
    public float CancelDistance { get; init; } = 1;

    /// <summary>An instance nobody is in (and nobody is on the way into) closes after this many seconds.</summary>
    public float EmptyCloseSeconds { get; init; } = 60;

    /// <summary>A completed instance closes this many seconds after the completion; the players still inside are sent out.</summary>
    public float FinishedLingerSeconds { get; init; } = 600;

    /// <summary>
    /// Whether the server checks the category's minitempoweraverage itself (a62 7). The client checks it with the real item power before it
    /// sends; the server's figure is the plain itempower average of the gear (no quality or mastery bonus), so it is off by default.
    /// </summary>
    public bool CheckItemPower { get; init; }

    /// <summary>
    /// Whether a Veteran or Hardcore category may be run by one player alone (PvE, SPEC §12.1: group content for 1-5 players, the mobs
    /// scaled by <see cref="GroupFactor"/>). Off: a group registration that ends with one player (no party member accepted, nobody else
    /// queued) ends with a62 16 (INVALID_PARTY_COMPOSITION).
    /// </summary>
    public bool SoloGroupCategories { get; init; } = true;

    /// <summary>The party members a leader's group registration covers: on the agent's map within this distance of it (m, SPEC §3.3: 30).</summary>
    public float InviteRange { get; init; } = 30;

    /// <summary>
    /// Whether the party members a leader's group registration covers are registered at once (263 kind 1, no 262 invitation; the group is
    /// ready when the leader's registration is). The client's Join button of the 262 invitation window may refuse a party smaller than the
    /// category's group size (TOO_FEW, SPEC §3.3: a chosen expedition needs exactly the group size) before it sends JoinExpedition 273, so
    /// the invitation path is kept behind this setting until a client check shows 273 comes for a party of 2. Off: 262, 273/274.
    /// </summary>
    public bool PartyAutoAccept { get; init; } = true;

    /// <summary>
    /// Whether a party member may go into the leader's hardcore level above its own open one (a "carry"; completing it opens the next
    /// level for that member too). Off: such a member is not covered ("level locked").
    /// </summary>
    public bool HardcorePartyCarry { get; init; } = true;

    /// <summary>An invited party member who neither accepts (273) nor declines (274) in this time declines (a62 20; gamedata invitationrequesttimeout).</summary>
    public float InvitationTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// A group category registered without a party (or by a member, "really solo"): it waits this long for others who register the same
    /// category (and level), up to 5, who then go in together (SPEC §3.3 [design]: replaces the official role matchmaking).
    /// </summary>
    public float GroupQueueSeconds { get; init; } = 5;

    /// <summary>
    /// Group scaling of a Veteran or Hardcore instance (SPEC §12.1): its mobs' max health (and, with <see cref="ScaleDamage"/>, their
    /// damage) × min(ScalingMax, ScalingBase + ScalingPerPlayer × players), the players being those registered together when the first
    /// one goes in (1: 0.55, 2: 0.70, 3: 0.85, 4+: 1.0 with the defaults). Off: the official 5-player mobs. Standard is never scaled.
    /// </summary>
    public bool GroupScaling { get; init; } = true;

    public double ScalingBase { get; init; } = 0.40;
    public double ScalingPerPlayer { get; init; } = 0.15;
    public double ScalingMax { get; init; } = 1.0;
    public bool ScaleDamage { get; init; } = true;

    /// <summary>The factor of <see cref="GroupScaling"/> for <paramref name="players"/> (1 when off).</summary>
    public double GroupFactor(int players) => GroupScaling ? Math.Max(0.01, Math.Min(ScalingMax, ScalingBase + ScalingPerPlayer * Math.Max(1, players))) : 1;

    /// <summary>
    /// The hardcore ladder (SPEC §12.2, the PvE endgame): a character may register a hardcore level (ExpeditionRegister key 3; 0 = its
    /// highest open one) up to the highest it has opened, level 1 always; completing level n opens n + 1 (with
    /// <see cref="HardcoreUnlockInBonusTime"/> only within the bonus time). Off: every level is open, and key 3 = 0 takes the character's
    /// recorded level (1 when none). Refused: a62 21 (NO_TOKEN).
    /// </summary>
    public bool HardcoreLadder { get; init; } = true;

    public bool HardcoreUnlockInBonusTime { get; init; } = true;

    /// <summary>
    /// The hardcore tokens (SPEC §3.1, §12.2): a completed level gives a roll of its tokenlootlist (QUESTITEM_EXP_TOKEN_D&lt;n+1&gt;_&lt;expedition&gt;),
    /// and a token in the inventory opens its level of its expedition beyond the ladder (consumed when its holder goes in).
    /// </summary>
    public bool HardcoreTokens { get; init; } = true;

    /// <summary>
    /// The leave penalty (SPEC §7, §12.5): leaving an instance through an exit or the escape spell before the completion keeps the
    /// character from registering for <see cref="PenaltySeconds"/> (a62 8). Off by default (PvE).
    /// </summary>
    public bool LeavePenalty { get; init; }

    /// <summary>The leave penalty's time (gamedata penaltyduration).</summary>
    public float PenaltySeconds { get; init; } = 180;

    /// <summary>How often the bonus rewards are given (SPEC §12.4): Off (every run in the bonus time), Expedition (daily per expedition), Character (daily).</summary>
    public ExpeditionBonus DailyBonus { get; init; } = ExpeditionBonus.Expedition;

    /// <summary>The daily reset of the bonus, UTC (gamedata ExpeditionBonusRewardTimer ResetTimer utctime="10:00").</summary>
    public TimeSpan BonusResetUtc { get; init; } = TimeSpan.FromHours(10);

    /// <summary>Items every completion gives on top of the category's list, unless that list has them (SPEC §12.3: the royal-sigil lootbag).</summary>
    public IReadOnlyList<string> EveryRunItems { get; init; } = ["T4_LOOTBAG_EXPEDITION_ROYAL_SIGIL"];

    /// <summary>
    /// A reward item this server cannot create, and the item given in its place (empty: none needed, SPEC §3.1; the lootbag exists). An
    /// item with neither is left out with a log line.
    /// </summary>
    public IReadOnlyDictionary<string, string> Substitutes { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a death inside respawns at the active checkpoint (else the start point) of the same instance (SPEC §6, §12.6).</summary>
    public bool CheckpointRespawn { get; init; } = true;

    /// <summary>The spell cast on the player when the Join channel ends (captured EXPEDITION_LOCK, a VFX for the transfer); "" = none.</summary>
    public string LockSpell { get; init; } = "EXPEDITION_LOCK";

    /// <summary>
    /// Spawn names of expedition clusters that mobs.xml lacks, and the mob each stands for (SPEC §4.3, §8): the built-in table
    /// (<see cref="Data.ExpeditionData.DefaultSpawnAliases"/>) with the file's entries over it; a "" value drops a built-in alias.
    /// </summary>
    public IReadOnlyDictionary<string, string> SpawnAliases { get; init; } = Data.ExpeditionData.DefaultSpawnAliases;

    /// <summary>Where a character whose instance is gone goes when it has no saved return point.</summary>
    public string FallbackCluster { get; init; } = "0000";

    public static ExpeditionSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("content", $"{path} missing: built-in expedition settings");
            return Default;
        }
        try
        {
            var s = Parse(File.ReadAllText(path), path);
            Log.Info("content", $"expeditions: {(s.Enabled ? "on" : "off")}, daily bonus {s.DailyBonus}, checkpoint respawn {(s.CheckpointRespawn ? "on" : "off")}, "
                + $"group scaling {(s.GroupScaling ? "on" : "off")}, hardcore ladder {(s.HardcoreLadder ? "on" : "off")}, leave penalty {(s.LeavePenalty ? "on" : "off")}");
            return s;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            Log.Error("content", $"{path}: {e.Message}; built-in expedition settings used");
            return Default;
        }
    }

    public static ExpeditionSettings Parse(string text, string path)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("not a JSON object");
        var s = Default;
        float Num(JsonElement v, string key, double min, double max) =>
            v.TryGetDouble(out var d) && d >= min && d <= max ? (float)d : throw new InvalidDataException($"{key}: {v} is not {min}..{max}");
        double Dbl(JsonElement v, string key, double min, double max) =>
            v.TryGetDouble(out var d) && d >= min && d <= max ? d : throw new InvalidDataException($"{key}: {v} is not {min}..{max}");
        bool Bool(JsonElement v, string key) =>
            v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : throw new InvalidDataException($"{key}: not true or false");
        string Text(JsonElement v, string key) => v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new InvalidDataException($"{key}: not a string");
        foreach (var p in root.EnumerateObject())
        {
            var v = p.Value;
            switch (p.Name)
            {
                case "Enabled": s = s with { Enabled = Bool(v, p.Name) }; break;
                case "RegisterRange": s = s with { RegisterRange = Num(v, p.Name, 0, 1000) }; break;
                case "EnterRange": s = s with { EnterRange = Num(v, p.Name, 0, 1000) }; break;
                case "EnteringSeconds": s = s with { EnteringSeconds = Num(v, p.Name, 0, 600) }; break;
                case "ReadyTimeoutSeconds": s = s with { ReadyTimeoutSeconds = Num(v, p.Name, 1, 3600) }; break;
                case "ExitUseSeconds": s = s with { ExitUseSeconds = Num(v, p.Name, 0, 600) }; break;
                case "ExitRange": s = s with { ExitRange = Num(v, p.Name, 0, 1000) }; break;
                case "CancelDistance": s = s with { CancelDistance = Num(v, p.Name, 0, 1000) }; break;
                case "EmptyCloseSeconds": s = s with { EmptyCloseSeconds = Num(v, p.Name, 1, 86400) }; break;
                case "FinishedLingerSeconds": s = s with { FinishedLingerSeconds = Num(v, p.Name, 1, 86400) }; break;
                case "CheckItemPower": s = s with { CheckItemPower = Bool(v, p.Name) }; break;
                case "SoloGroupCategories": s = s with { SoloGroupCategories = Bool(v, p.Name) }; break;
                case "InviteRange": s = s with { InviteRange = Num(v, p.Name, 0, 1000) }; break;
                case "PartyAutoAccept": s = s with { PartyAutoAccept = Bool(v, p.Name) }; break;
                case "HardcorePartyCarry": s = s with { HardcorePartyCarry = Bool(v, p.Name) }; break;
                case "InvitationTimeoutSeconds": s = s with { InvitationTimeoutSeconds = Num(v, p.Name, 1, 3600) }; break;
                case "GroupQueueSeconds": s = s with { GroupQueueSeconds = Num(v, p.Name, 0, 600) }; break;
                case "GroupScaling": s = s with { GroupScaling = Bool(v, p.Name) }; break;
                case "ScalingBase": s = s with { ScalingBase = Dbl(v, p.Name, 0, 100) }; break;
                case "ScalingPerPlayer": s = s with { ScalingPerPlayer = Dbl(v, p.Name, 0, 100) }; break;
                case "ScalingMax": s = s with { ScalingMax = Dbl(v, p.Name, 0.01, 100) }; break;
                case "ScaleDamage": s = s with { ScaleDamage = Bool(v, p.Name) }; break;
                case "HardcoreLadder": s = s with { HardcoreLadder = Bool(v, p.Name) }; break;
                case "HardcoreUnlockInBonusTime": s = s with { HardcoreUnlockInBonusTime = Bool(v, p.Name) }; break;
                case "HardcoreTokens": s = s with { HardcoreTokens = Bool(v, p.Name) }; break;
                case "LeavePenalty": s = s with { LeavePenalty = Bool(v, p.Name) }; break;
                case "PenaltySeconds": s = s with { PenaltySeconds = Num(v, p.Name, 0, 86400) }; break;
                case "DailyBonus":
                    s = s with
                    {
                        DailyBonus = Enum.TryParse<ExpeditionBonus>(Text(v, p.Name), ignoreCase: true, out var b) && Enum.IsDefined(b) ? b
                            : throw new InvalidDataException($"{p.Name}: '{v}' is not Off, Expedition or Character"),
                    };
                    break;
                case "BonusResetUtc":
                    s = s with
                    {
                        BonusResetUtc = TimeSpan.TryParseExact(Text(v, p.Name), @"hh\:mm", CultureInfo.InvariantCulture, out var t) ? t
                            : throw new InvalidDataException($"{p.Name}: '{v}' is not hh:mm"),
                    };
                    break;
                case "EveryRunItems":
                    s = s with
                    {
                        EveryRunItems = v.ValueKind == JsonValueKind.Array ? [.. v.EnumerateArray().Select(x => Text(x, p.Name)).Where(x => x.Length > 0)]
                            : throw new InvalidDataException($"{p.Name}: not a list of item names"),
                    };
                    break;
                case "Substitutes":
                    s = s with
                    {
                        Substitutes = v.ValueKind == JsonValueKind.Object
                            ? v.EnumerateObject().ToDictionary(x => x.Name, x => Text(x.Value, p.Name), StringComparer.OrdinalIgnoreCase)
                            : throw new InvalidDataException($"{p.Name}: not an object of item names"),
                    };
                    break;
                case "SpawnAliases":
                    if (v.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{p.Name}: not an object of spawn name -> mob");
                    var aliases = new Dictionary<string, string>(Data.ExpeditionData.DefaultSpawnAliases, StringComparer.OrdinalIgnoreCase);
                    foreach (var x in v.EnumerateObject())
                        if (Text(x.Value, p.Name) is { Length: > 0 } mob) aliases[x.Name] = mob;
                        else aliases.Remove(x.Name);
                    s = s with { SpawnAliases = aliases };
                    break;
                case "CheckpointRespawn": s = s with { CheckpointRespawn = Bool(v, p.Name) }; break;
                case "LockSpell": s = s with { LockSpell = Text(v, p.Name) }; break;
                case "FallbackCluster": s = s with { FallbackCluster = Text(v, p.Name) is { Length: > 0 } c ? c : throw new InvalidDataException($"{p.Name}: empty") }; break;
                default: Log.Warn("content", $"{path}: unknown key {p.Name}, skipped"); break;
            }
        }
        return s;
    }
}
