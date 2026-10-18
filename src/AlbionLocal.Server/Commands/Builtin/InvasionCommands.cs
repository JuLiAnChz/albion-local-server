using System.Globalization;
using System.Numerics;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>
/// Stage I (invasions SPEC §2.1): the invasions players start, for every account level: .invasion (status), .invasion &lt;1-10&gt; [kind],
/// .invasion tier &lt;n&gt;, .invasion stop, .invasion tiers (<see cref="PlayerInvasions"/>).
/// </summary>
public static class InvasionCommands
{
    /// <summary>
    /// .invasion: the player's chosen tier, its cooldown, today's starts and the event on its map; .invasion &lt;tier&gt; [kind]: starts a
    /// T1-T10 invasion (kind invasion by default, or champion) at a mob spawn point 15-45 m from the player.
    /// </summary>
    [Command("invasion", Level = AccountLevel.Player, Help = "your invasion status; '.invasion <1-10> [invasion|champion]' calls one of that tier near you")]
    public static void Invasion(CommandContext ctx, string tier = "", string kind = "")
    {
        var player = ctx.RequirePlayer();
        var world = ctx.World;
        var invasions = world.Invasions;
        if (tier.Length == 0)
        {
            var left = invasions.CooldownLeft(player);
            var here = world.Events.Running.FirstOrDefault(r => r.Map == player.Map);
            var limit = world.EventSettings.Player.DailyLimit;
            ctx.Reply(string.Create(CultureInfo.InvariantCulture,
                $"invasions: ticket tier T{invasions.Chosen(player)}, {(left > TimeSpan.Zero ? $"cooldown {Math.Ceiling(left.TotalMinutes):0} min" : "ready")}, {invasions.Today(player)}{(limit > 0 ? $" of {limit}" : "")} today; {(here is null ? "no event on this map" : $"#{here.Id} {here.Title} on this map")}"));
            return;
        }
        if (!int.TryParse(tier, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t)) throw new CommandError(".invasion <1-10> [invasion|champion], .invasion tier <n>, .invasion stop, .invasion tiers");
        var settings = world.EventSettings;
        var type = (kind.Length == 0 ? settings.Find("invasion") ?? settings.Events.FirstOrDefault() : settings.Find(kind))
            ?? throw new CommandError($"no invasion kind '{kind}' (kinds: {string.Join(", ", settings.Events.Select(e => e.Name))})");
        try
        {
            var run = invasions.Start(player, t, type, item: false);
            var at = player.PositionAt(DateTime.UtcNow);
            ctx.Reply(string.Create(CultureInfo.InvariantCulture,
                $"{run.Title}: {Vector2.Distance(at, run.Point):0} m {WorldEvents.Compass(run.Point - at)}, wave 1 of {run.Waves.Count}: {run.Mobs.Count} mob(s)"));
        }
        catch (InvalidOperationException e) { throw new CommandError(e.Message); }
    }

    /// <summary>.invasion tier &lt;n&gt;: the tier the player's next invasion ticket starts (kept until a restart).</summary>
    [Command("invasion tier", Level = AccountLevel.Player, Help = "sets the tier (1-10) your next invasion ticket starts")]
    public static void Tier(CommandContext ctx, int tier)
    {
        var player = ctx.RequirePlayer();
        var s = ctx.World.EventSettings.Player;
        if (tier < s.MinTier || tier > s.MaxTier) throw new CommandError($"tier must be {s.MinTier}-{s.MaxTier}");
        ctx.World.Invasions.Choose(player, tier);
        var cost = ctx.World.Invasions.Cost(new Content.InvasionItem("", ""), tier);
        ctx.Reply($"your next invasion ticket starts T{tier} ({cost} ticket(s) a use)");
    }

    /// <summary>.invasion stop: the invasion the player started ends now: its mobs go, no reward, the cooldown stays.</summary>
    [Command("invasion stop", Level = AccountLevel.Player, Help = "calls off the invasion you started (no reward; the cooldown stays)")]
    public static void Stop(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        var run = ctx.World.Invasions.Led(player) ?? throw new CommandError("you lead no invasion");
        ctx.World.Events.Stop(run, $"'{player.Character.Name}'");
        ctx.Reply($"{run.Title} called off");
    }

    /// <summary>.invasion tiers: the T1-T10 table.</summary>
    [Command("invasion tiers", Level = AccountLevel.Player, Help = "the invasion tiers T1-T10: mobs, waves, chest, silver and fame")]
    public static void Tiers(CommandContext ctx) => ctx.Reply("invasion tiers:\n" + string.Join("\n", ctx.World.Invasions.DescribeTiers()));
}
