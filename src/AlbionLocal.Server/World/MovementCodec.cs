using System.Buffers.Binary;
using System.Numerics;
using AlbionLocal.Photon.Serialization;

namespace AlbionLocal.Server;

/// <summary>
/// The compact movement event of other objects: Photon event 3 {0: long object id, 1: byte[]}, no Albion code 252
/// (analysis/work/stage7/movement/SPEC.md, analysis/work/stage7/SPEC.md §2.1). The layout comes from the client's decoder
/// c3f.a 0x180a7f880 (port and round trip: movecodec.py, test_movecodec.py), little endian:
/// flags u8 (1 speed, 2 target, 4 cs2 tail, 8 cs3), ts i64 (absolute GameTimeStamp ticks), pos.x, pos.y f32, direction u8
/// (deg = b · 360 / 256, a71.a 0x180509e50), then speed f32 if flag 1, target x, y f32 if flag 2 (absent = target = pos),
/// cs2 last if flag 4 (the remaining length picks 8/4/2 bytes; this encoder always writes 8). No XOR: the server sends no
/// KeySync 602, so the client's key (ci7.cg) stays null. Remote players go through csh.aft 0x180befd80, mobs through c0n.aft
/// 0x180a16d30; both insert the sample into the a5m history, which drops a ts older than its newest. Code 3 is always this
/// compact form; jumps use Teleport (event 4), which resets that history.
/// </summary>
public static class MovementCodec
{
    public const byte FlagSpeed = 1, FlagTarget = 2, FlagCs2 = 4, FlagCs3 = 8;

    /// <summary>Photon event code of the compact movement event (c3s.akw 0x180a8d960 maps it to Albion code 3).</summary>
    public const byte PhotonEventCode = 3;

    /// <summary>Length of a sample without target and cs2: 18 bytes, 22 with speed.</summary>
    public const int MinLength = 18;

    /// <summary>
    /// Key 1 for an explicit choice of the optional fields (null = flag off): the layout above. The relay's choice is
    /// <see cref="EncodeSample"/>; this form also writes the 18- and 26-byte variants, for the golden vectors of movecodec.py.
    /// </summary>
    public static byte[] Encode(long ticks, Vector2 position, float direction, float? speed, Vector2? target, long? cs2, bool cs3 = false)
    {
        var flags = (byte)((speed is null ? 0 : FlagSpeed) | (target is null ? 0 : FlagTarget) | (cs2 is null ? 0 : FlagCs2) | (cs3 ? FlagCs3 : 0));
        var data = new byte[MinLength + (speed is null ? 0 : 4) + (target is null ? 0 : 8) + (cs2 is null ? 0 : 8)];
        var s = data.AsSpan();
        s[0] = flags;
        BinaryPrimitives.WriteInt64LittleEndian(s[1..], ticks);
        BinaryPrimitives.WriteSingleLittleEndian(s[9..], position.X);
        BinaryPrimitives.WriteSingleLittleEndian(s[13..], position.Y);
        s[17] = DirectionByte(direction);
        var p = MinLength;
        if (speed is { } v)
        {
            BinaryPrimitives.WriteSingleLittleEndian(s[p..], v);
            p += 4;
        }
        if (target is { } t)
        {
            BinaryPrimitives.WriteSingleLittleEndian(s[p..], t.X);
            BinaryPrimitives.WriteSingleLittleEndian(s[(p + 4)..], t.Y);
            p += 8;
        }
        if (cs2 is { } tail) BinaryPrimitives.WriteInt64LittleEndian(s[p..], tail);
        return data;
    }

    /// <summary>
    /// Key 1 as the relay sends it (stage 7 SPEC §2.1): always the speed (a stop is 22 bytes, like most official stops), the
    /// target only when it differs from the position, cs2 (8 bytes) only when the Move had key 5, never cs3.
    /// </summary>
    public static byte[] EncodeSample(long ticks, Vector2 position, float direction, float speed, Vector2 target, long cs2) =>
        Encode(ticks, position, direction, speed, target == position ? null : target, cs2 == 0 ? null : cs2);

    /// <summary>The direction byte: round(deg / 360 · 256) mod 256, as movecodec.dir_to_byte (half to even, degrees taken mod 360).</summary>
    public static byte DirectionByte(float degrees)
    {
        var d = (double)degrees % 360.0;
        if (d < 0) d += 360.0;
        return (byte)((int)Math.Round(d / 360.0 * 256.0, MidpointRounding.ToEven) & 0xFF);
    }

    /// <summary>Event 3 for object <paramref name="objectId"/> with key 1 = <paramref name="payload"/> (key 0 a long, as ciy.am 0x180970aa0 reads it with Convert.ToInt64).</summary>
    public static EventData Event(long objectId, byte[] payload) => new(PhotonEventCode, new() { [0] = objectId, [1] = payload });
}
