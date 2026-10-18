namespace AlbionLocal.Server.Protocol;

/// <summary>Photon parameter key of a message field (client: position in the c0 field list).</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class ParamAttribute(byte key) : Attribute
{
    public byte Key { get; } = key;
}

/// <summary>
/// Wire form of a field when its C# type alone does not determine it. The client picks a converter
/// per field type in ce..ctor 0x180747cb0; these are the cases where the generated C# type (the
/// enum's underlying integer, long for GameTimeStamp) is not what travels.
/// </summary>
public enum WireKind : byte
{
    Default = 0,

    /// <summary>
    /// Client enum array (rank 1–3), generated as the underlying integer array. Sent as one byte[]:
    /// rank 1 = elements little-endian, 1/2/4 bytes each by underlying size (8-byte enums use 4,
    /// Convert.ToInt32) — ce+c.c/d/e 0x180747490/0x1807475d0/0x180747a80; rank 2 = [i32 n][n × i32 len]
    /// then the elements (ce+d); rank 3 = [i32 n] then per row [i32 m][m × i32 len], then the elements (ce+e).
    /// </summary>
    PackedEnum,

    /// <summary>
    /// GameTimeStamp (or GameTimeStamp[]) with the client's [eo] attribute. The client sends it as
    /// milliseconds since the Login key 251 base when it fits (ce.n getter 0x18074e4f0: Byte/Int16/Int32;
    /// ce.m getter 0x18074e240: Int32[]), else as Int64 ticks. Server → client both forms are accepted
    /// (ce.n setter 0x18074e6d0: Int64 = ticks, any other integer = ms; ce.m setter 0x18074dfb0: Int64[]
    /// or Int32[]). We send ticks, so the field value is always ticks (GameTime.Now).
    /// </summary>
    CompressedTime,
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class WireAttribute(WireKind kind) : Attribute
{
    public WireKind Kind { get; } = kind;
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class AlbionOperationAttribute(OperationCodes code) : Attribute
{
    public OperationCodes Code { get; } = code;
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class AlbionEventAttribute(EventCodes code) : Attribute
{
    public EventCodes Code { get; } = code;
}

/// <summary>Client → server operation (client base class x6).</summary>
public abstract class AlbionRequest;

/// <summary>Server → client operation response (client base class ah0).</summary>
public abstract class AlbionResponse
{
    /// <summary>Photon ReturnCode (ah0.bgm); 0 = success, otherwise an Rc* enum value.</summary>
    public short ReturnCode;

    /// <summary>Photon DebugMessage (ah0.bgn).</summary>
    public string? DebugMessage;

    /// <summary>Parameters outside the message class, e.g. key 251 (server time) on Login.</summary>
    public Dictionary<byte, object?> Extra { get; } = new();
}

/// <summary>Server → client event (client base class any).</summary>
public abstract class AlbionEvent
{
    /// <summary>
    /// Parameters sent as they are, over the [Param] fields (null = none): a value the official server always sends even
    /// when it is zero, which the field would leave out (NewBuilding key 29, byte 0; SPEC stage 2 §3).
    /// </summary>
    public Dictionary<byte, object?>? Extra { get; set; }
}

/// <summary>Reserved parameter keys (client class e0 constants).</summary>
public static class ReservedKeys
{
    /// <summary>
    /// Server time (GameTimeStamp ticks, long); required on the Login response (ci7.a9), where it is
    /// also the base of compressed GameTimeStamps (<see cref="AlbionSerializer.CompressedTimeBase"/>).
    /// </summary>
    public const byte ServerTime = 251;
    public const byte EventCode = 252;
    public const byte OperationCode = 253;
    public const byte RequestId = 255;
}

/// <summary>Response without a body (client base class ah0), for operations without a response class.</summary>
public sealed class GenericResponse : AlbionResponse;
