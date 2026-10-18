using System.Numerics;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Entities;

/// <summary>
/// A Roads of Avalon portal (analysis/work/stageL/roads/SPEC.md, "SPEC" here, §2.1, §5.3): an exit spot of the road network (a world.xml
/// criticalObjects &lt;tunnelexit&gt; and its template member, tile Tunnel_Exit in the open world, Tunnel_Exit_Inside in a road) as
/// NewTunnelExit 448 (au8; the client's cmt). The client places the spot's tile itself (key 3), draws the clock ring from keys 7-8, the charge
/// slider from 9-10, the next charge from 11 and the "recently used" ring from 12, and lets the player use it by a click within
/// gamedata defaultexitusagedistance (5 m) of key 1 (usage mode 1, V3): ChangeCluster with this object's id (Handlers/World/RoadHandler). A
/// change of its link resends the whole event with the same id (cjb.av updates in place). On a road every spot stands, an unlinked one with
/// key 4 false and no key 6 (its frame is inactivevisible); in the open world only a linked one, which leaves (Leave) when its link closes.
/// Key 4 goes true only together with key 6 (a world.xml cluster) and key 5 (0-2): the client's CanEnterExit and UpdateState read both.
/// Left by visibility like a building. Touched only under <see cref="World.Sync"/>.
/// </summary>
public sealed class TunnelExit(long id, CriticalMember spawn, RoadSlot slot) : WorldObject(id, spawn)
{
    /// <summary>The network's spot this portal shows.</summary>
    public RoadSlot Slot { get; } = slot;

    /// <summary>Key 3: the member's tile (Tunnel_Exit / Tunnel_Exit_Inside), which the client looks up in tiles.xml and places.</summary>
    public string Tile => Spawn.Tile;

    /// <summary>The tile's tunnelexit spawnpos (Tunnel_Exit 4 0, Tunnel_Exit_Inside 5.53 0): where a player coming through it arrives, turned with the tile.</summary>
    public Vector2 SpawnPos { get; init; }

    public override string Kind => "tunnel exit";

    public override string TypeName => Slot.Link is { } link ? $"{Tile} {Slot.Path} to {link.Other(Slot).Cluster} ({link.Charges}/{link.MaxCharges})" : $"{Tile} {Slot.Path} (no link)";

    public override bool LeaveOnRange => true;

    /// <summary>Where a player coming through the paired portal arrives: the position + R(rotation)·spawnpos, facing away from the portal (SPEC §2.2).</summary>
    public static Arrival ArrivalAt(string cluster, Vector2 position, float rotation, Vector2 spawnPos)
    {
        var (dx, dz) = TileMath.Rotate(spawnPos.X, spawnPos.Y, rotation);
        var at = position + new Vector2((float)dx, (float)dz);
        return new Arrival(cluster, at, Transfers.Facing(position, at));
    }

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        var link = Slot.Link;
        yield return new NewTunnelExitEvent
        {
            db5 = Id,                           // [0] object id
            db6 = Position,                     // [1] the spot (the client's range test is about it)
            db7 = Direction,                    // [2] the spot tile's world rotation (Q1: as is, no +180), absent when 0
            db8 = Tile,                         // [3] the tile the client places
            db9 = link is not null,             // [4] active: enterable, its animator on
            dca = link?.Type ?? 0,              // [5] tunnelexittype index: SOLO 0, GROUP 1, RAID 2 (the animator, the tooltip icon)
            dcb = link?.Other(Slot).Cluster,    // [6] target cluster (world.xml); none while unlinked
            dcc = link?.Opened ?? 0,            // [7] opened at: the start of the clock ring
            dcd = link?.Closes ?? 0,            // [8] closes at ("closes soon" within shutdownwarningtime)
            dce = link?.Charges ?? 0,           // [9] charges now
            dcf = link?.MaxCharges ?? 0,        // [10] the type's maxcharges
            dcg = link is { } l && l.Charges < l.MaxCharges ? l.NextCharge : 0, // [11] the next charge, only while not full
            dch = link is { } r && r.RecentUntil > 0 ? r.RecentUntil : 0,        // [12] recently used until: enterable at 0 charges meanwhile
        };
    }
}

/// <summary>
/// The short-lived portal visual NewTunnelExitTemp (524, au9; the client's cmu with TunnelExitTempObjectView: an animator and a delayed FX
/// destruction), stageL roads SPEC §2.3, §7.5 [inference: its official use is unknown]: where an open-world portal closed in a player's view,
/// tile Tunnel_Exit_Temp at the spot, Roads:TempPortalSeconds, then a Leave. Touched only under <see cref="World.Sync"/>.
/// </summary>
public sealed class TunnelExitTemp(long id, CriticalMember spawn) : WorldObject(id, spawn)
{
    /// <summary>Key 3: the tiles.xml tile of the temporary portal.</summary>
    public const string Tile = "Tunnel_Exit_Temp";

    public override string Kind => "tunnel exit (closing)";

    public override string TypeName => $"{Tile} {((CriticalMember)Spawn).Path}";

    public override bool LeaveOnRange => true;

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return new NewTunnelExitTempEvent
        {
            dci = Id,           // [0] object id
            dcj = Position,     // [1] the spot of the portal that closed
            dck = Direction,    // [2] its rotation, absent when 0
            dcl = Tile,         // [3] the tile the client places
        };
    }
}
