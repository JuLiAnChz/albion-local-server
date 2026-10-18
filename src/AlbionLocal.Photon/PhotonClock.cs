namespace AlbionLocal.Photon;

/// <summary>
/// Server time as Photon clients see it: the int32 millisecond value in every packet header.
/// The client derives its server clock from it (offset taken on the ACK of its ServerTime
/// command). It starts one hour after server start, like Photon's own server-start based clock.
/// </summary>
public static class PhotonClock
{
    private const long StartOffsetMs = 3_600_000;
    private static readonly long Start = Environment.TickCount64;

    /// <summary>Milliseconds on the server clock (wraps after 24.8 days like Photon's int).</summary>
    public static int ServerTimeMs => unchecked((int)(Environment.TickCount64 - Start + StartOffsetMs));
}
