using System.Runtime.CompilerServices;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Persistence;

namespace AlbionLocal.Server.Systems;

/// <summary>
/// The Black Market's NPC buyer (stageH blackmarket SPEC §3; PVE-DESIGN.md step 12). The client's Black Market is its marketplace
/// window in Black Market mode (SPEC §2: blackmarketbuilding → b55(1), the popup hides buying and buy orders), so the buyer is a
/// set of standing buy orders there, made on every read and never stored, as the journal merchant's: one per tradable T4-T8
/// equipment item type (<see cref="Buys"/>) and quality 1-5 (PVE-DESIGN §2 L), at its item value × the quality ×
/// Bm:BuyFactor in whole silver (an item of quality q fills the orders up to q; the client's sale and quick sell take the
/// dearest). The amount an order shows is what the character may still sell today under Bm:DailyLimit (at most <see
/// cref="Stock"/>); an order the rest cannot pay is left out. Owner Guid.Empty (nobody collects the items), ids <see
/// cref="FirstId"/> + item id × 8 + quality. The Black Market's orders live at a location of their own (<see cref="Location"/>),
/// apart from the city's marketplace.
/// </summary>
public static class BlackMarket
{
    /// <summary>The first order id: the order of item id n and quality q is FirstId + n × 8 + q (below the journal merchant's and the store's).</summary>
    public const long FirstId = 800_000_000;

    /// <summary>The end of the buyer's ids (item ids below 100 000).</summary>
    private const long EndId = FirstId + 8 * 100_000;

    /// <summary>The qualities bought, normal to masterpiece.</summary>
    public const int MaxQuality = 5;

    /// <summary>The most an order shows (as the journal merchant's stock).</summary>
    public const int Stock = 999;

    /// <summary>The lowest tier bought [choice: T4+, the tiers of the Black Market's loot].</summary>
    public const int MinTier = 4;

    /// <summary>The hour (UTC) a Black Market day starts, as the other daily resets.</summary>
    public const int ResetHour = 10;

    private const string Prefix = "BM:";

    /// <summary>The market location of the Black Market building of <paramref name="cluster"/>.</summary>
    public static string Location(string cluster) => Prefix + cluster;

    /// <summary>Whether <paramref name="location"/> is a Black Market's.</summary>
    public static bool IsLocation(string location) => location.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="o"/> is one of the buyer's orders.</summary>
    public static bool Owns(MarketOrder o) => o.Owner == Guid.Empty && o.Id > FirstId && o.Id < EndId;

    /// <summary>The Black Market day of <paramref name="utc"/> (from <see cref="ResetHour"/> UTC).</summary>
    public static int Day(DateTime utc) => DateOnly.FromDateTime(utc.AddHours(-ResetHour)).DayNumber;

    /// <summary>
    /// Whether the buyer takes <paramref name="t"/>: an equipmentitem or weapon (not mounts or transformation weapons) of tier
    /// <see cref="MinTier"/>-8, tradable, shown in the marketplace, with an item value.
    /// </summary>
    public static bool Buys(World world, ItemTemplate t) =>
        t.Kind is "equipmentitem" or "weapon" && t.Tier is >= MinTier and <= 8
        && t.Attrs.GetValueOrDefault("tradable") != "false" && t.Attrs.GetValueOrDefault("showinmarketplace") != "false"
        && world.ItemValues.Of(t) > 0;

    private static readonly ConditionalWeakTable<ItemStore, List<ItemTemplate>> Bought = new();

    /// <summary>The unit price of <paramref name="t"/> at <paramref name="quality"/> (a7m raw, whole silver): its item value × the quality × Bm:BuyFactor.</summary>
    public static long UnitPrice(World world, ItemTemplate t, int quality = 1) =>
        (long)Math.Round(world.ItemValues.Of(t) * quality * world.Config.BmBuyFactor, MidpointRounding.AwayFromZero) * Pricing.A7m;

    /// <summary>What <paramref name="c"/> may still sell today (a7m raw); long.MaxValue without a limit (Bm:DailyLimit 0).</summary>
    public static long Left(World world, Character c, DateTime utc)
    {
        if (world.Config.BmDailyLimit <= 0) return long.MaxValue;
        var sold = c.BlackMarket is { } log && log.Day == Day(utc) ? log.Sold : 0;
        return Math.Max(0, world.Config.BmDailyLimit * Pricing.A7m - sold);
    }

    /// <summary>Adds a sale of <paramref name="price"/> (a7m raw, before tax) to today's of <paramref name="c"/>.</summary>
    public static void Record(Character c, long price, DateTime utc)
    {
        var day = Day(utc);
        c.BlackMarket = new BlackMarketLog(day, (c.BlackMarket is { } log && log.Day == day ? log.Sold : 0) + price);
    }

    /// <summary>The buyer's open orders of <paramref name="type"/> at <paramref name="location"/> for <paramref name="c"/>; none elsewhere, for offers or at Bm:BuyFactor 0.</summary>
    public static IEnumerable<MarketOrder> Orders(World world, Character c, string location, string type)
    {
        if (!IsLocation(location) || type != MarketOrder.Request || world.Config.BmBuyFactor <= 0) return [];
        var left = Left(world, c, DateTime.UtcNow);
        var types = Bought.GetValue(world.Items, items => [.. items.All.Where(t => Buys(world, t))]);
        return types.SelectMany(t => Enumerable.Range(1, MaxQuality).Select(q => (Type: t, Quality: q, Unit: UnitPrice(world, t, q))))
            .Where(x => x.Unit > 0 && left / x.Unit > 0)
            .Select(x => Make(x.Type, x.Quality, location, x.Unit, (int)Math.Min(Stock, left / x.Unit)));
    }

    /// <summary>The buyer's order <paramref name="id"/> at <paramref name="location"/> for <paramref name="c"/>; null = none.</summary>
    public static MarketOrder? Order(World world, Character c, long id, string location)
    {
        if (id <= FirstId || id >= EndId || (id - FirstId) % 8 is < 1 or > MaxQuality
            || world.Items[(int)((id - FirstId) / 8)] is not { } t || !Buys(world, t)) return null;
        return Orders(world, c, location, MarketOrder.Request).FirstOrDefault(o => o.Id == id);
    }

    private static MarketOrder Make(ItemTemplate t, int quality, string location, long unit, int amount)
    {
        var id = FirstId + t.Index * 8L + quality;
        // Stable within a Black Market day, so a list the client keeps shows the same order: created at the day's start
        // (ResetHour UTC), open 30 days.
        var today = DateTime.UtcNow.AddHours(-ResetHour).Date.AddHours(ResetHour);
        return new MarketOrder(id, Guid.Empty, MarketOrder.Request, location, t.Name, quality, amount, unit, today, today.AddDays(30),
            new Guid((int)id, 0x424d, 0x4b54, [0x42, 0x55, 0x59, 0x45, 0x52, 0, 0, 0]), null);
    }
}
