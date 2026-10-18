using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>The goals of PVE-DESIGN step 11: .bounty (the server bounties), .bounty claim, .bounty reroll, .season.</summary>
public static class BountyCommands
{
    /// <summary>.bounty: today's and this week's bounties with the player's progress, its Adventurer's Challenge, its PvP points of the day.</summary>
    [Command("bounty", Level = AccountLevel.Player, Help = "the daily and weekly bounties with your progress, your Adventurer's Challenge points and today's PvP points")]
    public static void List(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        if (!ctx.World.Goals.Bounties.Enabled || ctx.World.Goals.Bounties.Pool.Count == 0) ctx.Reply("no bounties on this server");
        else ctx.Reply(string.Join("\n", ctx.World.Bounties.Describe(player)));
        ctx.Reply(Seasons.Describe(player));
        ctx.Reply(PvpGoals.Describe(player));
    }

    /// <summary>
    /// .bounty claim: the reward items that did not fit the inventory, as far as they fit now: of done bounties, and the Adventurer's
    /// Challenge rewards the server gives itself (Season:GrantPremiumRewards, Season:DailyExtra).
    /// </summary>
    [Command("bounty claim", Level = AccountLevel.Player, Help = "gives the bounty and Adventurer's Challenge rewards that did not fit the inventory")]
    public static void Claim(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        var owed = (player.Character.Goals?.BountiesOwed.Count ?? 0) + Seasons.OwedCount(player);
        if (owed == 0) throw new CommandError("no reward is waiting");
        var given = ctx.World.Bounties.Claim(player) + Seasons.ClaimOwed(player);
        ctx.Reply(given == owed ? $"{given} reward(s) given" : $"{given} of {owed} reward(s) given; the rest still does not fit the inventory");
    }

    /// <summary>.bounty reroll: other daily bounties for today, announced to every player.</summary>
    [Command("bounty reroll", Level = AccountLevel.GameMaster, Help = "draws other daily bounties for today (announced to everyone)")]
    public static void Reroll(CommandContext ctx)
    {
        ctx.World.Bounties.Reroll();
        ctx.Reply("today's bounties: " + string.Join("; ", ctx.World.Bounties.Current.Select(a => a.Bounty.Describe())));
    }

    /// <summary>.season add &lt;points&gt;: Adventurer's Challenge points for the player now, as a gain (376, unlocks).</summary>
    [Command("season add", Level = AccountLevel.GameMaster, Help = "gives the player <points> Adventurer's Challenge points (whole points)")]
    public static void SeasonAdd(CommandContext ctx, float points)
    {
        var player = ctx.RequirePlayer();
        if (points is <= 0 or > 1_000_000) throw new CommandError("points: 0 < points <= 1000000");
        var unlocked = Seasons.Gain(player, (long)Math.Round(points * 10000));
        ctx.Reply(Seasons.Describe(player) + (unlocked.Count > 0 ? $"; unlocked {string.Join(", ", unlocked)}" : ""));
    }
}
