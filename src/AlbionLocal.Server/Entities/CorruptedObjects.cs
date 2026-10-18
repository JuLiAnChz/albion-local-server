using System.Numerics;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Entities;

/// <summary>
/// A Hell Den's corrupted dungeon entrance (stageL corrupted SPEC §1.4, §2.6): the critical member corrupteddungeonstaticentrance_01 as
/// NewCorruptedStaticEntrance (637, atg). The client binds it to the landscape tile with that component at key 2's position (cjb.a
/// 0x180AA1B10: elsewhere no entrance at all), so key 2 is the tile's own position. A click opens the client's difficulty window
/// (CorruptedDungeonEntranceGui, GetInfamyInfo 419), whose Enter casts ENTER_CURRUPTED_DUNGEON_SPELL_n at it (slot 25). Landscape-bound:
/// never left, sent again on re-entry, as NewExit.
/// </summary>
public sealed class CorruptedEntrance(long id, CriticalMember spawn) : WorldObject(id, spawn)
{
    public CriticalMember Member { get; } = spawn;

    public override string Kind => "corrupted dungeon entrance";
    public override string TypeName => Member.Tile;
    public override bool LeaveOnRange => false;

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return new NewCorruptedStaticEntranceEvent
        {
            cx0 = Id,                           // [0]
            cx1 = Member.Guid ?? Guid.Empty,    // [1] the member's criticalObjects Guid [inf]
            cx2 = Position,                     // [2] the tile's position (the client's landscape binding)
            cx3 = Direction,                    // [3] its roty (90)
        };
    }
}

/// <summary>
/// The Demonic Shrine of a corrupted dungeon's start room (SPEC §2.6): shrine_01 (SHRINE_NON_ESCAPE, &lt;corruptedshrine/&gt;) as
/// NewCorruptedShrine (458, ath). State 0 lets its owner send UseCorruptedShrine (416); state 1 is inactive (a click shows
/// @CORRUPTED_SHRINE_OVERHEAD_INACTIVE). Phase B (SPEC §4.4): 0 while invasions may come and its window (shrinetimelimit) is open, 1 after the
/// use or the window (UpdateCorruptedShrine 459). Key 3 is the member's tiles.xml tile name (SHRINE_NON_ESCAPE): cj3.af 0x180B2DCC0 looks
/// it up with the scene's GetTile(name) (a9f.u 0x180538D60) and builds no shrine without it.
/// </summary>
public sealed class CorruptedShrine(long id, CriticalMember spawn, long ticks) : WorldObject(id, spawn)
{
    public const byte Usable = 0, Inactive = 1;

    public CriticalMember Member { get; } = spawn;

    public byte State { get; set; } = Inactive;

    /// <summary>GameTimeStamp of the state (key 5).</summary>
    public long StateTicks { get; set; } = ticks;

    public override string Kind => "corrupted shrine";
    public override string TypeName => $"{Member.Tile} (state {State})";
    public override bool LeaveOnRange => false;

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        var e = new NewCorruptedShrineEvent
        {
            cx4 = Id,               // [0]
            cx5 = Position,         // [1]
            cx6 = Direction,        // [2]
            cx7 = Member.Tile,      // [3] the tiles.xml tile name (SHRINE_NON_ESCAPE): a9f.u 0x180538D60 is the scene's GetTile(name)
            cx8 = State,            // [4] 0 usable, 1 inactive (cj3.a)
            cx9 = StateTicks,       // [5]
        };
        e.Extra = new() { [4] = State }; // state 0 goes out too
        yield return e;
    }
}

/// <summary>
/// The way out of a corrupted dungeon's start room (SPEC §4.2 step 8 [V]): the template's Exit_CORRUPT has no target, so the server puts a
/// walk-in portal on that tile, a NewRandomDungeonExit (324) without key 12 (no corrupted entrance window) with the chain exit's
/// visualization (CORRUPTED_DUNGEON_CHAIN_EXIT, prefab SHARED_CORRUPTED_DUNGEON_EXIT_10x10_PORTAL: key 6 and its index in key 7) and key 3 the
/// corrupted RandomExit of the run's zone rule [inf]. The client's walk-in sends ChangeCluster {0: its id} (Handlers/World/CorruptedHandler),
/// answered with the owner's way back. Under Corrupted:ExitPortal: from the start (Always) or once the demon is slain (AfterDemon).
/// Phase B (SPEC §6 phase B 4): <paramref name="chain"/> = the chain exit in the demon's lair, the same event with key 12 true (clw+0x6A:
/// RandomDungeonExitObjectView_EnterExit 0x181279E60 opens the corrupted entrance window on it, whose Enter casts the Next Dungeon at it).
/// StageL hellgates phase B: also a hellgate's chain exit (the HELLGATE_CHAIN_EXIT visualization at CHAIN_EXIT, chain false: the plain walk-in).
/// </summary>
public sealed class CorruptedExitPortal(long id, ClusterObject spawn, string name, string prefab, int visualization, Vector2 size, bool chain = false) : WorldObject(id, spawn)
{
    /// <summary>The chain exit (key 12 true) instead of the way out.</summary>
    public bool Chain { get; } = chain;

    /// <summary>Key 3: a RandomExit of randomdungeons.xml (CORRUPTED_SOLO_NONLETHAL / _LETHAL).</summary>
    public string Name { get; } = name;

    /// <summary>Keys 6 and 7: the prefab and its visualization id.</summary>
    public string Prefab { get; } = prefab;
    public int Visualization { get; } = visualization;

    /// <summary>The prefab's randomdungeonrandomexit size (2 × 2): the client's trigger square.</summary>
    public Vector2 Size { get; } = size;

    public override string Kind => Chain ? "corrupted chain exit" : "corrupted exit portal";
    public override string TypeName => Prefab;
    public override bool LeaveOnRange => true;

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return new NewRandomDungeonExitEvent
        {
            c8n = Id,               // [0]
            c8o = Position,         // [1] the Exit_CORRUPT tile
            c8p = Direction,        // [2]
            c8q = Name,             // [3] → clw+0x60
            c8t = Prefab,           // [6]
            c8u = Visualization,    // [7]
            c8v = true,             // [8]
            c8z = Chain,            // [12] the chain exit: the corrupted entrance window (absent for the way out)
            c84 = -1,               // [17] mists value, -1 as captured
            c85 = 1,                // [18] Subtypes.Solo
            c88 = "",               // [21] no reservation
            Extra = new() { [9] = (byte)0, [15] = (byte)0, [20] = (byte)0 }, // as RandomDungeonExit: level, party flag and key 20 as byte 0
        };
    }
}
