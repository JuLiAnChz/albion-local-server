using System.Globalization;
using AlbionLocal.Photon;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Systems;

/// <summary>
/// A player's reputation (stageE pvp SPEC §3.4, analysis/work/stageE/pvp/SPEC.md; gamedata &lt;Reputation&gt;,
/// <see cref="ReputationSettings"/>): points on the character (saved), clamped to min/maxreputation, whose level is the first
/// &lt;level&gt; top to bottom with requiredpoints not above them (Join 41/42/44, NewCharacter 37/38/39). Every change goes to the
/// player as ReputationUpdate (304) {0 me, 1 now, 2 the signed change (negative for a loss: the client flips the sign of reasons
/// 3-8 before it prints, §6 V6), 3 the new total, 4 the old level, 5 the new one, 6 the reason, 7 the mob or harvestable type
/// (reasons 1/2), 8 the supported player (reasons 4/7/8), 9 the victim (reasons 3-8)}. Gains [infer, SPEC §3.4]: a mob kill gives
/// pvegainfactor × fame (reason 1), a harvest gathergainfactor × fame (reason 2), of the player's level, flagged players too while
/// gamedata's flaggedplayersgainreputationfromgatheringandpve; the fame is the one awarded (Rates included). Losses come from the
/// crimes of <see cref="Pvp"/>. A negative level recovers by its recoveryperday, worked out at each Join and before each change
/// (<see cref="Recover"/>, silently). Not modelled (TODO): the support penalties 4/7/8 (no player heals or buffs another yet), the
/// crime protection buffs of the positive levels, gamedata flagginglosesreputation (false in 345319). Touched only under <see cref="World.Sync"/>.
/// </summary>
public static class Reputation
{
    /// <summary>ReputationUpdate key 6 (ctp.ahi 0x18099E4E0): 1 mob kill, 2 gathering, 3 LOST_ATTACK, 5 LOST_KNOCKDOWN, 6 LOST_KILL.</summary>
    public const byte MobKill = 1, Gathering = 2, LostAttack = 3, LostSupport = 4, LostKnockdown = 5, LostKill = 6;

    public static ReputationSettings Settings(World world) => world.GameData?.Reputation ?? ReputationSettings.Default;

    /// <summary>The level byte of <paramref name="p"/>'s points (4 NEUTRAL at 0).</summary>
    public static byte Level(Player p) => Settings(p.World).LevelOf(p.Character.Reputation);

    /// <summary>The &lt;level&gt; <paramref name="p"/> is at.</summary>
    public static ReputationLevel LevelData(Player p)
    {
        var s = Settings(p.World);
        return s.Levels[Math.Min(Level(p), s.Levels.Count - 1)];
    }

    /// <summary>
    /// The daily recovery of a negative level up to <paramref name="utc"/> [infer: the rate of the level the points are in, up to
    /// the next level up, whose own rate then counts; NEUTRAL_NEGATIVE ([-1000, 0)) recovers nothing, so the points stop at -1000]. The first
    /// call only starts the clock. No event: the next Join carries the points.
    /// </summary>
    public static void Recover(Player p, DateTime utc)
    {
        var c = p.Character;
        if (c.ReputationAt == 0 || utc.Ticks < c.ReputationAt)
        {
            c.ReputationAt = utc.Ticks;
            return;
        }
        var s = Settings(p.World);
        var days = (utc.Ticks - c.ReputationAt) / (double)TimeSpan.TicksPerDay;
        c.ReputationAt = utc.Ticks;
        var points = c.Reputation;
        while (days > 0)
        {
            var level = s.LevelOf(points);
            var rate = s.Levels[level].RecoveryPerDay;
            if (rate <= 0) break;
            var upper = level > 0 ? s.Levels[level - 1].RequiredPoints : s.Max;
            var need = (upper - points) / rate;
            if (need > days)
            {
                points += rate * days;
                break;
            }
            points = upper;
            days -= need;
        }
        if (points != c.Reputation)
        {
            Log.Debug("pvp", string.Create(CultureInfo.InvariantCulture, $"'{c.Name}' recovers reputation {c.Reputation:0.##} -> {points:0.##}"));
            c.Reputation = Math.Min(points, s.Max);
            p.World.Saves.MarkChanged(c);
        }
    }

    /// <summary>
    /// Adds <paramref name="delta"/> points (negative = a loss) for <paramref name="reason"/>, clamped, and tells the player
    /// (ReputationUpdate, see the class); nothing when the clamp leaves the points as they are. Saved with the character.
    /// </summary>
    public static void Change(Player p, double delta, byte reason, short type = 0, string? supported = null, string? victim = null)
    {
        if (delta == 0) return;
        var world = p.World;
        var s = Settings(world);
        var c = p.Character;
        Recover(p, DateTime.UtcNow);
        var before = c.Reputation;
        var after = Math.Clamp(before + delta, s.Min, s.Max);
        if (after == before) return;
        var (oldLevel, newLevel) = (s.LevelOf(before), s.LevelOf(after));
        c.Reputation = after;
        world.Saves.MarkChanged(c);
        p.Session.Send(new ReputationUpdateEvent
        {
            ddn = p.Id, dve = world.GameNow, dvf = (float)(after - before), dvg = (float)after, dvh = oldLevel, dvi = newLevel, dvj = reason,
            dvk = type, dvl = supported, dvm = victim,
        });
        Log.Info("pvp", string.Create(CultureInfo.InvariantCulture,
            $"'{c.Name}' reputation {before:0.##} -> {after:0.##} (reason {reason}{(oldLevel != newLevel ? $", level {oldLevel} -> {newLevel}" : "")})"));
    }

    /// <summary>
    /// The PvE gain of <paramref name="fame"/> (plain fame, not a7m) for a mob kill (<see cref="MobKill"/>, <paramref name="type"/>
    /// the mob's index) or a harvest (<see cref="Gathering"/>, the harvestable's type): × the level's pvegainfactor or
    /// gathergainfactor; none for a flagged player when gamedata says so, nor in a cluster whose reputationgainenabled is false.
    /// </summary>
    public static void Gain(Player p, double fame, byte reason, short type)
    {
        var s = Settings(p.World);
        if (fame <= 0 || !s.FlaggedGain && p.Flag != Pvp.Blue || !Pvp.RulesOf(p.World, p.Map).ReputationGainEnabled) return;
        var level = LevelData(p);
        Change(p, fame * (reason == Gathering ? level.GatherGainFactor : level.PveGainFactor), reason, type);
    }
}
