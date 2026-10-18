using AlbionLocal.Server.Game;

namespace AlbionLocal.Server.Entities;

/// <summary>
/// What a joined player carries (until step 9 Game/PlayerItems): the equipment, 10 slots in EquipmentSlot order where an
/// item goes only into the slot of its items.xml slottype, and the bag, the 48 inventory slots of the official server
/// (Join keys 51/52 and 54/55, the character's container Guids). Saved entries beyond the container sizes are kept in
/// <see cref="Overflow"/> and, like the entries that did not load (<see cref="Container.Unloaded"/>), written back by
/// <see cref="SaveTo"/>. The item objects are created for each Join (<see cref="Load"/>). Touched only under
/// <see cref="World.Sync"/>.
/// </summary>
public sealed class Inventory
{
    public const int EquipmentSlots = 10; // EquipmentSlot.NumEntries
    public const int BagSlots = 48;

    /// <summary>EquipmentSlot names as items.xml writes them in slottype.</summary>
    public static readonly string[] SlotNames =
        ["mainhand", "offhand", "head", "armor", "shoes", "bag", "cape", "mount", "potion", "food"];

    public Inventory(Guid equipmentId, Guid bagId)
    {
        Equipment = new Container(equipmentId, EquipmentSlots, (template, slot) => template.Slot == SlotNames[slot]);
        Bag = new Container(bagId, BagSlots);
    }

    /// <summary>The worn items (equipment container, Join key 51).</summary>
    public Container Equipment { get; }

    /// <summary>The inventory proper (Join key 54).</summary>
    public Container Bag { get; }

    /// <summary>Saved entries beyond the container sizes; never sent, appended to the inventory on save.</summary>
    public List<InventoryItem> Overflow { get; } = [];

    /// <summary>The container with that Guid; null = not one of this player's.</summary>
    public Container? GetContainer(Guid id) => id == Equipment.Id ? Equipment : id == Bag.Id ? Bag : null;

    /// <summary>The item with object id <paramref name="id"/> in either container; null = none.</summary>
    public Item? FindItem(long id) => Equipment.Find(id) ?? Bag.Find(id);

    /// <summary>Item objects in both containers.</summary>
    public int Count => Equipment.Count + Bag.Count;

    public bool IsEquipped(Item item) => Equipment.Contains(item);

    /// <summary>Item type id per equipment slot, 0 = empty (CharacterEquipmentChanged key 2).</summary>
    public int[] EquipmentTypeIds() => [.. Enumerable.Range(0, EquipmentSlots).Select(s => Equipment[s] is { } i ? (int)i.Template.Index : 0)];

    /// <summary>The equipment as the stat calculator takes it (EquipmentSlot order, null = empty).</summary>
    public EquippedItem?[] EquippedItems() => [.. Enumerable.Range(0, EquipmentSlots).Select(s => Equipment[s]?.ForStats())];

    /// <summary>
    /// The character's saved containers as item objects: <paramref name="create"/> makes (and sends) the object of each
    /// saved entry in save order, the equipment slots first, then the inventory slots; null = the entry does not become
    /// an object (unknown or unsupported item), it stays in <see cref="Container.Unloaded"/>. Entries beyond the
    /// container sizes go to <see cref="Overflow"/> without an object.
    /// </summary>
    public static Inventory Load(Character c, Func<InventoryItem, Item?> create)
    {
        var inventory = new Inventory(c.EquipmentContainer, c.InventoryContainer);
        void Fill(Container into, IReadOnlyList<InventoryItem?> saved)
        {
            for (var slot = 0; slot < saved.Count; slot++)
            {
                if (saved[slot] is not { } entry) continue;
                if (slot >= into.Size) inventory.Overflow.Add(entry);
                else if (create(entry) is { } item) into.Put(slot, item);
                else into.Unloaded[slot] = entry;
            }
        }
        Fill(inventory.Equipment, c.Equipment);
        Fill(inventory.Bag, c.Inventory);
        return inventory;
    }

    /// <summary>
    /// Writes the current layout back to the persisted character (spells by uniquename via
    /// <paramref name="spellName"/>, <see cref="Item.ToRecord"/>). Entries that were not loaded go back into their slot
    /// while it is still empty; if an item was moved onto such a slot, the entry is appended after the inventory slots
    /// (beyond slot 47, where it is kept but not sent).
    /// </summary>
    /// <summary>
    /// The saved form of a container that has no overflow (a bank vault tab, stage 6b): each slot's item record, a slot whose
    /// saved entry did not load keeps that entry (an item put over it moves it to the first slot without either, or it is
    /// dropped with a warning when there is none); trailing empty slots are cut.
    /// </summary>
    public static List<InventoryItem?> Records(Container from, Func<int, string?> spellName)
    {
        var slots = new List<InventoryItem?>();
        var lost = new List<InventoryItem>();
        for (var s = 0; s < from.Size; s++)
        {
            slots.Add(from[s] is { } item ? item.ToRecord(spellName) : from.Unloaded[s]);
            if (from[s] is not null && from.Unloaded[s] is { } entry) lost.Add(entry);
        }
        foreach (var entry in lost)
        {
            var free = slots.FindIndex(e => e is null);
            if (free >= 0) slots[free] = entry;
            else Photon.Log.Warn("items", $"container {from.Id}: no slot left for the unloaded entry {entry.Name}; dropped");
        }
        while (slots.Count > 0 && slots[^1] is null) slots.RemoveAt(slots.Count - 1);
        return slots;
    }

    public void SaveTo(Character c, Func<int, string?> spellName)
    {
        var overflow = new List<InventoryItem>(Overflow);
        InventoryItem?[] Saved(Container from)
        {
            var slots = new InventoryItem?[from.Size];
            for (var s = 0; s < from.Size; s++)
            {
                slots[s] = from[s] is { } item ? item.ToRecord(spellName) : from.Unloaded[s];
                if (from[s] is not null && from.Unloaded[s] is { } lost) overflow.Add(lost);
            }
            return slots;
        }
        var equipment = Saved(Equipment);
        var inventory = new List<InventoryItem?>(Saved(Bag));
        inventory.AddRange(overflow);
        while (inventory.Count > 0 && inventory[^1] is null) inventory.RemoveAt(inventory.Count - 1);
        c.Equipment = equipment;
        c.Inventory = inventory;
    }
}
