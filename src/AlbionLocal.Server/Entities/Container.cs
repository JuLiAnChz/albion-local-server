using AlbionLocal.Server.Data;
using AlbionLocal.Server.Game;

namespace AlbionLocal.Server.Entities;

/// <summary>
/// A saved item container (ARCHITECTURE.md §4.1): its Guid (the client registers a container by it, Join keys 51/54)
/// and a fixed number of slots, each empty or holding one <see cref="Item"/>. A slot rule (the equipment's slot types)
/// decides what <see cref="Fits"/>; <see cref="Put"/> and <see cref="Take"/> do not check it, the caller does (a Join
/// places the saved items as they were saved). Saved entries that did not become items (unknown name) keep their slot
/// in <see cref="Unloaded"/>, so saving writes them back. Touched only under <see cref="World.Sync"/>.
/// </summary>
public sealed class Container
{
    private readonly Item?[] _slots;
    private readonly Func<ItemTemplate, int, bool>? _rule;

    /// <param name="rule">Whether a template may lie in a slot; null = anything fits anywhere.</param>
    public Container(Guid id, int size, Func<ItemTemplate, int, bool>? rule = null)
    {
        Id = id;
        _slots = new Item?[size];
        _rule = rule;
        Unloaded = new InventoryItem?[size];
    }

    public Guid Id { get; }

    public int Size => _slots.Length;

    /// <summary>The item in <paramref name="slot"/>; null = empty.</summary>
    public Item? this[int slot] => _slots[slot];

    /// <summary>Saved entries that did not become item objects, by slot (null = none); never sent.</summary>
    public InventoryItem?[] Unloaded { get; }

    /// <summary>The items in slot order.</summary>
    public IEnumerable<Item> Items => _slots.OfType<Item>();

    public int Count => _slots.Count(i => i is not null);

    /// <summary>Whether <paramref name="item"/> may lie in <paramref name="slot"/>; emptiness fits every slot of the container.</summary>
    public bool Fits(Item? item, int slot) =>
        slot >= 0 && slot < _slots.Length && (item is null || _rule?.Invoke(item.Template, slot) != false);

    /// <summary>Puts <paramref name="item"/> (null = nothing) into <paramref name="slot"/>; returns what lay there.</summary>
    public Item? Put(int slot, Item? item)
    {
        var old = _slots[slot];
        _slots[slot] = item;
        return old;
    }

    /// <summary>Empties <paramref name="slot"/>; returns what lay there.</summary>
    public Item? Take(int slot) => Put(slot, null);

    /// <summary>The slot holding <paramref name="item"/>; -1 = not in this container.</summary>
    public int IndexOf(Item item) => Array.IndexOf(_slots, item);

    public bool Contains(Item item) => IndexOf(item) >= 0;

    /// <summary>The item with object id <paramref name="id"/>; null = not in this container.</summary>
    public Item? Find(long id) => Array.Find(_slots, i => i?.Id == id);

    /// <summary>The first slot without an item and without a saved entry that did not load; -1 when there is none.</summary>
    public int FreeSlot()
    {
        for (var slot = 0; slot < _slots.Length; slot++)
            if (_slots[slot] is null && Unloaded[slot] is null) return slot;
        return -1;
    }

    /// <summary>The object id per slot, 0 = empty: what Join sends for the container (keys 52/55, c15.m).</summary>
    public long[] ObjectIds() => [.. _slots.Select(i => i?.Id ?? 0)];
}
