using System.Globalization;
using AlbionLocal.Photon;
using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Systems;

/// <summary>
/// The one way the server makes mail (analysis/work/stageJ/mail/SPEC.md §3, §4.2): every flow (the market's notices, reward items that
/// do not fit, the guild's notices, player mail, the GM's .mail) stores through <see cref="Send"/>, which tells the mailbox's owner in game
/// (NewUnreadMails 201). Mails live in <see cref="World.Mail"/> (the character database), nothing in memory. Mail:Enabled false or no
/// store: no mail is made (<see cref="On"/>). Under <see cref="World.Sync"/>.
/// </summary>
public static class Mailer
{
    /// <summary>Mails per GetMailInfos page (bd3.a = 50: the client asks GetMailCount for a full page).</summary>
    public const int PageSize = 50;

    /// <summary>The market mails' type keys (SPEC §2: only the SUMMARY bodies have a localized text).</summary>
    public const string SellFinished = "MARKETPLACE_SELLORDER_FINISHED_SUMMARY", BuyFinished = "MARKETPLACE_BUYORDER_FINISHED_SUMMARY",
        SellExpired = "MARKETPLACE_SELLORDER_EXPIRED_SUMMARY", BuyExpired = "MARKETPLACE_BUYORDER_EXPIRED_SUMMARY";

    /// <summary>Whether mail is on (Mail:Enabled) and there is a store to keep it.</summary>
    public static bool On(World world) => world.Config.MailEnabled && world.Mail is not null;

    /// <summary>
    /// Stores a mail for the mailbox of <paramref name="to"/> (a guild's when <paramref name="guild"/>) with <paramref name="items"/> attached:
    /// its end from Mail:MarketExpiryDays (a market mail) or Mail:KeepDays, the mailbox trimmed to Mail:MaxPerBox, then NewUnreadMails to the
    /// owner in game (a guild's: the members who may read its mail). Returns the mail's id; 0 = none made (mail off).
    /// </summary>
    public static long Send(World world, Guid to, bool guild, MailKind kind, MailCategory category, string sender, string subject, string body,
        IReadOnlyList<InventoryItem>? items = null, Guid senderId = default, bool fromGuild = false)
    {
        if (!On(world)) return 0;
        var store = world.Mail!;
        var now = DateTime.UtcNow;
        var days = category == MailCategory.Market ? world.Config.MailMarketExpiryDays : world.Config.MailKeepDays;
        var id = store.AddMail(new Mail(0, to, guild, kind, category, sender, senderId, fromGuild, subject, body, null, now,
            days > 0 ? now.AddDays(days) : null, false, 0), items);
        store.TrimMailbox(to, guild, world.Config.MailMaxPerBox);
        Notify(world, to, guild);
        Log.Info("mail", $"mail {id} '{subject}' from '{sender}' to {(guild ? "guild" : "character")} {to}" + (items is { Count: > 0 } ? $" with {items.Count} attachment(s)" : ""));
        return id;
    }

    /// <summary>A plain-text notice of the server (kind 0, category System: SPEC §2, the subject and body shown as they are).</summary>
    public static long Notice(World world, Guid to, string subject, string body, IReadOnlyList<InventoryItem>? items = null) =>
        Send(world, to, guild: false, MailKind.Normal, MailCategory.System, "Albion Local", subject, body, items);

    /// <summary>
    /// NewUnreadMails (201, avd: 0 guild, 1 the unread count, 2 the notification kind 0 = none) with the mailbox's count now: to its owner when it
    /// plays, for a guild's to each member in game who may read it (<see cref="CanReadGuildMail"/>).
    /// </summary>
    public static void Notify(World world, Guid owner, bool guild)
    {
        if (!On(world)) return;
        if (!guild)
        {
            world.Parties.Online(owner)?.Session.Send(Unread(world, owner, guild: false));
            return;
        }
        if (world.Guilds.ById(owner) is not { } g) return;
        var e = Unread(world, owner, guild: true);
        foreach (var m in g.Members)
            if (CanReadGuildMail(m.Role) && world.Parties.Online(m.Id) is { } p) p.Session.Send(e);
    }

    /// <summary>NewUnreadMails of a mailbox (key 1 left out when 0, as the official Join's {2: 0}).</summary>
    public static NewUnreadMailsEvent Unread(World world, Guid owner, bool guild) =>
        new() { dcy = guild, dcz = world.Mail?.UnreadMails(owner, guild) ?? 0, dc0 = 0 };

    /// <summary>Whether a member of <paramref name="role"/> may read and write the guild's mail (GuildPermission ACCESS_GUILD_MAIL, SPEC §1.5).</summary>
    public static bool CanReadGuildMail(GuildRole role) => (GuildRoles.Permissions(role) & GuildRoles.AccessGuildMail) != 0;

    /// <summary>
    /// After the JoinFinished of <paramref name="p"/> (SPEC §1.3): NewUnreadMails {1: unread} of its own mailbox at every Join, the guild's to a
    /// member who may read it, and MailOperationPossible {0: true} after a login (<paramref name="login"/>; the official one came once, at the
    /// first Join).
    /// </summary>
    public static void Joined(Player p, bool login)
    {
        var world = p.World;
        if (!On(world)) return;
        var id = p.Character.Id;
        p.Session.Send(Unread(world, id, guild: false));
        if (world.Guilds.Of(id) is { } g && g.Find(id) is { } m && CanReadGuildMail(m.Role)) p.Session.Send(Unread(world, g.Id, guild: true));
        if (login) p.Session.Send(new MailOperationPossibleEvent { cof = true });
    }

    // ---- the market (SPEC §2, §3) ----

    /// <summary>
    /// The notice of a market order that is done (Mail:MarketNotices; a player's order only, SPEC §3): filled (<paramref name="finished"/>: the
    /// FINISHED type with all it was placed with), else expired or cancelled (the EXPIRED type with how much of it was filled). The body's fields
    /// are '|'-separated in the client's order, prices a7m raw (SPEC §2): the client works the average, the tax and a refund out itself. The
    /// sender is the order's cluster (the client shows "Marketplace Notification" and the city in the subject). An order placed before schema
    /// v22 (<see cref="MarketOrder.Initial"/> 0: the amount placed is not known) sends none.
    /// </summary>
    public static void MarketNotice(World world, MarketOrder o, bool finished)
    {
        if (!world.Config.MailMarketNotices || o.Owner == Guid.Empty) return;
        if (o.Initial == 0)
        {
            // Placed before schema v22 (initial_amount NULL): how much it was placed with is not known, so a SUMMARY's totals would be made up.
            Log.Info("mail", $"market order {o.Id} of {o.Owner} predates v22 (amount placed not known): no notice");
            return;
        }
        static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        var (type, body) = (o.Type, finished) switch
        {
            (MarketOrder.Offer, true) => (SellFinished, F($"{o.Placed}|{o.ItemName}|{o.UnitPrice * o.Placed}|{o.UnitPrice}")),
            (MarketOrder.Request, true) => (BuyFinished, F($"{o.Placed}|{o.ItemName}|{o.UnitPrice * o.Placed}|{o.UnitPrice}")),
            (MarketOrder.Offer, false) => (SellExpired, F($"{o.Filled}|{o.Placed}|{o.UnitPrice * o.Filled}|{o.ItemName}")),
            _ => (BuyExpired, F($"{o.Filled}|{o.Placed}|{o.UnitPrice * o.Amount}|{o.ItemName}")),
        };
        Send(world, o.Owner, guild: false, MailKind.System, MailCategory.Market, o.Location, type, body);
    }

    // ---- reward items that do not fit (SPEC §3) ----

    /// <summary>
    /// Reward items of <paramref name="c"/> that did not fit its inventory, as one mail with them attached (Mail:RewardOverflow): true = mailed
    /// (the caller keeps no waiting list), false = off, nothing to mail or no mail store (the caller's own waiting list as before). The mail's
    /// text says what they are from (<paramref name="reason"/>, "the expedition T3_EXP_STD_HERETIC_MINE"); the character is saved at the next
    /// tick (<see cref="Persistence.SaveScheduler.MarkUrgent"/>).
    /// </summary>
    public static bool Overflow(World world, Character c, IReadOnlyList<InventoryItem> items, string reason)
    {
        if (!world.Config.MailRewardOverflow || items.Count == 0 || !On(world)) return false;
        var list = string.Join(", ", items.Select(i => i.Count == 1 ? i.Name : $"{i.Name} x{i.Count}"));
        Notice(world, c.Id, "Reward items", $"These rewards of {reason} did not fit your inventory: {list}. Take them out of this mail.", items);
        // The mail is committed now; the character's record of the reward as given (BountiesDone, a claimed season reward, the chest's
        // decrement) goes at the next tick instead of ChangeDelay later, so a crash can hardly keep both the mail and the reward to earn again.
        world.Saves.MarkUrgent(c);
        Log.Info("mail", $"'{c.Name}': {list} of {reason} did not fit the inventory: mailed");
        return true;
    }

    // ---- the guild's mailbox (SPEC §3, phase 4) ----

    /// <summary>A notice of <paramref name="g"/> into its own mailbox (kind 0, category Guild, the guild as sender): a member joined, left, a payout.</summary>
    public static void GuildNotice(World world, Guild g, string subject, string body) =>
        Send(world, g.Id, guild: true, MailKind.Normal, MailCategory.Guild, g.Name, subject, body);
}
