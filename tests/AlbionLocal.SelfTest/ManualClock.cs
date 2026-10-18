using AlbionLocal.Server;

namespace AlbionLocal.SelfTest;

/// <summary>A world clock that moves only when the test says so.</summary>
public sealed class ManualClock : IWorldClock
{
    public TimeSpan Now { get; private set; }

    public void Advance(TimeSpan by) => Now += by;
}
