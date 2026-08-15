using System.Text;
using System.Text.RegularExpressions;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Resolution;

namespace ComponentIntelligence.Extraction;

public enum DocumentIdentityStatus
{
    NotChecked,
    Confirmed,
    Unconfirmed,
    Mismatch
}

public sealed record DocumentIdentityCheckResult(
    DocumentIdentityStatus Status,
    IReadOnlyList<string> Diagnostics)
{
    public bool IsAccepted => Status is DocumentIdentityStatus.Confirmed or DocumentIdentityStatus.NotChecked;
}

/// <summary>
/// Deterministic document identity gate. Engineering values from a PDF are not accepted for a target
/// component until the expected model is confirmed by document content or reliable manufacturer metadata.
/// </summary>
public sealed class DocumentIdentityChecker
{
    private static readonly Regex CandidateTokenRegex = new(
        @"[A-Za-z0-9][A-Za-z0-9._/\-]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public DocumentIdentityCheckResult Check(
        ComponentIdentity expected,
        ComponentDocument document,
        IEnumerable<string?> extractedTexts)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(extractedTexts);

        var normalizedModel = ModelNormalizer.Normalize(expected.OfficialModel)?.Canonical;
        var expectedKey = CanonicalModelKey(normalizedModel);
        if (string.IsNullOrWhiteSpace(expectedKey))
            return new DocumentIdentityCheckResult(DocumentIdentityStatus.Unconfirmed, ["DOCUMENT_IDENTITY_EXPECTED_MODEL_MISSING"]);

        var texts = extractedTexts.Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray();
        var contentTokens = texts.SelectMany(ExtractCanonicalTokens).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (contentTokens.Contains(expectedKey))
        {
            var diagnostics = new List<string> { $"DOCUMENT_IDENTITY_CONFIRMED_CONTENT:{expectedKey}" };
            if (ContainsManufacturer(texts, expected.OfficialManufacturer))
                diagnostics.Add("DOCUMENT_IDENTITY_MANUFACTURER_SIGNAL_PRESENT");
            return new DocumentIdentityCheckResult(DocumentIdentityStatus.Confirmed, diagnostics);
        }

        var urlPath = Uri.UnescapeDataString(document.Url.AbsolutePath);
        var metadataTokens = ExtractCanonicalTokens(urlPath)
            .Concat(ExtractCanonicalTokens(Path.GetFileNameWithoutExtension(urlPath)))
            .Concat(string.IsNullOrWhiteSpace(document.LocalPath)
                ? Array.Empty<string>()
                : ExtractCanonicalTokens(Path.GetFileNameWithoutExtension(document.LocalPath)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (metadataTokens.Contains(expectedKey) && IsReliableManufacturerMetadata(document.SourceType))
            return new DocumentIdentityCheckResult(DocumentIdentityStatus.Confirmed, [$"DOCUMENT_IDENTITY_CONFIRMED_METADATA:{expectedKey}"]);

        var expectedShape = ModelShape(expectedKey);
        var conflicting = contentTokens
            .Concat(metadataTokens)
            .Where(token => !string.Equals(token, expectedKey, StringComparison.OrdinalIgnoreCase))
            .Where(token => string.Equals(ModelShape(token), expectedShape, StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
        if (conflicting.Length > 0)
            return new DocumentIdentityCheckResult(
                DocumentIdentityStatus.Mismatch,
                [$"DOCUMENT_IDENTITY_MISMATCH:EXPECTED={expectedKey}:FOUND={string.Join(',', conflicting)}"]);

        return new DocumentIdentityCheckResult(
            DocumentIdentityStatus.Unconfirmed,
            [$"DOCUMENT_IDENTITY_UNCONFIRMED:EXPECTED={expectedKey}"]);
    }

    private static IEnumerable<string> ExtractCanonicalTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (Match match in CandidateTokenRegex.Matches(text))
        {
            var key = CanonicalModelKey(match.Value);
            if (key.Length >= 3) yield return key;
        }
    }

    private static string CanonicalModelKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
            if (char.IsLetterOrDigit(character)) builder.Append(char.ToUpperInvariant(character));
        return builder.ToString();
    }

    private static string ModelShape(string modelKey)
    {
        var builder = new StringBuilder(modelKey.Length);
        foreach (var character in modelKey)
            builder.Append(char.IsDigit(character) ? '#' : char.ToUpperInvariant(character));
        return builder.ToString();
    }

    private static bool IsReliableManufacturerMetadata(ComponentSourceType sourceType) => sourceType is
        ComponentSourceType.ManufacturerDatasheet or
        ComponentSourceType.ManufacturerManual or
        ComponentSourceType.ManufacturerDownloadCenter;

    private static bool ContainsManufacturer(IEnumerable<string> texts, string manufacturer)
    {
        var normalized = ManufacturerNormalizer.NormalizeKey(manufacturer);
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        return texts.Any(text => text.Contains(normalized, StringComparison.OrdinalIgnoreCase));
    }
}
