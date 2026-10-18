using System.Numerics;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Entities;

/// <summary>
/// A world object with a container a player may open and take from (RegisterToObject, InventoryMoveGivenItems; Network/LootHandler):
/// a killed mob's <see cref="LootBag"/> or a dead player's <see cref="PlayerLootBag"/> (stageE). Left when out of range, sent anew on
/// re-entry. Touched only under <see cref="World.Sync"/>.
/// </summary>
public abstract class LootObject(long id, ClusterObject spawn) : WorldObject(id, spawn)
{
    /// <summary>The items, one per slot (AttachItemContainer key 1 is its Guid, key 3 the object ids, key 5 the slot count).</summary>
    public abstract Container Items { get; }

    /// <summary>The container <paramref name="p"/> opens and takes from: <see cref="Items"/>, but a personal chest's own per player (stageH).</summary>
    public virtual Container ItemsFor(Player p) => Items;

    /// <summary>Whether <paramref name="p"/> may open it now (checked at open time).</summary>
    public abstract bool MayOpen(Player p);

    /// <summary>Left when out of range, sent anew on re-entry (the captures re-send NewLoot on every re-entry).</summary>
    public override bool LeaveOnRange => true;
}

/// <summary>
/// The item loot of a killed mob (stage 5c, analysis/work/stage5/SPEC.md §5c.4; ARCHITECTURE.md §4.1 LootChest): a world
/// object in the visibility grid at the mob's position, created in the kill batch after the rewards and before the death
/// clear, holding the rolled items in a container of its own (<see cref="Items"/>, a Guid the client registers when the
/// looter opens it). Only <see cref="Looters"/> (the killer) and the members of its party (<see cref="PartyIds"/>, stageD party SPEC §6)
/// may open it (<see cref="MayOpen"/>). It vanishes with a Leave when it is empty or
/// after <see cref="Content.CombatSettings.LootBagTime"/> (120 s). Mobs get NewLoot (98), not NewLootChest (392), also for
/// a lootprefab chest (the captured miniboss, SPEC §5c.4). Touched only under <see cref="World.Sync"/>.
/// </summary>
public sealed class LootBag(long id, Mob mob, Container items, Guid[] looters, float lootFactor, long createdTicks, int[]? partyIds = null)
    : LootObject(id, mob.Spawn)
{
    /// <summary>The killed mob: its type (key 1), object id (key 2), name tag (key 3).</summary>
    public Mob Mob { get; } = mob;

    public override Container Items { get; } = items;

    /// <summary>Character Guids that may open it (NewLoot key 10): the killer.</summary>
    public Guid[] Looters { get; } = looters;

    /// <summary>
    /// NewLoot key 11 (Int32[]): the killer's party id when it was in one, else null (left out). The client opens the bag
    /// without the protection countdown when its party id is listed (clk.aae 0x180b4f710).
    /// </summary>
    public int[]? PartyIds { get; } = partyIds;

    /// <summary>Whether <paramref name="p"/> may open it, checked at open time: a looter, or in a listed party.</summary>
    public override bool MayOpen(Player p) =>
        Looters.Contains(p.Character.Id) || PartyIds is { } ids && p.World.Parties.Of(p.Character.Id) is { } party && ids.Contains(party.Id);

    /// <summary>NewLoot key 15: gamedata ClusterDangerBonus moblootfactor of the cluster (safe 1.25).</summary>
    public float LootFactor { get; } = lootFactor;

    /// <summary>NewLoot key 12: when the bag was made (Int64 ticks).</summary>
    public long CreatedTicks { get; } = createdTicks;

    private readonly Vector2 _position = mob.Position;
    private readonly float _direction = mob.Direction;

    public override Vector2 Position => _position;

    public override float Direction => _direction;

    public override string Kind => "loot";

    public override string TypeName => $"{Mob.Template.Name} ({Items.Count} item(s))";

    /// <summary>Whether the mob's lootprefab is a chest (T0_LOOTCHEST_MINIBOSS): NewLoot key 14 (one sample, SPEC §9 Q19).</summary>
    public bool IsChest => Mob.Template.LootPrefab.Contains("LOOTCHEST", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// NewLoot (98, aud; the two captured T0_LOOTBAG mob bags of 114814): 0 loot id, 1 mob type, 2 the dead mob's object
    /// id, 3 "@" + namelocatag, 4 position, 5 direction, 7 byte 2 (a mob's), 10 the looters' Guids, 11 the party ids (only with
    /// a party), 12 creation ticks, 14
    /// true only for a lootprefab chest, 15 moblootfactor, 16 1, 19 [] (empty string[]), 24 byte 0, 25 byte 0, 26 short -1
    /// (24-26 always, as official).
    /// </summary>
    public override IEnumerable<AlbionEvent> BuildCreate(Player viewer)
    {
        var tag = Mob.Template.NameLocaTag ?? Mob.Template.Name;
        yield return new NewLootEvent
        {
            c3e = Id,                                   // [0]
            c3f = Mob.Template.Id,                      // [1] mob type
            c3g = Mob.Id,                               // [2] the dead mob
            c3h = tag.StartsWith('@') ? tag : "@" + tag,// [3]
            c3i = Position,                             // [4]
            c3j = Direction,                            // [5]
            c3l = 2,                                    // [7] a mob's loot
            c3o = Looters,                              // [10]
            c3p = PartyIds,                             // [11] Int32[], absent without a party
            c3q = CreatedTicks,                         // [12]
            c3s = IsChest,                              // [14] only for a lootprefab chest
            c3t = LootFactor,                           // [15]
            c3u = 1,                                    // [16]
            c3x = [],                                   // [19]
            Extra = new() { [24] = (byte)0, [25] = (byte)0, [26] = (short)-1 },
        };
    }
}
