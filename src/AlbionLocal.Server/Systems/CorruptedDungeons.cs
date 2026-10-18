using System.Globalization;
using System.Numerics;
using AlbionLocal.Photon;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Systems;

/// <summary>
/// One corrupted dungeon run (stageL corrupted SPEC §4.2): its instance id "@CORRUPTEDDUNGEON@&lt;guid&gt;", the world.xml map and the variant
/// the server resolves it as (<see cref="ClusterVariant"/>: the difficulty's tier and cluster type), the owner with the way back, the
/// points and the fame of the counted mobs they come from, the demon, the completion, the chests and the times.
/// </summary>
public sealed class CorruptedRun
{
    public required Guid Guid { get; init; }
    public string Id => CorruptedData.InstanceId(Guid);
    public required CorruptedData.Difficulty Difficulty { get; init; }

    /// <summary>The world.xml cluster (CORRUPT-0nn, ChangeCluster key 1) and the store's key of its variant for <see cref="Difficulty"/>.</summary>
    public required string World { get; init; }
    public required string Variant { get; init; }

    /// <summary>The character the run is for (solo: SPEC D1) and its way back (the place of the enter cast).</summary>
    public required Guid Owner { get; init; }
    public required string OwnerName { get; init; }
    public required DungeonReturn Back { get; init; }

    /// <summary>Where a player comes in: the Exit_CORRUPT tile's spawnpos (SPEC §1.2).</summary>
    public required Arrival Start { get; init; }

    /// <summary>The infamy bonus of the owner's infamy at the start (<see cref="CorruptedData.Difficulty.Bonus"/>) on the run's loot, chests and silver.</summary>
    public required double Bonus { get; init; }

    public required TimeSpan StartedAt { get; init; }

    /// <summary>The points so far (451), and the fame each counted mob of the map is worth with their sum W (SPEC §4.3), set at the fill.</summary>
    public int Points { get; set; }
    public Dictionary<long, double> Worth { get; } = [];
    public double Weight { get; set; }

    /// <summary>The demon's spawn point (CD_DEMON_ENDBOSS of the template), its mob once spawned (449 kind 1) and where.</summary>
    public MobSpawnPoint? DemonPoint { get; set; }
    public long DemonId { get; set; }
    public bool DemonSpawned { get; set; }
    public Vector2 DemonAt { get; set; }

    public bool Completed { get; set; }
    public TimeSpan? CompletedAt { get; set; }

    /// <summary>Its infamy has been credited (the completion's, or the share of an early leave): never twice.</summary>
    public bool Credited { get; set; }

    /// <summary>The owner's completion rewards wait for its next Join (not on the map at the demon's death).</summary>
    public bool Owed { get; set; }

    /// <summary>The owner left (an exit, the escape, .cd leave, a death's respawn outside): the instance closes Corrupted:EmptyCloseSeconds after it is empty.</summary>
    public bool OwnerLeft { get; set; }

    /// <summary>Characters sent in and not joined yet, with the clock time until which each counts as inside.</summary>
    public Dictionary<Guid, TimeSpan> Arriving { get; } = [];

    /// <summary>Whether the owner has joined it once (the arrival buff and chat line go once).</summary>
    public bool Entered { get; set; }

    public TimeSpan? EmptySince { get; set; }
    public bool Closed { get; set; }

    /// <summary>The "closes in 60 s" line of the time limit has gone out.</summary>
    public bool Warned { get; set; }

    /// <summary>The exit tile (Exit_CORRUPT) the portal stands on, the portal once placed, the chests (the boss chest apart).</summary>
    public ExitSpawn? ExitTile { get; set; }
    public CorruptedExitPortal? Portal { get; set; }
    public LootChest? BossChest { get; set; }
    public List<LootChest> Chests { get; } = [];

    // ---- phase B (SPEC §4.4, §6) ----

    /// <summary>The <see cref="World.Clock"/> time of the owner's first Join: the shrine's window (shrinetimelimit) and the rolls' safe period count from it.</summary>
    public TimeSpan? EnteredAt { get; set; }

    /// <summary>The Demonic Shrine of its start room (458), and the owner's consent by it (SPEC §4.4: nothing else makes it a target).</summary>
    public CorruptedShrine? Shrine { get; set; }
    public bool Consented { get; set; }
    public TimeSpan? ConsentedAt { get; set; }

    /// <summary>The invasion under way or ending (its invader still inside), and how many this run has had (Corrupted:MaxInvasionsPerRun).</summary>
    public CorruptedInvasion? Invasion { get; set; }
    public int Invasions { get; set; }

    /// <summary>The next roll of the rolls (rollinterval), and when a consenting owner's bot invader comes (content/corrupted.json Rolls Fixed).</summary>
    public TimeSpan NextRoll { get; set; }
    public TimeSpan? BotDue { get; set; }

    /// <summary>The owner is away invading another run (its run waits for it, never closing meanwhile).</summary>
    public bool Away { get; set; }

    /// <summary>The chain (SPEC §6 phase B 4): the dungeons before this one in the streak (0 = the first), its chain exit once the demon is slain.</summary>
    public int Streak { get; init; }
    public CorruptedExitPortal? ChainExit { get; set; }

    /// <summary>Its completion's silver, fame and infamy factor of the streak: 1 + min(Corrupted:ChainBonusMax, Streak × Corrupted:ChainBonus).</summary>
    public double ChainFactor { get; init; } = 1;

    public override string ToString() => $"corrupted dungeon {Difficulty.Rank} {World} {Id}";
}

/// <summary>
/// The corrupted dungeons, phase A (analysis/work/stageL/corrupted/SPEC.md, "SPEC" here, §4.2, §5): the Hell Dens' static entrances (637),
/// the client's difficulty window (GetInfamyInfo 419, its own gates), the enter cast of slot 25 as a server cast (CastStart 14, CastFinished
/// 18, CastSpell 19; damage, a knockdown, a cancel or another action end it), the solo instance of one of the 20 maps as the variant of its
/// difficulty (the ChangeCluster push {0 id, 1 map, 4 cluster type}; SelectCharacter keys 2 and 5 on a relog), the tracker (450, 451, 449
/// kinds 1 and 2), the points of the counted mobs that draw out the demon, its completion with the boss chest, the infamy (446, the
/// character's column) and the rewards with the client's infamy bonus, the chests and their locks, the exit portal, the escape and
/// .cd leave, and the closing. Phase B (SPEC §6, CorruptedDungeons.Invasions.cs): the Demonic Shrine and the invasions by consent (a bot
/// invader, consenting players), the open world's corrupted portals (an entrance as the Hell Den's), the chain into the next dungeon, the
/// anti-reset imps and the goals' credits. Only messages the unmodified client reads. <see cref="World.Update"/> runs <see cref="Update"/>.
/// Under <see cref="World.Sync"/>.
/// </summary>
public sealed partial class CorruptedDungeons(World world)
{
    /// <summary>CharacterSpellSlot.FakeSpell (25): the entrance window's cast (SPEC §2.2 step 3).</summary>
    public const byte EnterSlot = 25;

    /// <summary>The escape slot (21): a server cast (FakeSlotCasts.StartEscape) whose end, DungonEscapeReady 467, makes the client send ChangeCluster {0: 0, 3: true} (SPEC §2.4; stageN corrupted-exit SPEC).</summary>
    public const byte EscapeSlot = 21;

    /// <summary>The cast time and range of ENTER_CURRUPTED_DUNGEON_SPELL_n without spell data (spells.xml: castingtime 3, castrange 5).</summary>
    public const double DefaultCastSeconds = 3, DefaultCastRange = 5;

    /// <summary>The spawn buff of the corrupted cluster types (healing −40 %, Corrupted:EnterBuff).</summary>
    public const string EnterBuff = "CORRUPTED_DUNGEON_ENTER";

    /// <summary>Seconds a character sent into an instance counts as inside until its Join (the client loads the cluster first).</summary>
    public const double ArrivalGraceSeconds = 120;

    /// <summary>
    /// An enter cast under way: the entrance (a Hell Den's, an open-world portal, a chain exit; null for the chain's Next Dungeon without one)
    /// and where it is cast at, the difficulty, the spell, its counter, its GameTimeStamps and the <see cref="World.Clock"/> time it is due, the
    /// map and variant it goes into, and the completed run it chains from (phase B). A <see cref="FakeSlotCast"/> (one per player with the
    /// chest opening, <see cref="FakeSlotCasts"/>): its CastStart (14) names the aim, its CastSpell (19) the entrance.
    /// </summary>
    private sealed record EnterCast(Map Map, Vector2 Aim, WorldObject? Entrance, Vector2 At, CorruptedData.Difficulty Difficulty, int SpellId, byte Counter, long Start, long End,
        TimeSpan Due, string World, string Variant, CorruptedRun? From, CorruptedDungeons Owner)
        : FakeSlotCast(Map, Aim, SpellId, Counter, Start, End, Due)
    {
        public override string What => "corrupted dungeon enter cast";

        /// <summary>CastSpell (19) names the entrance (the aim without one).</summary>
        public override Vector2 HitAt => At;

        /// <summary>Its end waits until the variant is resolved (at most 30 s more, <see cref="GivesUp"/>).</summary>
        public override bool Ready(Player p) => p.World.CachedCluster(Variant) is not null;

        public override string? GivesUp(Player p, TimeSpan now)
        {
            if (now < Due + TimeSpan.FromSeconds(30)) return null;
            Expeditions.Tell(p, Owner.Settings.Text("cd.refused", ("reason", $"the map {World} could not be read; try again")));
            return $"cluster {Variant} not resolved";
        }

        public override void Complete(Player p) => Owner.Finish(p, this);
    }

    private readonly Dictionary<string, CorruptedRun> _runs = new(StringComparer.Ordinal);

    private CorruptedOptions Options => world.Config.Corrupted;

    private CorruptedSettings Settings => world.CorruptedSettings;

    private CorruptedData? Data => world.Clusters?.CorruptedIfLoaded;

    private Random Rng => _rng ??= world.MapSeed is { } seed ? new Random(seed) : new Random();
    private Random? _rng;

    public IReadOnlyCollection<CorruptedRun> Runs => _runs.Values;

    /// <summary>The live run of id <paramref name="cluster"/>; null for any other cluster or a closed one.</summary>
    public CorruptedRun? Find(string? cluster) => cluster is not null && _runs.TryGetValue(cluster, out var r) && !r.Closed ? r : null;

    public bool IsLive(string cluster) => _runs.ContainsKey(cluster);

    /// <summary>The variant a map of live run <paramref name="cluster"/> takes its data from (World.TemplateOf); null for any other cluster.</summary>
    public string? TemplateOf(string cluster) => Find(cluster)?.Variant;

    /// <summary>The live run of <paramref name="character"/> it has not left, if any (one it left may linger until its close).</summary>
    public CorruptedRun? RunOf(Guid character) => _runs.Values.FirstOrDefault(r => !r.Closed && !r.OwnerLeft && r.Owner == character);

    /// <summary>The current infamy (type 1) of <paramref name="c"/>.</summary>
    public static long Current(Character c) => c.Infamy?.Current.GetValueOrDefault(CorruptedData.InfamyType) ?? 0;

    /// <summary>The peak infamy (type 1) of <paramref name="c"/>: at least the current one.</summary>
    public static long Peak(Character c) => Math.Max(Current(c), c.Infamy?.Peak.GetValueOrDefault(CorruptedData.InfamyType) ?? 0);

    // ---- the Hell Den and the client's window (SPEC §2.2, §4.2 steps 1-2) ----

    /// <summary>
    /// At the fill of a map (MapSpawner): a Hell Den's corrupteddungeonstaticentrance members as entrances (637); a run's instance gets its
    /// shrine (458, inactive), the exit portal under Corrupted:ExitPortal Always and its chests (BossLocked, the run's tier and bonus), its
    /// scripted spawn behaviours left out of the spawning (<see cref="Map.ScriptedSpawns"/>), no respawn, and the infamy bonus on the kills'
    /// silver and loot (<see cref="Map.Difficulty"/>).
    /// </summary>
    internal IEnumerable<WorldObject> Populate(Map map, ClusterTemplate template)
    {
        if (!Options.Enabled) return [];
        if (Find(map.Cluster) is not { } run)
            return CorruptedData.IsInstance(map.Cluster) ? []
                : [.. template.Critical.Where(m => m.Type == "corrupteddungeonstaticentrance").Select(m => new CorruptedEntrance(world.ObjectIds.Next(), m))];
        map.NoRespawn = true;
        map.ScriptedSpawns = CorruptedData.ScriptedBehaviours;
        map.Difficulty = new DungeonDifficulty(null, 1 + run.Bonus, 1 + run.Bonus, 1);
        var objects = new List<WorldObject>();
        foreach (var member in template.Critical.Where(m => m.Type == "shrine"))
        {
            // Phase B (SPEC §4.4 [V]): usable (0) while invasions may come and its window is open, else inactive (1).
            var shrine = new CorruptedShrine(world.ObjectIds.Next(), member, world.GameNow)
            {
                State = Options.Invasions == CorruptedInvasionMode.Off || run.Consented ? CorruptedShrine.Inactive : CorruptedShrine.Usable,
            };
            run.Shrine ??= shrine;
            objects.Add(shrine);
        }
        run.DemonPoint = template.MobSpawnPoints.FirstOrDefault(p => p.SpawnBehavior == CorruptedData.EndBoss && p.Block is not null);
        if (run.DemonPoint is null) Log.Warn("world", $"{run}: {template.Id} has no {CorruptedData.EndBoss} point; the run completes at its points");
        run.ExitTile = template.Exits.FirstOrDefault(e => e.Tile == "Exit_CORRUPT") ?? template.Exits.FirstOrDefault();
        if (Options.ExitPortal == CorruptedExitMode.Always && MakePortal(run) is { } portal) objects.Add(portal);
        if (world.Clusters?.LootChestsIfLoaded is { } chests)
            foreach (var point in template.LootChests)
            {
                if (world.Dungeons.MakeChest(map, point, chests, RandomDungeons.BossLocked, run.Difficulty.Tier, 1 + run.Bonus) is not { } chest) continue;
                if (RandomDungeons.IsBossChest(point.SpawnBehaviour)) run.BossChest ??= chest;
                else run.Chests.Add(chest);
                objects.Add(chest);
            }
        return objects;
    }

    /// <summary>
    /// After a run's fill (its mobs on the map): the fame of every counted mob (<see cref="CorruptedData.Counts"/>: not the traps, hazards,
    /// watchers or scripted ones) and their sum W, the points' base (SPEC §4.3); the standard chests no counted mob guards unlock.
    /// </summary>
    internal void Filled(Map map)
    {
        if (Find(map.Cluster) is not { } run) return;
        run.Worth.Clear();
        foreach (var mob in map.Objects.Values.OfType<Mob>())
            if (!mob.Dead && mob.Template.Fame > 0 && CorruptedData.Counts(mob.Spawn.SpawnBehavior)) run.Worth[mob.Id] = mob.Template.Fame;
        run.Weight = run.Worth.Values.Sum();
        Log.Info("world", string.Create(CultureInfo.InvariantCulture,
            $"{run}: {run.Worth.Count} counted mobs, fame {run.Weight / 10000:0}; the demon after {Options.ClearShare:0.##} of it ({run.Difficulty.PointsRequirement} points), bonus +{run.Bonus * 100:0.#} %, {run.Chests.Count} chest(s){(run.BossChest is null ? "" : " and the boss chest")}"));
        CheckChests(run, map);
    }

    /// <summary>
    /// GetInfamyInfo (op 419; ctp.af(afv) 0x1809942D0, SPEC §2.5): keys 0-3 the current infamy of InfamyType 1-4, 4-7 the peak, 8 "unlocked
    /// hard" (the Slayer requirement reached). Types 2-4 are the hellgates' (stageL hellgates phase B: Hell2v2, Hell5v5, Hell10v10).
    /// </summary>
    public GetInfamyInfoResponse InfamyInfo(Player p)
    {
        var peak = Peak(p.Character);
        var hard = Data?.Difficulties.Where(d => d.InfamyRequirement > 0).Select(d => d.InfamyRequirement).DefaultIfEmpty(0).Max() ?? 100_000;
        var log = p.Character.Infamy;
        long Now(int type) => Math.Min(int.MaxValue, log?.Current.GetValueOrDefault(type) ?? 0);
        long Top(int type) => Math.Min(int.MaxValue, Math.Max(Now(type), log?.Peak.GetValueOrDefault(type) ?? 0));
        return new GetInfamyInfoResponse
        {
            azh = Math.Min(int.MaxValue, Current(p.Character)), // [0] InfamyType 1, current
            azi = Now(2), azj = Now(3), azk = Now(4),           // [1-3] the hellgates' types 2-4, current
            azl = Math.Min(int.MaxValue, peak),                 // [4] type 1's peak
            azm = Top(2), azn = Top(3), azo = Top(4),           // [5-7] the hellgates' peaks
            azp = hard > 0 && peak >= hard,                     // [8] Slayer open
        };
    }

    // ---- the enter cast (SPEC §2.2 steps 2-4, §4.2 step 3) ----

    /// <summary>
    /// CastStart on slot 25 with ENTER_CURRUPTED_DUNGEON_SPELL_n (<paramref name="spellId"/>) aimed at <paramref name="aim"/>: the server's copies of
    /// the client's gates (a corrupted entrance of the map within castrange of the aim, the aim within castrange of the player; not knocked
    /// down, not in a run; the lethal-zone rule unless the cluster type is in accessiblefromarchetype; Corrupted:CheckItemPower,
    /// Corrupted:CheckInfamy; no other server cast), then a server cast of castingtime (3 s): CastStart (14) to the observers (the client
    /// casts its own). The map is picked now and its variant read meanwhile. Returns why it was refused, or null.
    /// </summary>
    public string? StartEnter(Player p, int spellId, Vector2 aim, byte counter, long start)
    {
        if (!Options.Enabled) return "corrupted dungeons are off here";
        if (Data is not { } data) return "corrupteddungeons.xml is not read";
        if (data.IndexOfSpell(world.Spells?.Name(spellId)) is not { } index || data.At(index) is not { } d) return $"spell {spellId} is no corrupted dungeon entrance spell";
        if (p.Map is not { Template: not null } map) return "not on a map";
        if (p.Combat.Down is { } down) return $"knocked down ({down.Kind})";
        var spell = world.Spells?.Template(spellId);
        var range = spell?.CastRange is > 0 and var r ? r : DefaultCastRange;
        var at = p.PositionAt(DateTime.UtcNow);
        // Phase B (SPEC §2.4, §6 phase B 4): inside a completed run its owner's Next Dungeon, at the chain exit or without one (cws.u(int):
        // the current rank); the client skips the zone, item power and load gates in a corrupted dungeon (cws.u 0x1809E6190), so does the server.
        var from = Find(map.Cluster);
        WorldObject? entrance;
        if (from is not null || CorruptedData.IsInstance(map.Cluster))
        {
            if (from is null || !Options.Chain || from.Owner != p.Character.Id || !from.Completed) return "you are in a corrupted dungeon already";
            if (from.Invasion is not null) return "not during an invasion";
            if (d.Index != from.Difficulty.Index) return $"the next dungeon keeps the rank ({from.Difficulty.Rank})";
            entrance = from.ChainExit is { } chain && Vector2.Distance(chain.Position, aim) <= range + SpellCasts.RangeTolerance ? chain : null;
        }
        else
        {
            if (RunOf(p.Character.Id) is not null) return "you are in a corrupted dungeon already";
            entrance = map.Objects.Values.Where(o => o is CorruptedEntrance or RandomDungeonExit { Info.Corrupted: true }).MinBy(e => Vector2.Distance(e.Position, aim));
            if (entrance is null || Vector2.Distance(entrance.Position, aim) > range + SpellCasts.RangeTolerance || Vector2.Distance(at, aim) > range + SpellCasts.RangeTolerance)
                return "not at a corrupted dungeon entrance";
            // A map's portal (Corrupted:MapsOpenDungeons) lets in only those it is reserved for, as a random dungeon map's exit (1641 there).
            if (entrance is RandomDungeonExit { Reservation: { } reserved } && !reserved.Characters.Contains(p.Character.Id)
                && !(world.Parties.Of(p.Character.Id) is { } party && reserved.Parties.Contains(party.Id)))
                return $"this corrupted portal is reserved ({reserved.Profile})";
            if (!d.AccessibleFrom.Contains(map.Template.Info.Type) && d.LethalEntrance != Pvp.RulesOf(world, map).PlayersAreLethal)
                return $"{d.Rank} is not open from a {(d.LethalEntrance ? "non-lethal" : "lethal")} zone";
            if (Options.CheckItemPower && d.ItemPower > 0 && Expeditions.AverageItemPower(p) is var ip && ip < d.ItemPower)
                return string.Create(CultureInfo.InvariantCulture, $"{d.Rank} needs an average item power of {d.ItemPower:0} (yours {ip:0})");
        }
        if (Options.CheckInfamy && d.InfamyRequirement > 0 && Peak(p.Character) is var peak && peak < d.InfamyRequirement)
            return string.Create(CultureInfo.InvariantCulture, $"{d.Rank} needs {d.InfamyRequirement:N0} infamy (yours {peak:N0})");
        if (p.PvpState.Execute is not null || p.PvpState.FlagCast is not null || p.Combat.Cast is not null || world.Mists.Entering(p)) return "a cast is under way";
        if (PickMap(data, from?.World) is not { } world0) return "no corrupted dungeon map is known";
        var variant = new ClusterVariant(world0, d.Tier, d.ClusterType).Id;
        world.PrefetchCluster(variant);
        var seconds = spell?.CastingTime is > 0 and var ct ? ct : DefaultCastSeconds;
        var cast = new EnterCast(map, aim, entrance, entrance?.Position ?? aim, d, spellId, counter, start, start + SpellCasts.Seconds(seconds), world.Now + TimeSpan.FromSeconds(seconds),
            world0, variant, from, this);
        world.FakeCasts.Start(p, cast);
        Log.Info("world", $"'{p.Character.Name}' casts the {d.Rank} corrupted dungeon entrance ({world0})");
        return null;
    }

    /// <summary>A shrine use under way of <paramref name="p"/> ends (<see cref="FakeSlotCasts.Interrupt"/> calls it with every interrupt).</summary>
    public void InterruptShrine(Player p, string why)
    {
        if (_shrineUses.Count > 0) CancelShrine(p, why); // phase B
    }

    /// <summary>Whether <paramref name="p"/> has an enter cast under way.</summary>
    public bool Entering(Player p) => world.FakeCasts.Of(p) is EnterCast;

    /// <summary>
    /// A map by the weights (content/corrupted.json Maps over corrupteddungeons.xml's) among those world.xml has, another than
    /// <paramref name="except"/> (the chain's last) while there is one; null when none.
    /// </summary>
    private string? PickMap(CorruptedData data, string? except = null)
    {
        var known = world.Clusters?.WorldIfLoaded;
        var pool = data.Maps.Select(m => (m.Id, Weight: Settings.Maps.TryGetValue(m.Id, out var w) ? w : m.Weight))
            .Where(m => m.Weight > 0 && known?.Find(m.Id) is not null).ToList();
        if (except is not null && pool.Count > 1) pool.RemoveAll(m => m.Id == except);
        if (pool.Count == 0) return null;
        var roll = Rng.NextDouble() * pool.Sum(m => m.Weight);
        foreach (var (id, weight) in pool)
        {
            if (roll < weight) return id;
            roll -= weight;
        }
        return pool[^1].Id;
    }

    /// <summary>
    /// The cast is over (SPEC §4.2 step 4): after CastFinished (18) and CastSpell (19) to the player and its observers (<see cref="FakeSlotCasts"/>)
    /// the run: the way back is where it stands now, the instance a new variant copy of the map (<see cref="Enter"/>).
    /// </summary>
    private void Finish(Player p, EnterCast cast)
    {
        if (cast.Entrance is RandomDungeonExit portal) world.Dungeons.Used(portal); // an open-world portal counts as used (its lifetime after the last use)
        if (cast.From is { } from)
        {
            // The chain (SPEC §6 phase B 4): the completed run is left (no infamy: it is credited), the next one keeps its way back.
            from.OwnerLeft = true;
            Log.Info("world", $"'{p.Character.Name}' chains from {from}");
        }
        Enter(p, cast.Difficulty, cast.World, cast.Variant, cast.From);
    }

    /// <summary>
    /// A new run of difficulty <paramref name="d"/> on map <paramref name="map"/> for <paramref name="p"/> (its variant <paramref name="variant"/>
    /// resolved): the way back saved, the instance registered, the push {0 id, 1 map, 4 the difficulty's cluster type} (SPEC §2.3). Also .cd enter.
    /// </summary>
    public CorruptedRun Enter(Player p, CorruptedData.Difficulty d, string map, string variant, CorruptedRun? chainFrom = null)
    {
        var template = world.CachedCluster(variant) ?? throw new InvalidOperationException($"variant {variant} is not resolved");
        var guid = Guid.NewGuid();
        var at = p.PositionAt(DateTime.UtcNow);
        var streak = chainFrom is null ? 0 : chainFrom.Streak + 1;
        var run = new CorruptedRun
        {
            Guid = guid, Difficulty = d, World = map, Variant = variant, Owner = p.Character.Id, OwnerName = p.Character.Name,
            Back = chainFrom?.Back ?? new DungeonReturn(p.Map!.Cluster, at.X, at.Y, p.Facing), Start = StartOf(template) with { Cluster = CorruptedData.InstanceId(guid) },
            Bonus = d.Bonus(Current(p.Character)), StartedAt = world.Now, Streak = streak,
            ChainFactor = 1 + Math.Min(Options.ChainBonusMax, streak * Options.ChainBonus),
        };
        _runs[run.Id] = run;
        (p.Character.Infamy ??= new InfamyLog()).Runs++;
        world.Saves.MarkChanged(p.Character);
        Log.Info("world", string.Create(CultureInfo.InvariantCulture, $"{run}: for '{p.Character.Name}' as {variant}, {d.PointsRequirement} points to the demon, bonus +{run.Bonus * 100:0.#} %")
            + (streak > 0 ? string.Create(CultureInfo.InvariantCulture, $", streak {streak} (x{run.ChainFactor:0.##})") : ""));
        Send(p, run);
        if (streak > 0)
            Expeditions.Tell(p, Settings.Text("cd.chain", ("streak", streak), ("bonus", ((run.ChainFactor - 1) * 100).ToString("0.#", CultureInfo.InvariantCulture))));
        return run;
    }

    /// <summary>The arrival of a corrupted map: the Exit_CORRUPT tile's spawnpos facing away from it (SPEC §1.2), else the default spawn.</summary>
    private static Arrival StartOf(ClusterTemplate t) =>
        (t.Exits.FirstOrDefault(e => e.Tile == "Exit_CORRUPT") ?? t.Exits.FirstOrDefault()) is { SpawnPosition: { } spawn } exit
            ? new Arrival(t.Id, spawn, Transfers.Facing(exit.Position, spawn)) : new Arrival(t.Id, Transfers.DefaultSpawn(t), 0);

    /// <summary><paramref name="p"/> goes into <paramref name="run"/> at its start (the way back saved with the character): the push {0, 1, 4}.</summary>
    private void Send(Player p, CorruptedRun run)
    {
        run.Arriving[p.Character.Id] = world.Now + TimeSpan.FromSeconds(ArrivalGraceSeconds);
        run.EmptySince = null;
        if (p.Character.Id == run.Owner) p.Character.DungeonReturn = run.Back;
        p.Session.Teleport(run.Start, $"corrupted:{run.Id}", run.World, type: run.Difficulty.ClusterType);
    }

    // ---- inside (SPEC §2.4, §4.2 steps 5-7, §4.3) ----

    /// <summary>
    /// After JoinFinished (WorldSession): in a run, CorruptedDungeonStatus (450, the tracker; before any 449/451), at the owner's first arrival
    /// CORRUPTED_DUNGEON_ENTER (Corrupted:EnterBuff) and the chat line; a completion owed to the owner is paid now. Anywhere else that buff
    /// ends (its persistsafterclusterchange false, which the server's effects do not read themselves).
    /// </summary>
    public void Joined(Player p)
    {
        if (p.Map is not { } map) return;
        if (Find(map.Cluster) is not { } run)
        {
            if (p.Effects.Count > 0 && SpellEffects.Remove(world, p, s => s.Spell.Name == EnterBuff))
            {
                SpellEffects.Send(world, p);
                SpellEffects.Refresh(world, p);
            }
            return;
        }
        run.Arriving.Remove(p.Character.Id);
        run.EmptySince = null;
        p.Session.Send(Status(p, run));
        if (p.Character.Id != run.Owner)
        {
            InvaderJoined(p, run); // phase B: a player invader's arrival starts its invasion; one whose invasion is over goes home
            return;
        }
        run.Away = false;
        if (!run.Entered)
        {
            run.Entered = true;
            run.EnteredAt = world.Now;
            run.NextRoll = world.Now + TimeSpan.FromSeconds(Data?.Global("safeperiod", 60) ?? 60);
            if (Options.EnterBuff) Pvp.ApplySpell(p, EnterBuff, world.GameNow);
            Expeditions.Tell(p, Settings.Text("cd.entered", ("rank", run.Difficulty.Rank), ("tier", run.Difficulty.Tier), ("points", run.Points),
                ("required", run.Difficulty.PointsRequirement)));
        }
        if (run.Owed)
        {
            run.Owed = false;
            Reward(p, run);
        }
    }

    /// <summary>
    /// CorruptedDungeonStatus (450, dfp.l 0x180B09CE0) for <paramref name="p"/>: 1 the owner's Guid (the shrine's owner check), 2 the difficulty
    /// index, 3 the points, 4 shards destroyed, 5 the state (1 the demon is out, 2 completed: the boss checkbox and the free leave), 6 invaded
    /// (the tracker: "invaded" for the owner, "invading" for the invader), 7 searching (after the shrine), 8 exit allowed (the chain's
    /// Transition window, after the demon).
    /// </summary>
    private CorruptedDungeonStatusEvent Status(Player p, CorruptedRun run) => new()
    {
        ddn = p.Id,                                                         // [0] ignored
        b0p = run.Owner,                                                    // [1]
        b0q = (byte)run.Difficulty.Index,                                   // [2]
        b0r = run.Points,                                                   // [3]
        b0s = (byte)Math.Min(byte.MaxValue, run.Invasion?.Destroyed ?? 0), // [4]
        b0t = (byte)(run.Completed ? 2 : run.DemonSpawned ? 1 : 0),         // [5]
        b0u = run.Invasion is { Live: true, Ended: false },                 // [6]
        b0v = Searching(run),                                               // [7]
        b0w = run.ChainExit is not null,                                    // [8]
    };

    /// <summary>The players of <paramref name="run"/>'s map now.</summary>
    private IEnumerable<Player> Inside(CorruptedRun run) => world.Maps.Find(run.Id)?.Players.Values ?? Enumerable.Empty<Player>();

    /// <summary>CorruptedDungeonUpdate (449) of <paramref name="kind"/> to everyone inside (1 the demon is out, 2 the run is complete) but <paramref name="except"/>.</summary>
    private void Update(CorruptedRun run, byte kind, long value, Vector2 at, Guid? except = null)
    {
        foreach (var p in Inside(run).Where(p => p.Character.Id != except)) p.Session.Send(new CorruptedDungeonUpdateEvent { ddn = p.Id, b0x = kind, b0y = value, b0z = at });
    }

    /// <summary>
    /// A mob of a run died (Combat.Kill): the demon completes it; a counted mob adds ⌈required × its fame / (Corrupted:ClearShare × W)⌉
    /// points, at least 1 (451 to everyone inside), and at the requirement the demon spawns at its point (449 kind 1). Then the standard
    /// chests no counted mob guards any more unlock. Phase B: the run is its owner's alone; a kill by another (a player or bot invader)
    /// gives no points (its fame leaves W, so the rest still reach the requirement), and a demon it slays rises again in its lair.
    /// </summary>
    public void Killed(Map map, Player killer, Mob mob)
    {
        if (Find(map.Cluster) is not { } run) return;
        var owner = killer.Character.Id == run.Owner;
        if (run.Invasion is { } invasion && invasion.Shards.Remove(mob.Id)) ShardDestroyed(run, invasion, mob, killer);
        else if (run.DemonSpawned && mob.Id == run.DemonId && !run.Completed && !owner) SpawnDemon(run, map);
        else if (run.DemonSpawned && mob.Id == run.DemonId && !run.Completed) Complete(run, map);
        else if (!owner && !run.Completed && run.Worth.Remove(mob.Id, out var lost)) run.Weight = Math.Max(0, run.Weight - lost);
        else if (!run.Completed && run.Worth.Remove(mob.Id, out var fame) && run.Weight > 0)
        {
            var required = run.Difficulty.PointsRequirement;
            run.Points += Math.Max(1, (int)Math.Ceiling(required * fame / (Options.ClearShare * run.Weight) - 1e-9));
            foreach (var p in Inside(run)) p.Session.Send(new CorruptedDungeonInfamyEvent { ddn = p.Id, b0o = run.Points });
            if (!run.DemonSpawned && run.Points >= required) SpawnDemon(run, map);
        }
        CheckChests(run, map);
    }

    /// <summary>
    /// The points are in (SPEC §4.2 step 6): the demon (its CD_DEMON_ENDBOSS block's mob of the run's tier) at its point, 449 kind 1 and the
    /// chat line; a map without that point completes the run at once.
    /// </summary>
    private void SpawnDemon(CorruptedRun run, Map map)
    {
        run.DemonSpawned = true;
        if (run.DemonPoint is not { Block: { } block } point || block.Pick(map.Random) is not { } template)
        {
            Complete(run, map);
            return;
        }
        var demon = SpawnManager.SpawnAt(map, template, point.Position, point.Direction, world, point.Behavior ?? "GUARD", point.Waypoints);
        (run.DemonId, run.DemonAt) = (demon.Id, point.Position);
        Update(run, 1, 0, point.Position);
        foreach (var p in Inside(run)) Expeditions.Tell(p, Settings.Text("cd.demon"));
        Log.Info("world", $"{run}: {run.Points} points, the demon {template.Name} is out");
    }

    /// <summary>
    /// The demon is slain (SPEC §4.2 step 7): 449 kind 2 and 450 with state 2 to everyone inside, the boss chest unlocked, the exit portal
    /// under AfterDemon, the owner's infamy and rewards (or at its next Join), the closing after Corrupted:LingerSeconds.
    /// </summary>
    public void Complete(CorruptedRun run, Map map)
    {
        if (run.Completed) return;
        run.Completed = true;
        run.CompletedAt = world.Now;
        Update(run, 2, 0, run.DemonAt);
        foreach (var p in Inside(run)) p.Session.Send(Status(p, run));
        if (run.BossChest is { LockState: RandomDungeons.BossLocked } chest) Unlock(map, chest, "the demon is slain");
        if (Options.ExitPortal == CorruptedExitMode.AfterDemon && run.Portal is null && MakePortal(run) is { } portal) map.AddObject(portal);
        if (Options.Chain && MakeChainExit(run) is { } chain)
        {
            // Phase B (SPEC §6 phase B 4): the chain exit in the demon's lair and the exit-allowed state (449 kind 15, 450 key 8).
            map.AddObject(chain);
            Update(run, 15, 0, chain.Position);
            foreach (var p in Inside(run)) p.Session.Send(Status(p, run));
        }
        Log.Info("world", string.Create(CultureInfo.InvariantCulture, $"{run} completed in {(world.Now - run.StartedAt).TotalSeconds:0} s with {run.Points} points"));
        if (map.Players.Values.FirstOrDefault(p => p.Character.Id == run.Owner) is { } owner) Reward(owner, run);
        else run.Owed = true;
        foreach (var p in Inside(run)) Expeditions.Tell(p, Settings.Text("cd.closing", ("seconds", Math.Round(Options.LingerSeconds))));
    }

    /// <summary>
    /// The owner's completion (SPEC §4.3): infamy (points + infamykillbonus) × Corrupted:InfamyFactor (446), the difficulty's completion silver
    /// × Rates:Silver × Corrupted:RewardFactor × (1 + the run's bonus) and fame × Rates:Fame × RewardFactor (PvE fame, the run's tier), the
    /// completion counted, the chat line.
    /// </summary>
    private void Reward(Player p, CorruptedRun run)
    {
        var d = run.Difficulty;
        long infamy = 0;
        if (!run.Credited)
        {
            run.Credited = true;
            infamy = (long)Math.Round((run.Points + d.InfamyKillBonus) * Options.InfamyFactor * run.ChainFactor);
            Credit(p, infamy, d);
        }
        var c = Settings.CompletionOf(d.Name);
        var silver = (long)Math.Round(c.Silver * 10000 * world.Rates.Silver * Options.RewardFactor * (1 + run.Bonus) * run.ChainFactor);
        Wallet.Credit(p, silver, $"corrupted dungeon {d.Rank}");
        var fame = Rates.ScaleFame((long)Math.Round(c.Fame * 10000 * Options.RewardFactor * run.ChainFactor), world.Rates.Fame);
        if (fame > 0) Destiny.Award(p, fame, Destiny.Kill(p, d.Tier), journals: false);
        var log = p.Character.Infamy ??= new InfamyLog();
        if (d.Index < log.Completed.Length) log.Completed[d.Index]++;
        world.Saves.MarkChanged(p.Character);
        world.GuildGoals.OnDungeon(p, run); // stage I: the weekly guild goals' dungeons, once per guild and run
        world.GuildGoals.OnObjective(p, d.MightReward); // stage I: the guild might of the difficulty's objective (CORRUPTED)
        world.Bounties.CorruptedCompleted(p, d.Tier); // phase B: the bounties of kind corrupted
        Expeditions.Tell(p, Settings.Text("cd.completed", ("infamy", infamy.ToString("N0", CultureInfo.InvariantCulture)),
            ("silver", (silver / 10000).ToString("N0", CultureInfo.InvariantCulture))));
        Log.Info("world", string.Create(CultureInfo.InvariantCulture, $"'{p.Character.Name}' completed {run}: +{infamy} infamy, +{silver / 10000.0:0.##} silver, +{fame / 10000.0:0.##} fame"));
    }

    /// <summary>
    /// <paramref name="amount"/> infamy (type 1) for <paramref name="p"/>: the current total and the peak (both below 2^31, the client's int),
    /// saved; UpdateInfamy (446 {0 me, 1 type, 2 the total, 3 the gain}; ctp.af(a08)) and a chat line per milestone crossed with the bonus of
    /// <paramref name="d"/> there.
    /// </summary>
    public void Credit(Player p, long amount, CorruptedData.Difficulty d)
    {
        if (amount <= 0) return;
        var log = p.Character.Infamy ??= new InfamyLog();
        var before = Current(p.Character);
        var after = Math.Min(int.MaxValue, before + amount);
        log.Current[CorruptedData.InfamyType] = after;
        log.Peak[CorruptedData.InfamyType] = Math.Max(Peak(p.Character), after);
        world.Saves.MarkChanged(p.Character);
        p.Session.Send(new UpdateInfamyEvent { d35 = p.Id, d36 = CorruptedData.InfamyType, d37 = (int)after, d38 = (int)Math.Min(int.MaxValue, after - before) });
        foreach (var m in Data?.Crossed(before, after) ?? [])
            Expeditions.Tell(p, Settings.Text("cd.rank", ("value", m.ToString("N0", CultureInfo.InvariantCulture)),
                ("bonus", (d.Bonus(m) * 100).ToString("0.#", CultureInfo.InvariantCulture))));
        AlbionJournal.InfamyChanged(p, after); // phase B: the journal's haveinfamy (CorruptedDungeon)
    }

    /// <summary>
    /// <paramref name="share"/> of <paramref name="p"/>'s current infamy (type 1) lost (an invasion's loser, Corrupted:InfamyLossOnDefeat; never the
    /// peak), with UpdateInfamy (446, key 3 the negative change); returns what was lost.
    /// </summary>
    public long Lose(Player p, double share)
    {
        var before = Current(p.Character);
        var lost = (long)Math.Floor(before * Math.Clamp(share, 0, 1));
        if (lost <= 0) return 0;
        var log = p.Character.Infamy ??= new InfamyLog();
        log.Peak[CorruptedData.InfamyType] = Peak(p.Character);
        log.Current[CorruptedData.InfamyType] = before - lost;
        world.Saves.MarkChanged(p.Character);
        p.Session.Send(new UpdateInfamyEvent { d35 = p.Id, d36 = CorruptedData.InfamyType, d37 = (int)(before - lost), d38 = (int)-lost });
        return lost;
    }

    /// <summary>.cd infamy: the current and the peak infamy of <paramref name="c"/> set to <paramref name="value"/> (a GM's).</summary>
    public void SetInfamy(Character c, long value)
    {
        var log = c.Infamy ??= new InfamyLog();
        log.Current[CorruptedData.InfamyType] = Math.Clamp(value, 0, int.MaxValue);
        log.Peak[CorruptedData.InfamyType] = Math.Clamp(value, 0, int.MaxValue);
        world.Saves.MarkChanged(c);
    }

    /// <summary>
    /// The chests of a run (SPEC §4.2 step 7 [V]): a standard chest unlocks (6) once no living counted mob stands within
    /// Corrupted:ChestGuardRadius of it; the boss chest only with the demon's death (<see cref="Complete"/>).
    /// </summary>
    private void CheckChests(CorruptedRun run, Map map)
    {
        var locked = run.Chests.Where(c => c.LockState == RandomDungeons.BossLocked && c.Map == map).ToList();
        if (locked.Count == 0) return;
        var guards = map.Objects.Values.OfType<Mob>().Where(m => !m.Dead && !m.Removed && run.Worth.ContainsKey(m.Id)).Select(m => m.Position).ToList();
        foreach (var chest in locked.Where(c => !guards.Any(g => Vector2.Distance(g, c.Position) <= Options.ChestGuardRadius)))
            Unlock(map, chest, "no demon guards it any more");
    }

    private void Unlock(Map map, LootChest chest, string why)
    {
        chest.SetState(6, world.GameNow);
        foreach (var o in map.ObserversOf(chest)) o.Session.Send(chest.Update(o));
        Log.Info("world", $"{chest} unlocked: {why}");
    }

    /// <summary>The exit portal on the run's Exit_CORRUPT tile (<see cref="CorruptedExitPortal"/>); null without the tile or randomdungeons.xml's chain exit.</summary>
    private CorruptedExitPortal? MakePortal(CorruptedRun run)
    {
        if (run.ExitTile is not { } tile || world.Clusters?.DungeonsIfLoaded is not { } dungeons || Data is not { } data
            || !dungeons.Visualizations.TryGetValue(data.ChainExitVisualization, out var vis) || vis.Fallback.FirstOrDefault() is not { } prefab)
        {
            Log.Warn("world", $"{run}: no exit portal (no exit tile or no {Data?.ChainExitVisualization} visualization)");
            return null;
        }
        var size = TileMath.V2((string?)world.Clusters.Tiles.Components(prefab.Name).FirstOrDefault(c => c.Name.LocalName == "randomdungeonrandomexit")?.Attribute("size")) ?? Vector2.One;
        var name = run.Difficulty.LethalEntrance ? "CORRUPTED_SOLO_LETHAL" : "CORRUPTED_SOLO_NONLETHAL";
        return run.Portal = new CorruptedExitPortal(world.ObjectIds.Next(), tile, name, prefab.Name, prefab.Index, size);
    }

    // ---- leaving (SPEC §4.2 steps 8-9) ----

    /// <summary>
    /// <paramref name="p"/> leaves <paramref name="run"/> (the exit portal, the escape, .cd leave, the closing): the owner leaving before the demon
    /// gets points × InfamyFactor × Corrupted:EarlyLeaveShare as infamy (once); then to its way back (<paramref name="answer"/> false: the
    /// ChangeCluster push; true: the caller answers a ChangeCluster request with the returned arrival).
    /// </summary>
    public Arrival Leave(Player p, CorruptedRun run, string why, bool answer = false)
    {
        // Phase B: a player invader goes back into its own run (its invasion lost when still on); an owner's invasion ends with the leave.
        if (InvaderHome(p, run, why) is { } home)
        {
            if (!answer) p.Session.Teleport(home.To, $"corrupted:{run.Id}:{why}", home.World, type: home.Type);
            return home.To;
        }
        var owner = p.Character.Id == run.Owner;
        if (owner && run.Invasion is { } invasion) Abandon(run, invasion, $"the owner left ({why})");
        if (owner) LeftEarly(p, run);
        run.Arriving.Remove(p.Character.Id);
        var back = owner ? run.Back : p.Character.DungeonReturn ?? run.Back;
        p.Character.DungeonReturn = null;
        var to = new Arrival(back.Cluster, new Vector2(back.X, back.Y), back.Direction);
        Log.Info("world", $"'{p.Character.Name}' leaves {run} ({why}{(run.Completed ? "" : ", not completed")})");
        if (!answer) p.Session.Teleport(to, $"corrupted:{run.Id}:{why}");
        return to;
    }

    /// <summary>
    /// A dead player respawns outside its run (Death.Respawn and Death.Departed: the city respawn, as Expeditions.DiedOut): for the owner the
    /// run counts as left (the early leave's infamy, <see cref="CorruptedRun.OwnerLeft"/>: it closes EmptyCloseSeconds later and a new entry
    /// is open at once), the way back cleared. Nothing outside a run.
    /// </summary>
    public void DiedOut(Player p)
    {
        if (Find(p.Map?.Cluster) is not { } run) return;
        // Phase B: a player invader killed in the run (the official preset's lethal ranks) is out of its own run as well.
        if (run.Invasion is { Bot: null } invasion && invasion.Invader == p.Character.Id) InvaderDied(p, run, invasion);
        if (p.Character.Id == run.Owner && run.Invasion is { } own) Abandon(run, own, "the owner died");
        if (p.Character.Id == run.Owner) LeftEarly(p, run);
        run.Arriving.Remove(p.Character.Id);
        p.Character.DungeonReturn = null;
        world.Saves.MarkChanged(p.Character);
        Log.Info("world", $"'{p.Character.Name}' died in {run} and respawns outside it{(run.Completed ? "" : ", not completed")}");
    }

    /// <summary>The owner is out of <paramref name="run"/>: before the demon points × InfamyFactor × Corrupted:EarlyLeaveShare as infamy (once); OwnerLeft.</summary>
    private void LeftEarly(Player p, CorruptedRun run)
    {
        run.OwnerLeft = true;
        if (run.Completed || run.Credited) return;
        run.Credited = true;
        var early = (long)Math.Round(run.Points * Options.InfamyFactor * Options.EarlyLeaveShare);
        if (early <= 0) return;
        Credit(p, early, run.Difficulty);
        Expeditions.Tell(p, Settings.Text("cd.left", ("infamy", early.ToString("N0", CultureInfo.InvariantCulture))));
    }

    /// <summary>
    /// The cluster and point of a character whose saved cluster is the id of a run that is gone (closed, or a restart): its saved way back, else
    /// Thetford; the character is changed and saved. False when its cluster is no closed corrupted dungeon id.
    /// </summary>
    public bool ReturnIfClosed(Character c)
    {
        if (!CorruptedData.IsInstance(c.Cluster) || IsLive(c.Cluster)) return false;
        // Phase B: a player invader whose invasion ended while it was offline goes back into its own run while that is live (the select's
        // keys 2 and 5 are that run's), where it stood; else outside, its own run left.
        if (_homes.Remove(c.Id, out var home) && home.Cluster == c.Cluster && Find(home.To.Cluster) is { } own && own.Owner == c.Id)
        {
            (c.Cluster, c.PositionX, c.PositionY, c.Direction) = (home.To.Cluster, home.To.Position.X, home.To.Position.Y, home.To.Direction);
            c.PendingTransfer = null;
            own.Arriving[c.Id] = world.Now + TimeSpan.FromSeconds(ArrivalGraceSeconds);
            world.Saves.SaveNow(c);
            Log.Info("world", $"'{c.Name}' was in a closed corrupted dungeon: back in its own {own}");
            return true;
        }
        if (RunOf(c.Id) is { } left) left.OwnerLeft = true;
        var to = c.DungeonReturn ?? new DungeonReturn(Death.DefaultRespawnCluster, 0, 0, 0);
        (c.Cluster, c.PositionX, c.PositionY, c.Direction) = (to.Cluster, to.X, to.Y, to.Direction);
        c.PendingTransfer = null;
        c.DungeonReturn = null;
        world.Saves.SaveNow(c);
        Log.Info("world", $"'{c.Name}' was in a closed corrupted dungeon: back in {to.Cluster}");
        return true;
    }

    /// <summary>The run closes: every player still inside goes to its way back (a dead one as a respawn), the map goes, the id is never used again.</summary>
    public void Close(CorruptedRun run, string why)
    {
        if (run.Closed) return;
        run.Closed = true;
        if (run.Invasion is { } invasion) Abandon(run, invasion, $"the run closes ({why})");
        if (world.Maps.Find(run.Id) is { } map)
        {
            foreach (var p in map.Players.Values.ToList())
            {
                if (p.Session.Status == SessionStatus.InWorld && p.Session.Player == p)
                {
                    Expeditions.Tell(p, Settings.Text("cd.closed"));
                    if (p.Combat.Down is { Kind: not KnockdownKind.Dead }) Death.Abandoned(p);
                    var dead = p.Combat.Down is { Kind: KnockdownKind.Dead };
                    var to = Leave(p, run, why, answer: true);
                    var (map0, type) = KeysOf(to.Cluster); // a player invader goes back into its own run
                    p.Session.Teleport(to, dead ? Death.RespawnVia : $"corrupted:{run.Id}:{why}", map0, type: type);
                }
                else world.Maps.Remove(p, world.Clock.Now);
            }
            world.Maps.Unload(map, force: true);
        }
        _runs.Remove(run.Id);
        foreach (var gone in _homes.Where(h => h.Value.To.Cluster == run.Id).Select(h => h.Key).ToList()) _homes.Remove(gone); // phase B: no way back into it
        Log.Info("world", $"{run} closed ({why})");
    }

    // ---- the timers ----

    /// <summary>
    /// Every tick (<see cref="World.Update"/>, after <see cref="FakeSlotCasts.Update"/> has run the enter casts: a player gone or knocked down
    /// ends one; at its end, once the variant is resolved — at most 30 s more — the run): each run's time limit (Corrupted:MaxMinutes, a line a minute before), its closing after the
    /// completion (LingerSeconds) or empty (EmptyCloseSeconds once the owner left, RelogMinutes while it is logged out inside).
    /// </summary>
    public void Update()
    {
        var now = world.Now;
        foreach (var run in _runs.Values.ToList())
        {
            var limit = TimeSpan.FromMinutes(Options.MaxMinutes);
            if (now - run.StartedAt >= limit)
            {
                Close(run, "time limit");
                continue;
            }
            if (!run.Warned && now - run.StartedAt >= limit - TimeSpan.FromSeconds(60))
            {
                run.Warned = true;
                foreach (var p in Inside(run)) Expeditions.Tell(p, Settings.Text("cd.closing", ("seconds", 60)));
            }
            foreach (var late in run.Arriving.Where(a => now >= a.Value).Select(a => a.Key).ToList()) run.Arriving.Remove(late);
            if (Inside(run).Any() || run.Arriving.Count > 0 || run.Away) run.EmptySince = null;
            else run.EmptySince ??= now;
            var empty = TimeSpan.FromSeconds(run.OwnerLeft ? Options.EmptyCloseSeconds : Options.RelogMinutes * 60);
            if (run.EmptySince is { } since && now - since >= empty) Close(run, run.OwnerLeft ? "empty" : "the owner did not come back");
            else if (run.CompletedAt is { } done && now - done >= TimeSpan.FromSeconds(Options.LingerSeconds) && run.Invasion is null && !run.Away) Close(run, "completed");
            else UpdateInvasions(run, now); // phase B: the shrine, the rolls, the invasion under way
        }
    }

    /// <summary>.cd list: the live runs, one line each.</summary>
    public IEnumerable<string> Describe()
    {
        foreach (var r in _runs.Values)
            yield return string.Create(CultureInfo.InvariantCulture,
                $"  {r.Id} {r.Difficulty.Rank} T{r.Difficulty.Tier} {r.World}: {r.OwnerName}, {r.Points}/{r.Difficulty.PointsRequirement} points, "
                + $"{(r.Completed ? "completed" : r.DemonSpawned ? "demon out" : $"{r.Worth.Count} mobs left")}, {(world.Now - r.StartedAt).TotalMinutes:0} min, {Inside(r).Count()} inside")
                + (r.Streak > 0 ? $", streak {r.Streak}" : "") + (r.Consented ? ", shrine used" : "") + (r.Away ? ", invading" : "")
                + (r.Invasion is { } i ? $", invaded by {i.InvaderName} ({(i.Ended ? "over" : i.Live ? $"{i.Destroyed} shard(s)" : "coming")})" : "");
    }
}
