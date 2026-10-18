using System.Globalization;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>
/// The hellgates (stageL hellgates SPEC §5.10): .hg mode, .hg leave and (phase B) .hg infamy for every player; .hg enter, .hg finish, .hg close,
/// .hg list and (phase B) .hg area for GMs (<see cref="Hellgates"/>).
/// </summary>
public static class HellgateCommands
{
    /// <summary>.hg mode [name or 1-4]: the modes and the player's own; with an argument, the player's mode (the Fixed shrine's key 4).</summary>
    [Command("hg mode", Level = AccountLevel.Player, Help = "the hellgate modes and yours; with a name or 1-4 it sets your mode (the shrine uses it)")]
    public static void Mode(CommandContext ctx, string mode = "")
    {
        var player = ctx.RequirePlayer();
        var world = ctx.World;
        if (world.Clusters?.HellgatesIfLoaded is not { } data) throw new CommandError("hellgate.xml is not read (visit a Hell Den)");
        var offered = data.Modes.Where(m => world.Config.Hellgates.Modes.Contains(m.Name)).ToList();
        if (mode.Length > 0)
        {
            var chosen = int.TryParse(mode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? data.At(n) : data.Find(mode);
            if (chosen is null || !offered.Contains(chosen)) throw new CommandError($"{mode}: one of {string.Join(", ", offered.Select(m => $"{m.Id} {m.Name}"))}");
            world.Hellgates.SetMode(player, chosen);
            return;
        }
        var own = world.Hellgates.ModeOf(player);
        var versus = world.Config.Hellgates.VersusModes;
        ctx.Reply("hellgate modes: " + string.Join(", ", offered.Select(m => $"{m.Id} {m}{(versus.Contains(m.Name) ? " vs bots" : "")}{(m.Id == own ? " (yours)" : "")}")));
    }

    /// <summary>.hg leave (Hellgates:LeaveCommand; a GM always): out of the player's hellgate to its way back; not knocked down or in combat.</summary>
    [Command("hg leave", Level = AccountLevel.Player, Help = "takes you out of your hellgate to the shrine")]
    public static void Leave(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        if (!ctx.World.Config.Hellgates.LeaveCommand && ctx.Level < AccountLevel.GameMaster) throw new CommandError("not here: use the escape spell or the exit portal");
        if (ctx.World.Hellgates.Find(player.Map?.Cluster) is not { } i) throw new CommandError("you are in no hellgate");
        if (player.Combat.Down is { } down) throw new CommandError($"not while knocked down ({down.Kind})");
        if (player.Combat.InCombat) throw new CommandError("not in combat: use the escape spell, or wait until the fight is over");
        ctx.Reply($"leaving {i.Mode.Name} {i.World}");
        ctx.World.Hellgates.Leave(player, i, "command");
    }

    /// <summary>
    /// .hg enter &lt;1-4&gt; [map]: straight into a new hellgate of that mode on <paramref name="map"/> (HELLGATE-NN-…, or NN; default a pick of
    /// its mapset), without the shrine, the channel or a party; the way back is here.
    /// </summary>
    [Command("hg enter", Level = AccountLevel.GameMaster, Help = "sends the player into a new hellgate: mode 1-4 [map HELLGATE-NN-... or NN]")]
    public static void Enter(CommandContext ctx, int mode, string map = "")
    {
        var player = ctx.RequirePlayer();
        var world = ctx.World;
        if (world.Clusters?.HellgatesIfLoaded is not { } data) throw new CommandError("hellgate.xml is not read (no Hell Den or hellgate resolved yet)");
        if (data.At(mode) is not { TeamSize: > 0 } m) throw new CommandError($"mode {mode}: 1-{data.Modes.Count - 1}");
        var maps = data.MapsOf(m);
        var id = map.Length == 0 ? maps[Random.Shared.Next(maps.Count)].Id
            : int.TryParse(map, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= maps.Count ? maps[n - 1].Id : map;
        if (!maps.Any(e => e.Id == id)) throw new CommandError($"{id} is no map of {m.Name}");
        if (player.Map is not { } here || HellgateData.IsInstance(here.Cluster)) throw new CommandError("the player is in a hellgate already");
        if (world.CachedCluster(id) is not { } template || !world.Bots.Nav.TryGet(template, out _))
        {
            world.PrefetchCluster(id);
            throw new CommandError($"{id} is being read (and its walk grid built); try again in a moment");
        }
        var party = world.Parties.Of(player.Character.Id)?.Id ?? 0;
        var i = world.Hellgates.Create(m, id, party, 1, Hellgates.StartsOf(template, world.Bots.Nav.Find(id)!.Grid, world.Config.Hellgates.StartClearance));
        var at = player.PositionAt(DateTime.UtcNow);
        world.Hellgates.Send(player, i, new Game.DungeonReturn(here.Cluster, at.X, at.Y, player.Facing));
        ctx.Reply($"{i}: sending '{player.Character.Name}' in");
    }

    /// <summary>.hg finish: the player's hellgate goes on now: objective 1 done (state 1), objective 2 won (state 2), or the reward claimed (state 3).</summary>
    [Command("hg finish", Level = AccountLevel.GameMaster, Help = "advances the player's hellgate: the points done, the enemy beaten, or the reward claimed")]
    public static void Finish(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        if (ctx.World.Hellgates.Find(player.Map?.Cluster) is not { } i) throw new CommandError("the player is in no hellgate");
        if (i.State is not (HellgateState.Progress or HellgateState.Defeat or HellgateState.Defeated)) throw new CommandError($"{i} is past its objectives (state {(int)i.State})");
        ctx.World.Hellgates.Advance(i, player.Map!);
        ctx.Reply($"{i}: state {(int)i.State} {i.State}");
    }

    /// <summary>.hg close: the player's hellgate closes now (everyone inside out).</summary>
    [Command("hg close", Level = AccountLevel.GameMaster, Help = "closes the player's hellgate now")]
    public static void Close(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        if (ctx.World.Hellgates.Find(player.Map?.Cluster) is not { } i) throw new CommandError("the player is in no hellgate");
        ctx.Reply($"{i}: closing");
        ctx.World.Hellgates.Close(i, "gm");
    }

    /// <summary>.hg infamy: the player's hellgate infamy (phase B: 2v2 and 5v5, the peak, the next milestone).</summary>
    [Command("hg infamy", Level = AccountLevel.Player, Help = "your hellgate infamy (2v2, 5v5) and the next milestone")]
    public static void Infamy(CommandContext ctx) => ctx.Reply(ctx.World.Hellgates.DescribeInfamy(ctx.RequirePlayer().Character));

    /// <summary>.hg area: the restricted area of the player's hellgate starts now (phase B; its second objective under way), or its final phase.</summary>
    [Command("hg area", Level = AccountLevel.GameMaster, Help = "starts the restricted area of the player's hellgate now (in its second objective), or its final phase")]
    public static void Area(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        if (ctx.World.Hellgates.Find(player.Map?.Cluster) is not { } i) throw new CommandError("the player is in no hellgate");
        if (i.State != HellgateState.Defeat) throw new CommandError($"{i} is not in its second objective (state {(int)i.State})");
        if (ctx.World.Hellgates.StartArea(i, final: i.Area is not null) is not { } area) throw new CommandError("its template has no arearestrictionsettings");
        ctx.Reply(string.Create(CultureInfo.InvariantCulture, $"{i}: the restricted area{(area.Final ? " (final)" : "")} {area.From:0.#} → {area.To:0.#} m at {area.Velocity} m/s"));
    }

    /// <summary>.hg list: the channels under way and the live hellgates.</summary>
    [Command("hg list", Level = AccountLevel.GameMaster, Help = "the hellgate channels and the live hellgates")]
    public static void List(CommandContext ctx)
    {
        var lines = ctx.World.Hellgates.Describe().ToList();
        ctx.Reply(lines.Count == 0 ? "no hellgate" : $"hellgates:\n{string.Join("\n", lines)}");
    }
}
