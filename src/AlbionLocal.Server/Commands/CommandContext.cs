using AlbionLocal.Server.Entities;
using AlbionLocal.Server.Persistence;

namespace AlbionLocal.Server.Commands;

/// <summary>
/// Where a command runs: the world, the player it acts on, the caller's level and where the replies go (a
/// SystemMessage to the player for chat, the log for the console). Commands run under <see cref="World.Sync"/>
/// (in the chat handler, or posted by the console), so they may touch world state directly.
/// </summary>
public sealed class CommandContext(World world, Player? player, AccountLevel level, Action<string, bool> reply)
{
    public World World { get; } = world;

    /// <summary>
    /// The player the command acts on: in chat the one who typed it; on the console the only joined player, or the one
    /// whose name starts the line. Null when there is none (<see cref="RequirePlayer"/>).
    /// </summary>
    public Player? Player { get; } = player;

    /// <summary>The caller's account level (chat: the speaker's account, stored per account; console: Admin).</summary>
    public AccountLevel Level { get; } = level;

    /// <summary>Whether the line came from the server console (<see cref="CommandAttribute.ConsoleOnly"/> commands run only then).</summary>
    public bool Console { get; init; }

    /// <summary>Why <see cref="Player"/> is null, the error of <see cref="RequirePlayer"/>.</summary>
    public string NoPlayer { get; init; } = "no player in game";

    /// <summary>Answers the caller; a text of several lines is sent line by line.</summary>
    public void Reply(string text) => reply(text, false);

    /// <summary>An error answer (what a <see cref="CommandError"/> becomes).</summary>
    public void Error(string text) => reply(text, true);

    /// <summary>The player the command acts on, or a <see cref="CommandError"/> saying why there is none.</summary>
    public Player RequirePlayer() => Player ?? throw new CommandError(NoPlayer);
}
