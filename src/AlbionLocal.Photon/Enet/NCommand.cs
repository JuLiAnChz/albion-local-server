using AlbionLocal.Photon.Serialization;

namespace AlbionLocal.Photon.Enet;

/// <summary>Command types (client NCommand g7d constants).</summary>
public enum CommandType : byte
{
    Ack = 1,
    Connect = 2,
    VerifyConnect = 3,
    Disconnect = 4,
    Ping = 5,
    SendReliable = 6,
    SendUnreliable = 7,
    SendFragment = 8,
    SendUnsequenced = 11,
    ServerTime = 12,
    SendReliableUnsequenced = 14,
    SendFragmentUnsequenced = 15,
    AckUnsequenced = 16,
}

/// <summary>
/// One Photon UDP command. Header (12 bytes, big-endian ints): type, channel, flags, reserved,
/// length (incl. header), reliable sequence number; then type-specific fields and payload
/// (g7d.a(g64, byte[], ref int) / g7d.a(byte[], ref int)).
/// </summary>
public sealed class NCommand
{
    public const int HeaderSize = 12;
    public const int FragmentHeaderSize = 32;
    public const byte FlagReliable = 1;
    public const byte FlagUnsequenced = 2;

    public CommandType Type;
    public byte ChannelId;
    public byte Flags;
    public byte Reserved = 4;
    public int ReliableSequenceNumber;
    public int UnreliableSequenceNumber;
    public int AckReceivedReliableSequenceNumber;
    public int AckReceivedSentTime;
    public int StartSequenceNumber;
    public int FragmentCount;
    public int FragmentNumber;
    public int TotalLength;
    public int FragmentOffset;
    public int UnsequencedGroupNumber;
    public byte[] Payload = [];

    // Bookkeeping for outgoing reliable commands.
    public int SentTime;
    public int SendCount;
    public int ResendDeadline;

    public bool IsReliable => (Flags & FlagReliable) != 0;

    public int Size => Type switch
    {
        CommandType.Ack or CommandType.AckUnsequenced => 20,
        CommandType.Connect or CommandType.VerifyConnect => HeaderSize + Payload.Length,
        CommandType.SendUnreliable or CommandType.SendUnsequenced => 16 + Payload.Length,
        CommandType.SendFragment or CommandType.SendFragmentUnsequenced => FragmentHeaderSize + Payload.Length,
        _ => HeaderSize + Payload.Length,
    };

    public static NCommand Parse(ByteReader r)
    {
        var start = r.Position;
        var c = new NCommand
        {
            Type = (CommandType)r.Byte(),
            ChannelId = r.Byte(),
            Flags = r.Byte(),
            Reserved = r.Byte(),
        };
        var length = r.Int32BE();
        c.ReliableSequenceNumber = r.Int32BE();
        if (length < HeaderSize || length - HeaderSize > r.Remaining)
            throw new InvalidDataException($"bad command length {length}");
        switch (c.Type)
        {
            case CommandType.Ack:
            case CommandType.AckUnsequenced:
                c.AckReceivedReliableSequenceNumber = r.Int32BE();
                c.AckReceivedSentTime = r.Int32BE();
                break;
            case CommandType.SendUnreliable:
                c.UnreliableSequenceNumber = r.Int32BE();
                break;
            case CommandType.SendUnsequenced:
                c.UnsequencedGroupNumber = r.Int32BE();
                break;
            case CommandType.SendFragment:
            case CommandType.SendFragmentUnsequenced:
                c.StartSequenceNumber = r.Int32BE();
                c.FragmentCount = r.Int32BE();
                c.FragmentNumber = r.Int32BE();
                c.TotalLength = r.Int32BE();
                c.FragmentOffset = r.Int32BE();
                break;
        }
        var consumed = r.Position - start;
        c.Payload = r.Bytes(length - consumed);
        return c;
    }

    public void Write(ByteWriter w)
    {
        w.Byte((byte)Type);
        w.Byte(ChannelId);
        w.Byte(Flags);
        w.Byte(Reserved);
        w.Int32BE(Size);
        w.Int32BE(ReliableSequenceNumber);
        switch (Type)
        {
            case CommandType.Ack:
            case CommandType.AckUnsequenced:
                w.Int32BE(AckReceivedReliableSequenceNumber);
                w.Int32BE(AckReceivedSentTime);
                return;
            case CommandType.SendUnreliable:
                w.Int32BE(UnreliableSequenceNumber);
                break;
            case CommandType.SendUnsequenced:
                w.Int32BE(UnsequencedGroupNumber);
                break;
            case CommandType.SendFragment:
            case CommandType.SendFragmentUnsequenced:
                w.Int32BE(StartSequenceNumber);
                w.Int32BE(FragmentCount);
                w.Int32BE(FragmentNumber);
                w.Int32BE(TotalLength);
                w.Int32BE(FragmentOffset);
                break;
        }
        w.Bytes(Payload);
    }

    public override string ToString() =>
        $"{Type} ch{ChannelId} seq{ReliableSequenceNumber}" +
        (Type is CommandType.Ack ? $" ack{AckReceivedReliableSequenceNumber}" : "") +
        (Type is CommandType.SendFragment ? $" frag{FragmentNumber}/{FragmentCount}@{StartSequenceNumber}" : "") +
        $" {Payload.Length}b";
}

/// <summary>Packet header: peerId (ushort), crc flag (0xCC = CRC follows), command count, sent time, challenge.</summary>
public struct PacketHeader
{
    public const int Size = 12;
    public const byte CrcFlag = 0xCC;
    public const byte DatagramEncryptedFlag = 1;

    public ushort PeerId;
    public byte Flags;
    public byte CommandCount;
    public int SentTime;
    public int Challenge;
}

/// <summary>Photon's CRC (SupportClass.CalculateCrc): reflected CRC-32 without final xor.</summary>
public static class PhotonCrc
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320u : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        return crc;
    }
}
