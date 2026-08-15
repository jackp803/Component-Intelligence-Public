using System.Text.RegularExpressions;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Verification;

/// <summary>
/// Defense-in-depth gate for pin facts that may become engineering truth.
/// Manual/modelled pins without parser evidence are preserved. Automatically parsed pin rows must
/// have an explicit electrical meaning, and third-party/OCR parser output requires stronger review
/// before it can drive verification, topology readiness, local persistence, or central knowledge.
/// </summary>
public static class PinEngineeringValidationPolicy
{
    private static readonly string[] ContextHints =
    [
        "pin assignment", "pinout", "pin out", "wiring", "connector", "contact assignment",
        "electrical connection", "terminal assignment", "接線", "接头", "接頭", "腳位", "脚位", "端子"
    ];

    private static readonly string[] ElectricalFunctionHints =
    [
        "l+", "l-", "c/q", "+24", "24 v", "24v", "0 v", "0v", "gnd", "sg", "pe", "fe", "shield",
        "rs485", "rs-485", "a+", "b-", "rx", "tx", "di", "do", "ai", "ao", "input", "output",
        "4-20", "4...20", "0-10", "0...10", "io-link", "iolink", "ethernet", "power", "supply"
    ];

    public static bool IsAccepted(ComponentPin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        if (string.IsNullOrWhiteSpace(pin.PinNumber)) return false;

        // Explicitly modelled/manual pins frequently have no extraction evidence. Do not erase those.
        if (pin.Evidence.Count == 0) return true;

        // Human-reviewed or independently verified evidence is authoritative enough to pass this gate.
        if (pin.Evidence.Any(evidence => evidence.VerificationStatus is VerificationStatus.UserConfirmed or VerificationStatus.Verified))
            return true;

        var parserEvidence = pin.Evidence
            .Where(evidence => evidence.ExtractionMethod is ExtractionMethod.TableParser or ExtractionMethod.OcrText)
            .ToArray();
        if (parserEvidence.Length == 0) return true;

        // OCR-derived contact facts remain review-only until confirmed/verified.
        if (parserEvidence.Any(evidence => evidence.ExtractionMethod == ExtractionMethod.OcrText))
            return false;

        var hasElectricalSemantics = LooksLikeElectricalPinFunction(pin.Function) || LooksLikeExplicitPinContext(pin.Description);
        if (!hasElectricalSemantics) return false;

        // A single third-party/generic table parse must never become central engineering truth by itself.
        if (parserEvidence.Any(evidence => evidence.SourceType is ComponentSourceType.TrustedThirdParty or ComponentSourceType.GenericWeb or ComponentSourceType.AiInference))
            return false;

        return parserEvidence.Any(evidence => evidence.SourceType is
            ComponentSourceType.ManufacturerDatasheet or
            ComponentSourceType.ManufacturerProductPage or
            ComponentSourceType.ManufacturerManual or
            ComponentSourceType.ManufacturerDownloadCenter or
            ComponentSourceType.AuthorizedDistributor or
            ComponentSourceType.User);
    }

    public static IReadOnlyList<ComponentPin> AcceptedPins(IEnumerable<ComponentPin> pins) =>
        pins.Where(IsAccepted).ToArray();

    public static bool LooksLikeElectricalPinFunction(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return ElectricalFunctionHints.Any(hint => ContainsToken(value, hint));
    }

    private static bool LooksLikeExplicitPinContext(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return false;
        return ContextHints.Any(hint => description.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsToken(string value, string token)
    {
        var pattern = $@"(?<![A-Za-z0-9]){Regex.Escape(token)}(?![A-Za-z0-9])";
        return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
