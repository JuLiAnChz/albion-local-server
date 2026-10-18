using AlbionLocal.Server.Game;
using AlbionLocal.Server.Persistence;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>The expeditions (stageH expeditions SPEC §8): .exp list, .exp start, .exp finish, .exp leave, .exp level.</summary>
public static class ExpeditionCommands
{
    /// <summary>.exp list: the registrations and the live instances (members, step and counts, time left).</summary>
    [Command("exp list", Level = AccountLevel.GameMaster, Help = "the expedition registrations and instances")]
    public static void List(CommandContext ctx)
    {
        var lines = ctx.World.Expeditions.Describe().ToList();
        ctx.Reply(lines.Count == 0 ? "no expedition registration or instance" : $"expeditions:\n{string.Join("\n", lines)}");
    }

    /// <summary>
    /// .exp start &lt;index|uniquename&gt; [level] [players]: straight into a new instance of that expedition, without the agent (the way back is
    /// here); a hardcore one at <paramref name="level"/> (default 1), a group one with its mobs scaled for <paramref name="players"/> (default 1).
    /// </summary>
    [Command("exp start", Level = AccountLevel.GameMaster, Help = "sends the player into a new instance of an expedition (index or uniquename) [hardcore level] [players]")]
    public static void Start(CommandContext ctx, string expedition, int level = 1, int players = 1)
    {
        var player = ctx.RequirePlayer();
        var world = ctx.World;
        var exps = world.Expeditions;
        if (world.Clusters?.ExpeditionsIfLoaded is not { } data) throw new CommandError("the expedition tables are not read (no city with an agent resolved yet)");
        if (data.Find(expedition) is not { } e || !exps.Playable(e) || data.CategoryAt(e.CategoryIndex) is not { } cat)
            throw new CommandError($"no playable expedition '{expedition}'");
        if (player.Map is not { } map || Data.ExpeditionData.IsInstance(map.Cluster) || exps.InstanceOf(player.Character.Id) is not null)
            throw new CommandError("the player is in an expedition already");
        if (world.CachedCluster(e.World) is null)
        {
            world.PrefetchCluster(e.World);
            throw new CommandError($"cluster {e.World} is being read; try again in a moment");
        }
        exps.Cancel(player, "a GM's .exp start");
        var at = player.PositionAt(DateTime.UtcNow);
        var instance = exps.Create(e, cat, cat.Difficulties.Count > 0 ? Math.Clamp(level, 1, cat.Difficulties.Count) : 0, Math.Clamp(players, 1, 5));
        ctx.Reply($"{instance}: sending '{player.Character.Name}' in");
        exps.Send(player, instance, new DungeonReturn(map.Cluster, at.X, at.Y, player.Facing), $"expedition:{instance.Id}:gm");
    }

    /// <summary>.exp finish: every task of the player's instance done now (the completion and its rewards).</summary>
    [Command("exp finish", Level = AccountLevel.GameMaster, Help = "completes the player's expedition now")]
    public static void Finish(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        if (ctx.World.Expeditions.Find(player.Map?.Cluster) is not { } i) throw new CommandError("the player is in no expedition");
        if (i.Finished) throw new CommandError($"{i} is completed already");
        ctx.World.Expeditions.Finish(i);
        ctx.Reply($"{i} completed");
    }

    /// <summary>.exp level &lt;n&gt;: the highest hardcore level open to the player (the ladder, stageH expeditions SPEC §12.2).</summary>
    [Command("exp level", Level = AccountLevel.GameMaster, Help = "opens the hardcore expedition levels up to n for the player")]
    public static void Level(CommandContext ctx, int level)
    {
        var player = ctx.RequirePlayer();
        if (level < 1 || level > 18) throw new CommandError("the hardcore levels are 1-18");
        (player.Character.Expeditions ??= new ExpeditionLog()).Hardcore = level;
        ctx.World.Saves.MarkChanged(player.Character);
        ctx.Reply($"'{player.Character.Name}': hardcore expedition levels 1-{level} open");
    }

    /// <summary>.exp leave: the player out of its instance to its way back, as through an exit.</summary>
    [Command("exp leave", Level = AccountLevel.GameMaster, Help = "takes the player out of its expedition to where it came from")]
    public static void Leave(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        if (ctx.World.Expeditions.Find(player.Map?.Cluster) is not { } i) throw new CommandError("the player is in no expedition");
        ctx.Reply($"'{player.Character.Name}' leaves {i}");
        ctx.World.Expeditions.Leave(player, i, "gm");
    }
}
