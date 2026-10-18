using System.Globalization;
using System.Numerics;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Persistence;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>World object commands: .near (mobs: Commands/Builtin/MobCommands.cs).</summary>
public static class WorldCommands
{
    /// <summary>
    /// The world objects in the player's visibility window (SPEC stage 2 §5: cells within Visibility:EnterCells of the
    /// player's cell) and the cluster-wide ones, nearest first: kind, type, position, distance, and whether the client has
    /// been told (an object just over a cell border may not be yet). Resource nodes (stage 4b) are "resource node WOOD T4"
    /// with their charges, ".1"-".3" when enchanted; mobs (stage 5a) "mob T4_MOB_UNDEAD_SOLDIER_VETERAN" with their type id,
    /// tier, health and spawn key. For checking what the client shows against what the server sends.
    /// </summary>
    [Command("near", Level = AccountLevel.GameMaster, Help = "the world objects in view of the player, nearest first (at most <count>)")]
    public static void Near(CommandContext ctx, int count = 20)
    {
        var player = ctx.RequirePlayer();
        if (count < 1) throw new CommandError($"count: {count} is not 1 or more");
        if (player.Map is not { Grid: { } grid } map)
            throw new CommandError($"cluster {player.Map?.Cluster ?? player.Character.Cluster} has no world objects (yet)");
        var at = player.PositionAt(DateTime.UtcNow);
        var cell = grid.CellOf(at);
        var range = ctx.World.Config.Visibility.EnterCells;
        var inView = grid.Around(cell, range).Concat(map.ClusterWide).OrderBy(o => Vector2.Distance(o.Position, at)).ToList();
        var kinds = inView.GroupBy(o => o.Kind).Select(g => $"{g.Count()} {g.Key}{(g.Count() == 1 ? "" : "s")}");
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture,
                $"{map.Cluster} at ({at.X:0.#}, {at.Y:0.#}), cell ({cell.X}, {cell.Z}): {inView.Count} objects in view ({string.Join(", ", kinds)})"),
        };
        var known = player.Visibility.Known;
        foreach (var o in inView.Take(count))
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  {o.Kind} {o.TypeName} at ({o.Position.X:0.##}, {o.Position.Y:0.##}), {Vector2.Distance(o.Position, at):0.0} m"
                + $"{(o is Harvestable { IsResourceNode: true } node ? $", {node.Charges}/{node.FullCharges} charges" : "")}"
                + $"{(o is Mob mob ? $", type {mob.Template.Id} T{mob.Template.Tier}, {mob.Health.At(Game.GameTime.Now):0}/{mob.Health.Max:0} hp, spawn {mob.Key}" : "")}"
                + $"{(o.Range == AnnounceRange.Cluster ? ", cluster-wide" : "")}{(known.Contains(o) ? "" : ", not sent yet")}"));
        if (inView.Count > count) lines.Add($"  ... {inView.Count - count} more (.near {inView.Count})");
        ctx.Reply(string.Join("\n", lines));
    }
}
