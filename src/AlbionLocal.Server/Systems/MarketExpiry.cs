using AlbionLocal.Photon;
using AlbionLocal.Server.Persistence;

namespace AlbionLocal.Server.Systems;

/// <summary>
/// The end of a market order that runs out (stage 6 SPEC §6d; stageJ mail SPEC §4.2): it becomes a finished entry of its owner (an offer's
/// items back, a request's escrow as silver) and is deleted, and its owner gets the EXPIRED mail (<see cref="Mailer.MarketNotice"/>). Both the
/// market's lists (as they read the orders, as before) and a sweep once a minute (<see cref="Update"/>, so the mail comes on time) end orders
/// here; the batch deletes the order, so whichever comes second no longer sees it. Under <see cref="World.Sync"/>.
/// </summary>
public static class MarketExpiry
{
    /// <summary>How often <see cref="Update"/> looks for expired orders and mails.</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    /// <summary>The orders of <paramref name="orders"/> that are still open; each expired one is ended (<see cref="End"/>).</summary>
    public static List<MarketOrder> Expire(World world, IEnumerable<MarketOrder> orders)
    {
        var now = DateTime.UtcNow;
        var open = new List<MarketOrder>();
        foreach (var o in orders)
            if (o.Expires > now) open.Add(o);
            else End(world, o);
        return open;
    }

    /// <summary>Ends the expired order <paramref name="o"/>: the finished entry, the order deleted (one batch), the owner's mail.</summary>
    private static void End(World world, MarketOrder o)
    {
        world.Market!.Batch(b =>
        {
            b.SetAmount(o.Id, 0);
            b.AddFinished(new MarketFinished(0, o.Owner, o.Id, o.Type, o.Location, o.ItemName, o.Quality, o.Type == MarketOrder.Offer ? o.Amount : 0,
                o.UnitPrice, o.Type == MarketOrder.Request ? o.UnitPrice * o.Amount : 0, o.Created, o.Expires, o.Reference, o.Item));
        });
        Mailer.MarketNotice(world, o, finished: false);
        Log.Info("market", $"market order {o.Id} ({o.Type} {o.Amount} x {o.ItemName}) expired: back to its owner's finished entries");
    }

    /// <summary>
    /// World timer (<see cref="World.Update"/>), every <see cref="SweepInterval"/> from the first tick on: the orders that have run out
    /// (<see cref="IMarketStore.ExpiredOrders"/>) end, and the mails past their end go (<see cref="IMailStore.PurgeMails"/>).
    /// </summary>
    public static void Update(World world, TimeSpan now)
    {
        if (world.MarketSweepDue is not { } due)
        {
            world.MarketSweepDue = now + SweepInterval;
            return;
        }
        if (now < due) return;
        world.MarketSweepDue = now + SweepInterval;
        var utc = DateTime.UtcNow;
        if (world.Market is { } market) Expire(world, market.ExpiredOrders(utc));
        if (Mailer.On(world) && world.Mail!.PurgeMails(utc) is > 0 and var purged) Log.Info("mail", $"{purged} mail(s) past their end deleted");
    }
}
