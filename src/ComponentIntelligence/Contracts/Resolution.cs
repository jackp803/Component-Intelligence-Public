namespace ComponentIntelligence.Contracts;

public sealed record IdentityMatchResult
{
    public required ResolutionStatus Status { get; init; }
    public required MatchLevel MatchLevel { get; init; }
    public ComponentIdentity? Identity { get; init; }
    public ComponentCandidate? Candidate { get; init; }
    public IReadOnlyList<Evidence> Evidence { get; init; } = Array.Empty<Evidence>();
}
