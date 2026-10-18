using System.Numerics;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Entities;

/// <summary>
/// A border exit of a mists zone (stageL mists SPEC §3.6): the master's mistsborderexit member (MistsBorderExit, 15 × 15) as NewMistsBorderExit
/// (537). The client's cnk ctor finds its tile by key 1 (a null tile throws, V7), so key 1 is the member's exact world position; its target is
/// the world.xml cluster of MistsPlayerJoinedInfo key 4 (cnk.aqt), without which the client sends nothing. Walking in sends ChangeCluster {0: id}
/// (Handlers/World/MistsHandler): the next zone of the mode. Landscape-bound: never left, sent again on re-entry.
/// </summary>
public sealed class MistsBorderExit(long id, CriticalMember spawn) : WorldObject(id, spawn)
{
    public CriticalMember Member { get; } = spawn;

    /// <summary>The trigger square (mistsborderexit size, 15 × 15).</summary>
    public Vector2 Size => TileMath.V2(Member.Attr("size")) ?? new Vector2(15);

    /// <summary>Where a player coming into the zone by border <paramref name="m"/> arrives: the tile position + R(its direction)·spawnpos, facing away (SPEC §6.5, V11).</summary>
    public static Arrival ArrivalOf(CriticalMember m, string cluster)
    {
        var sp = TileMath.V2(m.Attr("spawnpos")) ?? Vector2.Zero;
        var (dx, dz) = TileMath.Rotate(sp.X, sp.Y, m.Direction);
        var at = m.Position + new Vector2((float)dx, (float)dz);
        return new Arrival(cluster, at, Transfers.Facing(m.Position, at));
    }

    public override string Kind => "mists border exit";
    public override string TypeName => Member.Member;
    public override bool LeaveOnRange => false;

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return new NewMistsBorderExitEvent
        {
            ebm = Id,           // [0]
            ebn = Position,     // [1] the mistsborderexit tile's world position (the client looks the tile up by it)
        };
    }
}

/// <summary>
/// The Brecilien entrance of a mists zone or of a mists world cluster (5100-5103) (SPEC §3.6): a mistscityentrance member (tile
/// MistsCityEntrance) as NewMistsCityEntrance (527). The client instantiates key 3's tile and targets mistscityentrancetargetclusterid (5001);
/// ChangeCluster {0: id} leads there.
/// </summary>
public sealed class MistsCityEntrance(long id, CriticalMember spawn) : WorldObject(id, spawn)
{
    public CriticalMember Member { get; } = spawn;

    public override string Kind => "mists city entrance";
    public override string TypeName => Member.Tile;
    public override bool LeaveOnRange => false;

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return new NewMistsCityEntranceEvent
        {
            ebo = Id,           // [0]
            ebp = Position,     // [1]
            ebq = Direction,    // [2]
            ebr = Member.Tile,  // [3] the tile name (MistsCityEntrance), instantiated by the client (a9f.u)
        };
    }
}

/// <summary>
/// The open-world exit of a mists zone (SPEC §3.6): the mistsopenworldexit member (tile MISTS_OPEN_WORLD_EXIT) as NewMistsOpenWorldExit (523):
/// key 5 the world.xml cluster it leads to (the client's tooltip and target, a0.abs) — the viewer's way back under Mists:OpenWorldExit Entrance,
/// the zone's drawn one under Random — key 6 "" [inference]. ChangeCluster {0: id} leads there.
/// </summary>
public sealed class MistsOpenWorldExit(long id, CriticalMember spawn, Func<Player, string> target) : WorldObject(id, spawn)
{
    public CriticalMember Member { get; } = spawn;

    /// <summary>The cluster this exit leads <paramref name="viewer"/> to.</summary>
    public string TargetOf(Player viewer) => target(viewer);

    public override string Kind => "mists open-world exit";
    public override string TypeName => Member.Tile;
    public override bool LeaveOnRange => false;

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return new NewMistsOpenWorldExitEvent
        {
            ecb = Id,               // [0]
            ecc = Position,         // [1]
            ecd = Direction,        // [2]
            ece = Member.Tile,      // [3] the tile name (MISTS_OPEN_WORLD_EXIT)
            ecg = target(viewer),   // [5] the target cluster (world.xml)
            ech = "",               // [6] kept at cnq+0x70 [inference]; key 4 (false) is not read
        };
    }
}

/// <summary>
/// The immediate return portal at a player's arrival in a mists zone (SPEC §3.6, §3.7): NewMistsImmediateReturnExit (520) {0 id, 1 position, 2
/// rotation, 3 its end}. Clickable only while MistsPlayerJoinedInfo key 0 names it and now ≤ key 1 (WillHandleMouseEnter); its use is op 476.
/// Gone at its end (openworldexitportalalivetimer, 30 s).
/// </summary>
public sealed class MistsReturnPortal(long id, ClusterObject spawn, Guid owner, long end) : WorldObject(id, spawn)
{
    /// <summary>The character it was made for (521 key 0 of that player only).</summary>
    public Guid Owner { get; } = owner;

    /// <summary>GameTimeStamp of its end (key 3, 521 key 1).</summary>
    public long End { get; } = end;

    public override string Kind => "mists return portal";
    public override string TypeName => $"for {Owner.ToString()[..8]}";
    public override bool LeaveOnRange => true;

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return new NewMistsImmediateReturnExitEvent
        {
            eb7 = Id,           // [0]
            eb8 = Position,     // [1] the arrival point
            eb9 = Direction,    // [2]
            eca = End,          // [3] GameTimeStamp of its end
        };
    }
}

/// <summary>
/// A Knightfall Abbey exit (stageL mists SPEC §3.6, phase B): a mistsdungeonexit member (MistsDungeonExit, 15 × 15) of the dungeon as
/// NewMistsDungeonExit (538) {0 id, 1 Guid, 2 position, 3 rotation}. The client accepts it only inside an a9i cluster and targets the mode's
/// exitsleadtomode (cnn 0x180B81FD0); walking in sends ChangeCluster {0: id} (Handlers/World/MistsHandler): a zone of that mode.
/// </summary>
public sealed class MistsDungeonExit(long id, CriticalMember spawn) : WorldObject(id, spawn)
{
    public CriticalMember Member { get; } = spawn;

    /// <summary>Key 1 [inference: its use is not traced]: the member's criticalObjects Guid, a fresh one in a guidless dungeon.</summary>
    public Guid Guid { get; } = spawn.Guid is { } g && g != Guid.Empty ? g : Guid.NewGuid();

    /// <summary>The trigger square (mistsdungeonexit size, 15 × 15).</summary>
    public Vector2 Size => TileMath.V2(Member.Attr("size")) ?? new Vector2(15);

    public override string Kind => "mists dungeon exit";
    public override string TypeName => Member.Member;
    public override bool LeaveOnRange => false;

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return new NewMistsDungeonExitEvent
        {
            eb3 = Id,           // [0]
            eb4 = Guid,         // [1]
            eb5 = Position,     // [2]
            eb6 = Direction,    // [3]
        };
    }
}

/// <summary>
/// A wisp spawn of a mists zone (SPEC §3.6, phase B): a mistswispspawn tile (the resource and 2xx masters' wisp trees) as NewMistsWispSpawn (525)
/// {0 id, 1 position, 2 rotation, 3 whether a wisp waits there [inference]}; MistsWispSpawnStateChange (526) {0 id, 1 the same} when that
/// changes. The wisp itself is a mob at the tile's wispspawnpos (<see cref="WispAt"/>).
/// </summary>
public sealed class MistsWispSpawn(long id, MistsSpot spawn) : WorldObject(id, spawn)
{
    public MistsSpot Spot { get; } = spawn;

    /// <summary>A wisp stands at it now.</summary>
    public bool Active { get; set; }

    /// <summary>Where its wisp stands: the tile + R(its direction)·wispspawnpos.</summary>
    public Vector2 WispAt
    {
        get
        {
            var off = TileMath.V2(Spot.Attr("wispspawnpos")) ?? Vector2.Zero;
            var (dx, dz) = TileMath.Rotate(off.X, off.Y, Spot.Direction);
            return Spot.Position + new Vector2((float)dx, (float)dz);
        }
    }

    public override string Kind => "mists wisp spawn";
    public override string TypeName => Spot.Tile;
    public override bool LeaveOnRange => false;

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return new NewMistsWispSpawnEvent
        {
            ecm = Id,           // [0]
            ecn = Position,     // [1] the mistswispspawn tile (cns finds it there)
            eco = Direction,    // [2]
            ecp = Active,       // [3] [inference]
        };
    }
}

/// <summary>
/// Brecilien's static entrance into the mists (SPEC §3.6, §6.7, phase B): 5001's mistsstaticentrance member (MISTS_STATIC_ENTRANCE) as
/// NewMistsStaticEntrance (522) {0 id, 1 its criticalObjects Guid, 2 position, 3 rotation}. The client's GUI lists the modes and sends
/// MistsUseStaticEntrance (op 477) {0 time, 1 this id, 2 the mode's id} (Handlers/World/MistsHandler).
/// </summary>
public sealed class MistsStaticEntrance(long id, CriticalMember spawn) : WorldObject(id, spawn)
{
    public CriticalMember Member { get; } = spawn;

    public override string Kind => "mists static entrance";
    public override string TypeName => Member.Tile;
    public override bool LeaveOnRange => false;

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return new NewMistsStaticEntranceEvent
        {
            eci = Id,                           // [0]
            ecj = Member.Guid ?? Guid.Empty,    // [1]
            eck = Position,                     // [2]
            ecl = Direction,                    // [3]
        };
    }
}

/// <summary>
/// Brecilien's roads entrance (SPEC §3.6, phase B item 7): 5001's mistscityroadsentrance member (MISTS_CITY_ROADS_ENTRANCE) as
/// NewMistsCityRoadsEntrance (528) {0 id, 1 Guid, 2 position, 3 rotation}; the party binding keys 4-10 are the roads spec's. Its use (op 478)
/// is refused with a chat line.
/// </summary>
public sealed class MistsCityRoadsEntrance(long id, CriticalMember spawn) : WorldObject(id, spawn)
{
    public CriticalMember Member { get; } = spawn;

    public override string Kind => "mists roads entrance";
    public override string TypeName => Member.Tile;
    public override bool LeaveOnRange => false;

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return new NewMistsCityRoadsEntranceEvent
        {
            ebs = Id,                           // [0]
            ebt = Member.Guid ?? Guid.Empty,    // [1]
            ebu = Position,                     // [2]
            ebv = Direction,                    // [3]
        };
    }
}

/// <summary>
/// A camp's free-for-all circle (SPEC §6.8, phase B, Mists:UnrestrictedZones: the official preset only): an unrestrictedpvpzone tile (radius 90)
/// as NewUnrestrictedPvpZone (544) {0 id, 1 position, 2 radius}; keys 3 and 4 are not traced and go out as 0 and false.
/// </summary>
public sealed class MistsUnrestrictedZone(long id, MistsSpot spawn) : WorldObject(id, spawn)
{
    public MistsSpot Spot { get; } = spawn;

    public float Radius => TileMath.Float(Spot.Attr("radius")) is > 0 and var r ? r : 90;

    public override string Kind => "unrestricted pvp zone";
    public override string TypeName => $"r {Radius:0}";
    public override bool LeaveOnRange => false;

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return new NewUnrestrictedPvpZoneEvent
        {
            dc2 = Id,           // [0]
            dc3 = Position,     // [1]
            dc4 = Radius,       // [2]
        };
    }
}
