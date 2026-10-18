using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using AlbionLocal.Photon.Serialization;

namespace AlbionLocal.Server.Protocol;

/// <summary>
/// Converts Albion message objects to Photon parameter tables and back, following the client's
/// field converters (ce, chosen per field type in ce..ctor 0x180747cb0): one key per [Param] field,
/// null/zero/false values are not sent, Guid → byte[16], Guid[] → concatenated bytes, Vector2 →
/// float[2] (arrays flattened). Op code goes in key 253 (short), event code in key 252, request id
/// in key 255. Albion operations use Photon op code 1.
/// Packed forms (all little-endian, one Photon byte[]):
///   byte[][] (ce.f) and Guid[][] (ce.p): [i32 n][n × i32 len][elements: bytes / 16-byte Guids];
///   byte[][][] (ce.e) and Guid[][][] (ce.o): [i32 n], per row [i32 m][m × i32 len], then elements;
///   enum arrays ([Wire(PackedEnum)], ce+c/ce+d/ce+e): same layouts with 1/2/4-byte elements,
///   rank 1 = elements only. An empty nested array is the 4-byte header 00000000 (the setters read
///   the count without a length check), an empty enum[] is byte[0].
/// Integer arrays are sent with their C# element type, never narrowed: most setters widen
/// byte[]/short[]/int[] (ce aa/ab/ac), but c4[] accepts only Int64[].
/// Other rank-3 arrays (a7m[][][], int[][][]) travel as Photon arrays of arrays; an empty middle row
/// is an array of arrays with count 0, which the client's reader g71.h 0x1832b7230 returns as null
/// (it takes the element type from the first element), so values with an empty or null middle row
/// are left out.
/// GameTimeStamp fields go out as Int64 ticks, also those with [Wire(CompressedTime)].
/// </summary>
public static class AlbionSerializer
{
    public const byte PhotonOperationCode = 1;
    public const byte PhotonEventCode = 1;

    /// <summary>
    /// Base of compressed GameTimeStamps (ms since this tick value): the Login response key 251,
    /// which the client stores with ce.e_1/ce.e (0x18074b900/0x18074b5e0, called from ci7.a9 and
    /// LoginInProcessDialog.OnOperationResponse only). Must equal the value sent there,
    /// <see cref="Game.GameTime.StartTicks"/>; Program.cs sets it at startup.
    /// </summary>
    public static long CompressedTimeBase { get; set; }

    private sealed record FieldMap(FieldInfo Field, byte Key, WireKind Wire);

    private static readonly ConcurrentDictionary<Type, FieldMap[]> Maps = new();

    public static readonly IReadOnlyDictionary<OperationCodes, Type> RequestTypes;
    public static readonly IReadOnlyDictionary<OperationCodes, Type> ResponseTypes;
    public static readonly IReadOnlyDictionary<EventCodes, Type> EventTypes;

    static AlbionSerializer()
    {
        var req = new Dictionary<OperationCodes, Type>();
        var resp = new Dictionary<OperationCodes, Type>();
        var ev = new Dictionary<EventCodes, Type>();
        foreach (var t in typeof(AlbionSerializer).Assembly.GetTypes())
        {
            if (t.GetCustomAttribute<AlbionOperationAttribute>() is { } op)
            {
                if (typeof(AlbionRequest).IsAssignableFrom(t)) req.TryAdd(op.Code, t);
                else if (typeof(AlbionResponse).IsAssignableFrom(t)) resp.TryAdd(op.Code, t);
            }
            else if (t.GetCustomAttribute<AlbionEventAttribute>() is { } e)
            {
                ev.TryAdd(e.Code, t);
            }
        }
        RequestTypes = req;
        ResponseTypes = resp;
        EventTypes = ev;
    }

    private static FieldMap[] Map(Type t) => Maps.GetOrAdd(t, static t =>
        t.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(f => (f, p: f.GetCustomAttribute<ParamAttribute>(), w: f.GetCustomAttribute<WireAttribute>()))
            .Where(x => x.p is not null)
            .Select(x => new FieldMap(x.f, x.p!.Key, x.w?.Kind ?? WireKind.Default))
            .OrderBy(x => x.Key)
            .ToArray());

    public static OperationCodes CodeOf(object msg) =>
        msg.GetType().GetCustomAttribute<AlbionOperationAttribute>()?.Code
        ?? throw new ArgumentException($"{msg.GetType().Name} has no operation code");

    public static Dictionary<byte, object?> ToParameters(object msg)
    {
        var d = new Dictionary<byte, object?>();
        foreach (var m in Map(msg.GetType()))
        {
            var v = ToWire(m.Field.GetValue(msg), m.Wire);
            if (v is not null) d[m.Key] = v;
        }
        return d;
    }

    public static OperationResponse BuildResponse(AlbionResponse resp, int? requestId, OperationCodes? code = null)
    {
        var p = ToParameters(resp);
        foreach (var (k, v) in resp.Extra) p[k] = v;
        p[ReservedKeys.OperationCode] = (short)(code ?? CodeOf(resp));
        if (requestId is { } id) p[ReservedKeys.RequestId] = id;
        return new OperationResponse(PhotonOperationCode, p) { ReturnCode = resp.ReturnCode, DebugMessage = resp.DebugMessage };
    }

    /// <summary>Error response without a body (ReturnCode = Rc* value).</summary>
    public static OperationResponse BuildError(OperationCodes code, short returnCode, int? requestId, string? debug = null)
    {
        var p = new Dictionary<byte, object?> { [ReservedKeys.OperationCode] = (short)code };
        if (requestId is { } id) p[ReservedKeys.RequestId] = id;
        return new OperationResponse(PhotonOperationCode, p) { ReturnCode = returnCode, DebugMessage = debug };
    }

    public static EventData BuildEvent(AlbionEvent ev)
    {
        var code = ev.GetType().GetCustomAttribute<AlbionEventAttribute>()?.Code
                   ?? throw new ArgumentException($"{ev.GetType().Name} has no event code");
        var p = ToParameters(ev);
        if (ev.Extra is { } extra)
            foreach (var (k, v) in extra) p[k] = v;
        p[ReservedKeys.EventCode] = (short)code;
        return new EventData(PhotonEventCode, p);
    }

    public static AlbionRequest? ParseRequest(OperationRequest req, out OperationCodes code, out int? requestId)
    {
        code = req.Parameters.GetValueOrDefault(ReservedKeys.OperationCode) is { } c
            ? (OperationCodes)Convert.ToInt16(c)
            : OperationCodes.Unused;
        requestId = req.Parameters.GetValueOrDefault(ReservedKeys.RequestId) is { } id ? Convert.ToInt32(id) : null;
        if (!RequestTypes.TryGetValue(code, out var t)) return null;
        var msg = (AlbionRequest)Activator.CreateInstance(t)!;
        FillFields(msg, req.Parameters);
        return msg;
    }

    public static void FillFields(object msg, Dictionary<byte, object?> parameters)
    {
        foreach (var m in Map(msg.GetType()))
        {
            if (!parameters.TryGetValue(m.Key, out var v) || v is null) continue;
            try { m.Field.SetValue(msg, FromWire(v, m.Field.FieldType, m.Wire)); }
            catch (Exception e)
            {
                throw new InvalidDataException($"{msg.GetType().Name}.{m.Field.Name} (key {m.Key}): cannot convert {v.GetType().Name}: {e.Message}");
            }
        }
    }

    // ------------------------------------------------------------------ to the client

    public static object? ToWire(object? v, WireKind wire = WireKind.Default)
    {
        if (wire == WireKind.PackedEnum && v is Array ea) return Pack(ea, EnumElement(ea));
        return v switch
        {
            null => null,
            bool b => b ? true : null,
            byte x => x == 0 ? null : x,
            short x => x == 0 ? null : x,
            int x => x == 0 ? null : x,
            long x => x == 0 ? null : x,
            float x => x == 0 ? null : x,
            double x => x == 0 ? null : x,
            Guid g => g == Guid.Empty ? null : g.ToByteArray(),
            Guid[] a => a.SelectMany(g => g.ToByteArray()).ToArray(),
            Guid[][] a => Pack(a, GuidElement),          // ce.p 0x18074f1d0
            Guid[][][] a => Pack(a, GuidElement),        // ce.o 0x18074e810
            byte[][] a => Pack(a, ByteElement),          // ce.f 0x18074bd40
            byte[][][] a => Pack(a, ByteElement),        // ce.e 0x18074b100
            Array a when Rank(a.GetType()) == 3 && HasEmptyRow(a) => null, // g71.h 0x1832b7230 reads that row as null
            Vector2 p => p == Vector2.Zero ? null : new[] { p.X, p.Y },
            Vector2[] a => a.SelectMany(p => new[] { p.X, p.Y }).ToArray(),
            _ => v,
        };
    }

    /// <summary>True when a row of a rank-3 array is null or empty (an array of arrays with count 0 on the wire).</summary>
    private static bool HasEmptyRow(Array a)
    {
        foreach (Array? row in a)
            if (row is not { Length: > 0 }) return true;
        return false;
    }

    private sealed record Element(int Size, Action<object, Span<byte>> Write, Func<ReadOnlySpan<byte>, Type, object> Read);

    private static readonly Element ByteElement = new(1, (v, s) => s[0] = (byte)v, (s, _) => s[0]);
    private static readonly Element GuidElement = new(16, (v, s) => ((Guid)v).TryWriteBytes(s), (s, _) => new Guid(s));

    /// <summary>Enum element by the C# (= underlying) element type: 1, 2 or 4 bytes (8-byte enums use 4, as ce+c.e).</summary>
    private static Element EnumElement(Array a) => EnumElement(LeafType(a.GetType()));

    private static Element EnumElement(Type leaf)
    {
        var size = leaf == typeof(byte) || leaf == typeof(sbyte) ? 1 : leaf == typeof(short) || leaf == typeof(ushort) ? 2 : 4;
        return new Element(size,
            (v, s) =>
            {
                var x = Convert.ToInt64(v);
                if (size == 1) s[0] = (byte)x;
                else if (size == 2) BinaryPrimitives.WriteInt16LittleEndian(s, (short)x);
                else BinaryPrimitives.WriteInt32LittleEndian(s, (int)x);
            },
            (s, t) =>
            {
                long x = size == 1 ? s[0] : size == 2 ? BinaryPrimitives.ReadInt16LittleEndian(s) : BinaryPrimitives.ReadInt32LittleEndian(s);
                return Convert.ChangeType(x, t);
            });
    }

    private static Type LeafType(Type t)
    {
        while (t.IsArray) t = t.GetElementType()!;
        return t;
    }

    private static int Rank(Type t)
    {
        var r = 0;
        for (; t.IsArray; t = t.GetElementType()!) r++;
        return r;
    }

    /// <summary>Rank 1: elements; rank 2: [i32 n][n × i32 len] + elements; rank 3: [i32 n]{[i32 m][m × i32 len]} + elements.</summary>
    private static byte[] Pack(Array a, Element el)
    {
        var rank = Rank(a.GetType());
        var header = new List<int>();
        var leaves = new List<object>();
        if (rank == 1) Collect(a, leaves);
        else
        {
            header.Add(a.Length);
            foreach (Array? row in a)
            {
                if (rank == 2) { header.Add(row?.Length ?? 0); if (row is not null) Collect(row, leaves); }
                else
                {
                    header.Add(row?.Length ?? 0);
                    if (row is null) continue;
                    foreach (Array? cell in row) { header.Add(cell?.Length ?? 0); if (cell is not null) Collect(cell, leaves); }
                }
            }
        }
        var bytes = new byte[header.Count * 4 + leaves.Count * el.Size];
        var span = bytes.AsSpan();
        for (var i = 0; i < header.Count; i++) BinaryPrimitives.WriteInt32LittleEndian(span[(i * 4)..], header[i]);
        var pos = header.Count * 4;
        foreach (var x in leaves) { el.Write(x, span.Slice(pos, el.Size)); pos += el.Size; }
        return bytes;
    }

    private static void Collect(Array a, List<object> leaves)
    {
        foreach (var x in a) leaves.Add(x!);
    }

    // ------------------------------------------------------------------ from the client

    public static object? FromWire(object v, Type t, WireKind wire = WireKind.Default)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        if (wire == WireKind.PackedEnum && u.IsArray && v is byte[] eb) return Unpack(eb, u, EnumElement(LeafType(u)));
        // ce.n getter 0x18074e4f0: Int64 ticks, or ms since the Login key 251 base narrowed to Byte/Int16/Int32;
        // ce.m getter 0x18074e240: Int64[] ticks or Int32[] ms. Any narrower integer form is read as ms.
        if (wire == WireKind.CompressedTime)
        {
            if (u == typeof(long) && IsNarrowInteger(v.GetType())) return FromCompressed(v);
            if (u == typeof(long[]) && v is Array ms && IsNarrowInteger(ms.GetType().GetElementType()!))
            {
                var ticks = new long[ms.Length];
                for (var i = 0; i < ticks.Length; i++) ticks[i] = FromCompressed(ms.GetValue(i)!);
                return ticks;
            }
        }
        if ((u == typeof(byte[][]) || u == typeof(byte[][][])) && v is byte[] bb) return Unpack(bb, u, ByteElement);
        if ((u == typeof(Guid[][]) || u == typeof(Guid[][][])) && v is byte[] gb2) return Unpack(gb2, u, GuidElement);
        if (t == typeof(object) || t.IsInstanceOfType(v)) return v;
        if (u == typeof(Guid) && v is byte[] gb) return new Guid(gb);
        if (u == typeof(Guid[]) && v is byte[] gab)
            return Enumerable.Range(0, gab.Length / 16).Select(i => new Guid(gab.AsSpan(i * 16, 16))).ToArray();
        if (u == typeof(Vector2) && v is float[] f2) return new Vector2(f2[0], f2[1]);
        if (u == typeof(Vector2[]) && v is float[] fa)
            return Enumerable.Range(0, fa.Length / 2).Select(i => new Vector2(fa[2 * i], fa[2 * i + 1])).ToArray();
        if (u.IsArray && v is Array src)
        {
            var et = u.GetElementType()!;
            var dst = Array.CreateInstance(et, src.Length);
            for (var i = 0; i < src.Length; i++)
                dst.SetValue(src.GetValue(i) is { } e ? FromWire(e, et) : null, i);
            return dst;
        }
        if (u.IsPrimitive && v is IConvertible) return Convert.ChangeType(v, u);
        throw new InvalidCastException($"{v.GetType().Name} -> {t.Name}");
    }

    /// <summary>Integer types narrower than Int64 (a compressed GameTimeStamp in ms, never ticks).</summary>
    private static bool IsNarrowInteger(Type t) =>
        t == typeof(byte) || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort) || t == typeof(int) || t == typeof(uint);

    /// <summary>Compressed GameTimeStamp (ms since <see cref="CompressedTimeBase"/>) → ticks, as the ce.n/ce.m setters do.</summary>
    private static long FromCompressed(object ms) => CompressedTimeBase + Convert.ToInt64(ms) * TimeSpan.TicksPerMillisecond;

    private static Array Unpack(byte[] b, Type arrayType, Element el)
    {
        var rank = Rank(arrayType);
        var leaf = LeafType(arrayType);
        var pos = 0;
        int Int() { var x = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(pos)); pos += 4; return x; }
        Array Row(int n)
        {
            var row = Array.CreateInstance(leaf, n);
            for (var i = 0; i < n; i++) { row.SetValue(el.Read(b.AsSpan(pos, el.Size), leaf), i); pos += el.Size; }
            return row;
        }
        if (rank == 1) return Row(b.Length / el.Size);
        var n = Int();
        if (rank == 2)
        {
            var lens = Enumerable.Range(0, n).Select(_ => Int()).ToArray();
            var outer = Array.CreateInstance(leaf.MakeArrayType(), n);
            for (var i = 0; i < n; i++) outer.SetValue(Row(lens[i]), i);
            return outer;
        }
        var shape = new int[n][];
        for (var i = 0; i < n; i++) { var m = Int(); shape[i] = Enumerable.Range(0, m).Select(_ => Int()).ToArray(); }
        var top = Array.CreateInstance(leaf.MakeArrayType().MakeArrayType(), n);
        for (var i = 0; i < n; i++)
        {
            var mid = Array.CreateInstance(leaf.MakeArrayType(), shape[i].Length);
            for (var j = 0; j < shape[i].Length; j++) mid.SetValue(Row(shape[i][j]), j);
            top.SetValue(mid, i);
        }
        return top;
    }
}
