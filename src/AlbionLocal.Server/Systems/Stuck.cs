using System.Numerics;
using AlbionLocal.Photon;
using AlbionLocal.Server.AI;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Systems;

/// <summary>
/// StageN qol (analysis/work/stageN/qol/SPEC.md §3): /stuck. The client asks "This will make you unable to move or attack, while still
/// being attackable. After {0} you will be moved to a safe location" and then sends Stuck (op 169, abk, no fields, no response class); it
/// holds its character in the Stuck action state (cpx ActionStates 21) meanwhile. After gamedata &lt;StuckCommand delayseconds&gt; (180 s) the
/// server moves the player to the arrival point of the nearest zone entrance of its map ("Teleports you to a nearby zone entrance",
/// @CHAT_DESCRIPTION_STUCK), made walkable on the map's walk grid; without an exit to the nearest walkable cell, and failing that it stays.
/// Then StuckCancel (event 466 {0 id}) ends the client's state. Refused (StuckCancel at once and a line in the chat): in combat, knocked
/// down, or within &lt;StuckCommand cooldownseconds&gt; (1800 s, "@STUCKCOMMAND_ERROR_COOLDOWN"; counted from the move). A knockdown meanwhile
/// drops it; a cluster change makes a new player object, which has none. Touched under World.Sync.
/// </summary>
public static class Stuck
{
    /// <summary>How far around the player and an exit's arrival point the walk grid is searched for a free cell.</summary>
    public const float SearchRadius = 40;

    /// <summary>Op 169: starts the wait, or refuses (the reason, also told to the player). Returns null when accepted.</summary>
    public static string? Start(World world, Player p)
    {
        var now = world.Now;
        string? why = p.Map is null ? "not on a map"
            : p.Combat.Down is not null ? "knocked down"
            : p.Combat.InCombat ? "in combat"
            : p.StuckDue is not null ? "already waiting"
            : world.StuckCooldowns.TryGetValue(p.Character.Id, out var until) && until > now
                ? $"used again in {Math.Ceiling((until - now).TotalMinutes):0} min" : null;
        if (why is not null)
        {
            if (why != "already waiting")
            {
                p.Session.Send(new StuckCancelEvent { ddn = p.Id });
                p.Session.Send(new SystemMessageEvent { d0p = $"/stuck refused: {why}.", d0q = 1 });
            }
            return why;
        }
        var map = p.Map!;
        var due = now + TimeSpan.FromSeconds(world.GameData?.StuckDelaySeconds ?? 180);
        p.StuckDue = due;
        map.Timers.At(due, () => Finish(world, p, map, due));
        return null;
    }

    /// <summary>The wait is over: the move (<see cref="Target"/>), the cooldown and StuckCancel; nothing when it was dropped meanwhile.</summary>
    private static void Finish(World world, Player p, Map map, TimeSpan due)
    {
        if (p.StuckDue != due) return;
        p.StuckDue = null;
        if (p.Map != map || p.Combat.Down is not null)
        {
            if (p.Map is not null) p.Session.Send(new StuckCancelEvent { ddn = p.Id });
            return;
        }
        world.StuckCooldowns[p.Character.Id] = world.Now + TimeSpan.FromSeconds(world.GameData?.StuckCooldownSeconds ?? 1800);
        var here = p.PositionAt(DateTime.UtcNow);
        var to = Target(world, map, here);
        if (to is { } spot) p.Session.Relocate(spot, p.Facing);
        p.Session.Send(new StuckCancelEvent { ddn = p.Id });
        Log.Info("stuck", $"'{p.Character.Name}' unstuck in {map.Cluster}: {here} -> {(to is { } t ? t.ToString() : "stays")}");
    }

    /// <summary>Where a stuck player at <paramref name="here"/> goes: the nearest exit's arrival, else the nearest walkable cell; null = nowhere better.</summary>
    public static Vector2? Target(World world, Map map, Vector2 here)
    {
        var grid = MobNav.Grid(world, map);
        Vector2? Walkable(Vector2 at) => grid is null ? at : grid.NearestFree(at, SearchRadius);
        if (map.Template is { } template)
            foreach (var spot in template.Exits.Select(e => e.SpawnPosition ?? e.Position).OrderBy(s => Vector2.DistanceSquared(s, here)))
                if (Walkable(spot) is { } to) return to;
        return grid?.NearestFree(here, SearchRadius);
    }
}
