using System.Net;
using AlbionLocal.Photon.Crypto;
using AlbionLocal.Photon.Enet;
using AlbionLocal.Photon.Serialization;

namespace AlbionLocal.Photon;

/// <summary>Photon message types (second byte after the 0xF3 signature; 0x80 = encrypted body).</summary>
public enum MessageType : byte
{
    Init = 0,
    InitResponse = 1,
    Operation = 2,
    OperationResponse = 3,
    Event = 4,
    InternalOperationRequest = 6,
    InternalOperationResponse = 7,
    Message = 8,
    RawMessage = 9,
}

/// <summary>Application callbacks for one connected client.</summary>
public interface IPhotonPeerHandler
{
    void OnOperationRequest(PhotonPeer peer, OperationRequest request, bool encrypted);
    void OnDisconnected(PhotonPeer peer, string reason);
}

/// <summary>
/// Server side of one client: Init handshake, payload encryption key exchange
/// (internal operation 0, client key in param 1, server key in response param 1) and
/// (de)serialization of operations, responses and events (client PeerBase g7k).
/// </summary>
public sealed class PhotonPeer
{
    public const byte Signature = 0xF3;
    private const byte InitEncryptionCode = 0;
    private const byte ClientKeyParam = 1;
    private const byte ServerKeyParam = 1;

    private readonly EnetConnection _conn;
    private readonly DiffieHellmanCrypto _crypto = new();
    private readonly string _name;

    internal PhotonPeer(EnetConnection conn, IPEndPoint endPoint, string name)
    {
        _conn = conn;
        _name = name;
        EndPoint = endPoint;
        conn.MessageReceived += OnMessage;
    }

    public IPEndPoint EndPoint { get; }
    public ushort PeerId => _conn.PeerId;
    public bool EncryptionReady => _crypto.IsReady;
    public IPhotonPeerHandler? Handler { get; set; }

    /// <summary>AppId from the Init message ("Albion.LoginServer", "Albion.StatusServer", …).</summary>
    public string AppId { get; private set; } = "";

    /// <summary>Free slot for the application (session object).</summary>
    public object? UserData { get; set; }

    private void OnMessage(byte[] data, byte channel)
    {
        if (data.Length >= 1 && data[0] == 0xF5)
        {
            Log.Warn(_name, "InitV3 received; answering like a classic Init");
            SendRaw([Signature, (byte)MessageType.InitResponse, 0], channel);
            return;
        }
        if (data.Length < 2 || (data[0] != Signature && data[0] != 0xFD))
        {
            Log.Warn(_name, $"message without signature ({data.Length} bytes)");
            return;
        }
        var type = (MessageType)(data[1] & 0x7F);
        var encrypted = (data[1] & 0x80) != 0;
        byte[] body;
        if (encrypted)
        {
            if (!_crypto.IsReady)
            {
                Log.Warn(_name, "encrypted message before key exchange; dropped");
                return;
            }
            body = _crypto.Decrypt(data.AsSpan(2));
        }
        else
        {
            body = data.AsSpan(2).ToArray();
        }

        switch (type)
        {
            case MessageType.Init:
                AppId = ParseAppId(body);
                Log.Info(_name, $"init ({body.Length} bytes, app '{AppId}')");
                SendRaw([Signature, (byte)MessageType.InitResponse, 0], channel);
                break;
            case MessageType.InternalOperationRequest:
                HandleInternal(Protocol18.ReadOperationRequest(new ByteReader(body)), channel);
                break;
            case MessageType.Operation:
                {
                    var req = Protocol18.ReadOperationRequest(new ByteReader(body));
                    if (Handler is null) Log.Warn(_name, $"no handler for {req}");
                    else Handler.OnOperationRequest(this, req, encrypted);
                    break;
                }
            default:
                Log.Warn(_name, $"unexpected message type {type}");
                break;
        }
    }

    private static string ParseAppId(byte[] body)
    {
        // Classic init after the 2-byte signature: version(2) sdk(1) clientver(3) 0 appId(32).
        if (body.Length < 39) return "?";
        return System.Text.Encoding.ASCII.GetString(body, 7, 32).TrimEnd('\0');
    }

    private void HandleInternal(OperationRequest req, byte channel)
    {
        if (req.Code != InitEncryptionCode || req.Parameters.GetValueOrDefault(ClientKeyParam) is not byte[] clientKey)
        {
            Log.Warn(_name, $"unknown internal operation {req.Code}");
            return;
        }
        _crypto.DeriveSharedKey(clientKey);
        var resp = new OperationResponse(InitEncryptionCode, new() { [ServerKeyParam] = _crypto.PublicKey });
        Send(MessageType.InternalOperationResponse, w => Protocol18.WriteOperationResponse(w, resp), encrypt: false, channel, reliable: true);
        Log.Info(_name, "payload encryption established");
    }

    public void SendOperationResponse(OperationResponse resp, bool encrypt, byte channel = 0) =>
        Send(MessageType.OperationResponse, w => Protocol18.WriteOperationResponse(w, resp), encrypt, channel, reliable: true);

    public void SendEvent(EventData ev, bool encrypt = false, bool reliable = true, byte channel = 0) =>
        Send(MessageType.Event, w => Protocol18.WriteEventData(w, ev), encrypt, channel, reliable);

    /// <summary>An unencrypted event as one unsequenced ENet command (<see cref="EnetConnection.SendUnsequenced"/>).</summary>
    public void SendEventUnsequenced(EventData ev, byte channel) =>
        _conn.SendUnsequenced(Message(MessageType.Event, w => Protocol18.WriteEventData(w, ev), encrypt: false), channel);

    private void Send(MessageType type, Action<ByteWriter> body, bool encrypt, byte channel, bool reliable) =>
        _conn.Send(Message(type, body, encrypt), channel, reliable);

    /// <summary>Signature, type (0x80 = encrypted) and the body written by <paramref name="body"/>.</summary>
    private byte[] Message(MessageType type, Action<ByteWriter> body, bool encrypt)
    {
        var bw = new ByteWriter();
        body(bw);
        var w = new ByteWriter(bw.Length + 34);
        w.Byte(Signature);
        if (encrypt && _crypto.IsReady)
        {
            w.Byte((byte)((byte)type | 0x80));
            w.Bytes(_crypto.Encrypt(bw.Span));
        }
        else
        {
            w.Byte((byte)type);
            w.Bytes(bw.Span);
        }
        return w.ToArray();
    }

    private void SendRaw(byte[] message, byte channel) => _conn.Send(message, channel, reliable: true);

    /// <summary>DISCONNECT reason the client takes as a deliberate end of the session by the server (status 1043).</summary>
    public const byte DisconnectByServerLogic = 1;

    /// <inheritdoc cref="Enet.EnetConnection.Disconnect"/>
    public void Disconnect(byte reason = 4) => _conn.Disconnect(reason);
}
