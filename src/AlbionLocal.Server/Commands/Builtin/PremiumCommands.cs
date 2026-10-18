using System.Globalization;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>Premium, focus and learning points (stageJ focus-lp SPEC): .premium, .focus, .lp.</summary>
public static class PremiumCommands
{
    /// <summary>
    /// The player's premium, focus and learning points; with on / off / mode its premium for this session over Premium:Mode (mode = the
    /// mode's again), sent live (94, 83, PremiumChanged 286).
    /// </summary>
    [Command("premium", Level = AccountLevel.GameMaster, Help = "the player's premium, focus and learning points; on|off switches its premium for the session, mode = Premium:Mode again")]
    public static void Premium(CommandContext ctx, string? state = null)
    {
        var player = ctx.RequirePlayer();
        var now = ctx.World.GameNow;
        if (state is not null)
        {
            bool? on = state.ToLowerInvariant() switch
            {
                "on" => true,
                "off" => false,
                "mode" => null,
                _ => throw new CommandError(".premium [on|off|mode]"),
            };
            if (!Systems.Premium.Set(player, on, now)) ctx.Reply("premium unchanged");
        }
        var c = player.Character;
        ctx.Reply(string.Create(CultureInfo.InvariantCulture,
            $"'{c.Name}': {(Systems.Premium.Is(player) ? "premium" : "no premium")} (Premium:Mode {ctx.World.Config.PremiumMode}{(player.PremiumOverride is { } o ? $", switched {(o ? "on" : "off")}" : "")}); "
            + $"focus {Focus.Current(player, now):0.##} / {Focus.Max(player):0} (+{Focus.RegenPerSecond(player) * 86400:0}/day); "
            + $"learning points {c.LearningPoints / 10000.0:0.####} (1 per {LearningPoints.SecondsPerPoint(player):0} s), use flags {Convert.ToString(c.UseFlags, 2).PadLeft(3, '0')}, "
            + $"{c.AutoLearn?.Ids.Length ?? 0} auto-learn node(s)"));
    }

    /// <summary>Sets the player's crafting focus (whole points, at most 10000000), with its CraftingFocusUpdate.</summary>
    [Command("focus", Level = AccountLevel.GameMaster, Help = "sets the player's crafting focus")]
    public static void SetFocus(CommandContext ctx, float amount)
    {
        if (amount is < 0 or > 10_000_000) throw new CommandError($"amount: {amount} is not 0..10000000");
        var player = ctx.RequirePlayer();
        var now = ctx.World.GameNow;
        Focus.Settle(player, now);
        var delta = amount - player.Character.CraftingFocus;
        player.Character.CraftingFocus = amount;
        ctx.World.Saves.MarkChanged(player.Character);
        Focus.Send(player, now, delta);
        ctx.Reply(string.Create(CultureInfo.InvariantCulture, $"'{player.Character.Name}': focus {amount:0.##}"));
    }

    /// <summary>Sets the player's learning points (whole points, at most 999999), with its UpdateLearningPoints.</summary>
    [Command("lp", Level = AccountLevel.GameMaster, Help = "sets the player's learning points")]
    public static void SetLearningPoints(CommandContext ctx, float amount)
    {
        if (amount is < 0 or > 999_999) throw new CommandError($"amount: {amount} is not 0..999999");
        var player = ctx.RequirePlayer();
        var c = player.Character;
        LearningPoints.Accrue(player, ctx.World.GameNow, send: false);
        var before = c.LearningPoints;
        c.LearningPoints = (long)Math.Round(amount * 10000);
        ctx.World.Saves.MarkChanged(c);
        player.Session.Send(LearningPoints.Event(player, c.LearningPoints - before, LearningPoints.Silent));
        ctx.Reply(string.Create(CultureInfo.InvariantCulture, $"'{c.Name}': {amount:0.####} learning points"));
    }
}
