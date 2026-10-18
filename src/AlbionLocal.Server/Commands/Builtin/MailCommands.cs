using AlbionLocal.Server.Data;
using AlbionLocal.Server.Game;
using AlbionLocal.Server.Persistence;
using AlbionLocal.Server.Systems;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>The mailbox (stageJ mail SPEC §5 phase 1): .mail (a plain-text notice to a character), .mail item (one with an item attached).</summary>
public static class MailCommands
{
    /// <summary>The character called <paramref name="name"/> (any case, also offline) when mail is on.</summary>
    private static (Guid Id, string Name) Recipient(CommandContext ctx, string name)
    {
        if (!Mailer.On(ctx.World)) throw new CommandError("mail is off (Mail:Enabled) or there is no mail store");
        return ctx.World.Mail!.CharacterNamed(name) ?? throw new CommandError($"no character '{name}'");
    }

    /// <summary>.mail &lt;name&gt; &lt;subject&gt; | &lt;text&gt;: a notice of the server (kind 0, category System) into the character's mailbox.</summary>
    [Command("mail", Level = AccountLevel.GameMaster, Help = "sends a server notice: .mail <name> <subject> | <text> (offline characters too)")]
    public static void Send(CommandContext ctx, string name, Tail text)
    {
        var to = Recipient(ctx, name);
        var parts = (text.Text ?? "").Split('|', 2);
        var (subject, body) = (parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "");
        if (subject.Length == 0 || body.Length == 0) throw new CommandError("give a subject and a text: .mail <name> <subject> | <text>");
        var id = Mailer.Notice(ctx.World, to.Id, subject, body);
        ctx.Reply($"mail {id} to '{to.Name}'");
    }

    /// <summary>.mail item &lt;name&gt; &lt;item&gt; [count] [quality]: a notice with the item attached (to try the attachments; the GM's item, no reward).</summary>
    [Command("mail item", Level = AccountLevel.GameMaster, Help = "sends a server notice with an item attached: .mail item <name> <item> [count=1] [quality=1]")]
    public static void Item(CommandContext ctx, string name, ItemTemplate item, int count = 1, int quality = 1)
    {
        var to = Recipient(ctx, name);
        if (!item.CanCreate) throw new CommandError($"{item.Name} is a {item.Kind}: only equipment, simple items and journals can be created yet");
        if (count is < 1 or > ItemCommands.MaxStack) throw new CommandError($"count: {count} is not 1..{ItemCommands.MaxStack}");
        if (quality is < 1 or > 5 || item.IsEquipment && quality > item.MaxQuality) throw new CommandError($"quality: {quality} is not 1..{(item.IsEquipment ? item.MaxQuality : 1)}");
        // Equipment one attachment per piece, as it lies in the inventory.
        var items = item.IsEquipment
            ? Enumerable.Range(0, count).Select(_ => new InventoryItem(item.Name) { Quality = quality }).ToList()
            : [new InventoryItem(item.Name, count)];
        var id = Mailer.Notice(ctx.World, to.Id, "A gift", $"{count} x {item.Name} from the game master.", items);
        ctx.Reply($"mail {id} with {count} x {item.Name} to '{to.Name}'");
    }
}
