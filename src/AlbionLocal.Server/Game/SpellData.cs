using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using AlbionLocal.Server.Data;

namespace AlbionLocal.Server.Game;

/// <summary>Spell candidates of an item: per active and passive slot the spell ids it may hold.</summary>
public sealed record ItemSpellSlots(
    [property: JsonPropertyName("na")] int ActiveSlots,
    [property: JsonPropertyName("np")] int PassiveSlots,
    [property: JsonPropertyName("a")] short[][] Active,
    [property: JsonPropertyName("p")] short[][] Passive);

/// <summary>
/// Spell ids and item spell lists, built at startup from the client's spells.xml and the item templates
/// (<see cref="FromClient"/>). Spell id = the client's counter over spells.xml (be7.da 0x18064e1d0 / bi1.n5 0x1806b5020,
/// <see cref="ClientNumbering.Spells"/>: active, passive and toggle spells in file order, each channeling child right
/// after its parent; 9326 ids in 345319),
/// the id space of NewEquipmentItem keys 9/10, CharacterEquipmentChanged key 7 and EquipmentItemChangeSpell.
/// data/spells.json, which analysis/tools/gen_item_spells.py made from the same files, is the golden reference of the
/// self-test (<see cref="Load"/>).
/// </summary>
public sealed class SpellData
{
    private readonly string[] _names;
    private readonly char[] _kinds;
    private readonly bool[] _locked;
    private readonly Dictionary<string, short> _byName = new();
    private SpellTemplate?[] _templates = [];

    /// <param name="spells">By spell id: uniquename, kind (a = active/toggle, p = passive, c = channeling), locked.</param>
    public SpellData(IReadOnlyList<(string Name, char Kind, bool Locked)> spells, Dictionary<string, ItemSpellSlots> items, Dictionary<string, short> consume)
    {
        _names = [.. spells.Select(s => s.Name)];
        _kinds = [.. spells.Select(s => s.Kind)];
        _locked = [.. spells.Select(s => s.Locked)];
        for (var i = _names.Length - 1; i >= 0; i--) _byName[_names[i]] = (short)i; // a name used twice finds its first id
        Items = items;
        Consume = consume;
    }

    /// <summary>Base item uniquename (no @N) -> slot candidates.</summary>
    public Dictionary<string, ItemSpellSlots> Items { get; }

    /// <summary>Item uniquename (with @N) -> consumespell id.</summary>
    public Dictionary<string, short> Consume { get; }

    /// <summary>
    /// The tables from the client's spells.xml (top-level elements) and the item templates, as
    /// analysis/tools/gen_item_spells.py builds them: locked = a top-level element of that name has
    /// unlockedtoequip="false"; per item with spell slots, active slot i takes the active/toggle spells of its
    /// <see cref="ItemTemplate.SpellLists"/>[i], passive slot i the passive ones (names that are no spell dropped);
    /// consume = every variant whose consumespell names a spell.
    /// </summary>
    public static SpellData FromClient(IEnumerable<XElement> spells, ItemStore items)
    {
        var locked = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<XElement> NoteLocked(IEnumerable<XElement> top)
        {
            foreach (var e in top)
            {
                if (e.Attribute("unlockedtoequip")?.Value == "false" && e.Attribute("uniquename")?.Value is { } name) locked.Add(name);
                yield return e;
            }
        }
        var rows = ClientNumbering.Spells(NoteLocked(spells)).ToList();
        var d = new SpellData(
            [.. rows.Select(r => (r.Name, r.Kind switch { "passivespell" => 'p', "channelingspell" => 'c', _ => 'a' }, locked.Contains(r.Name)))],
            new Dictionary<string, ItemSpellSlots>(StringComparer.Ordinal), new Dictionary<string, short>(StringComparer.Ordinal))
        {
            _templates = [.. rows.Select(r => SpellTemplate.From(r.Id, r.Name, r.Kind, r.Element))],
        };
        short[] Ids(string[] names, char kind) => [.. names.Select(d.IdOf).Where(id => id >= 0 && d._kinds[id] == kind)];
        foreach (var t in items.All)
        {
            if (t.BaseName.Length == 0) continue;
            if (t.Name == t.BaseName && (t.ActiveSpellSlots > 0 || t.PassiveSpellSlots > 0))
                d.Items[t.Name] = new ItemSpellSlots(t.ActiveSpellSlots, t.PassiveSpellSlots,
                    [.. Enumerable.Range(0, t.ActiveSpellSlots).Select(i => Ids(t.SpellLists[i], 'a'))],
                    [.. Enumerable.Range(0, t.PassiveSpellSlots).Select(i => Ids(t.SpellLists[i], 'p'))]);
            if (t.ConsumeSpell is { } consume && d._byName.TryGetValue(consume, out var id)) d.Consume[t.Name] = id;
        }
        return d;
    }

    /// <summary>data/spells.json: the golden reference of the self-test (the server builds its tables with <see cref="FromClient"/>).</summary>
    public static SpellData Load(string path)
    {
        using var s = File.OpenRead(path);
        var f = JsonSerializer.Deserialize<SpellsFile>(s) ?? throw new InvalidDataException($"{path}: empty");
        return new SpellData(
            [.. f.Spells.Select(r => (r[0].GetString() ?? "", r[1].GetString() is [var k] ? k : '?', r[2].GetBoolean()))], f.Items, f.Consume);
    }

    /// <summary>The layout of data/spells.json.</summary>
    private sealed class SpellsFile
    {
        /// <summary>[uniquename, kind (a = active/toggle, p = passive, c = channeling), locked] by spell id.</summary>
        [JsonPropertyName("spells")] public required JsonElement[][] Spells { get; init; }
        [JsonPropertyName("items")] public required Dictionary<string, ItemSpellSlots> Items { get; init; }
        [JsonPropertyName("consume")] public required Dictionary<string, short> Consume { get; init; }
    }

    public int Count => _names.Length;

    /// <summary>
    /// The cast data and effects of spell <paramref name="id"/> (stage 5b, <see cref="SpellTemplate"/>); null = no such id, or
    /// tables without them (data/spells.json, the golden reference, has names only).
    /// </summary>
    public SpellTemplate? Template(int id) => id >= 0 && id < _templates.Length ? _templates[id] : null;

    /// <summary>The template of the spell named <paramref name="name"/> (applyspell, pulsingspell, energyrewardspell); null = unknown.</summary>
    public SpellTemplate? Template(string? name) => Template(IdOf(name));

    /// <summary>
    /// How often each effect tag occurs in the spells' top-level effects (the start-up report of ARCHITECTURE.md §6.4: what
    /// the executor runs and what it still logs as a no-op); empty without templates.
    /// </summary>
    public IReadOnlyDictionary<string, int> EffectTags() =>
        _templates.OfType<SpellTemplate>().SelectMany(t => t.Effects).GroupBy(e => e.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    public string? Name(int id) => id >= 0 && id < _names.Length ? _names[id] : null;

    /// <summary>a = active/toggle, p = passive, c = channeling; '?' = no such id.</summary>
    public char KindOf(int id) => id >= 0 && id < _kinds.Length ? _kinds[id] : '?';

    /// <summary>Spell id by uniquename, -1 = unknown.</summary>
    public short IdOf(string? name) => name is not null && _byName.TryGetValue(name, out var id) ? id : (short)-1;

    public bool IsActive(int id) => id >= 0 && id < _kinds.Length && _kinds[id] == 'a';
    public bool IsPassive(int id) => id >= 0 && id < _kinds.Length && _kinds[id] == 'p';

    /// <summary>spells.xml unlockedtoequip="false": needs a destiny board SpellUse unlock (ctp.ahg 0x18099e250).</summary>
    public bool IsLocked(int id) => id >= 0 && id < _locked.Length && _locked[id];

    /// <summary>Slot candidates of an item (enchantments share the base item's lists); null = no spell slots.</summary>
    public ItemSpellSlots? SlotsOf(string itemName) => Items.GetValueOrDefault(itemName.Split('@')[0]);

    /// <summary>consumespell of a potion/food item (CharacterEquipmentChanged key 7 [12]/[13]), -1 = none.</summary>
    public short ConsumeSpell(string itemName) => Consume.TryGetValue(itemName, out var id) ? id : (short)-1;

    /// <summary>
    /// Selection for a new item, as the official server does (captures, analysis/work/item-spells): a slot
    /// with exactly one candidate the owner can use gets it, every other slot -1 (the player chooses).
    /// </summary>
    public (short[] Active, short[] Passive) DefaultSelection(string itemName, Func<int, bool> usable)
    {
        if (SlotsOf(itemName) is not { } s) return ([], []);
        static short Pick(short[] candidates, Func<int, bool> usable) =>
            candidates.Where(c => usable(c)).ToArray() is [var only] ? only : (short)-1;
        return (Enumerable.Range(0, s.ActiveSlots).Select(i => i < s.Active.Length ? Pick(s.Active[i], usable) : (short)-1).ToArray(),
                Enumerable.Range(0, s.PassiveSlots).Select(i => i < s.Passive.Length ? Pick(s.Passive[i], usable) : (short)-1).ToArray());
    }

    /// <summary>
    /// Validation of a spell choice in the order of the client's a6b.jp 0x180924e50 (ItemContainerErrorCode in
    /// the result): 25 InvalidSpell, 27 SpellNotUnlocked, 41 bad slot or kind, 26 not in the slot's list; 0 = ok.
    /// </summary>
    public int Check(string itemName, bool active, int slot, int spell, Func<int, bool> usable)
    {
        if (spell < 0 || spell >= Count) return 25;
        if (!usable(spell)) return 27;
        if (SlotsOf(itemName) is not { } s) return 41;
        var (count, kindOk, lists) = active
            ? (s.ActiveSlots, IsActive(spell), s.Active)
            : (s.PassiveSlots, IsPassive(spell), s.Passive);
        if (slot < 0 || slot >= count || !kindOk) return 41;
        return slot < lists.Length && lists[slot].Contains((short)spell) ? 0 : 26;
    }
}
