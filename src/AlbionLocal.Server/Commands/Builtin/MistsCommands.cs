using System.Globalization;
using System.Numerics;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>
/// The Mists (stageL mists SPEC §7 item 7, §8): .mists (status) and .mists standing for every player; .mists portal, .mists list, .mists close and,
/// phase B, .mists dungeon, .mists wisp and .mists standing add for GMs (<see cref="Mists"/>).
/// </summary>
public static class MistsCommands
{
    /// <summary>.mists: the zone the player is in (mode, map, rarity, tier, time to its shutdown, the way back) or that it is in none.</summary>
    [Command("mists", Level = AccountLevel.Player, Help = "the mists zone you are in: its mode, rarity, time left and your way back")]
    public static void Status(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        var world = ctx.World;
        if (world.Mists.Find(player.Map?.Cluster) is not { } zone)
        {
            ctx.Reply(world.Config.Mists.Enabled ? "you are in no mists zone (mists portals stand on the random dungeon spots of royal T5-T7, outlands and the Brecilien wilds)"
                : "the mists are closed on this server");
            return;
        }
        var left = Math.Max(0, (zone.ShutdownAt - world.Now).TotalMinutes);
        ctx.Reply(string.Create(CultureInfo.InvariantCulture,
            $"{zone.Mode.DisplayName} ({zone.Mode.Name}) on {zone.Map.Id}: {Mists.RarityNames[Math.Clamp((int)zone.Level, 0, 4)]}, T{zone.Tier}, shuts down in {left:0} min; way back {player.Character.DungeonReturn?.Cluster ?? world.Config.Mists.FallbackCluster}"));
    }

    /// <summary>
    /// .mists portal &lt;mode&gt; [rarity]: a mists portal of that mode (MISTS_SOLO_YELLOW, MISTS_SOLO_BLACK, MISTS_DUO_BLACK) and rarity (0-4; default
    /// its roll) on the random dungeon marker nearest to the player, in place of what stands there.
    /// </summary>
    [Command("mists portal", Level = AccountLevel.GameMaster, Help = "puts a mists portal of mode [rarity 0-4] on the nearest random dungeon marker")]
    public static void Portal(CommandContext ctx, string mode, int rarity = -1)
    {
        var player = ctx.RequirePlayer();
        var world = ctx.World;
        if (player.Map is not { Template: { } template } map) throw new CommandError("the player's cluster is not resolved yet");
        if (world.Clusters?.DungeonsIfLoaded is not { } data) throw new CommandError("randomdungeons.xml is not read (no cluster with exit markers resolved yet)");
        var exit = data.Exits.Values.FirstOrDefault(e => string.Equals(e.MistsMode, mode, StringComparison.OrdinalIgnoreCase) || string.Equals(e.Name, mode, StringComparison.OrdinalIgnoreCase) && e.MistsMode is not null)
                   ?? throw new CommandError($"{mode}: no mists portal (MISTS_SOLO_YELLOW, MISTS_SOLO_BLACK, MISTS_DUO_BLACK)");
        if (rarity is < -1 or > 4) throw new CommandError("rarity 0-4");
        var at = player.PositionAt(DateTime.UtcNow);
        var marker = template.DungeonMarkers.Where(m => m.Type != "FIXED").MinBy(m => Vector2.Distance(m.Position, at)) ?? throw new CommandError($"{map.Cluster} has no random dungeon marker");
        try
        {
            var made = world.Dungeons.SpawnOnMarker(map, marker, exit.Name);
            if (rarity >= 0) world.Dungeons.Reroll(made, (byte)rarity);
            ctx.Reply(string.Create(CultureInfo.InvariantCulture, $"{made}: {Vector2.Distance(made.Position, at):0} m away, prefab {made.Prefab}"));
        }
        catch (InvalidOperationException e) { throw new CommandError(e.Message); }
    }

    /// <summary>.mists list: the live mists zones.</summary>
    [Command("mists list", Level = AccountLevel.GameMaster, Help = "the live mists zones")]
    public static void List(CommandContext ctx)
    {
        var lines = ctx.World.Mists.Describe().ToList();
        ctx.Reply(lines.Count == 0 ? "no mists zone" : $"mists zones:\n{string.Join("\n", lines)}");
    }

    /// <summary>.mists close [id]: that zone (default the player's) closes now: everyone inside is pushed out to the way back.</summary>
    [Command("mists close", Level = AccountLevel.GameMaster, Help = "closes the player's mists zone (or the one of that id) now")]
    public static void Close(CommandContext ctx, string id = "")
    {
        var world = ctx.World;
        var zone = id.Length > 0 ? world.Mists.Find(id) ?? world.Mists.Zones.FirstOrDefault(z => !z.Closed && z.Id.Contains(id, StringComparison.OrdinalIgnoreCase))
            : world.Mists.Find(ctx.RequirePlayer().Map?.Cluster);
        if (zone is null) throw new CommandError(id.Length > 0 ? $"no mists zone {id}" : "the player is in no mists zone");
        ctx.Reply($"closing {zone}");
        world.Mists.Close(zone, "closed by a GM");
    }

    /// <summary>.mists dungeon: a Knightfall Abbey portal (the zone mode's MistsDungeonEntrance, a tier by its weights) 4 m in front of the player in its zone.</summary>
    [Command("mists dungeon", Level = AccountLevel.GameMaster, Help = "puts a Knightfall Abbey portal next to you in your mists zone")]
    public static void Dungeon(CommandContext ctx)
    {
        var player = ctx.RequirePlayer();
        var world = ctx.World;
        if (world.Mists.Find(player.Map?.Cluster) is not { } zone || player.Map is not { } map) throw new CommandError("the player is in no mists zone");
        var at = player.PositionAt(DateTime.UtcNow) + new Vector2(4, 0);
        var portal = world.Mists.DungeonPortal(zone, map, at) ?? throw new CommandError($"{zone.Mode.Name} has no Knightfall Abbey portal (or no prefab, randomdungeons.xml unread)");
        ctx.Reply($"{portal}");
    }

    /// <summary>.mists wisp [rarity]: a portal wisp of the zone (its death leaves a portal of the zone's next mode at that rarity, default a roll) 6 m from the player.</summary>
    [Command("mists wisp", Level = AccountLevel.GameMaster, Help = "puts a portal wisp [rarity 0-4] next to you in your mists zone")]
    public static void Wisp(CommandContext ctx, int rarity = -1)
    {
        var player = ctx.RequirePlayer();
        var world = ctx.World;
        if (world.Mists.Find(player.Map?.Cluster) is not { } zone || player.Map is not { } map) throw new CommandError("the player is in no mists zone");
        if (rarity is < -1 or > 4) throw new CommandError("rarity 0-4");
        var level = rarity >= 0 ? (byte)rarity : world.Mists.RollRarity(zone.Reroll, map.Random);
        var mob = world.Mists.SpawnWisp(zone, map, zone.Mode, level, player.PositionAt(DateTime.UtcNow) + new Vector2(6, 0), world.Mists.LeadsTo(zone))
                  ?? throw new CommandError($"{zone.Mode.Name} has no portal wisp of rarity {level}");
        ctx.Reply($"{mob}: it bursts into a portal of {world.Mists.LeadsTo(zone).Name} (rarity {level}) once you come near");
    }

    /// <summary>.mists standing: the player's Brecilien standing, its rank and the favour to spend at the City Trader.</summary>
    [Command("mists standing", Level = AccountLevel.Player, Help = "your Brecilien standing, rank and favour")]
    public static void Standing(CommandContext ctx)
    {
        var c = ctx.RequirePlayer().Character;
        ctx.Reply(string.Create(CultureInfo.InvariantCulture, $"Brecilien standing {c.MistStanding / 10000:N0} (rank {Mists.RankOf(c.MistStanding)} of 10), favour {c.MistFavor / 10000:N0}"));
    }

    /// <summary>.mists standing add &lt;points&gt;: the player gains that much Brecilien standing (and its favour), as from the mists' kills.</summary>
    [Command("mists standing add", Level = AccountLevel.GameMaster, Help = "gives you <points> Brecilien standing")]
    public static void StandingAdd(CommandContext ctx, long points)
    {
        var player = ctx.RequirePlayer();
        if (points <= 0) throw new CommandError("points above 0");
        ctx.World.Mists.GainStanding(player, checked(points * 10000));
        Standing(ctx);
    }
}
