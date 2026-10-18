namespace AlbionLocal.Photon.Serialization;

/// <summary>Photon operation request: one code byte plus a parameter table.</summary>
public sealed class OperationRequest(byte code, Dictionary<byte, object?>? parameters = null)
{
    public byte Code { get; } = code;
    public Dictionary<byte, object?> Parameters { get; } = parameters ?? new();
    public override string ToString() => $"OperationRequest(code {Code}, {Parameters.Count} params)";
}

/// <summary>Photon operation response. ReturnCode/DebugMessage travel in the header, not the parameter table.</summary>
public sealed class OperationResponse(byte code, Dictionary<byte, object?>? parameters = null)
{
    public byte Code { get; } = code;
    public short ReturnCode { get; set; }
    public string? DebugMessage { get; set; }
    public Dictionary<byte, object?> Parameters { get; } = parameters ?? new();
    public override string ToString() => $"OperationResponse(code {Code}, rc {ReturnCode}, {Parameters.Count} params)";
}

/// <summary>Photon event: one code byte plus a parameter table.</summary>
public sealed class EventData(byte code, Dictionary<byte, object?>? parameters = null)
{
    public byte Code { get; } = code;
    public Dictionary<byte, object?> Parameters { get; } = parameters ?? new();
    public override string ToString() => $"EventData(code {Code}, {Parameters.Count} params)";
}
