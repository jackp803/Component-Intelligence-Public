using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Extraction;

public sealed record CrossChannelReconciliationResult(
    IReadOnlyList<RawSpecification> Specifications,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Reconciles Native PDF text, table reconstruction and OCR from the same document.
/// Agreement across independent extraction channels raises confidence. Conflicting values remain visible,
/// while existing evidence trust ensures OCR cannot silently replace stronger native/table evidence.
/// </summary>
public sealed class CrossChannelSpecificationReconciler
{
    public CrossChannelReconciliationResult Reconcile(IEnumerable<RawSpecification> specifications)
    {
        ArgumentNullException.ThrowIfNull(specifications);
        var input = specifications.ToArray();
        var diagnostics = new List<string>();
        var output = new List<RawSpecification>();

        foreach (var group in input.GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase))
        {
            var values = group
                .Where(item => !string.IsNullOrWhiteSpace(item.RawValue))
                .GroupBy(item => NormalizeValue(item.RawValue), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (values.Length <= 1)
            {
                var merged = MergeEquivalent(group);
                var channelCount = merged.Evidence
                    .Select(evidence => evidence.ExtractionMethod)
                    .Where(IsDocumentExtractionChannel)
                    .Distinct()
                    .Count();
                if (channelCount >= 2)
                {
                    merged = merged with
                    {
                        Status = VerificationStatus.Verified,
                        Evidence = merged.Evidence.Select(evidence => evidence with
                        {
                            VerificationStatus = VerificationStatus.Verified
                        }).ToArray()
                    };
                    diagnostics.Add($"CROSS_CHANNEL_AGREEMENT:{FieldKey(merged)}:CHANNELS={channelCount}");
                }
                output.Add(merged);
                continue;
            }

            var conflicting = values.Select(valueGroup => MergeEquivalent(valueGroup) with
            {
                Status = VerificationStatus.Conflict,
                Evidence = valueGroup
                    .SelectMany(item => item.Evidence)
                    .Distinct()
                    .Select(evidence => evidence with { VerificationStatus = VerificationStatus.Conflict })
                    .ToArray()
            }).ToArray();
            output.AddRange(conflicting);
            diagnostics.Add($"CROSS_CHANNEL_CONFLICT:{FieldKey(conflicting[0])}:VALUES={string.Join('|', conflicting.Select(item => item.RawValue))}");
        }

        return new CrossChannelReconciliationResult(output, diagnostics);
    }

    private static RawSpecification MergeEquivalent(IEnumerable<RawSpecification> specifications)
    {
        var items = specifications.ToArray();
        var preferred = items
            .OrderByDescending(item => StatusRank(item.Status))
            .ThenByDescending(item => item.Evidence.Select(EvidenceChannelRank).DefaultIfEmpty(0).Max())
            .First();
        var evidence = items.SelectMany(item => item.Evidence).Distinct().ToArray();
        return preferred with { Evidence = evidence };
    }

    private static string GroupKey(RawSpecification specification)
    {
        var evidence = specification.Evidence.FirstOrDefault();
        var documentKey = evidence?.DocumentHashSha256
                          ?? evidence?.DocumentUrl?.AbsoluteUri
                          ?? evidence?.SourceUrl?.AbsoluteUri
                          ?? "NO_DOCUMENT";
        return $"{documentKey}\u001f{specification.Section}\u001f{FieldKey(specification)}";
    }

    private static string FieldKey(RawSpecification specification) =>
        string.IsNullOrWhiteSpace(specification.ProposedKey)
            ? NormalizeFieldName(specification.RawName)
            : specification.ProposedKey.Trim();

    private static string NormalizeFieldName(string value) =>
        string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static string NormalizeValue(string? value) =>
        string.Join(' ', (value ?? string.Empty)
            .Replace('…', '.')
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        .Replace("...", "..", StringComparison.Ordinal)
        .ToUpperInvariant();

    private static bool IsDocumentExtractionChannel(ExtractionMethod method) => method is
        ExtractionMethod.PdfText or
        ExtractionMethod.TableParser or
        ExtractionMethod.OcrText;

    private static int EvidenceChannelRank(Evidence evidence) => evidence.ExtractionMethod switch
    {
        ExtractionMethod.TableParser => 30,
        ExtractionMethod.PdfText => 30,
        ExtractionMethod.OcrText => 10,
        ExtractionMethod.AiText or ExtractionMethod.AiVision => 0,
        _ => 20
    };

    private static int StatusRank(VerificationStatus status) => status switch
    {
        VerificationStatus.UserConfirmed => 6,
        VerificationStatus.Verified => 5,
        VerificationStatus.SingleSource => 4,
        VerificationStatus.Inferred => 2,
        VerificationStatus.Conflict => 1,
        _ => 0
    };
}
