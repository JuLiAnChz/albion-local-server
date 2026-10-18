using System.Numerics;

namespace AlbionLocal.Server.Entities;

/// <summary>
/// A player's last Move (until step 6 WorldSession's LastMove). <paramref name="From"/> is the position when the
/// move was issued (Move key 1), so the player keeps walking towards <paramref name="Target"/> (key 3) at
/// <paramref name="Speed"/> (key 4, units/s) afterwards; the stop point is extrapolated.
/// Since stage 7 it is also the sample other clients get (analysis/work/stage7/SPEC.md §2.2): event 3 and the
/// NewCharacter keys 14/15/18/19/20 are built from it, so the first relayed sample after a spawn is never older than the
/// spawn's key 18.
/// </summary>
public sealed record MovementState(Vector2 From, Vector2 Target, float Speed, DateTime At)
{
    /// <summary>Facing in degrees (Move key 2, v7): event 3 direction byte, NewCharacter key 19.</summary>
    public float Direction { get; init; }

    /// <summary>
    /// GameTimeStamp (UTC ticks) of the sample: the server's receive time, never earlier than the player's previous one
    /// (<see cref="Player.NextMoveStamp"/>; the client's history a5m.b 0x180918c50 drops a sample older than its newest).
    /// Event 3 ts and NewCharacter key 18.
    /// </summary>
    public long Stamp { get; init; }

    /// <summary>Move key 5 (wa, a long of unknown meaning, SPEC §8 q5), relayed as is in event 3 (flag 4, cs2); 0 = none.</summary>
    public long Cs2 { get; init; }

    public Vector2 PositionAt(DateTime now)
    {
        var path = Target - From;
        var length = path.Length();
        var walked = Speed * (float)(now - At).TotalSeconds;
        return length <= 0 || walked >= length ? Target : From + path * (walked / length);
    }
}
