using AlbionLocal.Server.Persistence;

namespace AlbionLocal.Server.Commands;

/// <summary>
/// Marks a static method as a GM command (TC ChatCommand / CommandScript, ARCHITECTURE.md §6.6), found once by
/// <see cref="CommandRegistry"/>. The path is one or more lowercase words ("item add"), typed after a '.' in chat or
/// on the server console. The method takes a <see cref="CommandContext"/> first, then the arguments, each parsed by
/// its parameter type (<see cref="CommandRegistry"/> lists them); a parameter with a default value is optional.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CommandAttribute(string path) : Attribute
{
    public string Path { get; } = path;

    /// <summary>Lowest account level that may run it; a command that does not say is for admins only.</summary>
    public AccountLevel Level { get; set; } = AccountLevel.Admin;

    /// <summary>What it does, for .help (the usage line is built from the parameters).</summary>
    public string Help { get; set; } = "";

    /// <summary>Only on the server console, never from chat whatever the level (the account commands); .help in chat leaves it out.</summary>
    public bool ConsoleOnly { get; set; }
}

/// <summary>
/// A command argument that is a secret (a password): <see cref="CommandRegistry.Redact"/> hides it in every logged line, and
/// argument errors of such a command never quote what was typed.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class SecretAttribute : Attribute;

/// <summary>A command's answer to the user: thrown anywhere in a command (or by argument parsing), shown as an error reply.</summary>
public sealed class CommandError(string message) : Exception(message);

/// <summary>Parameter type that takes the rest of the line as typed (spaces and quotes kept); only as the last parameter.</summary>
public readonly record struct Tail(string? Text)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    public override string ToString() => Text ?? "";
}
