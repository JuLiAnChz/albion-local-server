using System.Globalization;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>The player trade window (stageJ trade SPEC): .trade shows the player's trade, .trade end cancels it.</summary>
public static class TradeCommands
{
    /// <summary>The player's trade: id, state, revision, and each side's offer and accept.</summary>
    [Command("trade", Level = AccountLevel.GameMaster, Help = "the player's trade: its state, revision, both offers and accepts")]
    public static void Show(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        if (ctx.World.Trades.Of(player) is not { } t) throw new CommandError("the player is in no trade");
        ctx.Reply($"trade {t.Id}: {t.State}, revision {t.Revision}");
        foreach (var side in new[] { t.A, t.B })
        {
            var items = side.Slots.Select(s => $"#{s.Key} {s.Value.Item.Template.Name} x{s.Value.Count}");
            ctx.Reply(string.Create(CultureInfo.InvariantCulture,
                $"  '{side.Player.Character.Name}'{(side.Accepted ? " (accepted)" : "")}: {string.Join(", ", items.DefaultIfEmpty("no items"))}, {side.Silver / 10000.0:0.####} silver"));
        }
    }

    /// <summary>Cancels the player's trade (177 to both; nothing moves).</summary>
    [Command("trade end", Level = AccountLevel.GameMaster, Help = "cancels the player's trade (nothing moves)")]
    public static void End(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        if (ctx.World.Trades.Of(player) is null) throw new CommandError("the player is in no trade");
        ctx.World.Trades.Cancel(player, "cancelled by a GM");
        ctx.Reply("trade cancelled");
    }
}
