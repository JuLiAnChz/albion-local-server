using AlbionLocal.Photon;

namespace AlbionLocal.Server.Commands;

/// <summary>
/// GM commands typed on the server console (TC CliRunnable): one thread reads stdin line by line and posts each line to
/// the world (<see cref="World.Post"/>), which runs it at the next tick under the lock as Admin
/// (<see cref="CommandRegistry.ExecuteConsole"/>); replies go to the log. The server usually runs in a background shell
/// whose stdin is empty or closed: the thread ends at the end of the input (or when it cannot be read) instead of
/// polling, and Program never waits for it.
/// </summary>
public static class ConsoleInput
{
    /// <summary>Starts the reader thread on <paramref name="input"/> (default: stdin); <paramref name="reply"/> defaults to the log.</summary>
    public static Thread Start(World world, TextReader? input = null, Action<string, bool>? reply = null)
    {
        input ??= Console.In;
        reply ??= (text, error) =>
        {
            foreach (var line in text.Split('\n')) Log.Always("console", error ? $"error: {line}" : line);
        };
        var thread = new Thread(() => Run(world, input, reply)) { Name = "console", IsBackground = true };
        thread.Start();
        return thread;
    }

    private static void Run(World world, TextReader input, Action<string, bool> reply)
    {
        while (true)
        {
            string? line;
            try { line = input.ReadLine(); }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
                Log.Debug("console", $"stdin cannot be read ({e.Message}): no console commands");
                return;
            }
            if (line is null)
            {
                Log.Debug("console", "end of stdin: no more console commands");
                return;
            }
            if (line.Trim() is not { Length: > 0 } command) continue;
            world.Post(() => world.Commands.ExecuteConsole(world, command, reply));
        }
    }
}
