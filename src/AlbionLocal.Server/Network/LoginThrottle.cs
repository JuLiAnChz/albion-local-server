using System.Net;

namespace AlbionLocal.Server.Network;

/// <summary>
/// How many password checks (Login, CreateAccount: one PBKDF2 hash each, <see cref="Persistence.Passwords"/>) an address may
/// cause per minute. The login handlers run under the world's lock (<see cref="ServerHost.Sync"/>), so a client that sends
/// Logins without end would stall the game of every player by one hash each; past the limit the request is refused
/// without a hash. One per server (<see cref="ServerHost"/> shares it between its login connections); a session without
/// one given (the self-test's) has its own.
/// </summary>
public sealed class LoginThrottle
{
    /// <summary>Password checks per address and minute.</summary>
    public const int Limit = 20;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly object _sync = new();
    private readonly Dictionary<IPAddress, Queue<DateTime>> _recent = new();

    /// <summary>Whether <paramref name="address"/> may have one more password check at <paramref name="now"/> (and counts it if so).</summary>
    public bool Allow(IPAddress address, DateTime now)
    {
        lock (_sync)
        {
            foreach (var (key, old) in _recent.Where(e => e.Value.Count == 0 || e.Value.Peek() <= now - Window).ToList())
            {
                while (old.Count > 0 && old.Peek() <= now - Window) old.Dequeue();
                if (old.Count == 0) _recent.Remove(key);
            }
            if (!_recent.TryGetValue(address, out var times)) _recent[address] = times = new Queue<DateTime>();
            if (times.Count >= Limit) return false;
            times.Enqueue(now);
            return true;
        }
    }
}
