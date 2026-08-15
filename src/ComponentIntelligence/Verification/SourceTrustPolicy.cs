using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Verification;

/// <summary>
/// Single authoritative trust ordering for engineering evidence.
/// A lower-trust source may fill an unknown field, but must not silently override an existing
/// higher-trust manufacturer fact. Conflicting values remain available to VerificationEngine.
/// Extraction quality is part of trust: OCR is useful fallback evidence, but native structured/text
/// extraction from the same source is preferred because OCR can introduce transcription errors.
/// </summary>
public static class SourceTrustPolicy
{
    public static int Score(ComponentSourceType sourceType) => sourceType switch
    {
        ComponentSourceType.ManufacturerDatasheet => 100,
        ComponentSourceType.ManufacturerManual => 95,
        ComponentSourceType.ManufacturerProductPage => 90,
        ComponentSourceType.ManufacturerDownloadCenter => 85,
        ComponentSourceType.User => 80,
        ComponentSourceType.AuthorizedDistributor => 70,
        ComponentSourceType.TrustedThirdParty => 55,
        ComponentSourceType.GenericWeb => 30,
        ComponentSourceType.AiInference => 10,
        _ => 0
    };

    public static int Score(Evidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var score = Score(evidence.SourceType);
        score += evidence.ExtractionMethod switch
        {
            ExtractionMethod.OcrText => -12,
            ExtractionMethod.AiText or ExtractionMethod.AiVision => -20,
            _ => 0
        };
        return Math.Max(0, score);
    }

    public static int Score(IEnumerable<Evidence> evidence) =>
        evidence.Select(Score).DefaultIfEmpty(0).Max();

    public static RawSpecification? BestSpecification(IEnumerable<RawSpecification> specifications, string key) =>
        specifications
            .Where(spec => string.Equals(spec.ProposedKey, key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(spec.RawValue))
            .OrderByDescending(spec => Score(spec.Evidence))
            .ThenByDescending(spec => spec.Status is VerificationStatus.Verified or VerificationStatus.UserConfirmed)
            .ThenByDescending(spec => spec.Evidence.Count)
            .FirstOrDefault();
}
