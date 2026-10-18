using System.Collections;
using System.Text;

namespace AlbionLocal.Photon.Serialization;

/// <summary>
/// Photon GpBinaryV18 serializer, matched against the client's Protocol18 (g71):
/// shorts/floats/doubles little-endian, ints/longs as zigzag varints, lengths as varints,
/// bool[] packed LSB-first, Array (0x40) = array of self-typed arrays.
/// </summary>
public static class Protocol18
{
    // ---------------------------------------------------------------- messages

    public static void WriteOperationRequest(ByteWriter w, OperationRequest req)
    {
        w.Byte(req.Code);
        WriteParameterTable(w, req.Parameters);
    }

    public static OperationRequest ReadOperationRequest(ByteReader r)
    {
        var code = r.Byte();
        return new OperationRequest(code, ReadParameterTable(r));
    }

    public static void WriteOperationResponse(ByteWriter w, OperationResponse resp)
    {
        w.Byte(resp.Code);
        w.Int16LE(resp.ReturnCode);
        WriteTyped(w, resp.DebugMessage);
        WriteParameterTable(w, resp.Parameters);
    }

    public static OperationResponse ReadOperationResponse(ByteReader r)
    {
        var code = r.Byte();
        var rc = r.Int16LE();
        var msg = ReadTyped(r) as string;
        return new OperationResponse(code, ReadParameterTable(r)) { ReturnCode = rc, DebugMessage = msg };
    }

    public static void WriteEventData(ByteWriter w, EventData ev)
    {
        w.Byte(ev.Code);
        WriteParameterTable(w, ev.Parameters);
    }

    public static EventData ReadEventData(ByteReader r)
    {
        var code = r.Byte();
        return new EventData(code, ReadParameterTable(r));
    }

    public static void WriteParameterTable(ByteWriter w, Dictionary<byte, object?> table)
    {
        if (table.Count > 255) throw new ArgumentException("more than 255 parameters");
        w.Byte((byte)table.Count);
        foreach (var (k, v) in table)
        {
            w.Byte(k);
            WriteTyped(w, v);
        }
    }

    public static Dictionary<byte, object?> ReadParameterTable(ByteReader r)
    {
        int n = r.Byte();
        var d = new Dictionary<byte, object?>(n);
        for (var i = 0; i < n; i++)
        {
            var k = r.Byte();
            d[k] = ReadTyped(r);
        }
        return d;
    }

    // ---------------------------------------------------------------- write

    /// <summary>Writes a type code followed by the value.</summary>
    public static void WriteTyped(ByteWriter w, object? v)
    {
        switch (v)
        {
            case null: w.Byte((byte)GpType.Null); return;
            case bool b: w.Byte((byte)(b ? GpType.BooleanTrue : GpType.BooleanFalse)); return;
            case byte b when b == 0: w.Byte((byte)GpType.ByteZero); return;
            case short s when s == 0: w.Byte((byte)GpType.ShortZero); return;
            case int i when i == 0: w.Byte((byte)GpType.IntZero); return;
            case long l when l == 0: w.Byte((byte)GpType.LongZero); return;
            case float f when f == 0f: w.Byte((byte)GpType.FloatZero); return;
            case double d when d == 0d: w.Byte((byte)GpType.DoubleZero); return;
            case NarrowInt n: WriteNarrowInt(w, n.Value); return;
        }
        var t = TypeCodeOf(v.GetType());
        w.Byte((byte)t);
        WriteValue(w, t, v);
    }

    /// <summary>Type code used for a CLR type; Unknown means "not representable".</summary>
    public static GpType TypeCodeOf(Type t)
    {
        if (t == typeof(bool)) return GpType.Boolean;
        if (t == typeof(byte)) return GpType.Byte;
        if (t == typeof(short)) return GpType.Short;
        if (t == typeof(int)) return GpType.CompressedInt;
        if (t == typeof(long)) return GpType.CompressedLong;
        if (t == typeof(float)) return GpType.Float;
        if (t == typeof(double)) return GpType.Double;
        if (t == typeof(string)) return GpType.String;
        if (t == typeof(byte[])) return GpType.ByteArray;
        if (t == typeof(bool[])) return GpType.BooleanArray;
        if (t == typeof(short[])) return GpType.ShortArray;
        if (t == typeof(float[])) return GpType.FloatArray;
        if (t == typeof(double[])) return GpType.DoubleArray;
        if (t == typeof(string[])) return GpType.StringArray;
        if (t == typeof(int[])) return GpType.CompressedIntArray;
        if (t == typeof(long[])) return GpType.CompressedLongArray;
        if (t == typeof(object[])) return GpType.ObjectArray;
        if (t == typeof(Hashtable)) return GpType.Hashtable;
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return GpType.Dictionary;
        if (t.IsArray) return GpType.Array;
        throw new NotSupportedException($"Protocol18 cannot serialize {t}");
    }

    /// <summary>Type code allowed in a Dictionary header for a declared key/value type (0 = per-entry types).</summary>
    private static GpType HeaderTypeOf(Type t)
    {
        if (t == typeof(object)) return GpType.Unknown;
        if (t == typeof(byte)) return GpType.Byte;
        if (t == typeof(short)) return GpType.Short;
        if (t == typeof(int)) return GpType.CompressedInt;
        if (t == typeof(long)) return GpType.CompressedLong;
        if (t == typeof(float)) return GpType.Float;
        if (t == typeof(double)) return GpType.Double;
        if (t == typeof(string)) return GpType.String;
        if (t == typeof(bool)) return GpType.Boolean;
        if (t == typeof(byte[]) || t == typeof(int[]) || t == typeof(long[]) || t == typeof(string[]) ||
            t == typeof(bool[]) || t == typeof(short[]) || t == typeof(float[]) || t == typeof(double[]))
            return TypeCodeOf(t);
        return GpType.Unknown;
    }

    private static void WriteValue(ByteWriter w, GpType t, object v)
    {
        switch (t)
        {
            case GpType.Boolean: w.Byte((bool)v ? (byte)1 : (byte)0); break;
            case GpType.Byte: w.Byte((byte)v); break;
            case GpType.Short: w.Int16LE((short)v); break;
            case GpType.CompressedInt: WriteCompressedInt(w, (int)v); break;
            case GpType.CompressedLong: WriteCompressedLong(w, (long)v); break;
            case GpType.Float: w.SingleLE((float)v); break;
            case GpType.Double: w.DoubleLE((double)v); break;
            case GpType.String: WriteString(w, (string)v); break;
            case GpType.ByteArray:
                {
                    var a = (byte[])v;
                    WriteVarUInt(w, (uint)a.Length);
                    w.Bytes(a);
                    break;
                }
            case GpType.BooleanArray: WriteBoolArray(w, (bool[])v); break;
            case GpType.ShortArray:
                {
                    var a = (short[])v;
                    WriteVarUInt(w, (uint)a.Length);
                    foreach (var x in a) w.Int16LE(x);
                    break;
                }
            case GpType.FloatArray:
                {
                    var a = (float[])v;
                    WriteVarUInt(w, (uint)a.Length);
                    foreach (var x in a) w.SingleLE(x);
                    break;
                }
            case GpType.DoubleArray:
                {
                    var a = (double[])v;
                    WriteVarUInt(w, (uint)a.Length);
                    foreach (var x in a) w.DoubleLE(x);
                    break;
                }
            case GpType.StringArray:
                {
                    var a = (string[])v;
                    WriteVarUInt(w, (uint)a.Length);
                    foreach (var x in a) WriteString(w, x ?? "");
                    break;
                }
            case GpType.CompressedIntArray:
                {
                    var a = (int[])v;
                    WriteVarUInt(w, (uint)a.Length);
                    foreach (var x in a) WriteCompressedInt(w, x);
                    break;
                }
            case GpType.CompressedLongArray:
                {
                    var a = (long[])v;
                    WriteVarUInt(w, (uint)a.Length);
                    foreach (var x in a) WriteCompressedLong(w, x);
                    break;
                }
            case GpType.ObjectArray:
                {
                    var a = (object?[])v;
                    WriteVarUInt(w, (uint)a.Length);
                    foreach (var x in a) WriteTyped(w, x);
                    break;
                }
            case GpType.Array:
                {
                    // Array of arrays: every element carries its own type code.
                    var a = (Array)v;
                    WriteVarUInt(w, (uint)a.Length);
                    foreach (var x in a)
                    {
                        if (x is not Array) throw new NotSupportedException($"Array element {x?.GetType()} is not an array");
                        WriteTyped(w, x);
                    }
                    break;
                }
            case GpType.Hashtable:
                {
                    var h = (Hashtable)v;
                    WriteVarUInt(w, (uint)h.Count);
                    foreach (DictionaryEntry e in h)
                    {
                        WriteTyped(w, e.Key);
                        WriteTyped(w, e.Value);
                    }
                    break;
                }
            case GpType.Dictionary: WriteDictionary(w, (IDictionary)v); break;
            default: throw new NotSupportedException($"cannot write {t}");
        }
    }

    private static void WriteDictionary(ByteWriter w, IDictionary d)
    {
        var args = d.GetType().GetGenericArguments();
        var kt = HeaderTypeOf(args[0]);
        var vt = HeaderTypeOf(args[1]);
        w.Byte((byte)kt);
        w.Byte((byte)vt);
        WriteVarUInt(w, (uint)d.Count);
        foreach (DictionaryEntry e in d)
        {
            if (kt == GpType.Unknown) WriteTyped(w, e.Key); else WriteValue(w, kt, e.Key);
            if (vt == GpType.Unknown) WriteTyped(w, e.Value);
            else if (e.Value is null) throw new NotSupportedException("null value in typed dictionary");
            else WriteValue(w, vt, e.Value);
        }
    }

    private static void WriteBoolArray(ByteWriter w, bool[] a)
    {
        WriteVarUInt(w, (uint)a.Length);
        for (var i = 0; i < a.Length; i += 8)
        {
            byte b = 0;
            for (var j = 0; j < 8 && i + j < a.Length; j++)
                if (a[i + j]) b |= (byte)(1 << j);
            w.Byte(b);
        }
    }

    public static void WriteString(ByteWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        WriteVarUInt(w, (uint)bytes.Length);
        w.Bytes(bytes);
    }

    public static void WriteVarUInt(ByteWriter w, uint v)
    {
        while (v >= 0x80)
        {
            w.Byte((byte)(v | 0x80));
            v >>= 7;
        }
        w.Byte((byte)v);
    }

    public static void WriteVarULong(ByteWriter w, ulong v)
    {
        while (v >= 0x80)
        {
            w.Byte((byte)(v | 0x80));
            v >>= 7;
        }
        w.Byte((byte)v);
    }

    public static void WriteCompressedInt(ByteWriter w, int v) => WriteVarUInt(w, (uint)((v << 1) ^ (v >> 31)));

    /// <summary>Type code and value of a <see cref="NarrowInt"/>: the smallest of IntZero, Int1(_), Int2(_), CompressedInt.</summary>
    private static void WriteNarrowInt(ByteWriter w, int v)
    {
        var magnitude = Math.Abs((long)v);
        if (v == 0) w.Byte((byte)GpType.IntZero);
        else if (magnitude <= byte.MaxValue)
        {
            w.Byte((byte)(v > 0 ? GpType.Int1 : GpType.Int1_));
            w.Byte((byte)magnitude);
        }
        else if (magnitude <= ushort.MaxValue)
        {
            w.Byte((byte)(v > 0 ? GpType.Int2 : GpType.Int2_));
            w.Int16LE(unchecked((short)(ushort)magnitude));
        }
        else
        {
            w.Byte((byte)GpType.CompressedInt);
            WriteCompressedInt(w, v);
        }
    }
    public static void WriteCompressedLong(ByteWriter w, long v) => WriteVarULong(w, (ulong)((v << 1) ^ (v >> 63)));

    // ---------------------------------------------------------------- read

    public static object? ReadTyped(ByteReader r) => ReadValue(r, (GpType)r.Byte());

    public static object? ReadValue(ByteReader r, GpType t)
    {
        if ((byte)t >= (byte)GpType.CustomTypeSlim)
            throw new NotSupportedException($"custom type 0x{(byte)t:x2}");
        switch (t)
        {
            case GpType.Unknown:
            case GpType.Null: return null;
            case GpType.Boolean: return r.Byte() != 0;
            case GpType.BooleanFalse: return false;
            case GpType.BooleanTrue: return true;
            case GpType.Byte: return r.Byte();
            case GpType.ByteZero: return (byte)0;
            case GpType.Short: return r.Int16LE();
            case GpType.ShortZero: return (short)0;
            case GpType.Float: return r.SingleLE();
            case GpType.FloatZero: return 0f;
            case GpType.Double: return r.DoubleLE();
            case GpType.DoubleZero: return 0d;
            case GpType.String: return ReadString(r);
            case GpType.CompressedInt: return ReadCompressedInt(r);
            case GpType.Int1: return (int)r.Byte();
            case GpType.Int1_: return -(int)r.Byte();
            case GpType.Int2: return (int)r.UInt16LE();
            case GpType.Int2_: return -(int)r.UInt16LE();
            case GpType.IntZero: return 0;
            case GpType.CompressedLong: return ReadCompressedLong(r);
            case GpType.L1: return (long)r.Byte();
            case GpType.L1_: return -(long)r.Byte();
            case GpType.L2: return (long)r.UInt16LE();
            case GpType.L2_: return -(long)r.UInt16LE();
            case GpType.LongZero: return 0L;
            case GpType.ByteArray: return r.Bytes((int)ReadVarUInt(r));
            case GpType.BooleanArray: return ReadBoolArray(r);
            case GpType.ShortArray: return ReadArray(r, rr => rr.Int16LE());
            case GpType.FloatArray: return ReadArray(r, rr => rr.SingleLE());
            case GpType.DoubleArray: return ReadArray(r, rr => rr.DoubleLE());
            case GpType.StringArray: return ReadArray(r, ReadString);
            case GpType.CompressedIntArray: return ReadArray(r, ReadCompressedInt);
            case GpType.CompressedLongArray: return ReadArray(r, ReadCompressedLong);
            case GpType.ObjectArray: return ReadArray(r, ReadTyped);
            case GpType.Array: return ReadArrayOfArrays(r);
            case GpType.Hashtable:
                {
                    var n = (int)ReadVarUInt(r);
                    var h = new Hashtable(n);
                    for (var i = 0; i < n; i++)
                    {
                        var k = ReadTyped(r) ?? throw new InvalidDataException("null hashtable key");
                        h[k] = ReadTyped(r);
                    }
                    return h;
                }
            case GpType.Dictionary: return ReadDictionary(r);
            case GpType.OperationRequest: return ReadOperationRequest(r);
            case GpType.OperationResponse: return ReadOperationResponse(r);
            case GpType.EventData: return ReadEventData(r);
            default: throw new NotSupportedException($"cannot read {t}");
        }
    }

    private static T[] ReadArray<T>(ByteReader r, Func<ByteReader, T> read)
    {
        var n = (int)ReadVarUInt(r);
        var a = new T[n];
        for (var i = 0; i < n; i++) a[i] = read(r);
        return a;
    }

    private static Array? ReadArrayOfArrays(ByteReader r)
    {
        var n = (int)ReadVarUInt(r);
        Array? result = null;
        for (var i = 0; i < n; i++)
        {
            var e = ReadTyped(r);
            if (e is null) continue;
            result ??= Array.CreateInstance(e.GetType(), n);
            result.SetValue(e, i);
        }
        return result;
    }

    private static bool[] ReadBoolArray(ByteReader r)
    {
        var n = (int)ReadVarUInt(r);
        var a = new bool[n];
        for (var i = 0; i < n; i += 8)
        {
            var b = r.Byte();
            for (var j = 0; j < 8 && i + j < n; j++) a[i + j] = (b & (1 << j)) != 0;
        }
        return a;
    }

    private static readonly Dictionary<GpType, Type> HeaderTypes = new()
    {
        [GpType.Unknown] = typeof(object),
        [GpType.Byte] = typeof(byte),
        [GpType.Short] = typeof(short),
        [GpType.CompressedInt] = typeof(int),
        [GpType.CompressedLong] = typeof(long),
        [GpType.Float] = typeof(float),
        [GpType.Double] = typeof(double),
        [GpType.String] = typeof(string),
        [GpType.Boolean] = typeof(bool),
        [GpType.ByteArray] = typeof(byte[]),
        [GpType.CompressedIntArray] = typeof(int[]),
        [GpType.CompressedLongArray] = typeof(long[]),
        [GpType.StringArray] = typeof(string[]),
        [GpType.BooleanArray] = typeof(bool[]),
        [GpType.ShortArray] = typeof(short[]),
        [GpType.FloatArray] = typeof(float[]),
        [GpType.DoubleArray] = typeof(double[]),
    };

    /// <summary>Consumes nested type info that follows a Dictionary/Array code in a dictionary header.</summary>
    private static void SkipNestedHeader(ByteReader r, GpType t)
    {
        if (t == GpType.Dictionary)
        {
            r.Byte();
            SkipNestedHeader(r, (GpType)r.Byte());
        }
        else if (t == GpType.Array)
        {
            SkipNestedHeader(r, (GpType)r.Byte());
        }
    }

    private static IDictionary ReadDictionary(ByteReader r)
    {
        var kt = (GpType)r.Byte();
        var vt = (GpType)r.Byte();
        SkipNestedHeader(r, vt);
        var kType = HeaderTypes.GetValueOrDefault(kt, typeof(object));
        var vType = HeaderTypes.GetValueOrDefault(vt, typeof(object));
        var d = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(kType, vType))!;
        var n = (int)ReadVarUInt(r);
        for (var i = 0; i < n; i++)
        {
            var k = kt == GpType.Unknown ? ReadTyped(r) : ReadValue(r, kt);
            var v = vt is GpType.Unknown or GpType.Dictionary or GpType.Array ? ReadTyped(r) : ReadValue(r, vt);
            d[k ?? throw new InvalidDataException("null dictionary key")] = v;
        }
        return d;
    }

    public static string ReadString(ByteReader r)
    {
        var n = (int)ReadVarUInt(r);
        return Encoding.UTF8.GetString(r.Bytes(n));
    }

    public static uint ReadVarUInt(ByteReader r)
    {
        uint v = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            var b = r.Byte();
            v |= (uint)(b & 0x7f) << shift;
            if ((b & 0x80) == 0) return v;
        }
        throw new InvalidDataException("varint too long");
    }

    public static ulong ReadVarULong(ByteReader r)
    {
        ulong v = 0;
        for (var shift = 0; shift < 70; shift += 7)
        {
            var b = r.Byte();
            v |= (ulong)(b & 0x7f) << shift;
            if ((b & 0x80) == 0) return v;
        }
        throw new InvalidDataException("varint too long");
    }

    public static int ReadCompressedInt(ByteReader r)
    {
        var u = ReadVarUInt(r);
        return (int)(u >> 1) ^ -(int)(u & 1);
    }

    public static long ReadCompressedLong(ByteReader r)
    {
        var u = ReadVarULong(r);
        return (long)(u >> 1) ^ -(long)(u & 1);
    }
}
