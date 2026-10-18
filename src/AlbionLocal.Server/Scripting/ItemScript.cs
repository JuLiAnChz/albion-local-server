using AlbionLocal.Server.Entities;

namespace AlbionLocal.Server.Scripting;

/// <summary>
/// Binds an <see cref="ItemScript"/> class to item templates by uniquename (ARCHITECTURE.md §6.1, §6.5; TC ItemScript),
/// found by <see cref="ScriptRegistry"/>: an exact name or a mask with '?' (one character) and '*' (any run), ignoring
/// case. A pattern without '@' is matched against the base name (the items.xml uniquename), so it covers the item's
/// enchantments too; one with '@' against the variant's own name (T4_MAIN_SWORD@1, *@4). The most precise pattern wins:
/// the item's own name exactly over every mask; then more literal characters, where a name without '@' counts on an
/// enchanted variant as a mask (T4_MAIN_SWORD@? beats T4_MAIN_SWORD on T4_MAIN_SWORD@2, T4_MAIN_SWORD beats *@2); at
/// an equal count a pattern with '@' beats an exact base name, which beats a mask without '@'. Two equally precise ones
/// of different classes on one item stop the start; a pattern outranked on every item it matches is warned. A class
/// may carry several.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ItemScriptAttribute(string pattern) : Attribute
{
    public string Pattern { get; } = pattern;
}

/// <summary>
/// What an item does beyond the default path (ARCHITECTURE.md §6.5): override only what differs. One instance per class
/// and <see cref="ScriptRegistry"/>, shared by every item it is bound to; the callbacks run in the world thread under
/// World.Sync, so no locks, and no static state (§6.8). OnUse comes with consumables (UseConsumableFromInventory, op 374).
/// </summary>
public abstract class ItemScript
{
    /// <summary>
    /// <paramref name="item"/> now counts as worn by <paramref name="p"/>: after a move into the equipment (before the
    /// bars are recalculated and the move's events go out; the move saves the items after it), and at Join for every
    /// equipped item (after JoinFinished, so the client has the player, before the hooks' OnLogin; the items are saved
    /// after them). The bars come only from the equipment and the destiny levels (<see cref="Player.RecalculateStats"/>):
    /// a script cannot change them yet (a stat modifier comes with buffs) and must not set them itself, since the next
    /// recalculation overwrites them.
    /// </summary>
    public virtual void OnEquip(Player p, Item item) { }

    /// <summary>
    /// After a move out of the equipment, before the bars are recalculated (as <see cref="OnEquip"/>). Not called when
    /// the player leaves the world.
    /// </summary>
    public virtual void OnUnequip(Player p, Item item) { }
}
