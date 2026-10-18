using AlbionLocal.Photon;

namespace AlbionLocal.Server;

/// <summary>
/// The timed actions of one map (ARCHITECTURE.md §4.4 step 4, "units: cast, auras"; stage 5 SPEC §6: "mob timers are
/// absolute"): a cast's end, a hit, a damage-over-time tick, a corpse's or loot bag's end, each at a <see cref="World.Clock"/>
/// time. <see cref="Run"/> runs, in the world tick, every action due by then in due order (equal times in the order they
/// were set), also those an action sets for a time already passed. An action checks itself whether what it acts on is
/// still there (a cancelled cast, a dead mob, a player gone); an unloaded map drops its timers with it. Touched only under
/// <see cref="World.Sync"/>.
/// </summary>
public sealed class MapTimers
{
    private readonly PriorityQueue<Action, (TimeSpan Due, long Order)> _queue = new();
    private long _order;

    /// <summary>Actions waiting.</summary>
    public int Count => _queue.Count;

    /// <summary>Runs <paramref name="action"/> in the first world tick at or after <paramref name="due"/>.</summary>
    public void At(TimeSpan due, Action action) => _queue.Enqueue(action, (due, _order++));

    /// <summary>Runs the actions due by <paramref name="now"/>; one that throws is logged and the rest still run.</summary>
    public void Run(TimeSpan now)
    {
        while (_queue.TryPeek(out var action, out var key) && key.Due <= now)
        {
            _queue.Dequeue();
            try { action(); }
            catch (Exception e) { Log.Error("world", $"timed action failed: {e}"); }
        }
    }
}
