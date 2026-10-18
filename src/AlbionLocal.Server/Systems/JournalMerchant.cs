using AlbionLocal.Server.Data;
using AlbionLocal.Server.Persistence;

namespace AlbionLocal.Server.Systems;

/// <summary>
/// The journal merchant (stageH journals SPEC §A.4, §E.3; PVE-DESIGN.md step 4; content/journals.json): standing NPC orders on the
/// marketplace of the cities in MerchantMarkets, made on every read and never stored: a sell order per _EMPTY journal of the
/// merchant's tiers at SellFactor × its labourer price, a buy order per _FULL journal at BuyBackFactor × that price (whole silver;
/// none at 0). They never run out (the amount shown is MerchantStock), never expire, have no owner (Guid.Empty: no character gets
/// paid or collects anything) and take the ids <see cref="FirstId"/> + the item id, below the store's (from 1000000000). Buying
/// one leaves the journals as a finished entry to collect, as any market purchase; selling into one pays at once less the
/// sales tax the client shows. Pure functions of the settings and the item table.
/// Stage I (invasions SPEC §3.4): it also sells the invasion tickets of content/events.json Merchant.Prices at the Merchant.Markets
/// cities (same id range: a ticket is no journal), Merchant.Stock shown.
/// </summary>
public static class JournalMerchant
{
    /// <summary>The first order id of the merchant: its order of item id n is FirstId + n.</summary>
    public const long FirstId = 900_000_000;

    /// <summary>Whether <paramref name="o"/> is one of the merchant's orders.</summary>
    public static bool Owns(MarketOrder o) => o.Owner == Guid.Empty && o.Id > FirstId && o.Id < FirstId + 100_000;

    /// <summary>The merchant's open orders of <paramref name="type"/> at the market of <paramref name="location"/>; none outside its cities.</summary>
    public static IEnumerable<MarketOrder> Orders(World world, string location, string type) => Journals(world, location, type).Concat(Tickets(world, location, type));

    private static IEnumerable<MarketOrder> Journals(World world, string location, string type)
    {
        var s = world.Journals;
        if (!s.MerchantMarkets.Contains(location) || type == MarketOrder.Request && s.BuyBackFactor <= 0) return [];
        var state = type == MarketOrder.Offer ? JournalState.Empty : JournalState.Full;
        return world.Items.All.Where(t => t.Journal is { } j && j.State == state && t.Tier >= s.MinTier && t.Tier <= s.MaxTier && j.Price > 0)
            .Select(t => Make(world, t, type, location, UnitPrice(world, t), s.MerchantStock));
    }

    /// <summary>Stage I: the sell orders of the invasion tickets at a Merchant.Markets city (Merchant.Prices, whole silver).</summary>
    private static IEnumerable<MarketOrder> Tickets(World world, string location, string type)
    {
        var m = world.EventSettings.Merchant;
        if (type != MarketOrder.Offer || !m.Markets.Contains(location)) return [];
        return m.Prices.Select(e => (Item: world.Items.Find(e.Key), Price: e.Value)).Where(e => e.Item is { Journal: null })
            .Select(e => Make(world, e.Item!, type, location, e.Price * 10000, m.Stock));
    }

    /// <summary>The merchant's order <paramref name="id"/> at the market of <paramref name="location"/>; null = none (another id, another city, not traded).</summary>
    public static MarketOrder? Order(World world, long id, string location)
    {
        if (id <= FirstId || id >= FirstId + 100_000 || world.Items[(int)(id - FirstId)] is not { } t) return null;
        var type = t.Journal is not { } j ? MarketOrder.Offer // stage I: a ticket
            : j.State == JournalState.Empty ? MarketOrder.Offer : j.State == JournalState.Full ? MarketOrder.Request : null;
        return type is null ? null : Orders(world, location, type).FirstOrDefault(o => o.Id == id);
    }

    /// <summary>The unit price of a merchant order of <paramref name="journal"/> (a7m raw, whole silver): the labourer price × the factor of its side.</summary>
    public static long UnitPrice(World world, ItemTemplate journal)
    {
        var j = journal.Journal!;
        var factor = j.State == JournalState.Full ? world.Journals.BuyBackFactor : world.Journals.SellFactor;
        return (long)Math.Round(j.Price / 10000.0 * factor, MidpointRounding.AwayFromZero) * 10000;
    }

    private static MarketOrder Make(World world, ItemTemplate t, string type, string location, long price, int stock)
    {
        var id = FirstId + t.Index;
        // Stable within a day, so a list the client keeps shows the same order: created at midnight UTC, open 30 days.
        var today = DateTime.UtcNow.Date;
        return new MarketOrder(id, Guid.Empty, type, location, t.Name, 1, stock, price, today, today.AddDays(30),
            new Guid((int)id, 0x4a4d, 0x4552, [0x43, 0x48, 0x41, 0x4e, 0x54, 0, 0, 0]), null);
    }
}
