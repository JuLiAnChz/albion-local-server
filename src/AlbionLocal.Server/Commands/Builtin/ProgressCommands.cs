using System.Globalization;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>A character's destiny board for .progress mode: everything unlocked (no board of its own) or a fair start.</summary>
public enum BoardMode
{
    UnlockAll,
    Fair,
}

/// <summary>Fame and the destiny board (analysis/work/progression/PROGRESSION.md, Systems/Destiny): .fame add, .progress show/set/mode.</summary>
public static class ProgressCommands
{
    /// <summary>
    /// <paramref name="amount"/> fame as a kill with the player's gear would give it (the board nodes of the equipped items
    /// and fameearned, the mob taken as T8 so no tier gate stops it), or, with <paramref name="node"/> (an achievements.xml
    /// id or a number), all of it to that one node in progress. Raises the total and sends the burst as a kill does
    /// (151/152, then UpdateFame without a zone bonus).
    /// </summary>
    [Command("fame add", Level = AccountLevel.GameMaster, Help = "gives <amount> fame as a kill with the worn gear, or all of it to one destiny board [node]")]
    public static void Add(CommandContext ctx, float amount, string? node = null)
    {
        var player = ctx.RequirePlayer();
        if (amount is <= 0 or > 100_000_000) throw new CommandError($"amount: {amount} is not above 0 (at most 100000000)");
        var board = ctx.World.GameData?.Board;
        int? only = null;
        if (node is not null)
        {
            if (board?.Find(node) is not { } target) throw new CommandError($"unknown destiny board node {node}");
            if (player.Character.Progression is not { } own) throw new CommandError("everything is unlocked (no board of its own): .progress mode fair first");
            if (!board.InProgress(own, target.Id)) throw new CommandError($"{target} is not in progress (finished, at its last level or not reached yet)");
            only = target.Id;
        }
        var gain = (long)Math.Round(amount * (double)DestinyBoard.A7m);
        var steps = Destiny.Award(player, gain, Destiny.Kill(player, 8) with { Only = only }, journals: false, guild: false); // the board only, no journals, no guild
        var finished = steps.Where(s => s.Finished).ToList();
        ctx.Reply(string.Create(CultureInfo.InvariantCulture, $"+{amount:0.####} fame (total {player.Character.Fame / 10000.0:0.####})")
            + (player.Character.Progression is null ? "; everything is unlocked, no destiny board of its own"
                : board is null ? "" : $"; {steps.Select(s => s.Node).Distinct().Count()} node(s)"
                    + (finished.Count > 0 ? ", level-ups: " + string.Join(", ", finished.Select(s => $"{board.Nodes[s.Node].Name} {s.Level + 1}")) : "")));
    }

    /// <summary>The board: mode, finished nodes, and the nodes in progress with a level or fame (at most 40 lines).</summary>
    [Command("progress show", Level = AccountLevel.GameMaster, Help = "the player's destiny board: finished nodes and those in progress")]
    public static void Show(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        var c = player.Character;
        if (c.Progression is not { } p) { ctx.Reply(string.Create(CultureInfo.InvariantCulture, $"mode unlockall: everything unlocked, total fame {c.Fame / 10000.0:0.####}")); return; }
        if (ctx.World.GameData?.Board is not { } board) throw new CommandError("no destiny board data (client data not loaded)");
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"mode fair: total fame {c.Fame / 10000.0:0.####}, {p.Completed.Count} finished, {p.Levels.Count} in progress"),
        };
        if (p.Completed.Count > 0) lines.Add("  finished: " + string.Join(", ", p.Completed.Where(id => id >= 0 && id < board.Nodes.Count).Select(id => board.Nodes[id].Name)));
        var active = p.Levels.Keys.Where(id => id >= 0 && id < board.Nodes.Count && (p.Levels[id] > 0 || p.Progress.ContainsKey(id))).ToList();
        lines.AddRange(active.Take(40).Select(id => "  " + Destiny.Describe(board, p, id)));
        if (active.Count > 40) lines.Add($"  … {active.Count - 40} more");
        var idle = p.Levels.Count - active.Count;
        if (idle > 0) lines.Add($"  {idle} more at level 0 without fame: " + string.Join(", ", p.Levels.Keys.Except(active).Take(12).Select(id => board.Nodes[id].Name)) + (idle > 12 ? ", …" : ""));
        ctx.Reply(string.Join("\n", lines));
    }

    /// <summary>
    /// Sets one node's level (a plain node: 1 = finished, 0 = in progress; a template 0..its level count), its neighbours
    /// becoming available, and sends the player a fresh FullAchievementInfo (the client rebuilds its unlocks from it).
    /// </summary>
    [Command("progress set", Level = AccountLevel.GameMaster, Help = "sets a destiny board node's level (plain: 1 = finished) and sends the board anew")]
    public static void Set(CommandContext ctx, string achievement, int level)
    {
        var player = ctx.RequirePlayer();
        if (ctx.World.GameData?.Board is not { } board) throw new CommandError("no destiny board data (client data not loaded)");
        if (board.Find(achievement) is not { } node) throw new CommandError($"unknown destiny board node {achievement}");
        if (player.Character.Progression is not { } p) throw new CommandError("everything is unlocked (no board of its own): .progress mode fair first");
        var max = node.IsTemplate ? board.LevelCount(node.Id) : 1;
        if (level < 0 || level > max) throw new CommandError($"level: {level} is not 0..{max} for {node}");
        board.SetLevel(p, node.Id, level);
        Refresh(ctx, player);
        ctx.Reply($"{node} at level {level}{(node.IsTemplate ? $"/{max}" : level == 1 ? " (finished)" : "")}; {p.Levels.Count} in progress, {p.Completed.Count} finished");
    }

    /// <summary>
    /// Switches the character's board: unlockall = none of its own (everything unlocked, fame only raises the total), fair
    /// = an empty one (START in progress; what it has earned on a board before is dropped). Sends a fresh FullAchievementInfo.
    /// </summary>
    [Command("progress mode", Level = AccountLevel.GameMaster, Help = "unlockall: everything unlocked; fair: an empty destiny board that fame fills (sends the board anew)")]
    public static void Mode(CommandContext ctx, BoardMode mode)
    {
        var player = ctx.RequirePlayer();
        var c = player.Character;
        if (mode == BoardMode.Fair && ctx.World.GameData?.Board is not { } board) throw new CommandError("no destiny board data (client data not loaded)");
        c.Progression = mode == BoardMode.Fair ? ctx.World.GameData!.Board!.FairStart() : null;
        player.Progression = ctx.World.GameData is null ? null : c.Progression ?? ctx.World.UnlockAll;
        Refresh(ctx, player);
        ctx.Reply(mode == BoardMode.Fair ? "fair start: an empty destiny board (START in progress); fame fills it" : "everything unlocked");
    }

    /// <summary>After a GM change of the board: saved, the board sent anew, the bars of the new destiny levels (96 when they changed).</summary>
    private static void Refresh(CommandContext ctx, Player player)
    {
        ctx.World.Saves.MarkChanged(player.Character);
        player.Session.SendDestinyBoard(player);
        var now = ctx.World.GameNow;
        if (player.RecalculateStats(now)) player.Session.Send(Combat.PlayerCombo(player, now));
    }
}
