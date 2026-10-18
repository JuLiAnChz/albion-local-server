using AlbionLocal.Server.Persistence;

namespace AlbionLocal.Server.Commands.Builtin;

/// <summary>The chat server and the friends lists (stageJ chat SPEC): .chat (the hub's connections and channels), .friends.</summary>
public static class ChatCommands
{
    /// <summary>.chat: the registered chat connections and the channels that have members.</summary>
    [Command("chat", Level = AccountLevel.GameMaster, Help = "the chat server's connections and the channels with members")]
    public static void Status(CommandContext ctx)
    {
        if (!ctx.World.Config.ChatEnabled) throw new CommandError("the chat server is off (Chat:Enabled)");
        ctx.Reply(ctx.World.Chat.Describe());
    }

    /// <summary>.friends [name]: the player's friends with their online state, its open requests and muted players (a GM: another character's).</summary>
    [Command("friends", Level = AccountLevel.Player, Help = "your friends (online or not), open friend requests and muted players; a GM may name a character")]
    public static void Friends(CommandContext ctx, Tail name = default)
    {
        if (name.IsEmpty)
        {
            var c = ctx.RequirePlayer().Character;
            ctx.Reply(ctx.World.Social.Describe(c.Id, c.Name));
            return;
        }
        if (ctx.Level < AccountLevel.GameMaster) throw new CommandError("only a GM may look at another character's friends");
        var other = ctx.World.Characters.Find(name.Text!.Trim()) ?? throw new CommandError($"no character '{name.Text!.Trim()}'");
        ctx.Reply(ctx.World.Social.Describe(other.Id, other.Name));
    }
}
