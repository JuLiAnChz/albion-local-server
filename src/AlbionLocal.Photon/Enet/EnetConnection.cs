using AlbionLocal.Photon.Serialization;

namespace AlbionLocal.Photon.Enet;

/// <summary>
/// Photon UDP reliability layer for one remote endpoint (both roles, so the self-test can play
/// the client). Mirrors the client's EnetPeer (g64): reliable commands are acked with
/// (sequence, packet sent time), dispatched in order per channel, resent until acked;
/// channel 0xFF carries CONNECT/VERIFY_CONNECT/PING/DISCONNECT; large messages are fragmented.
/// Not thread-safe: the owner serializes calls.
/// </summary>
public sealed class EnetConnection
{
    public const int Mtu = 1200;
    public const byte ControlChannel = 0xFF;
    private const int MaxResends = 12;
    private const int TimeoutMs = 30000;

    private readonly bool _isServer;
    private readonly Action<byte[]> _sendDatagram;
    private readonly string _name;
    private readonly Dictionary<byte, Channel> _channels = new();
    private readonly List<NCommand> _outgoing = new();
    private readonly List<NCommand> _unacked = new();
    private readonly List<NCommand> _acks = new();
    private int _rto = 300;
    private int _lastReceive = Environment.TickCount;

    /// <summary>Last unsequenced group number sent; one counter per connection, not per channel.</summary>
    private int _outUnsequencedGroup;

    public EnetConnection(bool isServer, Action<byte[]> sendDatagram, string name, ushort peerId = 0xFFFF, int challenge = 0)
    {
        _isServer = isServer;
        _sendDatagram = sendDatagram;
        _name = name;
        PeerId = peerId;
        Challenge = challenge;
    }

    public ushort PeerId { get; private set; }
    public int Challenge { get; private set; }
    public bool IsConnected { get; private set; }
    public bool IsClosed { get; private set; }

    /// <summary>Raised with the payload of every in-order reliable/unreliable/reassembled message.</summary>
    public event Action<byte[], byte>? MessageReceived;

    public event Action? Connected;
    public event Action<string>? Disconnected;

    private sealed class Channel
    {
        public int OutReliable;
        public int OutUnreliable;
        public int InReliable;
        public int InUnreliable;
        public readonly SortedDictionary<int, NCommand> Pending = new();
        public readonly Dictionary<int, Fragments> Assemblies = new();
    }

    private sealed class Fragments(int count, int total)
    {
        public readonly byte[] Data = new byte[total];
        public readonly bool[] Have = new bool[count];
        public int Received;
    }

    private Channel Ch(byte id) => _channels.TryGetValue(id, out var c) ? c : _channels[id] = new Channel();

    // ------------------------------------------------------------------ receive

    public void Receive(byte[] datagram)
    {
        if (IsClosed) return;
        var r = new ByteReader(datagram);
        if (datagram.Length < PacketHeader.Size) return;
        var h = new PacketHeader
        {
            PeerId = r.UInt16BE(),
            Flags = r.Byte(),
            CommandCount = r.Byte(),
            SentTime = r.Int32BE(),
            Challenge = r.Int32BE(),
        };
        if (h.Flags == PacketHeader.DatagramEncryptedFlag)
        {
            Log.Warn(_name, "datagram encryption is not supported; packet dropped");
            return;
        }
        if (h.Flags == PacketHeader.CrcFlag)
        {
            var crc = (uint)r.Int32BE();
            var copy = (byte[])datagram.Clone();
            copy.AsSpan(12, 4).Clear();
            if (PhotonCrc.Compute(copy) != crc)
            {
                Log.Warn(_name, "CRC mismatch; packet dropped");
                return;
            }
        }
        if (_isServer && !IsConnected && Challenge == 0) Challenge = h.Challenge;
        if (h.Challenge != Challenge)
        {
            Log.Debug(_name, $"challenge {h.Challenge:x8} != {Challenge:x8}; packet dropped");
            return;
        }
        _lastReceive = Environment.TickCount;

        for (var i = 0; i < h.CommandCount && r.Remaining > 0; i++)
        {
            NCommand cmd;
            try { cmd = NCommand.Parse(r); }
            catch (Exception e)
            {
                Log.Warn(_name, $"bad command: {e.Message}");
                return;
            }
            Log.Trace(_name, $"<- {cmd}");
            HandleCommand(cmd, h.SentTime);
        }
    }

    private void HandleCommand(NCommand cmd, int packetSentTime)
    {
        if (cmd.Type is CommandType.Ack or CommandType.AckUnsequenced)
        {
            var i = _unacked.FindIndex(c => c.ChannelId == cmd.ChannelId && c.ReliableSequenceNumber == cmd.AckReceivedReliableSequenceNumber);
            if (i >= 0)
            {
                var rtt = Environment.TickCount - _unacked[i].SentTime;
                _rto = Math.Clamp((_rto * 7 + rtt * 2 * 1) / 8 + 50, 100, 2000);
                _unacked.RemoveAt(i);
            }
            return;
        }

        if (cmd.IsReliable)
            _acks.Add(new NCommand
            {
                Type = (cmd.Flags & NCommand.FlagUnsequenced) != 0 ? CommandType.AckUnsequenced : CommandType.Ack,
                ChannelId = cmd.ChannelId,
                AckReceivedReliableSequenceNumber = cmd.ReliableSequenceNumber,
                AckReceivedSentTime = packetSentTime,
            });

        if (cmd.Type == CommandType.SendUnreliable)
        {
            var ch = Ch(cmd.ChannelId);
            if (cmd.ReliableSequenceNumber < ch.InReliable || cmd.UnreliableSequenceNumber <= ch.InUnreliable) return;
            ch.InUnreliable = cmd.UnreliableSequenceNumber;
            Deliver(cmd.Payload, cmd.ChannelId);
            return;
        }
        if (cmd.Type is CommandType.SendUnsequenced or CommandType.SendReliableUnsequenced)
        {
            Deliver(cmd.Payload, cmd.ChannelId);
            return;
        }
        if (cmd.Type == CommandType.Disconnect)
        {
            // Disconnect() sends it unreliable (Flags = 0); accept it either way, as ENet does.
            // The reserved header byte is the reason (see Disconnect).
            Close($"remote disconnect ({cmd.Reserved})");
            return;
        }
        if (!cmd.IsReliable)
        {
            Log.Debug(_name, $"ignored unreliable {cmd.Type}");
            return;
        }

        // Reliable, sequenced: buffer and dispatch in order.
        var chan = Ch(cmd.ChannelId);
        if (cmd.ReliableSequenceNumber <= chan.InReliable) return; // duplicate (ack already queued)
        chan.Pending.TryAdd(cmd.ReliableSequenceNumber, cmd);
        while (chan.Pending.Remove(chan.InReliable + 1, out var next))
        {
            chan.InReliable++;
            Dispatch(next, chan);
        }
    }

    private void Dispatch(NCommand cmd, Channel chan)
    {
        switch (cmd.Type)
        {
            case CommandType.Connect when _isServer:
                if (!IsConnected)
                {
                    IsConnected = true;
                    var payload = new byte[32];
                    payload[0] = (byte)(PeerId >> 8);
                    payload[1] = (byte)PeerId;
                    Enqueue(CommandType.VerifyConnect, ControlChannel, payload, reliable: true);
                    Connected?.Invoke();
                }
                break;
            case CommandType.VerifyConnect when !_isServer:
                PeerId = (ushort)((cmd.Payload[0] << 8) | cmd.Payload[1]);
                IsConnected = true;
                Connected?.Invoke();
                break;
            case CommandType.Disconnect:
                Close($"remote disconnect ({cmd.Reserved})");
                break;
            case CommandType.Ping:
                break;
            case CommandType.SendReliable:
                Deliver(cmd.Payload, cmd.ChannelId);
                break;
            case CommandType.SendFragment:
                {
                    if (!chan.Assemblies.TryGetValue(cmd.StartSequenceNumber, out var asm))
                        chan.Assemblies[cmd.StartSequenceNumber] = asm = new Fragments(cmd.FragmentCount, cmd.TotalLength);
                    if (cmd.FragmentNumber < asm.Have.Length && !asm.Have[cmd.FragmentNumber])
                    {
                        asm.Have[cmd.FragmentNumber] = true;
                        asm.Received++;
                        cmd.Payload.CopyTo(asm.Data, cmd.FragmentOffset);
                    }
                    if (asm.Received == asm.Have.Length)
                    {
                        chan.Assemblies.Remove(cmd.StartSequenceNumber);
                        Deliver(asm.Data, cmd.ChannelId);
                    }
                    break;
                }
            default:
                Log.Debug(_name, $"unhandled {cmd}");
                break;
        }
    }

    private void Deliver(byte[] payload, byte channel)
    {
        try { MessageReceived?.Invoke(payload, channel); }
        catch (Exception e) { Log.Error(_name, $"message handler failed: {e}"); }
    }

    // ------------------------------------------------------------------ send

    /// <summary>Client role: starts the handshake.</summary>
    public void Connect()
    {
        var payload = new byte[32];
        payload[2] = Mtu >> 8;
        payload[3] = Mtu & 0xFF;
        payload[6] = 0x80;
        payload[11] = 2;
        payload[22] = 0x13;
        payload[23] = 0x88;
        payload[27] = 2;
        payload[31] = 2;
        Enqueue(CommandType.Connect, ControlChannel, payload, reliable: true);
    }

    /// <summary>
    /// Sends DISCONNECT and closes. <paramref name="reason"/> goes in the reserved header byte; the client
    /// maps it to a status (g64.g 0x1832a5f50): 1 = DisconnectByServerLogic (1043), 2 = server timeout
    /// (1041, the client then tries to reconnect), 3 = 1042, anything else = 1044.
    /// </summary>
    public void Disconnect(byte reason = 4)
    {
        if (IsClosed) return;
        _outgoing.Add(new NCommand { Type = CommandType.Disconnect, ChannelId = ControlChannel, Flags = 0, Reserved = reason });
        Flush();
        Close("local disconnect");
    }

    /// <summary>
    /// Closes without sending a DISCONNECT (<see cref="PhotonServer"/>, when a new client connection takes over this one's
    /// endpoint: anything sent there now would reach the new socket). <see cref="Disconnected"/> fires as on a timeout.
    /// </summary>
    public void CloseSilently(string reason) => Close(reason);

    public void SendPing() => Enqueue(CommandType.Ping, ControlChannel, [], reliable: true);

    /// <summary>Queues a message payload; fragments it when it does not fit one datagram.</summary>
    public void Send(byte[] message, byte channel, bool reliable)
    {
        const int single = Mtu - PacketHeader.Size - NCommand.HeaderSize - 4;
        if (message.Length <= single)
        {
            Enqueue(reliable ? CommandType.SendReliable : CommandType.SendUnreliable, channel, message, reliable);
            return;
        }
        const int chunk = Mtu - PacketHeader.Size - NCommand.FragmentHeaderSize;
        var ch = Ch(channel);
        var count = (message.Length + chunk - 1) / chunk;
        var start = ch.OutReliable + 1;
        for (var i = 0; i < count; i++)
        {
            var off = i * chunk;
            var len = Math.Min(chunk, message.Length - off);
            var c = new NCommand
            {
                Type = CommandType.SendFragment,
                ChannelId = channel,
                Flags = NCommand.FlagReliable,
                ReliableSequenceNumber = ++ch.OutReliable,
                StartSequenceNumber = start,
                FragmentCount = count,
                FragmentNumber = i,
                TotalLength = message.Length,
                FragmentOffset = off,
                Payload = message.AsSpan(off, len).ToArray(),
            };
            _outgoing.Add(c);
        }
    }

    /// <summary>
    /// Queues a message as SendUnsequenced (command 11, flag 2): no ack, no resend and no order, delivered as it
    /// arrives, so a lost one stays lost. Reliable sequence 0 and a group number counting from 1, as the official
    /// server frames its keepalive (captures). Only for messages that fit one datagram (no unsequenced fragments).
    /// </summary>
    public void SendUnsequenced(byte[] message, byte channel)
    {
        if (message.Length > Mtu - PacketHeader.Size - NCommand.HeaderSize - 4)
            throw new ArgumentException($"{message.Length} bytes do not fit one unsequenced command", nameof(message));
        _outgoing.Add(new NCommand
        {
            Type = CommandType.SendUnsequenced,
            ChannelId = channel,
            Flags = NCommand.FlagUnsequenced,
            UnsequencedGroupNumber = ++_outUnsequencedGroup,
            Payload = message,
        });
    }

    private void Enqueue(CommandType type, byte channel, byte[] payload, bool reliable)
    {
        var ch = Ch(channel);
        var c = new NCommand { Type = type, ChannelId = channel, Payload = payload, Flags = reliable ? NCommand.FlagReliable : (byte)0 };
        if (reliable) c.ReliableSequenceNumber = ++ch.OutReliable;
        else
        {
            c.ReliableSequenceNumber = ch.OutReliable;
            c.UnreliableSequenceNumber = ++ch.OutUnreliable;
        }
        _outgoing.Add(c);
    }

    /// <summary>Sends queued acks/commands and due resends. Call regularly.</summary>
    public void Flush()
    {
        if (IsClosed) return;
        var now = Environment.TickCount;
        if (now - _lastReceive > TimeoutMs)
        {
            Close("timeout");
            return;
        }

        var batch = new List<NCommand>(_acks);
        _acks.Clear();
        foreach (var c in _unacked)
        {
            if (now - c.ResendDeadline < 0) continue;
            if (c.SendCount > MaxResends)
            {
                Close($"no ack for {c}");
                return;
            }
            batch.Add(c);
        }
        batch.AddRange(_outgoing);
        foreach (var c in _outgoing) if (c.IsReliable) _unacked.Add(c);
        _outgoing.Clear();
        if (batch.Count == 0) return;

        var w = new ByteWriter(Mtu);
        var count = 0;
        void Emit()
        {
            if (count == 0) return;
            var packet = w.ToArray();
            packet[3] = (byte)count;
            _sendDatagram(packet);
            w = new ByteWriter(Mtu);
            count = 0;
        }
        foreach (var c in batch)
        {
            if (count > 0 && (w.Length + c.Size > Mtu || count == 255)) Emit();
            if (count == 0)
            {
                w.UInt16BE(_isServer ? (ushort)0 : PeerId);
                w.Byte(0);
                w.Byte(0);
                w.Int32BE(_isServer ? PhotonClock.ServerTimeMs : now); // the client's server clock
                w.Int32BE(Challenge);
            }
            if (c.IsReliable && c.Type is not (CommandType.Ack or CommandType.AckUnsequenced))
            {
                c.SentTime = now;
                c.SendCount++;
                c.ResendDeadline = now + (_rto << Math.Min(c.SendCount - 1, 4));
            }
            Log.Trace(_name, $"-> {c}");
            c.Write(w);
            count++;
        }
        Emit();
    }

    private void Close(string reason)
    {
        if (IsClosed) return;
        IsClosed = true;
        IsConnected = false;
        Log.Info(_name, $"connection closed: {reason}");
        Disconnected?.Invoke(reason);
    }
}
