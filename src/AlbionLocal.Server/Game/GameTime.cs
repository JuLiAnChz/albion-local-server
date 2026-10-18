namespace AlbionLocal.Server.Game;

/// <summary>
/// Server-wide time base. GameTimeStamp = UTC DateTime ticks. Login key 251 (and the session
/// id, key 57) carry the server start, as on the official server; the client uses it as the
/// base of compressed GameTimeStamps (Int32 ms since that base; AlbionSerializer.CompressedTimeBase).
/// </summary>
public static class GameTime
{
    public static long StartTicks { get; } = DateTime.UtcNow.Ticks;

    /// <summary>
    /// Now as a GameTimeStamp. Put ticks into every time field, also [Wire(CompressedTime)] ones: the
    /// client reads an Int64 as absolute ticks; only Int32 values are ms since the Login key 251 base.
    /// </summary>
    public static long Now => DateTime.UtcNow.Ticks;
}
