using System.Net;
using AlbionLocal.Photon.Serialization;

namespace AlbionLocal.Server.Network;

/// <summary>
/// Stage I bots (analysis/work/stageI/bots/SPEC.md §1.1, §1.2): the connection of every bot's <see cref="WorldSession"/>, which has no
/// client: every send and the disconnect do nothing. <see cref="AlbionSession"/> and the movement relay return before they build a
/// message for it, so a bot costs no serialization; <see cref="Calls"/> counts what still reached it (the self-test expects 0).
/// </summary>
public sealed class BotPeer : IPeer
{
    public static BotPeer Instance { get; } = new();

    private BotPeer() { }

    public ushort PeerId => 0;

    public string AppId => "bot";

    public IPEndPoint EndPoint { get; } = new(IPAddress.Loopback, 0);

    private int _calls;

    /// <summary>Sends and disconnects that reached the peer (none should: the sessions skip a bot's peer).</summary>
    public int Calls => Volatile.Read(ref _calls);

    public void SendOperationResponse(OperationResponse resp, bool encrypt) => Interlocked.Increment(ref _calls);

    public void SendEvent(EventData ev, bool encrypt = false) => Interlocked.Increment(ref _calls);

    public void SendEventUnsequenced(EventData ev, byte channel) => Interlocked.Increment(ref _calls);

    public void SendEventUnreliable(EventData ev, byte channel) => Interlocked.Increment(ref _calls);

    public void Disconnect(byte reason = 4) => Interlocked.Increment(ref _calls);
}
