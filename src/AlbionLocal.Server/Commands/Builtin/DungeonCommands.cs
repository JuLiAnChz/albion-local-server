using System.Globalization;
using System.Numerics;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>Random dungeons (analysis/work/stageG/dungeons/SPEC.md §13 item 1): .rd.</summary>
public static class DungeonCommands
{
    /// <summary>
    /// .rd &lt;exit&gt;: a random dungeon exit of that RandomExit or FixedExit name (T4_PORTAL_ROYAL_SOLO, T4_HERETIC, …) 3 m in front
    /// of the player, facing them; it goes after Dungeons:GmExitSeconds unused. .rd close: the player's dungeon closes now (everyone
    /// inside is pushed out). .rd chest [spawnpoint]: a loot chest of that LootChestSpawnpoint (HERETIC_SOLO_LOOTCHEST_STANDARD by
    /// default) 3 m in front of the player. .rd: the exits of the cluster and the open dungeons.
    /// </summary>
    [Command("rd", Level = AccountLevel.GameMaster,
        Help = ".rd <exit name> puts a random dungeon exit in front of the player; .rd chest [spawnpoint] a loot chest; .rd close closes the player's dungeon; .rd lists exits and dungeons")]
    public static void Rd(CommandContext ctx, string exit = "", string spawnpoint = "HERETIC_SOLO_LOOTCHEST_STANDARD")
    {
        var player = ctx.RequirePlayer();
        var world = ctx.World;
        if (player.Map is not { } map) throw new CommandError("the player is on no map");
        if (exit.Equals("chest", StringComparison.OrdinalIgnoreCase))
        {
            var (cx, cz) = TileMath.Rotate(0, 3, player.Facing);
            try
            {
                var chest = world.Dungeons.SpawnChest(map, spawnpoint.ToUpperInvariant(), player.PositionAt(DateTime.UtcNow) + new Vector2((float)cx, (float)cz), player.Facing + 180);
                ctx.Reply($"{chest}: prefab {chest.Template.Prefab}, rarity {chest.State.Rarity}");
            }
            catch (InvalidOperationException e) { throw new CommandError(e.Message); }
            return;
        }
        if (exit.Equals("close", StringComparison.OrdinalIgnoreCase))
        {
            if (world.Dungeons.Find(map.Cluster) is not { } here) throw new CommandError("the player is in no random dungeon");
            ctx.Reply($"closing dungeon {here.Group.Ids[0]}");
            world.Dungeons.Close(here.Group, "closed by a GM");
            return;
        }
        if (exit.Length > 0)
        {
            var at = player.PositionAt(DateTime.UtcNow);
            var facing = player.Facing;
            var (dx, dz) = TileMath.Rotate(0, 3, facing);
            try
            {
                var made = world.Dungeons.Spawn(map, exit.ToUpperInvariant(), at + new Vector2((float)dx, (float)dz), facing + 180);
                ctx.Reply($"{made}: prefab {made.Prefab} (visualization {made.Visualization}), clustertype {made.ClusterType}");
            }
            catch (InvalidOperationException e) { throw new CommandError(e.Message); }
            return;
        }
        var here2 = player.PositionAt(DateTime.UtcNow);
        var lines = new List<string>();
        foreach (var e in world.Dungeons.ExitsOf(map).OrderBy(e => Vector2.Distance(e.Position, here2)))
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  #{e.Id} {e.Name} at ({e.Position.X:0.##}, {e.Position.Y:0.##}), {Vector2.Distance(e.Position, here2):0} m, {e.SubtypeName} level {e.Level}"));
        foreach (var g in world.Dungeons.Groups)
            lines.Add($"  dungeon {g.Ids[0]}: {g.Plan.Generator.Name} T{g.Tier} from {g.Entrance}, {g.Floors.Count(f => f is not null)}/{g.FloorCount} floor(s) made"
                      + (g.EmptySince is null ? ", players inside" : ", empty")
                      + string.Create(CultureInfo.InvariantCulture, $", {g.Players} player(s){(g.Bonus != 1 ? $", dungeon of the day x{g.Bonus:0.##}" : "")}"));
        // StageH PvE: the dungeon of the day (Dungeons:DailyBonus).
        var daily = world.DungeonSettings.DailyFaction(new DateTime(world.GameNow, DateTimeKind.Utc));
        lines.Add(daily is null ? "  no dungeon of the day" : string.Create(CultureInfo.InvariantCulture,
            $"  dungeon of the day: {RandomDungeons.DailyName(daily)} (x{world.DungeonSettings.DailyBonus:0.##} fame, silver and loot until {world.DungeonSettings.DailyResetHour:00}:00 UTC)"));
        ctx.Reply($"{map.Cluster}:\n{string.Join("\n", lines)}");
    }
}
