using System.Diagnostics;

namespace AlbionLocal.Server;

/// <summary>
/// Monotonic time of the world loop (ARCHITECTURE.md §4.4): each tick takes its dt and the due times of
/// the world timers from it. <see cref="RealClock"/> in the server, a hand-stepped clock in the self-test.
/// GameTimeStamps for the client still come from <see cref="Game.GameTime"/>.
/// </summary>
public interface IWorldClock
{
    /// <summary>Time since an arbitrary start; never goes back.</summary>
    TimeSpan Now { get; }
}

/// <summary>The machine's monotonic clock (Stopwatch), counted from the creation of the clock.</summary>
public sealed class RealClock : IWorldClock
{
    private readonly long _start = Stopwatch.GetTimestamp();

    public TimeSpan Now => Stopwatch.GetElapsedTime(_start);
}
