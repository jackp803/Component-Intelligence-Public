namespace ComponentIntelligence.Contracts;

public sealed record ComponentIdentityQuery
{
    public string? RawManufacturer { get; init; }
    public string? RawModel { get; init; }
    public string? NormalizedManufacturer { get; init; }
    public string? NormalizedModel { get; init; }
    public string? SearchKey { get; init; }
}

public sealed record ComponentIdentity
{
    public required string OfficialManufacturer { get; init; }
    public required string OfficialModel { get; init; }
    public string? Mpn { get; init; }
    public Uri? OfficialProductUrl { get; init; }

    public string Manufacturer => OfficialManufacturer;
}

public sealed record ComponentCandidate
{
    public required string Manufacturer { get; init; }
    public required string OfficialModel { get; init; }
    public string? Mpn { get; init; }
    public required ComponentSourceType SourceType { get; init; }
    public Uri? ProductUrl { get; init; }
    public string? RawSourceTitle { get; init; }
    public IReadOnlyList<Evidence> Evidence { get; init; } = Array.Empty<Evidence>();
}

public sealed record ResolutionResult
{
    public required ResolutionStatus Status { get; init; }
    public required MatchLevel MatchLevel { get; init; }
    public required ComponentIdentityQuery Input { get; init; }
    public ComponentIdentity? ResolvedIdentity { get; init; }
    public IReadOnlyList<ComponentCandidate> Candidates { get; init; } = Array.Empty<ComponentCandidate>();
    public IReadOnlyList<Evidence> Evidence { get; init; } = Array.Empty<Evidence>();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}
