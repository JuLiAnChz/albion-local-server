namespace AlbionLocal.Server.Game;

/// <summary>
/// StageN qol (analysis/work/stageN/qol/SPEC.md §2): one saved loadout of a character (the client's ctg), stored as the client sends it in
/// LoadoutUpdate (op 428, xl) and read back unchanged in LoadoutRead (op 426, ahu). The per-slot arrays are in EquipmentSlot order (10);
/// <see cref="SlotStacks"/> and <see cref="PotionFoodPreference"/> are for the potion and food slots (8, 9) only. Immutable: an update
/// replaces the record, so <see cref="Character.Clone"/> can share it. Saved as JSON in characters.loadouts (schema v29).
/// </summary>
public sealed record Loadout
{
    /// <summary>The loadout's id (LoadoutCreate answer key 0; the key of every later op).</summary>
    public required string Id { get; init; }

    /// <summary>The owner Guid the client asked with (the character's id; guild loadouts are not made).</summary>
    public Guid Owner { get; init; }

    public string Name { get; init; } = "";

    /// <summary>The colour and icon indices the client picks (ahu keys 3, 4; headers keys 5, 6).</summary>
    public int Color { get; init; }

    public int Icon { get; init; }

    /// <summary>Raised by every update (ahv key 1, ahu key 5); the client sends the one it read (xl key 2).</summary>
    public int Version { get; init; }

    /// <summary>The order in the list (headers key 3; LoadoutOrderUpdate).</summary>
    public int Position { get; init; }

    /// <summary>xl 6 / ahu 6: the item uniquename per equipment slot (with @N; "" = empty, as the client sends it).</summary>
    public string[] Equipment { get; init; } = [];

    /// <summary>xl 7 / ahu 7: the item quality per equipment slot.</summary>
    public int[] Quality { get; init; } = [];

    /// <summary>xl 8 / ahu 8: the stack size of the potion and food slots.</summary>
    public int[] SlotStacks { get; init; } = [];

    /// <summary>xl 9, 10 / ahu 9, 10: the chosen spells per slot (the client's own strings, ctj.i/ctj.j).</summary>
    public string[] ActiveSpells { get; init; } = [];

    public string[] PassiveSpells { get; init; } = [];

    /// <summary>xl 11-13 / ahu 11-13: three LoadoutItemPreference per slot (0 EqualOrWorse, 1 Equal, 2 EqualOrBetter); the first is taken as the quality's.</summary>
    public int[] PreferenceA { get; init; } = [];

    public int[] PreferenceB { get; init; } = [];

    public int[] PreferenceC { get; init; } = [];

    /// <summary>xl 14 / ahu 14: the preference of the potion and food slots.</summary>
    public int[] PotionFoodPreference { get; init; } = [];

    /// <summary>xl 15-17 / ahu 15-17: the inventory part: uniquename, quality and stack size per entry.</summary>
    public string[] Items { get; init; } = [];

    public int[] ItemQuality { get; init; } = [];

    public int[] ItemStacks { get; init; } = [];
}
