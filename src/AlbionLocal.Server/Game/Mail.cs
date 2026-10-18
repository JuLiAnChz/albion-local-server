namespace AlbionLocal.Server.Game;

/// <summary>bd3.a (stageJ mail SPEC §1.1): how the list row and the read view show a mail (GetMailInfos key 8).</summary>
public enum MailKind : byte
{
    /// <summary>Subject and body as free text (player mail, the server's own notices).</summary>
    Normal = 0,
    GuildApplication = 1,
    AllianceInvitation = 2,

    /// <summary>The subject is a type key (MARKETPLACE_SELLORDER_FINISHED_SUMMARY …) and the body its fields (SPEC §2).</summary>
    System = 3,
}

/// <summary>bd3.b (SPEC §1.1): the inbox filter a mail is listed under (key 9); <see cref="All"/> only in a request.</summary>
public enum MailCategory : byte { All = 0, Player = 1, Guild = 2, Market = 3, GoldMarket = 4, RealEstate = 5, System = 6 }

/// <summary>
/// A stored mail (SPEC §4.2): its id (from 1000000), the mailbox it lies in (<see cref="Owner"/> a character, or a guild when
/// <see cref="Guild"/>), the row keys of GetMailInfos (kind, category, sender, the sending character's Guid for player mail, read) and
/// the read view's body and last editor; <see cref="Received"/> and <see cref="Expires"/> UTC (null = never); <see cref="Attachments"/>
/// the item attachments still in it (only server-made mails have any: the write view has no slots).
/// </summary>
public sealed record Mail(long Id, Guid Owner, bool Guild, MailKind Kind, MailCategory Category, string Sender, Guid SenderId, bool FromGuild,
    string Subject, string Body, string? Editor, DateTime Received, DateTime? Expires, bool Read, int Attachments);

/// <summary>An item attachment of a mail (ReadMail keys 3-7): its Guid (the claim's key 2) and the stack as an inventory item.</summary>
public sealed record MailAttachment(Guid Id, long Mail, InventoryItem Item);
