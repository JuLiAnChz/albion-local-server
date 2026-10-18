using System.Numerics;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Entities;

/// <summary>
/// Albion.Common.Fishing.SubState (analysis/work/stageG/fishing/SPEC.md §2): FishingStart event key 3 and FishingCancel key 1.
/// The fisher's own client handles 5 (bite), 9, 11 and 14 and logs "unknown SubState" for 6, 12 and 13, so those three are
/// never sent as FishingStart (6 goes out as FishingMiniGame, 12 and 13 only as FishingCancel key 1).
/// </summary>
public enum FishingState
{
    Invalid = 0, Idle = 1, Waiting = 2, Casting = 3, Cast = 4, Bitten = 5, MiniGame = 6, Pull = 7, GiveLine = 8, Success = 9, Fail = 10,
    GroundFail = 11, ToolFail = 12, CaughtNothing = 13, CatchMissed = 14, CatchTooEarly = 15,
}

/// <summary>
/// A fishing zone of the template (SPEC §3.2, §7.1): a tile FishingZone_&lt;ZONE&gt;[_ROUND|_SQUARE] with a &lt;fishingzone zone radius
/// | width depth&gt; component, as NewFishingZoneObject (360, atu) {0 id, 1 the tile position (exact: 29/29 in 0204), 2 charges
/// left (Int16), 3 the prefab's VFX state = maxcharges − charges (Int16; SetVisualState), 4 the prefab under
/// FX/ClientPrefabs/fishing/ ("" = none, the base zones)}. A repeat with the known id updates keys 2 and 3 only (cjb.aj), sent
/// to everyone who knows the zone when its charges change. Left by visibility like the buildings (enter at cell distance 4,
/// Leave at 5/6, NOTES "Seen in 002835"). The strike takes a charge; the regrowth gives one back per
/// <see cref="FishingZoneSettings.ChargeMin"/>-<see cref="FishingZoneSettings.ChargeMax"/> s, first waiting
/// <see cref="FishingZoneSettings.DelayAtZero"/> at 0 (Systems/Fishing). Touched only under <see cref="World.Sync"/>.
/// </summary>
public sealed class FishingZone(long id, FishingZoneSpawn spawn, FishingZoneSettings settings) : WorldObject(id, spawn)
{
    public new FishingZoneSpawn Spawn { get; } = spawn;

    /// <summary>Its kind's numbers (content/fishing.json by <see cref="Name"/>).</summary>
    public FishingZoneSettings Settings { get; } = settings;

    /// <summary>fishingzone@zone: FRESHWATER_BASE, OCEAN_SPOT_SWARM, …</summary>
    public string Name => Spawn.Zone ?? "";

    /// <summary>Whether it is sea water (its zone name starts with OCEAN; OCEAN_SPOT_TREASURE's tile says River, its name Ocean).</summary>
    public bool IsOcean => Name.StartsWith("OCEAN", StringComparison.OrdinalIgnoreCase);

    public int MaxCharges => Settings.MaxCharges;

    public int Charges { get; set; } = settings.MaxCharges;

    /// <summary>When the next charge comes back (<see cref="World.Clock"/> time); null = full, or not counting.</summary>
    public TimeSpan? RegrowAt { get; set; }

    public override string Kind => "fishing zone";
    public override string TypeName => $"{Name} {Charges}/{MaxCharges}";
    public override bool LeaveOnRange => true;

    /// <summary>
    /// Whether <paramref name="p"/> lies in the zone: within the radius of a round one, or in the width × depth rectangle
    /// around the tile position turned by the tile's direction (the _SQUARE ones: 50 × 50, no radius).
    /// </summary>
    public bool Contains(Vector2 p)
    {
        var d = p - Position;
        if (Spawn.Radius > 0) return d.LengthSquared() <= Spawn.Radius * Spawn.Radius;
        var (w, h) = (TileMath.Float(Spawn.Attr("width")), TileMath.Float(Spawn.Attr("depth")));
        if (w <= 0 || h <= 0) return false;
        var (x, z) = TileMath.Rotate(d.X, d.Y, -Direction); // back into the tile's own axes (Rotate places a child by +Direction)
        return Math.Abs(x) <= w / 2 && Math.Abs(z) <= h / 2;
    }

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return Changed();
    }

    /// <summary>The NewFishingZoneObject of <see cref="BuildCreate"/>, also the charge update (the client takes keys 2 and 3 of a known id).</summary>
    public NewFishingZoneObjectEvent Changed() => new()
    {
        cz4 = Id,                                              // [0]
        cz5 = Position,                                        // [1] the tile position
        cz6 = (short)Math.Clamp(Charges, 0, short.MaxValue),   // [2] charges left (left out at 0)
        cz7 = (short)Math.Clamp(MaxCharges - Charges, 0, short.MaxValue), // [3] the VFX state (left out when full)
        cz8 = Settings.Prefab,                                 // [4] "" is sent too (the base zones)
    };
}

/// <summary>The client data of a fishing float: none, it is the server's own object at the landing point.</summary>
public sealed record FloatSpawn : ClusterObject;

/// <summary>
/// NewFloatObject key 4 (FloatObjectView.OnFloatChanged 0x181a4f3c0, SPEC §2): 1 floating (splash on spawn), 2 bitten (bite
/// sound), 3 hooked, 4 caught (success effect), 5 escaped (escape effect).
/// </summary>
public enum FloatState { Floating = 1, Bitten = 2, Hooked = 3, Caught = 4, Escaped = 5 }

/// <summary>
/// The bobber of a fishing session (NewFloatObject 359, atv; SPEC §3.2): a world object at the landing point in the dynamic id
/// range, {0 id, 1 the landing point (FishingCast key 1), 2 rotation = the fisher's facing, 3 the fisher (the client marks its
/// own float by it), 4 <see cref="State"/>}. A repeat with the known id changes only key 4: the state changes go out so, to
/// everyone who knows the float; it has its own visibility (a player coming into view gets it with its current state) and
/// leaves with an ordinary Leave when the session ends.
/// </summary>
public sealed class FishingFloat(long id, FloatSpawn spawn, long ownerId, float rotation) : WorldObject(id, spawn)
{
    public long OwnerId { get; } = ownerId;

    public float Rotation { get; } = rotation;

    public FloatState State { get; set; } = FloatState.Floating;

    public override string Kind => "fishing float";
    public override string TypeName => $"{State} of #{OwnerId}";
    public override bool LeaveOnRange => true;

    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        yield return Changed();
    }

    /// <summary>The NewFloatObject of <see cref="BuildCreate"/>, also the state change.</summary>
    public NewFloatObjectEvent Changed() => new()
    {
        cz9 = Id,             // [0]
        c0a = Position,       // [1]
        c0b = Rotation,       // [2] degrees
        c0c = OwnerId,        // [3]
        c0d = (int)State,     // [4]
    };
}

/// <summary>
/// A player's fishing session (SPEC §4; <see cref="Player.Fishing"/>): from an accepted FishingStart op to the FishingCancel event.
/// The rod, the session stamp (FishingStart op key 0, FishingStart event key 4 of every state but Bitten), where the player
/// stands, the cast's landing point and power, the float, the zone the landing point picked (null = the default water), the
/// bite, and from the strike the catch and its minigame. Its timers (the map's) check that the session is still this one.
/// </summary>
public sealed class FishingSession(Item rod, long stamp, Vector2 spot)
{
    public Item Rod { get; } = rod;

    /// <summary>FishingStart op key 0, the client's GameTimeStamp.</summary>
    public long Stamp { get; } = stamp;

    /// <summary>Where the player stands (a Move off it ends the session).</summary>
    public Vector2 Spot { get; } = spot;

    public FishingState State { get; set; } = FishingState.Casting;

    /// <summary>FishingCast key 1, the landing point.</summary>
    public Vector2 Target { get; set; }

    /// <summary>The cast's charge, 0-1 (FishingCast key 0 − the session stamp, / 1 s).</summary>
    public float Power { get; set; }

    public FishingFloat? Float { get; set; }

    /// <summary>The zone object the landing point picked (Systems/Fishing.Pick); null = the default zone of <see cref="Ocean"/> water.</summary>
    public FishingZone? Zone { get; set; }

    public bool Ocean { get; set; }

    /// <summary>The bite's GameTimeStamp (FishingStart key 4 of state Bitten); 0 before.</summary>
    public long BiteStamp { get; set; }

    /// <summary>What the strike rolled: given on a success, lost on a fail.</summary>
    public List<(ItemTemplate Item, int Amount)> Catch { get; set; } = [];

    /// <summary>The minigame setting's keys 3-15 (FishingMiniGame), kept for an observer's view-entry snapshot.</summary>
    public float[]? MiniGame { get; set; }

    /// <summary>The strike's GameTimeStamp (FishingMiniGame key 2); the success must come gamedata safety later.</summary>
    public long MiniGameStart { get; set; }
}
