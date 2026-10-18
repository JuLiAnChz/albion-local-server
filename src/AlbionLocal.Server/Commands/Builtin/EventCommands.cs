using System.Globalization;
using System.Numerics;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>The world events (PVE-DESIGN.md §2.H, §3 step 7): .event list, .event start, .event stop.</summary>
public static class EventCommands
{
    /// <summary>.event list: the running events (wave, mobs left, time left, damage) and when the schedule starts the next one.</summary>
    [Command("event list", Level = AccountLevel.GameMaster, Help = "the running world events and the next scheduled one")]
    public static void List(CommandContext ctx)
    {
        var lines = ctx.World.Events.Describe().ToList();
        var kinds = string.Join(", ", ctx.World.EventSettings.Events.Select(e => e.Name));
        ctx.Reply(lines.Count == 0 ? $"no world event running (kinds: {kinds})" : $"world events (kinds: {kinds}):\n{string.Join("\n", lines)}");
    }

    /// <summary>
    /// .event start [kind] [tier] [here]: an event of that kind (content/events.json Events; the first by default) on the player's map now, at a
    /// mob spawn point at the kind's Distance from the player, or with "here" 10 m in front of the player. Announced as a scheduled one.
    /// Stage I: with a tier 1-10 the mobs, waves and rewards of that row of Tiers (none: the cluster's, as a scheduled one).
    /// </summary>
    [Command("event start", Level = AccountLevel.GameMaster, Help = "starts a world event (invasion, champion, …; optional tier 1-10) near the player, or 10 m in front with 'here'")]
    public static void Start(CommandContext ctx, string a = "", string b = "", string c = "")
    {
        var player = ctx.RequirePlayer();
        var world = ctx.World;
        if (player.Map is not { Grid: not null } map) throw new CommandError("the player is on no loaded map");
        var (kind, tier, here) = ("", 0, false);
        foreach (var arg in new[] { a, b, c }.Where(x => x.Length > 0))
        {
            if (arg.Equals("here", StringComparison.OrdinalIgnoreCase)) here = true;
            else if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t))
                tier = t is >= 1 and <= 10 ? t : throw new CommandError("tier must be 1-10");
            else if (kind.Length == 0) kind = arg;
            else throw new CommandError(".event start [kind] [tier] [here]");
        }
        var settings = world.EventSettings;
        var type = kind.Length == 0 ? settings.Events.FirstOrDefault() : settings.Find(kind);
        if (type is null) throw new CommandError($"no world event kind '{kind}' (kinds: {string.Join(", ", settings.Events.Select(e => e.Name))})");
        var at = player.PositionAt(DateTime.UtcNow);
        var (dx, dz) = TileMath.Rotate(0, 10, player.Facing);
        var point = here ? at + new Vector2((float)dx, (float)dz) : world.Events.PointNear(map, at, type);
        try
        {
            var run = world.Events.Start(map, type, point, tier: tier);
            ctx.Reply(string.Create(CultureInfo.InvariantCulture,
                $"{run} ({run.Title}): {Vector2.Distance(at, point):0} m {WorldEvents.Compass(point - at)}, wave 1 of {run.Waves.Count}: {run.Mobs.Count} mob(s)"));
        }
        catch (InvalidOperationException e) { throw new CommandError(e.Message); }
    }

    /// <summary>.event stop [id]: the event of that id (by default the one on the player's map) ends now: its mobs go, no reward.</summary>
    [Command("event stop", Level = AccountLevel.GameMaster, Help = "calls off a world event (by id, else the one on the player's map): its mobs go, no reward")]
    public static void Stop(CommandContext ctx, int id = 0)
    {
        var events = ctx.World.Events;
        var run = id > 0 ? events.Running.FirstOrDefault(r => r.Id == id)
            : events.Running.FirstOrDefault(r => r.Map == ctx.RequirePlayer().Map);
        if (run is null) throw new CommandError(id > 0 ? $"no running world event #{id}" : "no world event on this map");
        events.Stop(run);
        ctx.Reply($"{run} stopped");
    }
}
