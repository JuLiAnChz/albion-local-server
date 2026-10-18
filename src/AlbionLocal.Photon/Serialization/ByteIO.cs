using System.Buffers.Binary;

namespace AlbionLocal.Photon.Serialization;

/// <summary>Growable byte buffer used by the serializers and the packet builder.</summary>
public sealed class ByteWriter(int capacity = 256)
{
    private byte[] _buf = new byte[capacity];

    public int Length { get; private set; }

    public ReadOnlySpan<byte> Span => _buf.AsSpan(0, Length);

    public byte[] ToArray() => _buf.AsSpan(0, Length).ToArray();

    private Span<byte> Grow(int n)
    {
        if (Length + n > _buf.Length)
            Array.Resize(ref _buf, Math.Max(_buf.Length * 2, Length + n));
        var s = _buf.AsSpan(Length, n);
        Length += n;
        return s;
    }

    public void Byte(byte v) => Grow(1)[0] = v;
    public void Bytes(ReadOnlySpan<byte> v) => v.CopyTo(Grow(v.Length));
    public void Int16LE(short v) => BinaryPrimitives.WriteInt16LittleEndian(Grow(2), v);
    public void Int16BE(short v) => BinaryPrimitives.WriteInt16BigEndian(Grow(2), v);
    public void UInt16BE(ushort v) => BinaryPrimitives.WriteUInt16BigEndian(Grow(2), v);
    public void Int32BE(int v) => BinaryPrimitives.WriteInt32BigEndian(Grow(4), v);
    public void SingleLE(float v) => BinaryPrimitives.WriteSingleLittleEndian(Grow(4), v);
    public void DoubleLE(double v) => BinaryPrimitives.WriteDoubleLittleEndian(Grow(8), v);

    /// <summary>Overwrites 4 bytes at <paramref name="offset"/> (big-endian); used to patch lengths.</summary>
    public void PatchInt32BE(int offset, int v) => BinaryPrimitives.WriteInt32BigEndian(_buf.AsSpan(offset, 4), v);
}

/// <summary>Forward-only reader over a byte span copy.</summary>
public sealed class ByteReader(byte[] data, int offset = 0, int? end = null)
{
    private readonly int _end = end ?? data.Length;

    public int Position { get; set; } = offset;
    public int Remaining => _end - Position;
    public byte[] Data => data;

    private ReadOnlySpan<byte> Take(int n)
    {
        if (n < 0 || Position + n > _end)
            throw new InvalidDataException($"read past end ({n} bytes at {Position}, end {_end})");
        var s = data.AsSpan(Position, n);
        Position += n;
        return s;
    }

    public byte Byte() => Take(1)[0];
    public byte[] Bytes(int n) => Take(n).ToArray();
    public short Int16LE() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public ushort UInt16LE() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public short Int16BE() => BinaryPrimitives.ReadInt16BigEndian(Take(2));
    public ushort UInt16BE() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));
    public int Int32BE() => BinaryPrimitives.ReadInt32BigEndian(Take(4));
    public float SingleLE() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public double DoubleLE() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));
    public void Skip(int n) => Take(n);
}
