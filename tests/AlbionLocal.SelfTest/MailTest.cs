using AlbionLocal.Photon.Serialization;
using AlbionLocal.Server;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Network;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Protocol;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Systems;
using static AlbionLocal.SelfTest.AlbionTests;
using static AlbionLocal.SelfTest.ChatTest;

namespace AlbionLocal.SelfTest;

/// <summary>
/// The mailbox (analysis/work/stageJ/mail/SPEC.md §6): the store, the list and read answers, the Join events, the market's notices, player
/// mail with its codes, the "select all" delete, claiming attachments and reward items that do not fit, each on <see cref="FakePeer"/>s.
/// </summary>
public static class MailTest
{
    public static IEnumerable<(string, Action)> All =>
    [
        ("mail: store pages newest first, counts, unread; a delete skips the mail with attachments", Store),
        ("mail: GetMailInfos of two mails: key 0 the character, ids, kinds 0/3, categories 6/3, UTC ticks, attachments flags", Infos),
        ("mail: ReadMail body, keys 3-7 empty, 9 true, read afterwards (201 with 0); another's mail RC 1", Read),
        ("mail: a login's Join 201 {1: unread} and 202 {0: true}; a cluster change's arrival 201 only", JoinEvents),
        ("mail: an offer sold out mails SELLORDER_FINISHED_SUMMARY amount|item|gross|unit from 0000; the sweep mails an expired buy order bought|ordered|refund|item", Market),
        ("mail: SendNewMail 7080 unknown, 7082 no postage, RC 0 to an offline player (-50 silver, category 1), 7083 at 30 a UTC day (yesterday's not counted)", SendCodes),
        ("mail: DeleteMail select all: every mail up to key 0 but key 3's and the one with attachments", DeleteKeep),
        ("mail: claim all with a full bag RC 1 and nothing moves; with room both attachments in the bag (quality kept), key 13 false", Claim),
        ("mail: an expedition reward into a full bag: pve a mail with it, official the waiting list", RewardOverflow),
    ];

    private static void Check(bool cond, string what) => AlbionTests.Check(cond, what);

    private static OperationResponse Call(WorldSession s, FakePeer peer, AlbionRequest req)
    {
        s.OnRequest(Request(req, null), encrypted: false);
        return peer.Responses[^1];
    }

    private static Guid G(object? v) => new((byte[])v!);

    private static Character Saved(SqliteCharacterRepository repo, string name)
    {
        var c = StarterKit.Default.NewCharacter(name);
        Check(repo.Create(c), $"character {name} created");
        return c;
    }

    private static void Store()
    {
        var dir = NewDir();
        using (var repo = Repository(dir))
        {
            var c = Saved(repo, "Postbox");
            Mail M(string subject) => new(0, c.Id, false, MailKind.Normal, MailCategory.System, "Albion Local", Guid.Empty, false, subject, "text", null, DateTime.UtcNow, null, false, 0);
            var a = repo.AddMail(M("a"));
            var b = repo.AddMail(M("b"), [new InventoryItem("T4_ORE", 3)]);
            var d = repo.AddMail(M("d") with { Category = MailCategory.Market });
            repo.MarkMailsRead(c.Id, false, [a], read: true);
            var deleted = repo.DeleteMails(c.Id, false, [a, b], 0, [], MailCategory.All);
            Check(a == SqliteCharacterRepository.FirstMailId && repo.MailPage(c.Id, false, MailCategory.All, 0, 50).Select(m => m.Id).SequenceEqual([d, b])
                  && repo.MailCount(c.Id, false, MailCategory.Market) == 1 && repo.UnreadMails(c.Id, false) == 2 && deleted == 1
                  && repo.Mail(b) is { Attachments: 1 } && repo.Attachments(b) is [{ Item: { Name: "T4_ORE", Count: 3 } }],
                  "ids from 1000000, newest first, Market count 1, 2 unread; deleting a and b deletes a only (b has an attachment)");
            repo.DeleteMails(c.Id, false, [d], 0, [], MailCategory.All);
            Check(repo.AddMail(M("e")) > d, "the newest mail deleted, the next one still gets a new id (never reused)");
        }
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private static void Infos()
    {
        using var town = new ChatTown();
        var a = town.Login("Anna");
        Mailer.Send(town.World, a.Id, false, MailKind.System, MailCategory.Market, "0000", Mailer.SellFinished, "1|T4_ORE|10000|10000");
        Mailer.Notice(town.World, a.Id, "Hello", "World", [new InventoryItem("T4_ORE", 2)]);
        var r = Call(a.Game, a.GamePeer, new GetMailInfosRequest());
        var now = DateTime.UtcNow.Ticks;
        Check(r is { ReturnCode: 0 } && G(r.Parameters[0]) == a.Id && r.Parameters[3] is long[] { Length: 2 } ids && ids[0] > ids[1]
              && r.Parameters[8] is byte[] and [0, 3] && r.Parameters[9] is byte[] and [6, 3] && r.Parameters[7] is string[] and ["Albion Local", "0000"]
              && r.Parameters[11] is string[] and ["Hello", Mailer.SellFinished] && r.Parameters[12] is long[] { Length: 2 } ticks && ticks.All(t => Math.Abs(now - t) < TimeSpan.TicksPerMinute)
              && r.Parameters[13] is bool[] and [true, false] && r.Parameters[10] is bool[] and [false, false],
              "two rows newest first: the notice (kind 0, System, attachment) and the market mail (kind 3, Market), unread, ticks of now");
    }

    private static void Read()
    {
        using var town = new ChatTown();
        var a = town.Login("Anna");
        var b = town.New("Boris");
        var mine = Mailer.Notice(town.World, a.Id, "Hello", "the body");
        var theirs = Mailer.Notice(town.World, b.Id, "Private", "not yours");
        var mark = a.GamePeer.Events.Count;
        var r = Call(a.Game, a.GamePeer, new ReadMailRequest { y6 = mine });
        var foreign = Call(a.Game, a.GamePeer, new ReadMailRequest { y6 = theirs });
        var read = new ReadMailResponse();
        AlbionSerializer.FillFields(read, r.Parameters);
        Check(r is { ReturnCode: 0 } && read.bhf == mine && read.bhg == "the body" && !r.Parameters.ContainsKey(2)
              && Enumerable.Range(3, 5).All(k => r.Parameters.ContainsKey((byte)k)) && read is { bhi.Length: 0, bhj.Length: 0, bhk.Length: 0, bhl.Length: 0, bhm.Length: 0, bho: true }
              && town.World.Mail!.Mail(mine) is { Read: true } && Of(a.GamePeer, EventCodes.NewUnreadMails, mark) is [var unread] && !unread.ContainsKey(1)
              && foreign.ReturnCode == WorldSession.MailRefused && town.World.Mail.Mail(theirs) is { Read: false },
              "ReadMail: keys 0, 1, 3-7 empty, 9 true, no key 2; marked read, 201 without key 1 (0 unread); Boris's mail RC 1 and still unread");
    }

    private static void JoinEvents()
    {
        _ = AlbionTests.Client.Clusters!.Get("0000");
        _ = AlbionTests.Client.Clusters!.Get("0007");
        using var town = new ChatTown(clusters: true);
        var c = town.New("Anna");
        Mailer.Notice(town.World, c.Id, "One", "1");
        Mailer.Notice(town.World, c.Id, "Two", "2");
        var a = town.Login(c);
        var unread = Of(a.GamePeer, EventCodes.NewUnreadMails, 0);
        var possible = Of(a.GamePeer, EventCodes.MailOperationPossible, 0);
        town.Tele(a, "0007");
        Check(unread is [var u] && Convert.ToInt32(u[1]) == 2 && !u.ContainsKey(0) && possible is [var p] && p[0] is true
              && Of(a.GamePeer, EventCodes.NewUnreadMails, 0).Count == 1 && Of(a.GamePeer, EventCodes.MailOperationPossible, 0).Count == 0,
              "login: 201 {1: 2} and 202 {0: true}; the arrival in 0007: 201 again, no 202");
    }

    private static void Market()
    {
        using var town = new EconomyTest.Town();
        var world = town.World;
        var (sellerSession, sellerPeer, seller) = town.Join("Seller", 1000, new InventoryItem("T4_ORE", 2));
        var (buyerSession, buyerPeer, buyer) = town.Join("Buyer", 1000);
        var market = town.Station("MARKETPLACE");
        lock (world.Sync)
        {
            Call(sellerSession, sellerPeer, new AuctionCreateOfferRequest { bqs = market.Id, bn5 = 2, bn6 = seller.Inventory.Bag[0]!.Id, bn7 = 100 * Pricing.A7m, bn8 = 24 });
            var offer = world.Market!.OrdersOf(seller.Character.Id).Single();
            Call(buyerSession, buyerPeer, new AuctionBuyOfferRequest { bqs = market.Id, bn2 = 2, bn3 = offer.Id });
            // A buy order of 3 ore at 50 that ran out an hour ago (written as the store keeps it), found by the minute's sweep.
            var past = DateTime.UtcNow.AddHours(-1);
            world.Market.Batch(b => b.AddOrder(new MarketOrder(0, buyer.Character.Id, MarketOrder.Request, "0000", "T4_ORE", 1, 3, 50 * Pricing.A7m,
                past.AddHours(-1), past, Guid.NewGuid(), null)));
        }
        town.Run(61);
        var sold = world.Mail!.MailPage(seller.Character.Id, false, MailCategory.All, 0, 50);
        var expired = world.Mail.MailPage(buyer.Character.Id, false, MailCategory.All, 0, 50);
        Check(sold is [{ Kind: MailKind.System, Category: MailCategory.Market, Sender: "0000", Subject: Mailer.SellFinished, Expires: not null } s]
              && s.Body.Split('|') is ["2", "T4_ORE", "2000000", "1000000"]
              && expired is [{ Subject: Mailer.BuyExpired } e] && e.Body.Split('|') is ["0", "3", "1500000", "T4_ORE"]
              && world.Market!.FinishedOf(buyer.Character.Id).Any(f => f.Silver == 150 * Pricing.A7m),
              $"seller: 2|T4_ORE|2000000|1000000 from 0000 (expiring); buyer after the sweep: 0|3|1500000|T4_ORE and the 150 silver to collect (got {string.Join(" / ", sold.Concat(expired).Select(m => m.Subject + " " + m.Body))})");
    }

    private static void SendCodes()
    {
        using var town = new ChatTown();
        var a = town.Login("Anna");
        var b = town.New("Boris");
        OperationResponse Mail(string to) => Call(a.Game, a.GamePeer, new SendNewMailRequest { acq = to, acs = true, act = "Hi", acu = "a letter" });
        var unknown = Mail("Nobody").ReturnCode;
        var poor = Mail("boris").ReturnCode;
        a.P.Character.Silver = 1000 * Pricing.A7m;
        var sent = Mail("boris");
        var store = town.World.Mail!;
        var now = DateTime.UtcNow;
        store.AddMailSend(a.Id, now.Date.AddHours(-1), now.Date.AddDays(-1)); // yesterday: not counted
        for (var i = 0; i < 28; i++) store.AddMailSend(a.Id, now, now.Date);
        var thirtieth = Mail("boris").ReturnCode;
        var limit = Mail("boris").ReturnCode;
        Check(unknown == WorldSession.MailPlayerNotFound && poor == WorldSession.MailNotEnoughSilver && sent is { ReturnCode: 0 } && sent.Parameters.Keys.All(k => k >= 250)
              && thirtieth == 0 && limit == WorldSession.MailLimitReached && a.P.Character.Silver == 900 * Pricing.A7m
              && store.MailPage(b.Id, false, MailCategory.Player, 0, 50) is [{ Sender: "Anna", Subject: "Hi", Body: "a letter", Kind: MailKind.Normal } m, _] && m.SenderId == a.Id,
              $"7080, 7082 with no silver, RC 0 (no keys) into offline Boris's box as category 1 from Anna, the 30th of today RC 0, the 31st 7083; 2 x 50 postage (got {unknown}/{poor}/{sent.ReturnCode}/{thirtieth}/{limit})");
    }

    private static void DeleteKeep()
    {
        using var town = new ChatTown();
        var a = town.Login("Anna");
        var w = town.World;
        var first = Mailer.Notice(w, a.Id, "1", "1");
        var kept = Mailer.Notice(w, a.Id, "2", "2");
        var gift = Mailer.Notice(w, a.Id, "3", "3", [new InventoryItem("T4_ORE")]);
        var last = Mailer.Notice(w, a.Id, "4", "4");
        var later = Mailer.Notice(w, a.Id, "5", "5"); // past key 0: the client had not listed it
        var r = Call(a.Game, a.GamePeer, new DeleteMailRequest { ji = last, jl = [kept] });
        Check(r is { ReturnCode: 0 } && w.Mail!.MailPage(a.Id, false, MailCategory.All, 0, 50).Select(m => m.Id).SequenceEqual([later, gift, kept]) && first < kept,
              "select all up to mail 4 keeping mail 2: 1 and 4 go; 2, the one with an attachment and 5 (newer than key 0) stay");
    }

    private static void Claim()
    {
        using var town = new EconomyTest.Town();
        var world = town.World;
        var (session, peer, me) = town.Join("Taker", 0);
        var id = Mailer.Notice(world, me.Character.Id, "Gift", "for you", [new InventoryItem("T4_ORE", 5), new InventoryItem("T4_MAIN_SWORD") { Quality = 3 }]);
        var bag = me.Inventory.Bag;
        var sword = AlbionTests.Client.Items.Find("T4_MAIN_SWORD")!;
        OperationResponse full, ok;
        lock (world.Sync)
        {
            var free = Enumerable.Range(0, bag.Size).Where(n => bag[n] is null).ToList();
            foreach (var n in free) bag.Put(n, new Item(world.ObjectIds.Next(), sword, 1, 1, 0, [], []));
            full = Call(session, peer, new ClaimAttachmentFromMailRequest { fj = id, fk = true, fm = -1 });
            foreach (var n in free) bag.Take(n);
            ok = Call(session, peer, new ClaimAttachmentFromMailRequest { fj = id, fk = true, fm = -1 });
        }
        var list = Call(session, peer, new GetMailInfosRequest());
        Check(full.ReturnCode == WorldSession.MailRefused && ok is { ReturnCode: 0 } && world.Mail!.Attachments(id).Count == 0
              && bag.Items.Count(i => i.Template.Name == "T4_ORE" && i.Count == 5) == 1 && bag.Items.Count(i => i.Template == sword && i.Quality == 3) == 1
              && list.Parameters[13] is bool[] and [false],
              "a full bag: RC 1, both attachments stay; with room: RC 0, 5 ore and the quality 3 sword in the bag, the row's key 13 false");
    }

    private static void RewardOverflow()
    {
        (int Mails, int Waiting) Run(ServerConfig config)
        {
            using var town = new EconomyTest.Town(config: config);
            var world = town.World;
            var (_, _, me) = town.Join("Explorer", 0);
            var data = AlbionTests.Client.Clusters!.Expeditions;
            var sword = AlbionTests.Client.Items.Find("T4_MAIN_SWORD")!;
            lock (world.Sync)
            {
                var bag = me.Inventory.Bag;
                foreach (var n in Enumerable.Range(0, bag.Size).Where(n => bag[n] is null).ToList()) bag.Put(n, new Item(world.ObjectIds.Next(), sword, 1, 1, 0, [], []));
                world.Expeditions.Reward(me, world.Expeditions.Create(data.ExpeditionAt(3)!, data.CategoryAt(3)!));
            }
            var mails = world.Mail!.MailPage(me.Character.Id, false, MailCategory.All, 0, 50);
            return (mails.Sum(m => m.Attachments), me.Character.Expeditions?.Unclaimed.Count ?? 0);
        }
        var pve = Run(ServerConfig.PveDefaults with { JoinByCharacterId = true });
        var official = Run(new ServerConfig { JoinByCharacterId = true });
        Check(pve is { Mails: > 0, Waiting: 0 } && official is { Mails: 0, Waiting: > 0 },
              $"pve: the items in a mail, nothing waiting; official: no mail, the waiting list (got pve {pve}, official {official})");
    }
}
