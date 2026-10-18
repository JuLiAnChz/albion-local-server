using System.Globalization;
using System.Numerics;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Persistence;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>The world bosses (analysis/work/stageH/bosses/SPEC.md §6.5): .boss list, .boss spawn, .boss kill.</summary>
public static class BossCommands
{
    /// <summary>.boss list [all]: the world boss slots of the player's cluster (or of every cluster filled so far): boss, mob, place, state, next time.</summary>
    [Command("boss list", Level = AccountLevel.GameMaster, Help = "the world boss slots of the cluster (all: of every cluster so far), their state and next spawn")]
    public static void List(CommandContext ctx, string which = "here")
    {
        var all = which.Equals("all", StringComparison.OrdinalIgnoreCase);
        var cluster = all ? null : ctx.RequirePlayer().Map?.Cluster ?? throw new CommandError("the player is on no map");
        var lines = ctx.World.Bosses.Describe(cluster).ToList();
        var now = ctx.World.Bosses.UtcNow.ToString("HH:mm", CultureInfo.InvariantCulture);
        ctx.Reply(lines.Count == 0 ? $"{cluster ?? "all clusters"}: no world boss slots (now {now} UTC)" : $"{cluster ?? "all clusters"} (now {now} UTC):\n{string.Join("\n", lines)}");
    }

    /// <summary>
    /// .boss spawn &lt;WORLD_BOSS_… | mob&gt; [here]: the boss of a slot of the player's cluster now (announced; its schedule goes on from its
    /// death), or with "here", or when the cluster has no slot of it, a one-off boss 5 m in front of the player (with the file's factors;
    /// it never comes back). A mobs.xml mob name works too.
    /// </summary>
    [Command("boss spawn", Level = AccountLevel.GameMaster, Help = "spawns a world boss (WORLD_BOSS_T5_KEEPER or a mob) at its slot of the cluster now, or 5 m in front of the player with 'here'")]
    public static void Spawn(CommandContext ctx, string boss, string where = "")
    {
        var player = ctx.RequirePlayer();
        var world = ctx.World;
        if (player.Map is not { Grid: not null } map) throw new CommandError("the player is on no loaded map");
        var here = where.Equals("here", StringComparison.OrdinalIgnoreCase);
        if (where.Length > 0 && !here) throw new CommandError(".boss spawn <boss> [here]");
        var type = world.BossData?.Find(boss);
        var mob = world.Clusters?.MobsIfLoaded?.Find(type?.Mob ?? boss.ToUpperInvariant())
            ?? throw new CommandError($"{boss} is neither a world boss of {world.BossSettings.DataFile}.xml nor a mob of mobs.xml");
        var slot = here ? null : world.Bosses.SlotsOf(map.Cluster).FirstOrDefault(s => s.Template == mob);
        var at = player.PositionAt(DateTime.UtcNow);
        var (dx, dz) = TileMath.Rotate(0, 5, player.Facing);
        try
        {
            var made = world.Bosses.SpawnNow(map, slot, type, mob, at + new Vector2((float)dx, (float)dz), player.Facing + 180);
            ctx.Reply(string.Create(CultureInfo.InvariantCulture,
                $"{made}: {(slot is null ? "a one-off boss (never back)" : $"slot {slot.Key}")}, {made.Health.Max:0} hp (x{made.HealthFactor}), damage x{made.DamageFactor}"));
        }
        catch (InvalidOperationException e) { throw new CommandError(e.Message); }
    }

    /// <summary>.boss kill: the nearest world boss of the player's map off the map (a despawn: no corpse, loot or fame); its slot waits for its next time.</summary>
    [Command("boss kill", Level = AccountLevel.GameMaster, Help = "despawns the nearest world boss of the cluster; its slot comes back at its next time")]
    public static void Kill(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        if (player.Map is not { } map) throw new CommandError("the player is on no map");
        var at = player.PositionAt(DateTime.UtcNow);
        if (map.Objects.Values.OfType<Mob>().Where(m => m.Boss is not null && !m.Dead && !m.Removed).MinBy(m => Vector2.Distance(m.Position, at)) is not { } boss)
            throw new CommandError("no world boss on this map");
        ctx.World.Bosses.Despawn(boss);
        var next = boss.Boss!.NextSpawn;
        ctx.Reply(string.Create(CultureInfo.InvariantCulture,
            $"{boss} despawned; {(next is { } n ? $"back at {n:yyyy-MM-dd HH:mm:ss} UTC (in {(n - ctx.World.Bosses.UtcNow).TotalSeconds:0} s)" : "never back")}"));
    }
}
