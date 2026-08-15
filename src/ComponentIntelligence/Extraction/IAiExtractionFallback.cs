namespace ComponentIntelligence.Extraction;

/// <summary>
/// Boundary only. The deterministic v0.1 implementation never calls a model.
/// Future AI/Vision adapters must return inferred data and can never promote it to VERIFIED.
/// </summary>
public interface IAiExtractionFallback
{
    Task<AiExtractionResult> ExtractAsync(string localPath, CancellationToken cancellationToken = default);
}

public sealed record AiExtractionResult(bool IsAvailable, IReadOnlyDictionary<string, string?> Values, string? Message = null);

public sealed class DisabledAiExtractionFallback : IAiExtractionFallback
{
    public Task<AiExtractionResult> ExtractAsync(string localPath, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AiExtractionResult(false, new Dictionary<string, string?>(), "AI extraction is intentionally disabled in the deterministic build."));
}
