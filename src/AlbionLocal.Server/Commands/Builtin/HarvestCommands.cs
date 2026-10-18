using System.Globalization;
using System.Numerics;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Persistence;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>Gathering commands (stage 4a, 4b): .harvest info.</summary>
public static class HarvestCommands
{
    /// <summary>
    /// The harvestable nearest to the player (static trees and rocks, and the resource nodes of stage 4b; World/Harvesting.cs):
    /// type, tier and enchantment, id, position and distance, for a resource node the prefab and node tile the client's
    /// placement put it on, charges of full, the next regrowth step, what one harvest gives (item, amount, charges taken)
    /// and takes (time by hand, the tools that can harvest it), and the player's harvest under way. For testing gathering
    /// in the client: the server's view of a node next to what the client shows.
    /// </summary>
    [Command("harvest info", Level = AccountLevel.GameMaster, Help = "the harvestable nearest to the player: type, tier, charges, regrowth, what a harvest gives")]
    public static void Info(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        if (player.Map is not { Grid: not null } map)
            throw new CommandError($"cluster {player.Map?.Cluster ?? player.Character.Cluster} has no world objects (yet)");
        var at = player.PositionAt(DateTime.UtcNow);
        if (map.Objects.Values.OfType<Harvestable>().MinBy(h => Vector2.Distance(h.Position, at)) is not { } node)
            throw new CommandError($"{map.Cluster} has no harvestables");
        var tier = node.TierData;
        var now = ctx.World.Clock.Now;
        string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        var charges = node.Charges > 0 ? node.Charges : node.FullCharges; // what the next harvest takes, of a full one when empty
        var tools = node.Type.ToolTimeFactors.Count == 0 ? "none"
            : Inv($"{node.Type.Resource} tools of T{tier.Tier + node.Type.ToolTimeFactors.Keys.Min()} or higher");
        // The yield with Rates:Harvest (the finish rounds its fraction at random).
        var rate = ctx.World.Rates.Harvest;
        var scaled = rate == 1 ? "" : Inv($" (x Rates:Harvest {rate:0.####} = {Harvesting.Amount(tier, charges) * rate:0.##})");
        var lines = new List<string>
        {
            Inv($"{node.Type.Name} T{node.Tier}{(node.RareState > 0 ? $".{node.RareState}" : "")} (type {node.Type.Id}) #{node.Id} at ({node.Position.X:0.##}, {node.Position.Y:0.##}), {Vector2.Distance(node.Position, at):0.0} m")
                + (player.Visibility.Known.Contains(node) ? node.Listed ? "" : ", sent as NewHarvestableObject (enchanted)" : ", not sent to the client yet"),
            Inv($"  charges {node.Charges}/{node.FullCharges}, rare state {node.RareState}")
                + (node.RegrowAt is { } next ? Inv($", next charge in {Math.Max(0, (next - now).TotalSeconds):0} s") : node.Charges < node.FullCharges ? ", does not regrow" : ""),
            Inv($"  a harvest{(node.Charges > 0 ? "" : " when full")}: {Harvesting.Amount(tier, charges)}{scaled} x {tier.ItemOf(node.RareState)} for {Harvesting.ChargesTaken(tier, charges)} charge(s); ")
                + (tier.RequiresTool ? "needs a tool" : Inv($"by hand {Harvesting.Duration(tier, tier.NoToolTimeFactor):0.###} s")) + $"; {tools}",
        };
        if (node.Spawn.Prefab is { } prefab)
            lines.Insert(1, Inv($"  resource node: tier {node.Tier}, enchantment {node.RareState}; prefab {prefab.Name}{(prefab.Name == node.Spawn.Tile ? "" : $" ({node.Spawn.Tile})")} for {prefab.Resource} T{prefab.Tier} on a ")
                + Inv($"{ResourcePresets.NodeTypes.GetValueOrDefault(prefab.NodeType)} node tile at ({prefab.Node.X:0.##}, {prefab.Node.Y:0.##})"));
        if (player.Harvest is { } h)
            lines.Add(Inv($"  harvesting {h.Node.Type.Name} T{h.Node.Tier} #{h.Node.Id} {(h.Tool is { } tool ? $"with {tool.Template.Name}" : "by hand")}, {Math.Max(0, (h.Due - now).TotalSeconds):0.0} s left"));
        ctx.Reply(string.Join("\n", lines));
    }
}
