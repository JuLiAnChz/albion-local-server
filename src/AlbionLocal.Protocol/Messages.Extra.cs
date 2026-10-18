#nullable enable
namespace AlbionLocal.Server.Protocol;

// Messages the generator (analysis/tools/gen_protocol_cs.py) misses because the Inspector found no Param attribute on them; written by hand
// from the cpp2il class layouts. Keep the generated naming (client field names).

/// <summary>
/// FarmableHarvest request (client class <c>sr</c>, chain sr &lt; x6 &lt; er; stageH islands SPEC §3.5): one long field, the crop's object id
/// (cpp2il; protocol.json lists the class without its Param).
/// </summary>
[AlbionOperation(OperationCodes.FarmableHarvest)]
public sealed partial class FarmableHarvestRequest : AlbionRequest
{
    /// <summary>long (sr @0x10): the crop (farmable) object id.</summary>
    [Param(0)] public long k9;
}
