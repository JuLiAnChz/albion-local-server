namespace AlbionLocal.Server;

/// <summary>
/// The one id counter for every runtime object (players, items; later mobs, buildings, exits…), shared
/// by all connections. Ids are never reused while the server runs and never saved: the client caches
/// objects by id. The first id handed out is 2.
/// </summary>
public sealed class ObjectIds
{
    private long _last = 1;

    public long Next() => Interlocked.Increment(ref _last);
}
