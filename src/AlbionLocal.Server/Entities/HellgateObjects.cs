using AlbionLocal.Server.Data;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Entities;

/// <summary>
/// A Hell Den's hellgate shrine (stageL hellgates SPEC §2.5, §3.1): the SHARED_HELLGATE_ENTRANCE tile (hellgateshrine 7 × 7) as
/// NewHellgateShrine (400, at0; cjb.ax 0x180AA5430 → ckh). Key 3 is the tiles.xml tile name: the client's ctor looks it up (a9f.u) and
/// builds the collider from it ("HellgateShrineObject: Tile not found" without). Keys 4 and 5 are per viewer (<see cref="Systems.Hellgates.ShrineKeys"/>):
/// Hellgates:ShrineMode Fixed → key 5 true (DoAutoUse sends op 255 for key 4's mode at once) and key 4 the viewer's mode; Gui → key 5 false
/// and key 4 0 (hellgate_generic: the client's mode selector). Landscape-bound: never left, sent again on re-entry, as NewExit.
/// </summary>
public sealed class HellgateShrine(long id, HellgateShrineSpawn spawn) : WorldObject(id, spawn)
{
    public HellgateShrineSpawn Member { get; } = spawn;

    public override string Kind => "hellgate shrine";
    public override string TypeName => Member.Tile;
    public override bool LeaveOnRange => false;

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        var (mode, fixedMode) = viewer.World.Hellgates.ShrineKeys(viewer);
        var e = new NewHellgateShrineEvent
        {
            c00 = Id,           // [0]
            c01 = Position,     // [1] the tile's position
            c02 = Direction,    // [2] the tile's roty (301) [inference: client check C1]
            c03 = Member.Tile,  // [3] the tiles.xml tile name (SHARED_HELLGATE_ENTRANCE)
            c04 = mode,         // [4] bcr.abp(mode) → ckh+0x60
            c05 = fixedMode,    // [5] ckh+0x69: DoAutoUse in key 4's mode instead of the selector
            c06 = 0,            // [6] the initial channel count (ddv.u(bct, byte))
        };
        e.Extra = new() { [4] = mode, [5] = fixedMode, [6] = (byte)0 }; // 0 and false go out too: the at0 keys 0-6 complete
        yield return e;
    }
}

/// <summary>
/// A hellgate's exit portal (SPEC §3.7): NewHellgateExitPortal (250, atz; cjb.ar 0x180AA4610 → ckg) on the template's EXIT_PORTAL marker
/// once the run is won and claimed, or lost. The client's Use asks @HELLGATE_EXIT_PORTAL_CONFIRMATI… and casts slot 21 (the escape:
/// ESCAPE_DUNGEON, 7 s once the hellgate state is 3 or more, ddv.as), a server cast (FakeSlotCasts.StartEscape) whose end, DungonEscapeReady 467,
/// makes it send ChangeCluster {0: 0, 3: true}, answered by Handlers/World/HellgateHandler. A second 250 with a known id is ignored by the client.
/// </summary>
public sealed class HellgateExitPortal(long id, NamedMarkerSpawn spawn) : WorldObject(id, spawn)
{
    public override string Kind => "hellgate exit portal";
    public override string TypeName => Spawn.Tile;
    public override bool LeaveOnRange => true;

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return new NewHellgateExitPortalEvent
        {
            c0x = Id,           // [0]
            c0y = Position,     // [1] the EXIT_PORTAL marker
            c0z = Direction,    // [2]
        };
    }
}
