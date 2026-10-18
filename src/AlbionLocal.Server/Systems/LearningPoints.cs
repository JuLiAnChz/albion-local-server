using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Systems;

/// <summary>
/// Learning points (analysis/work/stageJ/focus-lp/SPEC.md D6, §2.5, §5.2): <see cref="Game.Character.LearningPoints"/> (a7m) rise by one
/// point per learningpointsecondsperpointpremium (2880 s, LearningPoints:SecondsPerPoint) while premium, the remainder carried in
/// <see cref="Game.Character.LpStamp"/> [choice: official pays at DOWNTIME and resets the stamp; carrying keeps the client's bar
/// continuous, Q2], up to the cap (999999); rewards add to them (<see cref="Grant"/>: awardlearningpoints items, learningpointsreward
/// season rewards). Only auto-learn spends them (<see cref="Game.DestinyBoard.Apply"/>, PROGRESSION.md §4). The client gets them from the
/// Join (keys 37-40) and UpdateLearningPoints (83, <see cref="Event"/>); its bar adds the accrual by itself while key 4 is set.
/// Under <see cref="World.Sync"/>.
/// </summary>
public static class LearningPoints
{
    /// <summary>UpdateLearningPoints key 6 (enum a09.a): 0 silent (auto-learn spending, a premium change), 1 a reward, 2 a payout; the client shows a notification for 1 and 2.</summary>
    public const int Silent = 0, Reward = 1, Payout = 2;

    /// <summary>How often the world tick pays the online players' points (<see cref="Update"/>).</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private const long A7m = 10000;

    private static CharacterDefaults Defaults(World world) => world.GameData?.Defaults ?? CharacterDefaults.Default;

    /// <summary>The cap, a7m (Join key 39, 83 key 3): LearningPoints:Max, else characters.xml learningpointsmax(premium), 999999.</summary>
    public static long Max(Player p) =>
        (long)Math.Round((p.World.Config.LearningPointsMax ?? (Premium.Is(p) ? Defaults(p.World).LearningPointsMaxPremium : Defaults(p.World).LearningPointsMax)) * A7m);

    /// <summary>Seconds per point (Join key 40, 83 key 4 as a7m): while premium LearningPoints:SecondsPerPoint, else learningpointsecondsperpointpremium (2880); without premium learningpointsecondsperpoint (0 = none).</summary>
    public static double SecondsPerPoint(Player p) =>
        Premium.Is(p) ? p.World.Config.LearningPointsSecondsPerPoint ?? Defaults(p.World).SecondsPerPointPremium : Defaults(p.World).SecondsPerPoint;

    /// <summary>
    /// The whole points earned since the stamp up to <paramref name="now"/>: n = floor((now − stamp) / seconds per point), the points
    /// + n (at most the cap), the stamp + n × seconds per point (the rest carries). No accrual (no premium, a stamp of 0): the stamp
    /// becomes now. With <paramref name="send"/> and n &gt; 0 an UpdateLearningPoints with key 6 = 2 (the payout). Returns the points
    /// added (a7m).
    /// </summary>
    public static long Accrue(Player p, long now, bool send)
    {
        var c = p.Character;
        var per = (long)Math.Round(SecondsPerPoint(p) * TimeSpan.TicksPerSecond);
        if (c.LpStamp == 0 || per <= 0)
        {
            c.LpStamp = now; // saved with whatever is saved next: without accrual the stamp means nothing yet
            return 0;
        }
        if (now - c.LpStamp < per) return 0;
        var n = (now - c.LpStamp) / per;
        c.LpStamp += n * per;
        var before = c.LearningPoints;
        c.LearningPoints = Math.Max(before, Math.Min(Max(p), before + n * A7m));
        p.World.Saves.MarkDirty(c);
        var gained = c.LearningPoints - before;
        if (send && gained > 0) p.Session.Send(Event(p, gained, Payout));
        return gained;
    }

    /// <summary><paramref name="amount"/> points (a7m) as a reward (at most the cap), with its UpdateLearningPoints of kind <paramref name="kind"/>.</summary>
    public static void Grant(Player p, long amount, int kind = Reward)
    {
        if (amount <= 0) return;
        Accrue(p, p.World.GameNow, send: true);
        var c = p.Character;
        var before = c.LearningPoints;
        c.LearningPoints = Math.Max(before, Math.Min(Max(p), before + amount));
        p.World.Saves.MarkChanged(c);
        p.Session.Send(Event(p, c.LearningPoints - before, kind));
    }

    /// <summary>
    /// UpdateLearningPoints (83, a09; ctp.x8_8): 0 player (the local player's id, else the client drops it), 1 the points, 2 the stamp
    /// (plain ticks), 3 the cap, 4 seconds per point as a7m (left out without premium), 5 <paramref name="delta"/> (left out at 0),
    /// 6 <paramref name="kind"/> (0 is left out by the serializer; the client reads 0).
    /// </summary>
    public static UpdateLearningPointsEvent Event(Player p, long delta, int kind) => new()
    {
        d39 = p.Id, d4a = p.Character.LearningPoints, d4b = p.Character.LpStamp, d4c = Max(p),
        d4d = (long)Math.Round(SecondsPerPoint(p) * A7m), d4e = delta, d4f = kind,
    };

    /// <summary>The Join keys (after the accrual up to now, without an event): 37 the points, 38 the stamp, 39 the cap, 40 seconds per point (only while premium).</summary>
    public static void JoinKeys(Player p, JoinResponse r, long now)
    {
        Accrue(p, now, send: false);
        r.a9b = p.Character.LearningPoints;                     // [37] a7m → the LP bar (ctp.af_24)
        r.a9c = p.Character.LpStamp;                            // [38] GameTimeStamp
        r.a9d = Max(p);                                         // [39] a7m
        r.a9e = (long)Math.Round(SecondsPerPoint(p) * A7m);     // [40] a7m (0 = left out: no accrual)
    }

    /// <summary>
    /// World timer (<see cref="World.Update"/>), every <see cref="TickInterval"/> from the first tick on: the points of every online player
    /// (<see cref="Accrue"/> with the payout event) and a Free premium end that runs short (<see cref="Premium.Update"/>).
    /// </summary>
    public static void Update(World world, TimeSpan now)
    {
        if (world.LearningTickDue is not { } due)
        {
            world.LearningTickDue = now + TickInterval;
            return;
        }
        if (now < due) return;
        world.LearningTickDue = now + TickInterval;
        var ticks = world.GameNow;
        foreach (var map in world.Maps.Maps)
            foreach (var p in map.Players.Values)
            {
                Accrue(p, ticks, send: true);
                Premium.Update(p, ticks);
            }
    }
}
