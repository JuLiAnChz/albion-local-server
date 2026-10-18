using System.Globalization;
using System.Text.Json;
using AlbionLocal.Photon.Serialization;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Data;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Systems;
using static AlbionLocal.SelfTest.AlbionTests;
using static AlbionLocal.SelfTest.CombatTest;

namespace AlbionLocal.SelfTest;

/// <summary>
/// Labourer journals (analysis/work/stageH/journals/SPEC.md §A and its self-test list §F 1-3; PVE-DESIGN.md step 4): the
/// _EMPTY/_FULL ids against journal-ids.tsv, filling from fame, the full journal, the fill error, saving, and the journal
/// merchant's market orders.
/// </summary>
public static class JournalTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("journals: _EMPTY 11972 + 2k and _FULL 11973 + 2k after the 11971 items, all 133 as journal-ids.tsv (maxfame, mission, mintier, value, validitems)", Ids),
        ("journals: T4 wood fills 22.5 per 15 harvest fame (key 8), 80 harvests fill it (JournalGotFull, the _FULL item in its slot), a journal at 1790 fills with the rest lost onto the _FULL stack; kill/craft/fishing missions; saved", Fill),
        ("journals: a stack of 3 empty ones with no free slot sends JournalFillError and changes nothing; with one free, one is split off", FillError),
        ("journals: a full bag with a stack of 3 empty ones and a single one fills the single one; a partly filled one bought on the market needs a free slot (the fetch is refused, the entry kept)", Crowded),
        ("journals: the merchant's market orders in Thetford: _EMPTY at the labourer price, bought and collected; _FULL bought back at 1.5 x less the tax", Merchant),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static ItemStore Items => Client.Items;

    private static void Ids()
    {
        if (AlbionTests.Golden("journal-ids.tsv") is not { } tsv) return;
        var rows = File.ReadLines(tsv).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('\t')).ToList();
        var bad = new List<string>();
        foreach (var r in rows)
        {
            int I(int k) => r[k].Length == 0 ? 0 : int.Parse(r[k], CultureInfo.InvariantCulture); // fameearned has no mintier
            var (base_, empty, full) = (Items[I(0)], Items[I(1)], Items[I(2)]);
            if (base_ is not { Name: var n, Kind: "journalitem", Journal: { State: JournalState.Base } j, CanCreate: false } || n != r[3]
                || empty is not { IsJournal: true, CanCreate: true, Journal.State: JournalState.Empty } || empty.Name != r[3] + "_EMPTY" || Items.Find(empty.Name) != empty
                || full is not { IsJournal: true, Journal.State: JournalState.Full } || full.Name != r[3] + "_FULL" || Items.Find(full.Name) != full
                || j.Empty != empty.Index || j.Full != full.Index || empty.Tier != I(4) || j.MaxFame != I(5) * 10000L || j.Mission != r[6] || j.MinTier != I(7)
                || j.Value != double.Parse(r[8], CultureInfo.InvariantCulture) || j.ValidItems.Count != I(9))
                bad.Add(r[3]);
        }
        Check(rows.Count == 133 && bad.Count == 0, $"133 journals as journal-ids.tsv ({rows.Count} rows, {bad.Count} differ, first {bad.FirstOrDefault()})");
        Check(Items.Find("T2_JOURNAL_WOOD_EMPTY")?.Index == 11972 && Items.Find("T2_JOURNAL_WOOD_FULL")?.Index == 11973 && Items.Find("T4_JOURNAL_WOOD_EMPTY")?.Index == 11976
              && Items.Find("T8_JOURNAL_FISHING_FULL")?.Index == 12237 && Items[11971]?.Journal is null && Items[12238] is null,
              "SPEC §F.1/2b: 11972, 11973, 11976, 12237; nothing between 11971 and 11972, nothing after");
        var wood = Items.Find("T4_JOURNAL_WOOD_EMPTY")!.Journal!;
        Check(wood.Fill(15 * 10000) == 225_000 && wood.Price == 2000 * 10000L && Items.Find("T4_JOURNAL_MERCENARY_EMPTY")!.Journal!.Fill(123_456) == 123_456,
              "15 gathering fame = 22.5 journal fame (1800 / 1200); mercenary 1:1; the T4 price 2000");
    }

    private static void Fill()
    {
        using var town = new EconomyTest.Town();
        var world = town.World;
        var (_, peer, me) = town.Join("Journaler", 0, new InventoryItem("T4_JOURNAL_WOOD_EMPTY"), new InventoryItem("T4_JOURNAL_WOOD_EMPTY") { Fame = 17_900_000 },
            new InventoryItem("T4_JOURNAL_MERCENARY_EMPTY"), new InventoryItem("T4_JOURNAL_WARRIOR_EMPTY"), new InventoryItem("T4_JOURNAL_FISHING_EMPTY"));
        var bag = me.Inventory.Bag;
        var wood = Items.Find("T4_WOOD")!;
        void Harvest(int times)
        {
            lock (world.Sync)
                for (var i = 0; i < times; i++) Journals.OnFame(me, 15 * 10000, Destiny.Harvest(wood), wood, pvp: false);
        }
        var mark = peer.Events.Count;
        Check(bag[1]!.JournalFame == 17_900_000 && peer.Events.Take(mark).Select(e => Wire(e)).Any(p => p.GetValueOrDefault((byte)1) is short t && t == 11976 && p.GetValueOrDefault((byte)8) is long f && f == 17_900_000),
              "the saved fame of a partly filled journal: Join sends it as NewJournalItem key 8");
        Harvest(1);
        var burst = Since(peer, mark);
        // The partly filled journal (slot 1) takes the gain first: 1790 + 22.5 ≥ 1800, full, the rest lost.
        Check(bag[1] is { Template.Name: "T4_JOURNAL_WOOD_FULL", JournalFame: 18_000_000, Count: 1 } && bag[0]!.JournalFame == 0
              && burst is [{ Code: EventCodes.InventoryDeleteItem }, { Code: EventCodes.NewJournalItem } nj, { Code: EventCodes.InventoryPutItem } put, { Code: EventCodes.JournalGotFull } got]
              && L(nj.P, 1) == 11977 && L(nj.P, 8) == 18_000_000 && L(put.P, 1) == 1 && L(got.P, 0) == me.Id && L(got.P, 1) == 11977 && !got.P.ContainsKey(2),
              $"1790 + 22.5: full, the _FULL item in slot 1 (InventoryDeleteItem, NewJournalItem key 8 = 1800, InventoryPutItem, JournalGotFull {{0 me, 1 11977}}) (got {Show(burst)})");
        mark = peer.Events.Count;
        Harvest(1);
        Check(bag[0]!.JournalFame == 225_000 && Since(peer, mark) is [{ Code: EventCodes.NewJournalItem } one] && L(one.P, 0) == bag[0]!.Id && L(one.P, 8) == 225_000,
              "the empty one: one harvest of 15 fame = 22.5 (NewJournalItem key 8 = 225000)");
        Harvest(79);
        Check(bag[0] is null && bag[1] is { Template.Name: "T4_JOURNAL_WOOD_FULL", Count: 2 } && bag.Items.Count(i => i.Template.Name.StartsWith("T4_JOURNAL_WOOD")) == 1,
              "80 harvests fill it exactly: onto the _FULL stack (2)");
        Check(me.Inventory.Bag[2]!.JournalFame == 0 && me.Inventory.Bag[3]!.JournalFame == 0 && me.Inventory.Bag[4]!.JournalFame == 0,
              "a harvest fills no mercenary, warrior or fishing journal");

        var (mercenary, warrior, fishing) = (bag[2]!, bag[3]!, bag[4]!);
        lock (world.Sync)
        {
            Destiny.Award(me, 100 * 10000, Destiny.Kill(me, 3));                  // a T3 mob: below the mercenary's mintier 4
            Destiny.Award(me, 100 * 10000, Destiny.Kill(me, 4), pvp: true);       // a player kill: no killmobfame
            Destiny.Award(me, 100 * 10000, Destiny.Kill(me, 4));                  // a T4 mob
            var shield = Items.Find("T4_OFF_SHIELD")!;
            Destiny.Award(me, 30 * 10000, Destiny.Craft(shield), boardGain: 33 * 10000, source: shield);
            var fish = Items.Find("T4_FISH_FRESHWATER_ALL_COMMON")!;
            Destiny.Award(me, 40 * 10000, Destiny.Fishing(fish), source: fish);
        }
        Check(mercenary.JournalFame == 100 * 10000 && warrior.JournalFame == 33 * 10000 * 3 && fishing.JournalFame == 40 * 10000,
              $"a T4 PvE kill fills the mercenary 1:1 (not T3, not PvP); the craft's board share x 3 (3600 / 1200) the warrior's; a catch the fishing one (got {mercenary.JournalFame}, {warrior.JournalFame}, {fishing.JournalFame})");

        lock (world.Sync) peer.Disconnect();
        world.Saves.Flush();
        var (_, _, again) = town.Rejoin(me.Character.Id);
        Check(again.Inventory.Bag[1] is { Template.Name: "T4_JOURNAL_WOOD_FULL", Count: 2, JournalFame: 18_000_000 } && again.Inventory.Bag[2]!.JournalFame == 100 * 10000
              && again.Inventory.Bag[3]!.JournalFame == 990_000, "after a relog: the full stack and the fame of the partly filled ones");
    }

    private static void FillError()
    {
        using var town = new EconomyTest.Town();
        var world = town.World;
        var inventory = new List<InventoryItem?> { new("T4_JOURNAL_MERCENARY_EMPTY", 3) };
        for (var i = 1; i < 48; i++) inventory.Add(new InventoryItem("T4_ORE"));
        var (_, peer, me) = town.Join("Crowded", 0, [.. inventory]);
        var mark = peer.Events.Count;
        lock (world.Sync) Journals.OnFame(me, 50 * 10000, Destiny.Kill(me, 5), null, pvp: false);
        Check(Since(peer, mark) is [{ Code: EventCodes.JournalFillError }] && me.Inventory.Bag[0] is { Count: 3, JournalFame: 0 },
              "no free slot to split one off: JournalFillError, the stack unchanged");
        lock (world.Sync)
        {
            me.Inventory.Bag.Take(47);
            mark = peer.Events.Count;
            Journals.OnFame(me, 50 * 10000, Destiny.Kill(me, 5), null, pvp: false);
        }
        var burst = Since(peer, mark);
        Check(me.Inventory.Bag[0] is { Count: 2, JournalFame: 0 } && me.Inventory.Bag[47] is { Count: 1, JournalFame: 500_000 }
              && burst is [{ Code: EventCodes.NewJournalItem } stack, { Code: EventCodes.NewJournalItem }, { Code: EventCodes.InventoryPutItem }, { Code: EventCodes.NewJournalItem } filled]
              && L(stack.P, 2) == 2 && L(filled.P, 8) == 500_000,
              $"a free slot: one split off into it and filled, the stack 2 (got {Show(burst)})");
    }

    private static void Crowded()
    {
        using var town = new EconomyTest.Town();
        var world = town.World;
        var inventory = new List<InventoryItem?> { new("T4_JOURNAL_WOOD_EMPTY", 3), new("T4_JOURNAL_WOOD_EMPTY") };
        for (var i = 2; i < 48; i++) inventory.Add(new InventoryItem("T4_ORE"));
        var (session, peer, me) = town.Join("Crowded", 0, [.. inventory]);
        var (bag, empty) = (me.Inventory.Bag, Items.Find("T4_JOURNAL_WOOD_EMPTY")!);
        lock (world.Sync) Journals.OnFame(me, 15 * 10000, Destiny.Harvest(Items.Find("T4_WOOD")!), Items.Find("T4_WOOD"), pvp: false);
        Check(bag[0] is { Count: 3, JournalFame: 0 } && bag[1] is { Count: 1, JournalFame: 225_000 }, "no split needed: the single empty one in slot 1 fills, the stack stays");

        // A partly filled one (900 fame) bought on the market cannot go onto the stack of empty ones: no free slot, not collected.
        world.Market!.Batch(b => b.AddFinished(new MarketFinished(0, me.Character.Id, 1, MarketOrder.Offer, "0000", empty.Name, 1, 1, 100, 0, DateTime.UtcNow,
            DateTime.UtcNow.AddDays(1), Guid.Empty, new InventoryItem(empty.Name) { Fame = 9_000_000 })));
        var entry = world.Market.FinishedOf(me.Character.Id).Single();
        var before = peer.Responses.Count;
        lock (world.Sync) session.OnRequest(Request(new AuctionFetchAuctionRequest { bqs = town.Station("MARKETPLACE").Id, boe = entry.Id }, 3), encrypted: false);
        Check(peer.Responses.Skip(before).Last().ReturnCode != 0 && world.Market.FinishedOf(me.Character.Id) is [{ Amount: 1 }] && bag[0] is { Count: 3 },
              "the fetch is refused and the entry kept");
    }

    private static void Merchant()
    {
        var shipped = JournalSettings.Parse(File.ReadAllText(Path.Combine(DataDir(), "..", "content", "journals.json")), "journals.json");
        Check(shipped == JournalSettings.Default with { MerchantMarkets = shipped.MerchantMarkets } && shipped.MerchantMarkets.SetEquals(["0000"]),
              "content/journals.json holds the built-in values");
        using var town = new EconomyTest.Town(JournalSettings.Default);
        var world = town.World;
        var (session, peer, me) = town.Join("Buyer", 10_000, new InventoryItem("T4_JOURNAL_MERCENARY_FULL", 2));
        var market = town.Station("MARKETPLACE");
        OperationResponse Call(AlbionRequest req)
        {
            var before = peer.Responses.Count;
            lock (world.Sync) session.OnRequest(Request(req, 3), encrypted: false);
            return peer.Responses.Skip(before).Last();
        }
        static JsonElement Dto(string json) => JsonDocument.Parse(json).RootElement;
        var empty = Items.Find("T4_JOURNAL_WOOD_EMPTY")!;
        var offers = ((string[])Call(new AuctionGetOffersRequest { bqs = market.Id, bpl = [empty.Index] }).Parameters[0]!).Select(Dto).ToList();
        Check(offers is [var offer] && offer.GetProperty("ItemTypeId").GetString() == "T4_JOURNAL_WOOD_EMPTY" && offer.GetProperty("UnitPriceSilver").GetInt64() == 2000 * 10000L
              && offer.GetProperty("Amount").GetInt32() == 999 && offer.GetProperty("Id").GetInt64() == JournalMerchant.FirstId + empty.Index,
              "the merchant's sell order of T4 wood journals: 2000 silver, 999 shown");
        Check(((string[])Call(new AuctionGetOffersRequest { bqs = market.Id, bpe = "other", bpf = "labourers" }).Parameters[0]!).Length == 50
              && ((string[])Call(new AuctionGetRequestsRequest { bqs = market.Id }).Parameters[0]!).Select(Dto).All(o => o.GetProperty("ItemTypeId").GetString()!.EndsWith("_FULL")),
              "the labourer category lists journals (a page of 50); the buy orders are all _FULL journals");
        Check(Call(new AuctionBuyOfferRequest { bqs = market.Id, bn2 = 2, bn3 = JournalMerchant.FirstId + empty.Index }) is { ReturnCode: 0 } && me.Character.Silver == 6000 * 10000L
              && world.Market!.FinishedOf(me.Character.Id) is [{ ItemName: "T4_JOURNAL_WOOD_EMPTY", Amount: 2 } entry]
              && Call(new AuctionFetchAuctionRequest { bqs = market.Id, boe = entry.Id }) is { ReturnCode: 0 }
              && me.Inventory.Bag.Items.SingleOrDefault(i => i.Template == empty) is { Count: 2, JournalFame: 0 } && world.Market.Orders("0000", MarketOrder.Offer).Count == 0,
              "buying 2 costs 4000; collected, 2 empty journals in the inventory; nothing stored for the merchant");
        var full = me.Inventory.Bag.Items.Single(i => i.Template.Name == "T4_JOURNAL_MERCENARY_FULL");
        var request = JournalMerchant.FirstId + full.Template.Index;
        Check(Call(new QuickSellAuctionQueryActionRequest { bqs = market.Id, bqt = [full.Id] }).Parameters[1] is string[] and [var quick] && Dto(quick).GetProperty("Id").GetInt64() == request
              && Call(new AuctionSellSpecificItemRequestRequest { bqs = market.Id, bqe = request, bqf = full.Template.Index, bqg = me.Inventory.Bag.IndexOf(full), bqh = 2 }) is { ReturnCode: 0 }
              && me.Character.Silver == (6000 + 2 * 3000 - 480) * 10000L && me.Inventory.Bag.Items.All(i => i.Template != full.Template)
              && world.Market!.FinishedOf(Guid.Empty).Count == 0,
              "2 full mercenary journals sold into the merchant's buy order (quick sell finds it): 2 x 3000 less 8 % tax, nobody gets them");
    }
}
