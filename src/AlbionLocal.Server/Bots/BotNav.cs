using System.Numerics;
using AlbionLocal.Server.AI;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;

namespace AlbionLocal.Server.Bots;

/// <summary>
/// Stage I bots (analysis/work/stageI/bots/SPEC.md §1.1, §1.9, bots-world SPEC §2.2): the walks of a bot that are not a brain's plain goal:
/// a path from a spot off the walk grid (a gatherer at its node stands on the ground right by the rock, outside the inflated component: it
/// steps back first), the chase of a target into weapon range and the flight from an attacker. Every leg is a <see cref="WalkGrid.Clear"/>
/// line of the component, or a short straight one that crosses no blocked cell at all (<see cref="WalkGrid.ClearOpen"/>, at most
/// <see cref="ShortStep"/> m): an observer never sees a bot cross a wall.
/// </summary>
public static class BotNav
{
    /// <summary>The longest straight leg off the component (m): the step to a node or to a target that stands by a rock.</summary>
    public const float ShortStep = 8;

    /// <summary>How far a chased target may have moved before the chase is planned anew (m), and how often at most (s).</summary>
    public const float ReplanDistance = 4;

    public const double ReplanSeconds = 1;

    /// <summary>
    /// A path from <paramref name="from"/> to <paramref name="to"/> (to the walkable cell nearest to it within 6 m when it is off the grid):
    /// <see cref="BotManager.FindPath"/>, from a spot off the component first a straight step back onto it (<paramref name="back"/> when
    /// given and clear, else the nearest walkable cell within 6 m); a short open line when the two are near; null when none.
    /// </summary>
    public static IReadOnlyList<Vector2>? PathFrom(BotManager bots, ClusterNav nav, Vector2 from, Vector2 to, Vector2? back = null)
    {
        var grid = nav.Grid;
        if (Vector2.Distance(from, to) <= ShortStep && grid.ClearOpen(from, to)) return [from, to];
        var goal = grid.Walkable(to) ? to : grid.Nearest(to, 6) ?? to;
        if (grid.Walkable(from)) return bots.FindPath(nav, from, goal);
        var step = back is { } b && grid.Walkable(b) && grid.ClearOpen(from, b) ? b : grid.Nearest(from, 6);
        if (step is not { } onto || !grid.ClearOpen(from, onto) || bots.FindPath(nav, onto, goal) is not { } rest) return null;
        return [from, .. rest];
    }

    /// <summary>
    /// <paramref name="bot"/> walks into weapon range of <paramref name="target"/> (SPEC §1.9): towards where the target is in 0.3 s, to
    /// <paramref name="reach"/> m (centre to centre) short of it, straight when the line is clear, else along a path; a new plan only every
    /// <see cref="ReplanSeconds"/> or when the target moved <see cref="ReplanDistance"/> m. False when no way to it was found.
    /// </summary>
    public static bool ChaseTo(BotManager bots, ClusterNav nav, Player bot, Player target, float reach)
    {
        var fight = bot.Bot!.Fight;
        var world = bot.World;
        var utc = DateTime.UtcNow;
        var aim = target.PositionAt(utc.AddSeconds(0.3));
        var ticks = world.GameNow;
        if (bot.Bot.Motion.Busy && ticks - fight.ChasedAt < ReplanSeconds * TimeSpan.TicksPerSecond && Vector2.Distance(aim, fight.ChasedTo) < ReplanDistance) return true;
        var here = bot.PositionAt(bots.Time);
        var away = here - aim;
        var goal = away.LengthSquared() > 0.0001f ? aim + Vector2.Normalize(away) * Math.Min(reach, away.Length()) : aim;
        fight.ChasedTo = aim;
        fight.ChasedAt = ticks;
        var path = nav.Grid.Clear(here, goal) ? [here, goal] : PathFrom(bots, nav, here, goal);
        if (path is null) return false;
        bots.Walk(bot, path, MotionMaster.Facing(goal, aim));
        return true;
    }

    /// <summary>
    /// <paramref name="bot"/> runs off from <paramref name="from"/> (SPEC §1.9, a gatherer under Bots:Gatherer:FleeHealth): of 12 directions,
    /// the walkable point about <paramref name="distance"/> m away with a path that ends farthest from the attacker. False when none.
    /// </summary>
    public static bool FleeFrom(BotManager bots, ClusterNav nav, Player bot, Vector2 from, float distance)
    {
        var here = bot.PositionAt(bots.Time);
        IReadOnlyList<Vector2>? best = null;
        var bestDistance = 0f;
        for (var i = 0; i < 12; i++)
        {
            var angle = i * Math.PI / 6;
            var spot = here + new Vector2((float)Math.Sin(angle), (float)Math.Cos(angle)) * distance;
            if (nav.Grid.Nearest(spot, 4) is not { } onto || Vector2.Distance(onto, from) <= bestDistance) continue;
            if (PathFrom(bots, nav, here, onto) is not { } path) continue;
            (best, bestDistance) = (path, Vector2.Distance(onto, from));
        }
        if (best is null) return false;
        bots.Walk(bot, best, null);
        return true;
    }
}
