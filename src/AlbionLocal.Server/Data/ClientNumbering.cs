using System.Xml.Linq;

namespace AlbionLocal.Server.Data;

/// <summary>
/// How the client numbers its templates while it loads the GameData XML (ARCHITECTURE.md §5.1): one method per kind,
/// each taking the file's top-level elements in file order (<see cref="GameDataFile.Elements"/>). These ids go over the
/// wire, so each rule is pinned by a golden test against the tables the analysis tools generated (data/items.tsv,
/// data/spells.json, data/gamedata.json) or, for buildings and harvestables, against the values in the captures.
/// </summary>
public static class ClientNumbering
{
    /// <summary>
    /// Item type ids (NewSimpleItem/NewEquipmentItem key 1, CharacterEquipmentChanged key 2). bdh.da 0x18060a3f0 resets
    /// the counter bdi.fi to 0 and every variant it creates takes ++fi (bdi.hu 0x1805f31d0), so ids start at 1: the
    /// top-level item elements of items.xml in file order (not &lt;shopcategories&gt;, not elements without uniquename),
    /// each followed by the &lt;enchantment&gt; children of its &lt;enchantments&gt; (@1, @2, …). Same rule as
    /// analysis/tools/gen_items.py; confirmed in the client (T4 kit and resources showed the right items).
    /// </summary>
    public static IEnumerable<(short Index, XElement Item, XElement? Enchantment)> Items(IEnumerable<XElement> items)
    {
        var index = 0;
        foreach (var el in items)
        {
            if (el.Name.LocalName == "shopcategories" || el.Attribute("uniquename") is null) continue;
            yield return (checked((short)++index), el, null);
            if (el.Element("enchantments") is not { } enchantments) continue;
            foreach (var e in enchantments.Elements("enchantment"))
                yield return (checked((short)++index), el, e);
        }
    }

    /// <summary>
    /// Spell ids (NewEquipmentItem keys 9/10, CharacterEquipmentChanged key 7, EquipmentItemChangeSpell key 3). be7.da
    /// 0x18064e1d0 (SpellData.LoadDataFromXml) walks the top-level elements of spells.xml; activespell, passivespell and
    /// togglespell each take id = counter++ in bi1.n5 0x1806b5020 (from 0, before their attributes and children are
    /// read), colortag and anything else none. bio.nw 0x1806c2340: each direct &lt;channelingspell&gt; child of an
    /// active or toggle spell takes the next id right after its parent, named parent + "_CHANNELING" (bip.nv
    /// 0x1806c3750). No other code calls bi1.n5. 9326 ids in 345319; analysis/tools/gen_spells.py.
    /// Kind is the element name ("channelingspell" for the children); Parent is the parent's id, -1 for top-level spells.
    /// </summary>
    public static IEnumerable<(short Id, string Name, string Kind, XElement Element, short Parent)> Spells(IEnumerable<XElement> spells)
    {
        var next = 0;
        foreach (var el in spells)
        {
            var kind = el.Name.LocalName;
            if (kind is not ("activespell" or "passivespell" or "togglespell")) continue;
            var name = el.Attribute("uniquename")?.Value ?? "";
            var id = checked((short)next++);
            yield return (id, name, kind, el, -1);
            if (kind == "passivespell") continue;
            foreach (var channeling in el.Elements("channelingspell"))
                yield return (checked((short)next++), name + "_CHANNELING", "channelingspell", channeling, id);
        }
    }

    /// <summary>
    /// Building type ids (NewBuilding key 2; SPEC stage 2 §4): the 0-based index of the element among all top-level
    /// elements of buildings.xml, plus <paramref name="offset"/> (ClientNumbering:BuildingIdOffset, 1). All 13 buildings
    /// in the captures, made with this client and data, are index + 1 (Thetford: SWAMP_GREEN_REPAIRSHOP 495, VANITY_STORE
    /// 371, T8_MELDBUILDING 27, FACTION_TRADER 21, ARENA_AGENCY 349, T1_BANK 52, MARKETPLACE_CENTERCITY 125). The client's
    /// loader resets the b5j counter to 0 (bbh.c8 0x18057d6e0) and post-increments it (b5j.ul 0x180562ba0), which would
    /// give the index itself: what takes id 0 is open (SPEC §9), hence the setting. durabilitythresholddefinition,
    /// fortificationtype and fortificationtypegate are no buildings (the loader skips them) but take their index here;
    /// the first of them is at index 626, so the rule is certain for 0-625 and open for the four castle and shrine
    /// buildings after it (627-629, 635), which nothing before territories needs.
    /// </summary>
    public static IEnumerable<(short Id, XElement Element)> Buildings(IEnumerable<XElement> buildings, int offset = 1)
    {
        var index = 0;
        foreach (var el in buildings)
        {
            var raw = index++;
            if (el.Name.LocalName is "durabilitythresholddefinition" or "fortificationtype" or "fortificationtypegate") continue;
            yield return (checked((short)(raw + offset)), el);
        }
    }

    /// <summary>
    /// Harvestable type ids (NewSimpleHarvestableObjectList key 1, NewHarvestableObject key 5; SPEC stage 2 §4): the
    /// 0-based order of the &lt;Harvestable&gt; elements of harvestables.xml as bng.da 0x1806e80d0 reads them (bng.abo
    /// 0x1806e7320 looks the type up by it): 0 WOOD, 2 WOOD_GIANTTREE, 3 WOOD_CRITTER, 7 ROCK, 14 FIBER, 20 HIDE, 27 ORE,
    /// every captured value matching. A byte, as key 1 carries it.
    /// </summary>
    public static IEnumerable<(byte Id, XElement Element)> Harvestables(IEnumerable<XElement> harvestables)
    {
        var next = 0;
        foreach (var el in harvestables)
            if (el.Name.LocalName == "Harvestable")
                yield return (checked((byte)next++), el);
    }

    /// <summary>
    /// Expedition agent ids (NewExpeditionAgent key 3; SPEC stage 2 §4): the 0-based order of the &lt;expeditionagent&gt;
    /// elements of expeditionagents.xml, by uniquename. The client looks the agent up by it (cj8.af_2 → bcc.abo_1; an id
    /// outside the file gives a null agent template). TEST is 0, so T6_EXPAGENT_ROYAL_CITY is 4, the captured value.
    /// </summary>
    public static IEnumerable<(int Id, string Name)> ExpeditionAgents(IEnumerable<XElement> agents)
    {
        var next = 0;
        foreach (var el in agents)
            if (el.Name.LocalName == "expeditionagent")
                yield return (next++, (string?)el.Attribute("uniquename") ?? "");
    }

    /// <summary>
    /// Quest giver type ids (QuestGiverInfoForPlayer key 2; SPEC stage 2 §4): the 0-based order of all children of
    /// questgivertypes.xml, questgivertype and factionquestgivertype alike (26 in 345319; beq.c8 0x18064f2d0 numbers them
    /// by the list count), by uniquename: FACTIONSTORE_SWAMP 20, FACTIONSTORE_FOREST 21 (captured). The client
    /// ignores the key (coz.ajv → cmh.r 0x180b641c0), so the capture is the only check.
    /// </summary>
    public static IEnumerable<(int Id, string Name)> QuestGiverTypes(IEnumerable<XElement> types)
    {
        var next = 0;
        foreach (var el in types)
            yield return (next++, (string?)el.Attribute("uniquename") ?? "");
    }

    /// <summary>
    /// Destiny board ids (FullAchievementInfo keys 1-3): the 0-based order of the top-level &lt;achievement&gt; and
    /// &lt;templateachievement&gt; elements of achievements.xml (b74.v 0x180822ad0 numbers them while loading; 704 in
    /// 345319: 30 plain, 674 template). &lt;template&gt; elements take none.
    /// </summary>
    public static IEnumerable<(int Id, XElement Element)> Achievements(IEnumerable<XElement> achievements)
    {
        var next = 0;
        foreach (var el in achievements)
            if (el.Name.LocalName is "achievement" or "templateachievement")
                yield return (next++, el);
    }
}
