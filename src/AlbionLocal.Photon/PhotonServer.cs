using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using AlbionLocal.Photon.Enet;

namespace AlbionLocal.Photon;

/// <summary>
/// UDP listener hosting Photon peers. All peer state is touched under one lock: the receive
/// loop and the 10 ms service tick both take it, and handlers run inside it, so application
/// code may call peer methods without extra synchronization.
/// </summary>
public sealed class PhotonServer : IDisposable
{
    private readonly UdpClient _udp;
    private readonly string _name;
    private readonly Func<PhotonPeer, IPhotonPeerHandler> _handlerFactory;
    private readonly Dictionary<IPEndPoint, (EnetConnection Conn, PhotonPeer Peer)> _peers = new();
    private readonly CancellationTokenSource _cts = new();
    private ushort _nextPeerId;
    private Task? _receive;
    private Task? _service;

    /// <param name="sync">Lock to share with other listeners (one lock for the whole server); null = own lock.</param>
    public PhotonServer(IPEndPoint endPoint, string name, Func<PhotonPeer, IPhotonPeerHandler> handlerFactory, object? sync = null)
    {
        _name = name;
        Sync = sync ?? new object();
        _handlerFactory = handlerFactory;
        _udp = new UdpClient(endPoint);
        if (OperatingSystem.IsWindows())
        {
            // Ignore ICMP port-unreachable resets when a client goes away (SIO_UDP_CONNRESET).
            _udp.Client.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null);
        }
        EndPoint = (IPEndPoint)_udp.Client.LocalEndPoint!;
    }

    public IPEndPoint EndPoint { get; }

    /// <summary>Lock that guards every peer; take it when touching peers from outside handlers.</summary>
    public object Sync { get; }

    public void Start()
    {
        _receive = Task.Run(ReceiveLoop);
        _service = Task.Run(ServiceLoop);
        Log.Info(_name, $"listening on udp {EndPoint}");
    }

    private async Task ReceiveLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            UdpReceiveResult res;
            try { res = await _udp.ReceiveAsync(_cts.Token); }
            catch (OperationCanceledException) { return; }
            catch (SocketException e)
            {
                Log.Debug(_name, $"socket: {e.SocketErrorCode}");
                continue;
            }
            lock (Sync)
            {
                try { OnDatagram(res.RemoteEndPoint, res.Buffer); }
                catch (Exception e) { Log.Error(_name, $"datagram from {res.RemoteEndPoint}: {e}"); }
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="data"/> carries CONNECT as its first command: the command type byte follows the 12-byte
    /// header (+4 when the CRC flag is set).
    /// </summary>
    private static bool IsConnect(byte[] data)
    {
        var first = data.Length > 2 && data[2] == PacketHeader.CrcFlag ? 16 : 12;
        return data.Length > first && data[first] == (byte)CommandType.Connect;
    }

    private void OnDatagram(IPEndPoint from, byte[] data)
    {
        // A CONNECT with a new challenge (header bytes 8-11, big-endian) from an endpoint that still has a connection is a
        // new client socket on a port the OS handed out again, e.g. the next game connection after a cluster change while
        // the old one waits out its timeout (SPEC stage 3 §4.7). The old connection would drop it at its challenge check,
        // so it is closed here, without a DISCONNECT (that would reach the new socket); its handler hears of it as of a
        // timeout. A resent CONNECT carries the old challenge and stays a duplicate of the old connection.
        if (_peers.TryGetValue(from, out var entry) && data.Length >= PacketHeader.Size && IsConnect(data)
            && BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(8, 4)) != entry.Conn.Challenge)
        {
            Log.Info(_name, $"peer {entry.Peer.PeerId}: {from} connects again with a new challenge; the old connection is closed without a DISCONNECT");
            // Its Disconnected handler removes the _peers entry before it calls the session, so a session that throws
            // does not keep the new connection out.
            try { entry.Conn.CloseSilently("replaced by a new connection from the same endpoint"); }
            catch (Exception e) { Log.Error(_name, $"peer {entry.Peer.PeerId}: closing the replaced connection: {e}"); }
        }
        if (!_peers.TryGetValue(from, out entry))
        {
            // Only a packet carrying CONNECT opens a peer.
            if (!IsConnect(data)) return;
            var id = ++_nextPeerId;
            var peerName = $"{_name}#{id}";
            var conn = new EnetConnection(isServer: true, d => SendTo(from, d), peerName, id);
            var peer = new PhotonPeer(conn, from, peerName);
            peer.Handler = _handlerFactory(peer);
            conn.Disconnected += reason =>
            {
                // Only while the endpoint is still this connection's: a replaced one must not remove its successor.
                if (_peers.TryGetValue(from, out var current) && current.Conn == conn) _peers.Remove(from);
                peer.Handler?.OnDisconnected(peer, reason);
            };
            entry = (conn, peer);
            _peers[from] = entry;
            Log.Info(_name, $"peer {id} connecting from {from}");
        }
        entry.Conn.Receive(data);
        entry.Conn.Flush();
    }

    private void SendTo(IPEndPoint to, byte[] datagram)
    {
        try { _udp.Send(datagram, datagram.Length, to); }
        catch (SocketException e) { Log.Debug(_name, $"send to {to}: {e.SocketErrorCode}"); }
    }

    private async Task ServiceLoop()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                lock (Sync)
                {
                    foreach (var (conn, _) in _peers.Values.ToArray())
                    {
                        try { conn.Flush(); }
                        catch (Exception e) { Log.Error(_name, $"flush: {e}"); }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        lock (Sync)
        {
            foreach (var (conn, _) in _peers.Values.ToArray()) conn.Disconnect();
        }
        _udp.Dispose();
        try { Task.WaitAll([_receive ?? Task.CompletedTask, _service ?? Task.CompletedTask], 1000); }
        catch (AggregateException) { }
    }
}
