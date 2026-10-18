using System.Globalization;
using System.Xml.Linq;
using AlbionLocal.Photon;

namespace AlbionLocal.Server.Data;

/// <summary>
/// loot.xml (stage 5c, analysis/work/stage5/SPEC.md §5c.4): the &lt;Lootlist&gt;s by name, and the roll of a mob's
/// &lt;Loot&gt; through them, as the client's own copy of the roll does it (bd0.abo 0x180614970; attributes from b4h.tw
/// 0x1807f8c50, b4g.tw, b4j.tw; amounts b4g.ab 0x1807f84b0). Every child of a list, of an &lt;AND&gt; or of the mob's
/// &lt;Loot&gt; comes with its chance (1 when missing; above 1 an expected count: its whole part always, plus one more with
/// the rest as probability); an &lt;OR&gt; picks one child by weight (attribute weight, 1 when missing) and does not roll
/// that child's chance again; an &lt;Item type amount enchantmentlevel&gt; gives amount (a number or "min-max") of type, as
/// NAME@N for enchantment level N; a &lt;LootListReference name&gt; rolls that list. The total loot factor (the cluster's
/// moblootfactor × globalmoblootfactor) scales the chances of the mob's own &lt;Loot&gt; entries only (the client's "top
/// level factor only"; scaled further down it would double every chance-1 item now and then), and only where
/// cangetmodified is not false. A reference with evaluateChance="false" is not rolled: its chance scales the chances of
/// the list's entries below it, and the amounts of its items. An item with useblackmarket="true" drops only with
/// Loot:BlackMarketChance (the client's "drops only via blackmarket": the official server takes it from its Black Market).
/// Not modelled: scalewithgoldeconomy (the gold price), the per-listtype factors (1 in the client) and the continent filter
/// (no loot.xml entry has one). The mob's &lt;Harvestable&gt; children (silver, skinning) are not items and are left to the
/// caller. Immutable; read off the world thread (<see cref="ClusterStore.Loot"/>).
/// </summary>
public sealed class LootTables
{
    /// <summary>Recursion depth past which a reference is not followed (a list that names itself).</summary>
    private const int MaxDepth = 16;

    private readonly Dictionary<string, XElement> _lists;

    private LootTables(Dictionary<string, XElement> lists) => _lists = lists;

    public int Count => _lists.Count;

    public static LootTables Load(XElement root)
    {
        var lists = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var e in root.Elements("Lootlist"))
            if ((string?)e.Attribute("name") is { Length: > 0 } name) lists.TryAdd(name, e);
        if (lists.Count == 0) throw new InvalidDataException("loot.bin: no <Lootlist>");
        Log.Debug("data", $"loot.bin: {lists.Count} loot lists");
        return new LootTables(lists);
    }

    public bool Has(string name) => _lists.ContainsKey(name);

    /// <summary>
    /// A roll of a mob's &lt;Loot&gt; element with the total loot factor <paramref name="factor"/> and
    /// <paramref name="blackMarketChance"/> for useblackmarket items: the item uniquenames (NAME@N with an enchantment) and
    /// amounts, in roll order, the same name merged. <paramref name="unknown"/> gets each list name loot.xml does not have.
    /// </summary>
    public List<(string Item, int Amount)> RollMob(XElement loot, Random random, double factor = 1, double blackMarketChance = 1, Action<string>? unknown = null)
    {
        var roll = new Roll(this, random, blackMarketChance, unknown);
        roll.Children(loot, factor, 1, 1, 0);
        return [.. roll.Drops.GroupBy(d => d.Item, StringComparer.Ordinal).Select(g => (g.Key, g.Sum(d => d.Amount)))];
    }

    /// <summary>The roll of the list named <paramref name="name"/> (once, chance 1, every useblackmarket item dropping).</summary>
    public List<(string Item, int Amount)> RollList(string name, Random random)
    {
        var roll = new Roll(this, random, 1, null);
        if (_lists.TryGetValue(name, out var list)) roll.Children(list, 1, 1, 1, 0);
        return roll.Drops;
    }

    /// <summary>One roll: its random, the black market chance and the drops so far.</summary>
    private sealed class Roll(LootTables tables, Random random, double blackMarketChance, Action<string>? unknown)
    {
        public readonly List<(string Item, int Amount)> Drops = [];

        /// <summary>
        /// The entries of a list, an &lt;AND&gt; or the mob's &lt;Loot&gt;, each with its own chance (bd0.abo's container
        /// branch). <paramref name="factor"/> is the total loot factor of this level, <paramref name="inherited"/> the chance
        /// of an evaluateChance="false" reference above, <paramref name="amountFactor"/> the factor on item amounts.
        /// </summary>
        public void Children(XElement container, double factor, double inherited, double amountFactor, int depth)
        {
            if (depth > MaxDepth) return;
            foreach (var child in container.Elements())
            {
                var kind = child.Name.LocalName;
                if (!IsEntry(child)) continue; // <Harvestable>: the caller's
                var own = Number(child, "chance", 1);
                var chance = own;
                var amounts = amountFactor;
                var modified = XmlAttr.Bool(child, "cangetmodified", true);
                if (modified && kind == "Item")
                {
                    chance *= factor;
                    amounts *= inherited;
                }
                else if (modified) chance *= factor * inherited;
                // Below this level the factor is 1 (top level only) and nothing is inherited, but for a reference that is not rolled.
                var (childFactor, childInherited) = (1.0, 1.0);
                if (kind == "LootListReference" && !XmlAttr.Bool(child, "evaluateChance", true))
                    (chance, childFactor, childInherited) = (1, factor, modified ? inherited * own : own);
                for (; chance > 0; chance -= 1)
                    if (chance >= 1 || random.NextDouble() < chance) Node(child, childFactor, childInherited, amounts, depth + 1);
            }
        }

        /// <summary>One entry whose chance came: an item drops, a reference rolls its list, an &lt;OR&gt; picks, an &lt;AND&gt; rolls its entries.</summary>
        private void Node(XElement e, double factor, double inherited, double amountFactor, int depth)
        {
            if (depth > MaxDepth) return;
            switch (e.Name.LocalName)
            {
                case "Item":
                    Item(e, amountFactor);
                    break;
                case "LootListReference":
                    if ((string?)e.Attribute("name") is not { } name) break;
                    if (tables._lists.TryGetValue(name, out var list)) Children(list, factor, inherited, amountFactor, depth + 1);
                    else unknown?.Invoke(name);
                    break;
                case "OR":
                    // The pick's chance is not rolled; a picked reference starts without an inherited chance (bd0.abo's OR branch).
                    if (Pick([.. e.Elements().Where(IsEntry)]) is { } one)
                        Node(one, 1, one.Name.LocalName == "LootListReference" ? 1 : inherited, amountFactor, depth + 1);
                    break;
                case "AND":
                    Children(e, factor, inherited, amountFactor, depth + 1);
                    break;
            }
        }

        /// <summary>An item that came: its amount (× <paramref name="amountFactor"/>, the rest a chance of one more); a useblackmarket item only with the black market chance.</summary>
        private void Item(XElement e, double amountFactor)
        {
            if ((string?)e.Attribute("type") is not { Length: > 0 } type) return;
            var amount = Amount(e, random);
            if (amount <= 0) return;
            if (amountFactor != 1)
            {
                var scaled = amount * amountFactor;
                var whole = (int)Math.Floor(scaled);
                amount = whole + (scaled > whole && random.NextDouble() < scaled - whole ? 1 : 0);
                if (amount <= 0) return;
            }
            if (blackMarketChance < 1 && XmlAttr.Bool(e, "useblackmarket", false) && !(random.NextDouble() < blackMarketChance)) return;
            Drops.Add((XmlAttr.Int(e, "enchantmentlevel", 0) is > 0 and var level ? $"{type}@{level}" : type, amount));
        }

        /// <summary>The child an &lt;OR&gt; picks by weight (1 when missing); null when it has none or all weights are 0.</summary>
        private XElement? Pick(List<XElement> children)
        {
            var total = children.Sum(c => Math.Max(0, Number(c, "weight", 1)));
            if (total <= 0) return null;
            var roll = random.NextDouble() * total;
            foreach (var c in children)
            {
                roll -= Math.Max(0, Number(c, "weight", 1));
                if (roll < 0) return c;
            }
            return children[^1];
        }
    }

    /// <summary>Whether <paramref name="e"/> is a loot entry (not a &lt;Harvestable&gt; or anything else).</summary>
    private static bool IsEntry(XElement e) => e.Name.LocalName is "Item" or "LootListReference" or "OR" or "AND";

    /// <summary>amount: "n" or "min-max" (uniform, both included); 1 when missing.</summary>
    private static int Amount(XElement e, Random random)
    {
        var text = ((string?)e.Attribute("amount"))?.Trim();
        if (string.IsNullOrEmpty(text)) return 1;
        var dash = text.IndexOf('-', 1);
        if (dash > 0 && int.TryParse(text[..dash], NumberStyles.Integer, CultureInfo.InvariantCulture, out var min)
            && int.TryParse(text[(dash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var max))
            return max <= min ? min : random.Next(min, max + 1);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 1;
    }

    private static double Number(XElement e, string name, double fallback) =>
        (string?)e.Attribute(name) is { Length: > 0 } v && double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;
}
