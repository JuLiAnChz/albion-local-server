using System.Net;
using AlbionLocal.Photon.Serialization;

namespace AlbionLocal.Server.Network;

/// <summary>
/// What a session needs of its connection: send responses and events, end it, name it in the log.
/// <see cref="PhotonPeerAdapter"/> wraps the UDP peer; the self-test drives sessions through a fake one.
/// </summary>
public interface IPeer
{
    ushort PeerId { get; }

    /// <summary>AppId from the Init message ("Albion.LoginServer", "Albion.StatusServer", …).</summary>
    string AppId { get; }

    IPEndPoint EndPoint { get; }

    void SendOperationResponse(OperationResponse resp, bool encrypt);

    void SendEvent(EventData ev, bool encrypt = false);

    /// <summary>
    /// Sends an unencrypted event as an unsequenced ENet command on <paramref name="channel"/>: no ack, no resend,
    /// no order with the other messages (the keepalive, framed as the official server's).
    /// </summary>
    void SendEventUnsequenced(EventData ev, byte channel);

    /// <summary>
    /// Sends an unencrypted event as an unreliable ENet command (SendUnreliable, command 7) on <paramref name="channel"/>:
    /// no ack and no resend, but in order with the channel's other messages (a late one is dropped by ENet). The movement
    /// relay (event 3, stage 7 SPEC §2.2: officially 96 % unreliable on channel 0).
    /// </summary>
    void SendEventUnreliable(EventData ev, byte channel);

    /// <summary>Ends the connection with a DISCONNECT carrying <paramref name="reason"/> (see PhotonPeer.DisconnectByServerLogic).</summary>
    void Disconnect(byte reason = 4);
}
