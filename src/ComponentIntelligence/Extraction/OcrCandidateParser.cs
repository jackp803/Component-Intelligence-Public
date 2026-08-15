using System.Text.RegularExpressions;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Extraction;

/// <summary>
/// Converts OCR text into candidate specifications. OCR candidates are always marked Inferred and
/// remain reviewable evidence. In particular, pin/contact rows are preserved as candidates but are
/// not promoted to formal ComponentPin facts by PinoutExtractor until a non-OCR or user-confirmed
/// source exists.
/// </summary>
public sealed class OcrCandidateParser
{
    private static readonly Regex ExplicitPair = new(
        @"^(?<label>[A-Za-z][A-Za-z0-9 /+()._%°-]{2,80})\s*[:：=]\s*(?<value>\S.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SpacedPair = new(
        @"^(?<label>[A-Za-z][A-Za-z0-9 /+()._%°-]{2,80})\s{2,}(?<value>\S.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PinLine = new(
        @"^(?:(?:pin|contact|terminal|pole)\s*(?:no\.?|number|#)?\s*)?(?<pin>\d{1,3})\s*(?:[:：=|-]\s*)?(?<value>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] ElectricalHints =
    [
        "l+", "l-", "c/q", "+24", "24v", "24 v", "0v", "0 v", "gnd", "sg", "pe", "fe", "shield",
        "rs485", "rs-485", "a+", "b-", "rx", "tx", "di", "do", "ai", "ao", "input", "output",
        "4-20", "4...20", "0-10", "0...10", "io-link", "iolink", "ethernet", "supply", "power"
    ];

    public IReadOnlyList<RawSpecification> Parse(
        string text,
        Uri documentUrl,
        int pageNumber,
        string documentHash,
        ComponentSourceType sourceType)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<RawSpecification>();

        var results = new List<RawSpecification>();
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = Clean(rawLine);
            if (line.Length < 3) continue;

            var pinMatch = PinLine.Match(line);
            if (pinMatch.Success && LooksElectrical(pinMatch.Groups["value"].Value))
            {
                Add(
                    results,
                    $"OCR pinout candidate / page {pageNumber}",
                    pinMatch.Groups["pin"].Value,
                    Clean(pinMatch.Groups["value"].Value),
                    null,
                    documentUrl,
                    documentHash,
                    pageNumber,
                    sourceType);
                continue;
            }

            var pair = ExplicitPair.Match(line);
            if (!pair.Success) pair = SpacedPair.Match(line);
            if (!pair.Success) continue;

            var label = Clean(pair.Groups["label"].Value);
            var value = Clean(pair.Groups["value"].Value);
            if (label.Length == 0 || value.Length == 0 || value.Length > 300) continue;
            var key = SpecificationDictionary.Map("OCR candidate", label);
            if (key is null && !LooksEngineeringValue(value)) continue;

            Add(
                results,
                $"OCR candidate / page {pageNumber}",
                label,
                value,
                key,
                documentUrl,
                documentHash,
                pageNumber,
                sourceType);
        }

        return results
            .GroupBy(item => $"{item.ProposedKey}\u001f{item.RawName}\u001f{item.RawValue}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First() with
            {
                Evidence = group.SelectMany(item => item.Evidence).Distinct().ToArray()
            })
            .ToArray();
    }

    private static void Add(
        List<RawSpecification> results,
        string section,
        string label,
        string value,
        string? key,
        Uri documentUrl,
        string documentHash,
        int pageNumber,
        ComponentSourceType sourceType)
    {
        var evidence = new Evidence
        {
            SourceType = sourceType,
            SourceUrl = documentUrl,
            DocumentUrl = documentUrl,
            DocumentHashSha256 = documentHash,
            PageNumber = pageNumber,
            ExtractionMethod = ExtractionMethod.OcrText,
            RawValue = value,
            RetrievedAt = DateTimeOffset.UtcNow,
            VerificationStatus = VerificationStatus.Inferred
        };
        results.Add(new RawSpecification
        {
            RawName = label,
            Section = section,
            RawValue = value,
            ProposedKey = key,
            Status = VerificationStatus.Inferred,
            Evidence = [evidence]
        });
    }

    private static bool LooksElectrical(string value)
    {
        var lower = value.ToLowerInvariant();
        return ElectricalHints.Any(hint => lower.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksEngineeringValue(string value) =>
        Regex.IsMatch(value, @"\d", RegexOptions.CultureInvariant) || LooksElectrical(value);

    private static string Clean(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}
