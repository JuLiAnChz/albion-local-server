using AlbionLocal.Photon;
using AlbionLocal.Photon.Serialization;
using AlbionLocal.Server.Protocol;

namespace AlbionLocal.Server.Network;

/// <summary>
/// The Photon-level keepalive of a joined connection: event 2 {0: counter, 1: 0.0}, no Albion code 252, once a
/// second, as the official game server sends it (captures: the first one about 1 s after the Join response, the
/// counter starting at 1 on every game connection, independent of the client's op 2 counter; SendUnsequenced on
/// channel 1). The client (c3s.akw 0x180a8d960 → c3r.xg, ci7.xg 0x180982550) unboxes key 0 as Int32 and key 1 as
/// Single, both required; it keeps key 1 in c3d.cd and feeds the counter into c3d.cb (cq, a window of received
/// counters, cq.i 0x180858ec0), so a skipped number would count as a lost packet. The client's own op 2 is dropped
/// unanswered in <see cref="AlbionSession.OnRequest"/>.
/// </summary>
public sealed partial class WorldSession
{
    private static readonly TimeSpan KeepaliveInterval = TimeSpan.FromSeconds(1);

    /// <summary>ENet channel of the keepalive (the official server's and the client's op 2).</summary>
    private const byte KeepaliveChannel = 1;

    /// <summary>Keepalive events sent on this connection.</summary>
    private int _keepalives;

    /// <summary><see cref="World.Clock"/> time the next keepalive is due.</summary>
    private TimeSpan _nextKeepalive;

    /// <summary>Join: the first keepalive goes out one interval later.</summary>
    private void StartKeepalive() => _nextKeepalive = world.Clock.Now + KeepaliveInterval;

    /// <summary>World timer (<see cref="World.Update"/>, under the lock): sends what is due; a late tick catches up.</summary>
    internal void UpdateKeepalive(TimeSpan now)
    {
        while (now >= _nextKeepalive)
        {
            _nextKeepalive += KeepaliveInterval;
            // Wire as official: 0B nn (Int1; Int2 above 255, as Photon writes the client's counter) and 20 (FloatZero),
            // unsequenced, so a lost one is not resent and shows in the client's received-counter window.
            Peer.SendEventUnsequenced(new EventData(KeepaliveCode, new() { [0] = new NarrowInt(++_keepalives), [1] = 0f }), KeepaliveChannel);
            Log.Trace(Name, $"-> keepalive {_keepalives}");
        }
    }

    /// <summary>
    /// ReportClientError (op 44, client class z0 {0, 1, 2 strings}): an exception the client caught, which the official server answers
    /// with nothing. Logged at Info (stageM movement NOTES §5.4: a client exception in an event handler, the ForcedMovement one say,
    /// shows here); before stageM it took the unhandled path and its text went to the Debug log only.
    /// </summary>
    [Handles(OperationCodes.ReportClientError, Status = SessionStatus.Connected)]
    private AlbionResponse? ReportClientError(ReportClientErrorRequest req)
    {
        static string Cut(string? s) => s is null ? "" : s.Length > 2000 ? s[..2000] + "…" : s;
        Log.Info(Name, $"client error: {Cut(req.zr)} | {Cut(req.zs)}{(string.IsNullOrEmpty(req.zt) ? "" : " | " + Cut(req.zt))}");
        return null;
    }
}
