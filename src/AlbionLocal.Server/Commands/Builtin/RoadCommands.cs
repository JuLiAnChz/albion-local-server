using System.Globalization;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>
/// The Roads of Avalon (stageL roads SPEC §5.10, <see cref="AvalonRoads"/>): .roads and .roads route for the players Roads:Command lets (pve
/// every one, official GMs); .roads info, .roads link, .roads close, .roads regen, .roads charges and (phase B) .roads dungeons for GMs.
/// </summary>
public static class RoadCommands
{
    /// <summary>The network when it is built, after the Roads:Command check (GMs always).</summary>
    private static AvalonRoads Roads(CommandContext ctx, bool everyone)
    {
        if (everyone && ctx.World.Config.Roads.Command == RoadsCommandAccess.Gm && ctx.Level < AccountLevel.GameMaster)
            throw new CommandError("the road network is not shown on this server (Roads:Command gm)");
        if (!ctx.World.Config.Roads.Enabled) throw new CommandError("the Roads of Avalon are off (Roads:Enabled false)");
        if (!ctx.World.Roads.Ready) throw new CommandError("the road network is being built; try again in a moment");
        return ctx.World.Roads;
    }

    /// <summary>The portal nearest the player on its map.</summary>
    private static TunnelExit Nearest(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        var at = player.PositionAt(DateTime.UtcNow);
        return player.Map?.Objects.Values.OfType<TunnelExit>().MinBy(p => System.Numerics.Vector2.Distance(p.Position, at))
               ?? throw new CommandError("no road portal on this map");
    }

    /// <summary>.roads: this cluster's portals (spot, type, target, charges, closes in), then the nearest open-world entrances into the roads.</summary>
    [Command("roads", Level = AccountLevel.Player, Help = "the road portals of this cluster and the nearest entrances into the Roads of Avalon")]
    public static void List(CommandContext ctx)
    {
        var roads = Roads(ctx, everyone: true);
        var player = ctx.RequirePlayer();
        var cluster = player.Map?.Cluster ?? player.Character.Cluster;
        var now = ctx.World.GameNow;
        if (roads.NodeOf(cluster) is { } node)
        {
            ctx.Reply($"{cluster} ({node.Preset.Name}): {node.Slots.Count(s => s.Link is not null)} of {node.Slots.Count} portals open");
            foreach (var s in node.Slots.Where(s => s.Link is not null || node.IsRoad)) ctx.Reply("  " + roads.Describe(s, now));
        }
        else ctx.Reply($"{cluster} has no road portals");
        var entrances = roads.Entrances(cluster).Take(5).ToList();
        if (entrances.Count == 0) return;
        ctx.Reply("nearest entrances into the roads:");
        foreach (var (s, distance) in entrances)
        {
            var name = ctx.World.Clusters?.WorldIfLoaded?.Find(s.Cluster)?.DisplayName is { Length: > 0 } n ? $" {n}" : "";
            var far = distance < double.MaxValue ? string.Create(CultureInfo.InvariantCulture, $" ({distance:0} on the world map)") : "";
            ctx.Reply($"  {s.Cluster}{name}{far}: {roads.Describe(s, now)}");
        }
    }

    /// <summary>
    /// .roads route &lt;cluster&gt;: the shortest way over the open portals from here, each cluster with its name; phase B (SPEC §7.6): the way
    /// marked, the portal to take here first with its distance and compass direction from the player, and every hop's portal type and charges.
    /// </summary>
    [Command("roads route", Level = AccountLevel.Player, Help = "the shortest way over the open road portals from here to a cluster (world.xml id), the first portal to take marked")]
    public static void Route(CommandContext ctx, string cluster)
    {
        var roads = Roads(ctx, everyone: true);
        var player = ctx.RequirePlayer();
        var from = player.Map?.Cluster ?? player.Character.Cluster;
        if (roads.Route(from, cluster) is not { } path) throw new CommandError($"no open portal way from {from} to {cluster} now");
        string Named(string id) => ctx.World.Clusters?.WorldIfLoaded?.Find(id)?.DisplayName is { Length: > 0 } n ? $"{id} {n}" : id;
        ctx.Reply($"{path.Count - 1} portal(s): {string.Join(" -> ", path.Select(Named))}");
        if (path.Count < 2) return;
        var now = ctx.World.GameNow;
        for (var i = 0; i + 1 < path.Count; i++)
        {
            // The hop's portal: the slot of path[i] linked to path[i + 1] (the search went over these links).
            if (roads.NodeOf(path[i])?.Slots.FirstOrDefault(s => s.Link is { } l && l.Other(s).Cluster == path[i + 1]) is not { Link: { } link } slot) continue;
            var where = "";
            if (i == 0 && player.Map?.Objects.Values.OfType<TunnelExit>().FirstOrDefault(p => p.Slot == slot) is { } portal)
            {
                var d = portal.Position - player.PositionAt(DateTime.UtcNow);
                where = string.Create(CultureInfo.InvariantCulture, $" >>> {d.Length():0} m {WorldEvents.Compass(d)} of you <<<");
            }
            var enter = AvalonRoads.Enterable(link, now) ? "" : $", no charge before {AvalonRoads.In(link.NextCharge, now)}";
            ctx.Reply($"  {i + 1}. in {path[i]}: {slot.Path} ({roads.Data!.Types[link.Type].Name}, {link.Charges}/{link.MaxCharges}{enter}) to {path[i + 1]}{where}");
        }
    }

    /// <summary>.roads info: per preset its clusters, exits by state and links.</summary>
    [Command("roads info", Level = AccountLevel.GameMaster, Help = "the road network: clusters, exits by state and links per preset")]
    public static void Info(CommandContext ctx)
    {
        var roads = Roads(ctx, everyone: false);
        ctx.Reply($"road network: {roads.Nodes.Count} clusters, {roads.Links.Count} links ({roads.Links.Count(l => l.Special)} rescue or GM ones)");
        foreach (var line in roads.Summary()) ctx.Reply(line);
    }

    /// <summary>.roads link &lt;cluster|preset&gt;: the nearest free spot here linked to a free spot there, for 30 minutes.</summary>
    [Command("roads link", Level = AccountLevel.GameMaster, Help = "links the nearest free road spot here to a cluster or tunnel preset (TUNNEL_ROYAL, ...) for 30 min")]
    public static void Link(CommandContext ctx, string target)
    {
        var roads = Roads(ctx, everyone: false);
        var player = ctx.RequirePlayer();
        if (player.Map is not { } map) throw new CommandError("the player is on no map");
        RoadLink link;
        try { link = roads.GmLink(map, player.PositionAt(DateTime.UtcNow), target); }
        catch (InvalidOperationException e) { throw new CommandError(e.Message); }
        ctx.Reply($"{link}: {roads.Describe(link.Out, ctx.World.GameNow)}");
    }

    /// <summary>.roads close: the nearest portal's link closes now (its ends roll again).</summary>
    [Command("roads close", Level = AccountLevel.GameMaster, Help = "closes the link of the nearest road portal")]
    public static void Close(CommandContext ctx)
    {
        var roads = Roads(ctx, everyone: false);
        var portal = Nearest(ctx);
        if (portal.Slot.Link is not { } link) throw new CommandError($"{portal.Slot} has no link");
        roads.Close(link);
        ctx.Reply($"{link} closed");
    }

    /// <summary>.roads charges &lt;n&gt;: the nearest portal's link has n charges now.</summary>
    [Command("roads charges", Level = AccountLevel.GameMaster, Help = "sets the charges of the nearest road portal's link")]
    public static void Charges(CommandContext ctx, int charges)
    {
        var roads = Roads(ctx, everyone: false);
        var portal = Nearest(ctx);
        if (portal.Slot.Link is not { } link) throw new CommandError($"{portal.Slot} has no link");
        roads.SetCharges(link, charges);
        ctx.Reply($"{link}: {link.Charges}/{link.MaxCharges} charges");
    }

    /// <summary>.roads dungeons: the road dungeon entrances of the player's map (phase B, SPEC §7.1: the fixed exits) and the dungeons open behind them.</summary>
    [Command("roads dungeons", Level = AccountLevel.GameMaster, Help = "the road dungeon entrances (fixed exits) of this map and the dungeons open behind them")]
    public static void Dungeons(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        if (player.Map is not { } map) throw new CommandError("the player is on no map");
        var at = player.PositionAt(DateTime.UtcNow);
        var exits = ctx.World.Dungeons.ExitsOf(map).Where(e => e.Info.Fixed).OrderBy(e => System.Numerics.Vector2.Distance(e.Position, at)).ToList();
        if (exits.Count == 0)
            throw new CommandError(ctx.World.Config.Roads.FixedDungeons ? $"{map.Cluster} has no fixed dungeon exit" : "no road dungeons (Roads:FixedDungeons false)");
        ctx.Reply($"{map.Cluster}: {exits.Count} fixed dungeon exit(s)");
        foreach (var e in exits)
        {
            var open = ctx.World.Dungeons.Groups.Count(g => g.Exit == e && !g.Closed);
            ctx.Reply(string.Create(CultureInfo.InvariantCulture, $"  {e.Name}: {e.Generator} T{e.Tier} level {e.Level}, {e.SubtypeName}, {e.ClusterType}, "
                + $"{System.Numerics.Vector2.Distance(e.Position, at):0} m away, {open} dungeon(s) open"));
        }
    }

    /// <summary>.roads regen: the whole network anew (every link closes, the saved one is dropped).</summary>
    [Command("roads regen", Level = AccountLevel.GameMaster, Help = "generates the road network anew (every link closes)")]
    public static void Regen(CommandContext ctx)
    {
        var roads = Roads(ctx, everyone: false);
        roads.Regenerate();
        ctx.Reply($"road network generated anew: {roads.Links.Count} links");
    }
}
