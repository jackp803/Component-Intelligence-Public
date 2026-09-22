namespace ComponentIntelligence.Electrical.Domain;

/// <summary>
/// Manufacturing topology only. The implicit sheath split is never an electrical endpoint.
/// Port references identify actual conductor ownership, not an inferred mating-pin mapping.
/// </summary>
public sealed class MultiEndCableTopology
{
    public required string CableInstanceId { get; init; }
    public required string CommonPortId { get; set; }
    public double? TrunkLengthMm { get; set; }
    public List<MultiEndCableBranch> Branches { get; init; } = new();
    public List<string> ConnectionIds { get; init; } = new();
}

public sealed class MultiEndCableBranch
{
    public required string PortId { get; init; }
    public int Index { get; set; }
    public double? LengthMm { get; set; }
}
