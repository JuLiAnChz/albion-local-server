using System.Text.Json;
using AlbionLocal.Photon;
using AlbionLocal.Server;
using AlbionLocal.Server.Commands;
using AlbionLocal.Server.Data;

ServerConfig config;
try
{
    config = ServerConfig.Load(Path.Combine("config", "albionlocal.json"), args);
}
catch (Exception e) when (e is InvalidDataException or JsonException)
{
    Log.Error("main", $"config: {e.Message}");
    return 1;
}
Log.Level = args.Contains("-v") ? LogLevel.Debug : args.Contains("-vv") ? LogLevel.Trace : config.LogLevel;
// --check-content: the offline golden checks of the cluster data over every world.xml cluster (Data/ContentCheck), a
// summary, then the exit code. No port, save or log file is touched, so a build of its own (README: a running server
// locks its bin folder) can run next to a running server.
if (args.Contains("--check-content"))
{
    try
    {
        return ContentCheck.Run(new ClusterStore(config.ClientGameDataPath, config.Clusters), Console.Out, all: args.Contains("-v"));
    }
    catch (InvalidDataException e)
    {
        Log.Error("main", e.Message);
        return 1;
    }
}
// Here, not in ServerHost: the self-test builds its servers with the default config and must not write into data/.
if (config.LogFile is { } logFile) Log.SetFile(logFile);

try
{
    ServerHost built;
    try
    {
        built = new ServerHost(config);
    }
    catch (InvalidDataException e)
    {
        // The client data (Paths:ClientGameData) is missing or broken: no start without it.
        Log.Error("main", e.Message);
        return 1;
    }
    using var host = built;
    host.Start();
    // GM commands typed here (stdin); in a background shell stdin is empty and the reader just ends.
    ConsoleInput.Start(host.World);
    Log.Info("main", "press Ctrl+C to stop");
    if (!Console.IsInputRedirected) Log.Info("main", "commands: type .help here, or in the game's Say chat");

    var stop = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        stop.TrySetResult();
    };
    await stop.Task;
    Log.Info("main", "stopping");
    return 0;
}
finally
{
    Log.SetFile(null);
}
