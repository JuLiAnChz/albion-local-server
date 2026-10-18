using System.Numerics;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Entities;

/// <summary>How the visibility announces a kind of object (SPEC stage 2 §5; replaces the AnnouncePolicy of ARCHITECTURE.md §4.3).</summary>
public enum AnnounceRange
{
    /// <summary>In the 9×9-cell window around the player (<see cref="VisibilityTracker"/>).</summary>
    Window,

    /// <summary>To every player on the map at Join (phase B), whatever the distance, and never left while the player stays: the faction trader.</summary>
    Cluster,
}

/// <summary>
/// An object of a map that players see (ARCHITECTURE.md §4.1; TC WorldObject): a building, exit, travel point, NPC spot,
/// location marker or static harvestable made from the cluster's client data (<see cref="MapSpawner"/>). Its object
/// id (key 0 of every event about it) is taken once when the map spawns it, so it stays the same while the map lives;
/// re-sending its New* event is safe (every New* handler of the client first looks the id up, ciy.a, and only updates an
/// object it has). Each kind builds its own event for a viewer (<see cref="BuildCreate"/>); the client drops it with the
/// common Leave (event 1, key 0 = object id). Static for now: nothing moves, so its <see cref="Cell"/> is fixed. Mobs
/// (stage 5a, <see cref="Mob"/>) are WorldObjects too, put on and taken off the map while it lives
/// (<see cref="Map.AddObject"/>, <see cref="Map.RemoveObject"/>) and standing still until their AI comes (5d); players are
/// not. Touched only under <see cref="World.Sync"/>.
/// </summary>
public abstract class WorldObject(long id, ClusterObject spawn)
{
    /// <summary>Object id, from <see cref="ObjectIds"/>.</summary>
    public long Id { get; } = id;

    /// <summary>The client data the object comes from (tile, position, component).</summary>
    public ClusterObject Spawn { get; } = spawn;

    /// <summary>The stable key of the template object (&lt;cluster&gt;:&lt;slot&gt;:&lt;n&gt;).</summary>
    public SpawnKey Key => Spawn.Key;

    /// <summary>World 2D (x, z): the exact tile position the client binds landscape objects to (SPEC §1).</summary>
    public virtual Vector2 Position => Spawn.Position;

    /// <summary>Yaw in degrees.</summary>
    public virtual float Direction => Spawn.Direction;

    /// <summary>The map the object is on (set when the map spawns it).</summary>
    public Map? Map { get; internal set; }

    /// <summary>Its visibility cell on the map (<see cref="CellGrid"/>), set when the map adds it.</summary>
    public (int X, int Z) Cell { get; internal set; }

    /// <summary>What it is, for logs and .near ("building", "exit", …).</summary>
    public abstract string Kind { get; }

    /// <summary>Its type for logs and .near: building uniquename, target cluster, tile, location id, "WOOD T1".</summary>
    public abstract string TypeName { get; }

    public virtual AnnounceRange Range => AnnounceRange.Window;

    /// <summary>
    /// Whether leaving the window sends Leave (buildings, travel points, war wagon, location markers). False for the
    /// landscape-bound kinds (exits, island access points, expedition agents, static harvestables): the official server
    /// drops them from the player's known set without telling the client and sends them again on re-entry, and never a
    /// Leave, not even before ChangeCluster (SPEC §5).
    /// </summary>
    public abstract bool LeaveOnRange { get; }

    /// <summary>
    /// The events that create the object in <paramref name="viewer"/>'s client, in order; empty for a kind that goes out
    /// in a list (<see cref="Harvestable"/>: NewSimpleHarvestableObjectList, built by <see cref="Harvestable.Lists"/>).
    /// </summary>
    public abstract IEnumerable<AlbionEvent> BuildCreate(Player viewer);

    /// <summary>Leave (event 1, arp): key 0 = the object id, nothing else (1944 of 1953 captured Leaves; a key 1 byte 3 on 9 is unknown).</summary>
    public LeaveEvent BuildLeave() => new() { ObjectId = Id };

    public override string ToString() => $"{Kind} {TypeName} #{Id} at ({Position.X:0.##}, {Position.Y:0.##})";
}
