namespace ComponentIntelligence.Electrical.Domain;

/// <summary>
/// Copy-only electrical-domain representation of explicit component conversion evidence. It carries
/// identities and provenance but deliberately defines no graph edge, reachability, ordering, or DAG.
/// </summary>
public sealed class PowerConversionEvidence
{
    public string? ConversionId { get; init; }
    public required string ComponentInstanceId { get; init; }
    public string? InputPowerDomainId { get; init; }
    public string? OutputPowerDomainId { get; init; }
    public List<string> InputSourcePortIds { get; init; } = new();
    public List<string> InputSourcePinIds { get; init; } = new();
    public List<string> OutputSourcePortIds { get; init; } = new();
    public List<string> OutputSourcePinIds { get; init; } = new();
    public List<PowerEvidenceProvenance> Evidence { get; init; } = new();
}

/// <summary>
/// Typed provenance copied from source Evidence. Strings preserve source enum values without making
/// the Electrical domain depend on extraction-policy behavior.
/// </summary>
public sealed record PowerEvidenceProvenance
{
    public required string SourceType { get; init; }
    public string? SourceUrl { get; init; }
    public string? DocumentUrl { get; init; }
    public string? DocumentHashSha256 { get; init; }
    public int? PageNumber { get; init; }
    public required string ExtractionMethod { get; init; }
    public string? RawValue { get; init; }
    public required DateTimeOffset RetrievedAt { get; init; }
    public required string VerificationStatus { get; init; }
}
