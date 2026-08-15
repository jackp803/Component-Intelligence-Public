namespace ComponentIntelligence.Contracts;

public sealed record Evidence
{
    public required ComponentSourceType SourceType { get; init; }
    public Uri? SourceUrl { get; init; }
    public Uri? DocumentUrl { get; init; }
    public string? DocumentHashSha256 { get; init; }
    public int? PageNumber { get; init; }
    public required ExtractionMethod ExtractionMethod { get; init; }
    public string? RawValue { get; init; }
    public required DateTimeOffset RetrievedAt { get; init; }
    public required VerificationStatus VerificationStatus { get; init; }
}

public sealed record RawSpecification
{
    public required string RawName { get; init; }
    public string? Section { get; init; }
    public string? RawValue { get; init; }
    public string? ProposedKey { get; init; }
    public VerificationStatus Status { get; init; } = VerificationStatus.SingleSource;
    public IReadOnlyList<Evidence> Evidence { get; init; } = Array.Empty<Evidence>();
}
