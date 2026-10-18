using System.Numerics;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Entities;

/// <summary>
/// An open-world random dungeon entrance (analysis/work/stageG/dungeons/SPEC.md, "SPEC" here, §2.1, §6.1, §8): a RandomExit or
/// FixedExit of randomdungeons.xml standing on a RandomExitPositionMarker of the cluster (or where a GM put it), as
/// NewRandomDungeonExit 324 (auq; the client's clw). The client places the portal prefab itself (key 5 false: the prefab of
/// visualization id key 7, 1-based; key 6 is sent too, as captured; a fixed exit: key 5 true and its own tile in key 6, no key 7, since
/// cjb.aw takes key 6 only with key 5 and an unknown visualization leaves clw without a tile, unseen and closed) and lets the player in when they stand in pos ± size/2 (the
/// prefab component's size): ChangeCluster with this object's id (Systems/RandomDungeons). A level reroll resends the whole
/// event with the same id (cjb.aw updates in place, captured 0 → 2). Left by visibility like a building, and when it
/// despawns. Touched only under <see cref="World.Sync"/>.
/// </summary>
public sealed class RandomDungeonExit(long id, ClusterObject spawn, RandomExitInfo info) : WorldObject(id, spawn)
{
    /// <summary>The marker it stands on; null for a GM's exit (.rd).</summary>
    public DungeonExitMarker? Marker => Spawn as DungeonExitMarker;

    public RandomExitInfo Info { get; } = info;

    /// <summary>Key 3: the RandomExit or FixedExit name.</summary>
    public string Name => Info.Name;

    /// <summary>The generator and tier of the dungeon beneath (drawn when the exit spawns: SPEC §5.3 step 1).</summary>
    public required string Generator { get; init; }
    public required int Tier { get; init; }

    /// <summary>Key 2: (marker rotation + 180) mod 360 (SPEC §2.1: 0204 marker 180 → 0, 1207 marker 270 → 90), a fixed exit's tile rotation as it is; absent when 0.</summary>
    public required float Rotation { get; init; }

    /// <summary>Key 4: the dungeon clustertype (ClusterTypeMappings, SPEC §8.3; RD_SAFEAREA in a SAFEAREA cluster).</summary>
    public required string ClusterType { get; init; }

    /// <summary>Keys 6 and 7: the prefab tile name and its visualization id (document index of the &lt;prefab&gt;, SPEC §8.2; 0 = absent for a fixed exit's own tile).</summary>
    public required string Prefab { get; init; }
    public int Visualization { get; init; }

    /// <summary>Key 18 bn5.Subtypes: 1 Solo, 2 Veteran, 3 Elite, 4 Event_Solo, 5 Event_Veteran.</summary>
    public required byte Subtype { get; init; }

    /// <summary>The marker's subtype name (SOLO, VETERAN, …; FIXED for a fixed exit): the distribution's and the lifetime's key.</summary>
    public required string SubtypeName { get; init; }

    /// <summary>The prefab component's trigger size (randomdungeonrandomexit size, 1 × 1 without) and spawnpos (the arrival out of the dungeon, SPEC §6.4).</summary>
    public Vector2 Size { get; init; } = Vector2.One;
    public Vector2 SpawnPos { get; init; }

    /// <summary>Key 9: the random level 0-4 (RandomLevelReroll), frozen once a dungeon is made from the exit (SPEC §8.4).</summary>
    public byte Level { get; set; }

    /// <summary>The level's reroll (null = none: level 0 for good) and the <see cref="World.Clock"/> time of the next.</summary>
    public LevelReroll? Reroll { get; init; }
    public TimeSpan NextReroll { get; set; }

    /// <summary>A dungeon has been made from it: the level no longer rerolls.</summary>
    public bool Frozen { get; set; }

    /// <summary><see cref="World.Clock"/> time of the last entry (null = never used): it despawns lifetimeafterlastuse later (SPEC §9).</summary>
    public TimeSpan? LastUse { get; set; }

    /// <summary>The <see cref="World.Clock"/> time it despawns by itself (a GM's exit after use); null = only through <see cref="LastUse"/>.</summary>
    public TimeSpan? Expires { get; set; }

    /// <summary>Spawned by a dungeon map (activaterandomdungeon, SPEC §10): its level is the map's difficulty and never rerolls.</summary>
    public bool FromMap { get; init; }

    /// <summary>
    /// A map's reservation (SPEC §10 step 2, keys 21-24): the ReservationProfile name, the characters who may enter (the caster and
    /// the members of its party at the cast), the caster's party (key 23: whoever is in it when entering may enter too [assume]) and
    /// the GameTimeStamp it ends (the exit's expiry); null = anyone may enter.
    /// </summary>
    public (string Profile, Guid[] Characters, int[] Parties, long Until)? Reservation { get; init; }

    public override float Direction => Rotation;
    public override string Kind => "random dungeon exit";
    public override string TypeName => $"{Name} ({Generator} T{Tier}, level {Level})";
    public override bool LeaveOnRange => true;

    /// <summary>Where a player leaving the dungeon arrives: the position + R(rotation)·spawnpos, facing away from the exit (SPEC §6.4).</summary>
    public Arrival ArrivalOut(string cluster)
    {
        var (dx, dz) = TileMath.Rotate(SpawnPos.X, SpawnPos.Y, Rotation);
        var at = Position + new Vector2((float)dx, (float)dz);
        return new Arrival(cluster, at, Transfers.Facing(Position, at));
    }

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return new NewRandomDungeonExitEvent
        {
            c8n = Id,                   // [0] object id
            c8o = Position,             // [1] the marker position
            c8p = Rotation,             // [2] (marker rotation + 180) mod 360, a fixed exit's tile rotation; absent when 0
            c8q = Name,                 // [3] RandomExit / FixedExit name → clw+0x60
            c8r = ClusterType.Length > 0 ? ClusterType : null, // [4] dungeon clustertype → the TargetCluster of the client's exit checks (a corrupted portal: none, as captured)
            c8s = Info.Fixed,           // [5] true: cjb.aw places key 6 itself (a fixed exit's own tile); false: the prefab of key 7
            c8t = Prefab,               // [6] prefab tile, sent though key 5 is false (a random exit, as captured)
            c8u = Visualization,        // [7] visualization id (bn5.abo when key 5 is false); 0 = absent for a fixed exit
            c8v = true,                 // [8] clw+0x68, always true as captured
            c8x = Info.Fixed,           // [10] a fixed exit (randomdungeonfixedexit component)
            c8z = Info.Corrupted,       // [12] stageL: a corrupted portal (clw+0x6A: the corrupted entrance window, no ChangeCluster)
            c83 = Info.MistsMode,       // [16] stageL mists: the mode (clw.af: brz lookup → clw+0x78; the TargetCluster is bg3.abo(a9i, its clustertype), so no key 4)
            c84 = -1,                   // [17] mists value, -1 as captured
            c85 = Subtype,              // [18] bn5.Subtypes
            c88 = Reservation?.Profile ?? "", // [21] reservation profile ("" = none, as captured)
            c89 = Reservation?.Characters,    // [22] the characters it is reserved for
            c9a = Reservation is { Parties.Length: > 0 } r ? r.Parties : null, // [23] the party it is reserved for [assume]
            c9b = Reservation?.Until ?? 0,    // [24] the reservation's end (absent without one)
            // [9] the level, [15] the party flag and [20] are captured as byte 0 too, so they always go out.
            Extra = new() { [9] = Level, [15] = (byte)0, [20] = (byte)0 },
        };
    }
}
