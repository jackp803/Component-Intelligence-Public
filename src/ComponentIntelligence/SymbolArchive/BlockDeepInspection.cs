namespace ComponentIntelligence.SymbolArchive;

public interface IBlockDeepInspector
{
    Task<BlockDeepInspectionResult> InspectAsync(
        BlockArchiveCandidate candidate,
        CancellationToken cancellationToken = default);
}

public sealed record BlockDeepInspectionResult
{
    public DeepInspectionStatus Status { get; init; }
    public BlockDeepInspectionMetadata? Metadata { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
    public string? SourceHashBefore { get; init; }
    public string? SourceHashAfter { get; init; }
    public string? InspectedCopyPath { get; init; }
    public bool SourceIntegrityFailed =>
        !string.IsNullOrWhiteSpace(SourceHashBefore) &&
        !string.IsNullOrWhiteSpace(SourceHashAfter) &&
        !string.Equals(SourceHashBefore, SourceHashAfter, StringComparison.Ordinal);
}
