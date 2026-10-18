using System.Globalization;
using System.Numerics;
using AlbionLocal.Photon;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Systems;

/// <summary>
/// The Mists, phase B (analysis/work/stageL/mists/SPEC.md §8 items 1-3, 6): Knightfall Abbey (the MistsDungeonEntrance portals in waves inside
/// an open zone, the dungeon's room rewards, boss chests and treasures; its exits are Handlers/World/MistsHandler's), the portal wisps (at the
/// end of a zone's life, and a black zone's resource wisp) whose death leaves a portal of their mode and rarity, the wisp spawns (525/526), and
/// under Mists:UnrestrictedZones and Pvp:Mode Official the camps' free-for-all circles (544). Under <see cref="World.Sync"/>.
/// </summary>
public sealed partial class Mists
{
    /// <summary>The treasure subtype a black zone's resource wisp is planned under (its name: the resource mode) [design].</summary>
    public const string ResourceWisp = "WISPS_RESOURCE";

    /// <summary>MISTS_PORTAL_WISP_SUICIDE (mobs.xml: after 0.5 s of fight) and the 3.5 s it flees before it dies: a wisp's end after it noticed a player.</summary>
    public const string WispSuicide = "MISTS_PORTAL_WISP_SUICIDE";
    public static readonly TimeSpan WispFuse = TimeSpan.FromSeconds(0.5 + 3.5);

    /// <summary>A uniform number between <paramref name="a"/> and <paramref name="b"/> (in any order).</summary>
    private static double Between(Random rng, double a, double b) => Math.Min(a, b) + rng.NextDouble() * Math.Abs(b - a);

    /// <summary>
    /// A world cluster's mists objects (at its fill): the Brecilien entrances of the mists clusters (5100-5103, phase A), and Brecilien 5001's
    /// static entrance (522) and roads entrance (528, phase B).
    /// </summary>
    private List<WorldObject> WorldObjectsOf(ClusterTemplate template)
    {
        var objects = new List<WorldObject>();
        foreach (var m in template.Critical)
            switch (m.Type)
            {
                case "mistscityentrance": objects.Add(new MistsCityEntrance(world.ObjectIds.Next(), m)); break;
                case "mistsstaticentrance": objects.Add(new MistsStaticEntrance(world.ObjectIds.Next(), m)); break;
                case "mistscityroadsentrance": objects.Add(new MistsCityRoadsEntrance(world.ObjectIds.Next(), m)); break;
            }
        return objects;
    }

    /// <summary>
    /// Phase B at a zone's fill (<see cref="Populate"/>): its wisp spawns (525), the camps' free-for-all circles (544, <see cref="CampsUnrestricted"/>),
    /// the first Knightfall Abbey wave of an open mode (uniform(initial, final block time) × Mists:BlockFactor after the start), the times of the
    /// end-of-life wisps (<see cref="MistsWispTiming"/>) and, in Knightfall Abbey, its room rewards and boss chests.
    /// </summary>
    private void PopulateB(MistsZone zone, Map map, ClusterTemplate template, List<WorldObject> objects)
    {
        var rng = map.Random;
        foreach (var s in template.MistsSpots.Where(s => s.Kind == "mistswispspawn")) objects.Add(new MistsWispSpawn(world.ObjectIds.Next(), s));
        if (CampsUnrestricted)
            foreach (var s in template.MistsSpots.Where(s => s.Kind == "unrestrictedpvpzone")) objects.Add(new MistsUnrestrictedZone(world.ObjectIds.Next(), s));
        if (zone.Mode.DungeonEntrances is { Tiers.Count: > 0 } e && Data?.Modes.ContainsKey(e.TargetMode) == true)
            zone.NextWave = zone.Created + TimeSpan.FromSeconds(Between(rng, e.InitialBlockSeconds, e.FinalBlockSeconds) * Options.BlockFactor);
        if (Data?.WispTimings.GetValueOrDefault(zone.Mode.Type) is { BeforeShutdown: > 0 } w)
        {
            var at = zone.ShutdownAt - TimeSpan.FromSeconds(w.BeforeShutdown) + TimeSpan.FromSeconds(w.IntervalStart);
            if (at < zone.Created) at = zone.Created;
            do zone.WispTimes.Add(at);
            while (w.Interval > 0 && (at += TimeSpan.FromSeconds(w.Interval)) < zone.ShutdownAt);
        }
        if (zone.Mode.IsDungeon) objects.AddRange(Rooms(zone, map, template));
    }

    /// <summary>
    /// Knightfall Abbey's room objects (SPEC §8 item 1): each BOSS room's boss chest (its lootchestspawnpoint MD_UNDEAD_SOLO_BOSS, _DUO_ in a duo
    /// mode; BossLocked until the room's boss dies, RandomDungeons.BossChecks), and each MOBS room's RoomSetting draw for its set (rose, scale,
    /// sword: a MD_UNDEAD_SOLO|DUO_REWARD chest, 15, or a shrine, 85) at its reward spot (the statue: the room's BOSS-layer chest tile). Both
    /// come once the room's mobs are dead (<see cref="KilledB"/>): the chest appears then (lootchests.xml: ismistdungeonchest, its
    /// spawnsoundevent Play_mist_dungeon_clear_room_transition), the shrine blesses the players in the room [design: the shrine object's
    /// protocol is not traced]. Returns the boss chests; the room chests are only planned.
    /// </summary>
    private List<WorldObject> Rooms(MistsZone zone, Map map, ClusterTemplate template)
    {
        var made = new List<WorldObject>();
        if (world.Clusters is not { } store || store.LootChestsIfLoaded is not { } chests || zone.Build.Layout.Templates is not [{ Rooms: { } flags } dungeon, ..]
            || Data?.DungeonMetas.GetValueOrDefault(dungeon.Template) is not { } meta || store.Layout(dungeon.Folder, dungeon.Template) is not { } layout) return made;
        string Spawnpoint(string name) => zone.Mode.Duo && name.Replace("_SOLO_", "_DUO_", StringComparison.Ordinal) is var duo && chests.Spawnpoints.ContainsKey(duo) ? duo : name;
        foreach (var point in template.LootChests.Where(p => RandomDungeons.IsBossChest(p.SpawnBehaviour)))
        {
            var name = Spawnpoint(point.SpawnBehaviour);
            if (!chests.Spawnpoints.TryGetValue(name, out var behaviour)) continue;
            if (world.Dungeons.MakeChest(map, point with { SpawnBehaviour = name, Behaviour = behaviour }, chests, lootTier: zone.Tier) is { } chest) made.Add(chest);
        }
        // The reward spot of each room: its BOSS layer's chest tile (the statue of _MISTS_GREEN_DNG_UND_REWARD_STATUE stands there in its MOBS layer).
        var spots = new Dictionary<int, Vector2>();
        foreach (var t in layout.Tiles)
            if (t.Component.Name.LocalName == "lootchestspawnpoint" && t.Layer is { Length: 10 } l && l.StartsWith("R#", StringComparison.Ordinal) && l.EndsWith("_BOSS", StringComparison.Ordinal)
                && int.TryParse(l.AsSpan(2, 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var room))
                spots.TryAdd(room, new Vector2((float)t.X, (float)t.Z));
        var setting = Data.RoomSettings.GetValueOrDefault(zone.Mode.RoomSetting) ?? Data.RoomSettings.GetValueOrDefault("default");
        var shrines = 0;
        for (var i = 0; i < flags.Length && i < meta.Rooms.Count; i++)
        {
            if ((flags[i] & MistsGenerator.RoomBoss) != 0 || setting?.GetValueOrDefault(MistsGenerator.SetOf(flags[i])) is not { Count: > 0 } rewards
                || !spots.TryGetValue(meta.Rooms[i].Index, out var at)) continue;
            var reward = DungeonGenerator.Pick(map.Random, [.. rewards.Where(r => r.Weight > 0).Select(r => (r, r.Weight))]);
            if (reward.Kind == "Shrine")
            {
                zone.Shrines[i] = reward;
                shrines++;
                continue;
            }
            var name = Spawnpoint(reward.Name);
            if (!chests.Spawnpoints.TryGetValue(name, out var behaviour)) continue;
            zone.RewardChests[i] = new LootChestSpawn
            {
                Key = new SpawnKey(map.Cluster, "mists-room", map.CustomSpawns++), Tile = "", Position = at, Direction = 0,
                Component = new System.Xml.Linq.XElement("lootchestspawnpoint", new System.Xml.Linq.XAttribute("spawnbehaviour", name)), SpawnBehaviour = name, Behaviour = behaviour,
            };
        }
        Log.Info("world", $"{zone}: {flags.Count(f => (f & MistsGenerator.RoomBoss) != 0)} boss rooms, {made.Count} boss chest(s), {zone.RewardChests.Count} room chest(s), {shrines} shrine(s)");
        return made;
    }

    /// <summary>
    /// Phase B every tick of a live zone (<see cref="Update"/>): a Knightfall Abbey wave when due (the next one uniform(min, max wave interval)
    /// later; with exitspawnchance a portal of a tier drawn by weight from those of the zone's rarity, alive portaldisappearancetimer, at a spot
    /// playerdistanceblocking away from the players), the end-of-life wisps when due, and each wisp's end: a player within its aggroradius starts
    /// its fight, <see cref="WispFuse"/> later it dies (MISTS_PORTAL_WISP_SUICIDE) and leaves its portal.
    /// </summary>
    private void UpdateB(MistsZone zone, Map map, TimeSpan now)
    {
        var rng = map.Random;
        if (zone.NextWave is { } wave && now >= wave && zone.Mode.DungeonEntrances is { } e && Data?.Modes.GetValueOrDefault(e.TargetMode) is { } target)
        {
            zone.NextWave = now + TimeSpan.FromSeconds(Math.Max(1, Between(rng, e.MinWaveSeconds, e.MaxWaveSeconds)));
            if (rng.NextDouble() < e.Chance && now + TimeSpan.FromSeconds(e.LifeSeconds) <= zone.ShutdownAt) DungeonPortal(zone, map, null);
        }
        while (zone.WispTimes.Count > 0 && now >= zone.WispTimes[0])
        {
            zone.WispTimes.RemoveAt(0);
            SpawnWisp(zone, map, zone.Mode, RollRarity(zone.Reroll, rng), null, LeadsTo(zone));
        }
        if (zone.Wisps.Count == 0) return;
        var utc = DateTime.UtcNow;
        foreach (var (id, wisp) in zone.Wisps.ToList())
        {
            if (!map.Objects.TryGetValue(id, out var o) || o is not Mob { Dead: false, Removed: false } mob)
            {
                zone.Wisps.Remove(id);
                SetSpawn(map, wisp.Spot, false);
                continue;
            }
            if (wisp.AggroAt is null)
            {
                var reach = mob.Template.AggroRadius > 0 ? mob.Template.AggroRadius : 5;
                if (map.Players.Values.Any(p => p.Combat.Down is null && Vector2.Distance(p.PositionAt(utc), mob.Position) <= reach)) wisp.AggroAt = now;
                continue;
            }
            if (now >= wisp.AggroAt + WispFuse) Burst(zone, map, mob, wisp);
        }
    }

    /// <summary>
    /// A Knightfall Abbey portal of <paramref name="zone"/>'s MistsDungeonEntrance (a wave's, or .mists dungeon's at <paramref name="at"/>): a tier
    /// drawn by weight from those of the zone's rarity, at a spot playerdistanceblocking away from the players, alive portaldisappearancetimer
    /// (120 s); null when the mode has none or no prefab fits.
    /// </summary>
    public RandomDungeonExit? DungeonPortal(MistsZone zone, Map map, Vector2? at)
    {
        if (zone.Mode.DungeonEntrances is not { } e || Data?.Modes.GetValueOrDefault(e.TargetMode) is not { } target) return null;
        var tiers = e.Tiers.Where(t => t.Weight > 0 && t.MinDifficulty <= zone.Level).ToList();
        if (tiers.Count == 0 || (at ?? PortalSpot(zone, map, e.PlayerDistance)) is not { } spot) return null;
        var tier = DungeonGenerator.Pick(map.Random, [.. tiers.Select(t => (t, t.Weight))]);
        var portal = SpawnPortal(map, target, 0, tier.Tier, tier.Visualization, spot, e.LifeSeconds);
        if (portal is not null) Log.Info("world", $"{zone}: a Knightfall Abbey portal T{tier.Tier}, {portal}");
        return portal;
    }

    /// <summary>
    /// A wisp's end (its suicide, MISTS_PORTAL_WISP_SUICIDE): dead with a HealthUpdate to 0 to its observers, off the map after its corpse time
    /// (no respawn), its spawn free again, and its &lt;MistEntrance/&gt; loot: a portal of its mode and rarity where it died (alive
    /// portaldisappearancetimer, 30 s; gone 5 s after a use).
    /// </summary>
    private void Burst(MistsZone zone, Map map, Mob mob, MistsWisp wisp)
    {
        var ticks = world.GameNow;
        zone.Wisps.Remove(mob.Id);
        SetSpawn(map, wisp.Spot, false);
        var before = mob.Health.At(ticks);
        mob.Dead = true;
        mob.AI.OnDeath();
        mob.TargetId = 0;
        mob.Health.RegenPerSecond = 0;
        mob.Health.Set(0, ticks);
        Combat.EnemyGone(map, mob);
        map.SendToObservers(mob, Combat.HealthUpdate(mob.Id, ticks, -before, 0, EffectType.Magic, HitOrigin.Direct, mob.Id, (short)(world.Spells?.IdOf(WispSuicide) ?? -1)));
        map.Timers.At(world.Clock.Now + TimeSpan.FromSeconds(mob.Template.PostDeathLifetime), () => SpawnManager.Despawn(mob, world, respawn: false));
        var life = Data?.Global("portaldisappearancetimer", 30) ?? 30;
        var portal = SpawnPortal(map, wisp.Target, wisp.Level, zone.Tier, wisp.Visualization, mob.Position, life);
        Log.Info("world", $"{zone}: the wisp {mob.Template.Name} bursts{(portal is null ? ", no portal (no prefab)" : $" into {portal}")} → {wisp.Target.Name} rarity {wisp.Level}");
    }

    /// <summary>
    /// A portal wisp of <paramref name="of"/>'s level <paramref name="level"/> (its wispmob, else its first level's) in <paramref name="zone"/>: at
    /// <paramref name="at"/>, else at a free wisp spawn's wispspawnpos (now active, 526), else at a camp's spot; its portal leads into
    /// <paramref name="target"/> (null: <paramref name="of"/>) at that rarity, shown by the level's wispmobexitvisualizationref. The wisp and
    /// its portal look like the mode they lead into: a target other than <paramref name="of"/> (a resource zone's end-of-life wisps into the
    /// black mists) gives the target's level, the data's own <paramref name="of"/> level only when the target has none [design].
    /// </summary>
    public Mob? SpawnWisp(MistsZone zone, Map map, MistsMode of, byte level, Vector2? at, MistsMode? target)
    {
        var look = target ?? of;
        var entry = (look.Level(level) ?? look.Levels.FirstOrDefault()) is { WispMob: not null } led ? led : of.Level(level) ?? of.Levels.FirstOrDefault();
        if (entry?.WispMob is not { } name || world.Clusters?.MobsIfLoaded?.Find(name) is not { } template)
        {
            Combat.LogOnce("mists-wisp:" + of.Name, $"mists mode {of.Name} level {level}: no portal wisp mob ({entry?.WispMob}) in mobs.xml, none spawned");
            return null;
        }
        var spawn = at is null ? map.Objects.Values.OfType<MistsWispSpawn>().Where(s => !s.Active).OrderBy(_ => map.Random.Next()).FirstOrDefault() : null;
        var spots = map.Template?.MobSpawnPoints;
        var pos = at ?? spawn?.WispAt ?? (spots is { Count: > 0 } ? spots[map.Random.Next(spots.Count)].Position : (Vector2?)null);
        if (pos is not { } where) return null;
        var mob = SpawnManager.SpawnAt(map, template, where, (float)(map.Random.NextDouble() * 360), world);
        zone.Wisps[mob.Id] = new MistsWisp(target ?? of, (byte)Math.Clamp((int)level, 0, 4), entry.WispVisualization, spawn);
        SetSpawn(map, spawn, true);
        Log.Info("world", string.Create(CultureInfo.InvariantCulture, $"{zone}: a portal wisp {name} at ({where.X:0.#}, {where.Y:0.#}) → {(target ?? of).Name} rarity {level}"));
        return mob;
    }

    /// <summary>A wisp spawn's state changes: MistsWispSpawnStateChange (526) {0 id, 1 active} to its observers.</summary>
    private static void SetSpawn(Map map, MistsWispSpawn? spawn, bool active)
    {
        if (spawn is null || spawn.Active == active) return;
        spawn.Active = active;
        if (spawn.Map == map) map.SendToObservers(spawn, new MistsWispSpawnStateChangeEvent { ddn = spawn.Id, ebl = active });
    }

    /// <summary>
    /// A spot for a Knightfall Abbey portal: a MistsSpecialExitSpawnpoint of the zone, else a camp's spot; one at least <paramref name="distance"/>
    /// from every player and 30 m from another portal when there is one; null on a map without either.
    /// </summary>
    private static Vector2? PortalSpot(MistsZone zone, Map map, double distance)
    {
        var candidates = zone.Template.MistsSpots.Where(s => s.Kind == "MistsSpecialExitSpawnpoint").Select(s => s.Position).ToList();
        if (candidates.Count == 0) candidates = [.. zone.Template.MobSpawnPoints.Select(s => s.Position).Distinct()];
        if (candidates.Count == 0) return null;
        var utc = DateTime.UtcNow;
        var players = map.Players.Values.Select(p => p.PositionAt(utc)).ToList();
        var portals = map.Objects.Values.OfType<RandomDungeonExit>().Select(x => x.Position).ToList();
        var free = candidates.Where(c => players.All(p => Vector2.Distance(p, c) >= distance) && portals.All(p => Vector2.Distance(p, c) >= 30)).ToList();
        var from = free.Count > 0 ? free : candidates;
        return from[map.Random.Next(from.Count)];
    }

    /// <summary>
    /// A portal of <paramref name="mode"/> inside a zone (event 324 key 16 = the mode, key 9 = <paramref name="level"/>): the RandomExit of
    /// randomdungeons.xml with that MistsGenerator (an open mode's), else one of the mode's name with <paramref name="visualization"/>
    /// (Knightfall Abbey's T&lt;n&gt;_MISTS_DUNGEON_*, a resource wisp's MISTS_RESOURCE_BLACK); the dungeon map behind it of
    /// <paramref name="tier"/>, gone after <paramref name="lifeSeconds"/> unused or 5 s after a use. Entered by ENTER_MISTS like the open world's.
    /// </summary>
    private RandomDungeonExit? SpawnPortal(Map map, MistsMode mode, byte level, int tier, string? visualization, Vector2 at, double lifeSeconds)
    {
        if (world.Clusters?.DungeonsIfLoaded is not { } rd) return null;
        var info = rd.Exits.Values.FirstOrDefault(x => x.MistsMode == mode.Name && (visualization is null || x.Visualization == visualization))
                   ?? new RandomExitInfo { Name = mode.Name, MistsMode = mode.Name, Visualization = visualization, LifetimeAfterLastUseSeconds = 5 };
        return world.Dungeons.SpawnMistsPortal(map, info, at, (float)(map.Random.NextDouble() * 360), level, tier, lifeSeconds);
    }

    /// <summary>
    /// Phase B: a mob of a zone died (<see cref="Killed"/>): a wisp killed by a player leaves its portal all the same; in Knightfall Abbey the
    /// last mob of a MOBS room (its spawn point in the room's rectangles) clears it: its reward chest appears at the statue, or its shrine
    /// blesses every player in the room (BUFFSHRINE_MD_&lt;SET&gt;_BUFF_APPLY, buffshrines.xml's effectspell; chat line Mists.Shrine).
    /// </summary>
    private void KilledB(MistsZone zone, Map map, Mob mob)
    {
        if (zone.Wisps.TryGetValue(mob.Id, out var wisp))
        {
            zone.Wisps.Remove(mob.Id);
            SetSpawn(map, wisp.Spot, false);
            var life = Data?.Global("portaldisappearancetimer", 30) ?? 30;
            SpawnPortal(map, wisp.Target, wisp.Level, zone.Tier, wisp.Visualization, mob.Position, life);
        }
        if (zone.Shrines.Count + zone.RewardChests.Count == 0 || zone.Build.Layout.Templates is not [{ } dungeon, ..]
            || Data?.DungeonMetas.GetValueOrDefault(dungeon.Template) is not { } meta) return;
        foreach (var index in zone.Shrines.Keys.Concat(zone.RewardChests.Keys).ToList())
        {
            if (zone.Cleared.Contains(index) || index >= meta.Rooms.Count || !meta.Rooms[index].Contains(mob.Spawn.Position)) continue;
            var room = meta.Rooms[index];
            if (map.Objects.Values.OfType<Mob>().Any(m => m != mob && !m.Dead && !m.Removed && room.Contains(m.Spawn.Position))) continue;
            zone.Cleared.Add(index);
            if (zone.RewardChests.TryGetValue(index, out var point))
            {
                if (world.Clusters?.LootChestsIfLoaded is { } chests && world.Dungeons.MakeChest(map, point, chests, lootTier: zone.Tier) is { } chest)
                {
                    map.AddObject(chest);
                    Log.Info("world", $"{zone}: room {room.Index} cleared, its reward chest {chest} appears");
                }
                continue;
            }
            if (!zone.Shrines.TryGetValue(index, out var shrine)) continue;
            var spell = shrine.Name.Replace("MD_SHRINE_", "BUFFSHRINE_MD_", StringComparison.Ordinal) + "_APPLY";
            var set = shrine.Name.Replace("MD_SHRINE_", "", StringComparison.Ordinal).Replace("_BUFF", "", StringComparison.Ordinal).ToLowerInvariant();
            var utc = DateTime.UtcNow;
            foreach (var p in map.Players.Values.Where(p => room.Contains(p.PositionAt(utc)) || Vector2.Distance(p.PositionAt(utc), room.Centre) <= 30))
                if (Pvp.ApplySpell(p, spell, world.GameNow)) Expeditions.Tell(p, Settings.Text("Shrine", ("set", set)));
            Log.Info("world", $"{zone}: room {room.Index} cleared, its {shrine.Name} shrine blesses ({spell})");
        }
    }
}
