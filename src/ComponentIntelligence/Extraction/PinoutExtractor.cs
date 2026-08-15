using System.Text.RegularExpressions;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Extraction;

/// <summary>
/// Converts explicit textual pin/contact assignment rows into ComponentPin facts.
/// It never invents missing pin functions. Numeric table rows are accepted only when the section
/// or value clearly looks like electrical connector/pinout information.
/// OCR-only pin rows remain review candidates because a single OCR character error can change a pin
/// number or function. They are promoted only after non-OCR evidence or explicit user confirmation.
/// </summary>
public sealed class PinoutExtractor
{
    private static readonly Regex PinLabel = new(@"^(?:(?:pin|contact|terminal|pole)\s*(?:no\.?|number|#)?\s*)?(?<pin>\d{1,3})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] SectionHints = ["pin", "wiring", "connection", "connector", "contact", "assignment", "electrical", "terminal", "接線", "接头", "接頭", "腳位", "脚位", "端子"];
    private static readonly string[] FunctionHints =
    [
        "l+", "l-", "c/q", "+24", "24 v", "24v", "0 v", "0v", "gnd", "sg", "pe", "fe", "shield",
        "rs485", "rs-485", "a+", "b-", "rx", "tx", "di", "do", "ai", "ao", "input", "output",
        "4-20", "4...20", "0-10", "0...10", "io-link", "iolink", "ethernet"
    ];

    public IReadOnlyList<ComponentPin> Extract(IEnumerable<RawSpecification> specifications)
    {
        var candidates = new List<ComponentPin>();
        foreach (var spec in specifications)
        {
            var label = Clean(spec.RawName);
            var value = Clean(spec.RawValue);
            var match = PinLabel.Match(label);
            if (!match.Success || value.Length == 0) continue;

            var ocrOnly = spec.Evidence.Count > 0 && spec.Evidence.All(evidence => evidence.ExtractionMethod == ExtractionMethod.OcrText);
            var userConfirmed = spec.Status == VerificationStatus.UserConfirmed ||
                                spec.Evidence.Any(evidence => evidence.VerificationStatus == VerificationStatus.UserConfirmed);
            if (ocrOnly && !userConfirmed) continue;

            var section = Clean(spec.Section).ToLowerInvariant();
            var lowerValue = value.ToLowerInvariant();
            var sectionLooksElectrical = SectionHints.Any(hint => section.Contains(hint, StringComparison.OrdinalIgnoreCase));
            var valueLooksElectrical = FunctionHints.Any(hint => lowerValue.Contains(hint, StringComparison.OrdinalIgnoreCase));
            if (!sectionLooksElectrical && !valueLooksElectrical) continue;

            candidates.Add(new ComponentPin
            {
                PinNumber = match.Groups["pin"].Value,
                Function = value,
                SignalType = InferSignalType(value),
                Direction = InferDirection(value),
                VoltageDomain = InferVoltageDomain(value),
                Description = string.IsNullOrWhiteSpace(spec.Section) ? "Pin assignment extracted from engineering table." : $"Pin assignment extracted from: {spec.Section}",
                Evidence = spec.Evidence
            });
        }

        return candidates
            .GroupBy(pin => pin.PinNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var functions = group.Select(pin => pin.Function).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                return first with
                {
                    Function = functions.Length == 1 ? functions[0] : string.Join(" | ", functions),
                    Evidence = group.SelectMany(pin => pin.Evidence).Distinct().ToArray()
                };
            })
            .OrderBy(pin => int.TryParse(pin.PinNumber, out var number) ? number : int.MaxValue)
            .ThenBy(pin => pin.PinNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? InferSignalType(string value)
    {
        var text = value.ToUpperInvariant();
        if (ContainsAny(text, "IO-LINK", "IOLINK", "C/Q", "RS485", "RS-485", "ETHERNET", "RX", "TX")) return "Communication";
        if (ContainsAny(text, "4-20", "4...20", "0-10", "0...10", " AI", "AO ", "ANALOG")) return "Analog";
        if (ContainsAny(text, "DI", "DO", "DIGITAL")) return "Digital";
        if (ContainsAny(text, "L+", "L-", "+24", "24V", "24 V", "0V", "0 V", "POWER", "SUPPLY")) return "Power";
        if (ContainsAny(text, "PE", "FE", "GND", "SG", "SHIELD")) return "Reference";
        return null;
    }

    private static string? InferDirection(string value)
    {
        var text = value.ToUpperInvariant();
        if (Regex.IsMatch(text, @"\b(?:DI|AI|INPUT)\b")) return "Input";
        if (Regex.IsMatch(text, @"\b(?:DO|AO|OUTPUT)\b")) return "Output";
        if (ContainsAny(text, "C/Q", "IO-LINK", "IOLINK")) return "Bidirectional";
        return null;
    }

    private static string? InferVoltageDomain(string value)
    {
        var match = Regex.Match(value, @"(?<!\d)(?<v>\d{1,3}(?:[.,]\d+)?)\s*V\s*(?<type>DC|AC)?\b", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var type = match.Groups["type"].Success ? match.Groups["type"].Value.ToUpperInvariant() : string.Empty;
        return $"{match.Groups["v"].Value}V{type}";
    }

    private static bool ContainsAny(string value, params string[] candidates) => candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    private static string Clean(string? value) => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
}
