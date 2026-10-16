using System.Text.RegularExpressions;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Verification;

public sealed class VerificationEngine : IVerificationEngine
{
    private static readonly HashSet<string> ConflictSensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "power.operating_voltage",
        "io.output_type",
        "connector.family",
        "connector.coding",
        "connector.pin_count",
        "communication.interface",
        "communication.protocol",
        "sensing.measuring_range",
        "sensing.pressure_rating",
        "environment.ip_rating"
    };

    public Task<VerificationSummary> VerifyAsync(
        ComponentIR component,
        RawComponentProfile raw,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(raw);

        var acceptedPinCount = component.Pins.Count(PinEngineeringValidationPolicy.IsAccepted);
        var rejectedPinCount = component.Pins.Count - acceptedPinCount;
        var criticalChecks = new Dictionary<string, bool>
        {
            ["operating_voltage"] = component.Power.OperatingVoltage is not null,
            ["output_type"] = !string.IsNullOrWhiteSpace(component.Io.OutputType),
            ["connector"] = !string.IsNullOrWhiteSpace(component.Connector.Family) && component.Connector.Pins is > 0,
            ["pins"] = acceptedPinCount > 0
        };
        var passedCritical = criticalChecks.Count(check => check.Value);
        var criticalCoverage = passedCritical / (decimal)criticalChecks.Count;

        var meaningfulSpecs = raw.Specifications
            .Where(spec => !string.IsNullOrWhiteSpace(spec.RawName) && !string.IsNullOrWhiteSpace(spec.RawValue))
            .ToArray();
        var mappedKeys = meaningfulSpecs
            .Where(spec => !string.IsNullOrWhiteSpace(spec.ProposedKey))
            .Select(spec => spec.ProposedKey!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var evidence = raw.Evidence.Concat(meaningfulSpecs.SelectMany(spec => spec.Evidence)).Distinct().ToArray();
        var documentCount = raw.Documents.Count;

        var rawRichness = Clamp01(meaningfulSpecs.Length / 24m);
        var mappedRichness = Clamp01(mappedKeys / 12m);
        var evidenceRichness = Clamp01(evidence.Length / 12m);
        var documentRichness = raw.Documents.Any(document =>
                document.Type.Contains("datasheet", StringComparison.OrdinalIgnoreCase) ||
                document.Type.Contains("manual", StringComparison.OrdinalIgnoreCase))
            ? 1m
            : documentCount > 0 || raw.Identity.OfficialProductUrl is not null ? 0.5m : 0m;

        var completeness = decimal.Round(
            0.30m * rawRichness +
            0.25m * mappedRichness +
            0.25m * criticalCoverage +
            0.10m * documentRichness +
            0.10m * evidenceRichness,
            3,
            MidpointRounding.AwayFromZero);

        var issues = criticalChecks
            .Where(check => !check.Value)
            .Select(check => $"MISSING_WIRING_{check.Key.ToUpperInvariant()}")
            .ToList();
        if (rejectedPinCount > 0)
            issues.Add($"PIN_ENGINEERING_GATE_REJECTED:{rejectedPinCount}");
        if (component.Pins.Count > 0 && acceptedPinCount == 0)
            issues.Add("PINS_PRESENT_BUT_ENGINEERING_UNVERIFIED");
        if (meaningfulSpecs.Length == 0) issues.Add("NO_ENGINEERING_SPECIFICATIONS_EXTRACTED");
        if (mappedKeys == 0 && meaningfulSpecs.Length > 0) issues.Add("SPECIFICATIONS_CAPTURED_BUT_NOT_NORMALIZED");
        if (documentCount == 0) issues.Add("NO_ENGINEERING_DOCUMENT_DISCOVERED");

        var detectedConflicts = DetectConflicts(meaningfulSpecs);
        issues.AddRange(detectedConflicts.Select(conflict =>
            $"DATA_CONFLICT:{conflict.Key}:{string.Join(" <> ", conflict.Values)}"));

        var hasConflict = meaningfulSpecs.Any(spec => spec.Status == VerificationStatus.Conflict) || detectedConflicts.Count > 0;
        var sourceTypes = evidence.Select(item => item.SourceType).Distinct().ToHashSet();
        var hasEvidence = evidence.Length > 0;
        var corroborated = HasCorroboratedField(meaningfulSpecs);
        var status = hasConflict
            ? VerificationStatus.Conflict
            : corroborated
                ? VerificationStatus.Verified
                : hasEvidence
                    ? VerificationStatus.SingleSource
                    : VerificationStatus.NotFound;

        var allCritical = passedCritical == criticalChecks.Count;
        var wiringReady = criticalChecks["output_type"] && criticalChecks["connector"] && criticalChecks["pins"];
        var topologyReady = criticalChecks["connector"] && criticalChecks["pins"];
        var readiness = new ComponentReadiness
        {
            Wiring = wiringReady ? ReadinessStatus.Ready : passedCritical > 0 ? ReadinessStatus.Partial : ReadinessStatus.NotReady,
            Topology = topologyReady ? ReadinessStatus.Ready : meaningfulSpecs.Length > 0 ? ReadinessStatus.Partial : ReadinessStatus.NotReady,
            Validation = allCritical && !hasConflict ? ReadinessStatus.Ready : meaningfulSpecs.Length > 0 ? ReadinessStatus.Partial : ReadinessStatus.NotReady,
            Drawing = allCritical && !hasConflict ? ReadinessStatus.Ready : meaningfulSpecs.Length > 0 ? ReadinessStatus.Partial : ReadinessStatus.NotReady
        };

        var confidence = Confidence(hasConflict, sourceTypes, raw.Documents, hasEvidence, corroborated);
        return Task.FromResult(new VerificationSummary(status, completeness, confidence, readiness, issues.Distinct().ToArray()));
    }

    private static IReadOnlyList<FieldConflict> DetectConflicts(IEnumerable<RawSpecification> specs)
    {
        var conflicts = new List<FieldConflict>();
        foreach (var group in specs
                     .Where(spec => !string.IsNullOrWhiteSpace(spec.ProposedKey) && ConflictSensitiveKeys.Contains(spec.ProposedKey!))
                     .GroupBy(spec => spec.ProposedKey!, StringComparer.OrdinalIgnoreCase))
        {
            var variants = group
                .Where(spec => !string.IsNullOrWhiteSpace(spec.RawValue))
                .GroupBy(spec => NormalizeValue(spec.RawValue), StringComparer.OrdinalIgnoreCase)
                .Select(valueGroup => new
                {
                    Normalized = valueGroup.Key,
                    Display = valueGroup.Select(spec => spec.RawValue!).First(),
                    Domains = valueGroup.SelectMany(spec => spec.Evidence).Select(EvidenceDomain).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                })
                .Where(variant => variant.Domains.Length > 0)
                .ToArray();

            if (variants.Length < 2) continue;
            var independentConflict = variants.Any(left => variants.Any(right =>
                !ReferenceEquals(left, right) &&
                left.Domains.Any(leftDomain => right.Domains.All(rightDomain => !string.Equals(leftDomain, rightDomain, StringComparison.OrdinalIgnoreCase)))));
            if (independentConflict)
                conflicts.Add(new FieldConflict(group.Key, variants.Select(variant => variant.Display).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray()));
        }
        return conflicts;
    }

    private static string Confidence(
        bool hasConflict,
        IReadOnlySet<ComponentSourceType> sourceTypes,
        IReadOnlyList<ComponentDocument> documents,
        bool hasEvidence,
        bool corroborated)
    {
        if (hasConflict) return "Low";
        if (!hasEvidence) return "Unknown";

        var hasManufacturerPage = sourceTypes.Contains(ComponentSourceType.ManufacturerProductPage);
        var hasManufacturerDocument = sourceTypes.Contains(ComponentSourceType.ManufacturerDatasheet) ||
                                      sourceTypes.Contains(ComponentSourceType.ManufacturerManual) ||
                                      documents.Any(document => document.SourceType is ComponentSourceType.ManufacturerDatasheet or ComponentSourceType.ManufacturerManual);
        var hasDistributor = sourceTypes.Contains(ComponentSourceType.AuthorizedDistributor);
        var hasTrustedThirdParty = sourceTypes.Contains(ComponentSourceType.TrustedThirdParty);

        if (hasManufacturerDocument && (corroborated || hasManufacturerPage || hasDistributor)) return "High";
        if (hasManufacturerDocument || (hasManufacturerPage && (hasDistributor || hasTrustedThirdParty))) return "High";
        if (hasManufacturerPage || hasDistributor) return "Medium";
        return "Low";
    }

    private static bool HasCorroboratedField(IEnumerable<RawSpecification> specs)
    {
        return specs
            .Where(spec => !string.IsNullOrWhiteSpace(spec.ProposedKey) && !string.IsNullOrWhiteSpace(spec.RawValue))
            .GroupBy(spec => spec.ProposedKey!, StringComparer.OrdinalIgnoreCase)
            .Any(group =>
            {
                var values = group
                    .GroupBy(spec => NormalizeValue(spec.RawValue), StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return values.Any(valueGroup => valueGroup
                    .SelectMany(spec => spec.Evidence)
                    .Select(EvidenceDomain)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() >= 2);
            });
    }

    private static string EvidenceDomain(Evidence evidence)
    {
        if (evidence.SourceType is ComponentSourceType.ManufacturerProductPage or ComponentSourceType.ManufacturerDatasheet or ComponentSourceType.ManufacturerManual or ComponentSourceType.ManufacturerDownloadCenter)
            return evidence.SourceType.ToString();
        if (evidence.SourceType == ComponentSourceType.AuthorizedDistributor)
            return $"DISTRIBUTOR:{evidence.SourceUrl?.Host ?? evidence.DocumentUrl?.Host ?? "UNKNOWN"}";
        if (evidence.SourceType == ComponentSourceType.TrustedThirdParty)
            return $"TRUSTED:{evidence.SourceUrl?.Host ?? evidence.DocumentUrl?.Host ?? "UNKNOWN"}";
        if (evidence.SourceType == ComponentSourceType.User)
            return $"USER:{evidence.DocumentHashSha256 ?? evidence.SourceUrl?.AbsoluteUri ?? "MANUAL"}";
        return $"{evidence.SourceType}:{evidence.SourceUrl?.Host ?? evidence.DocumentUrl?.Host ?? "UNKNOWN"}";
    }

    private static string NormalizeValue(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .ToUpperInvariant()
            .Replace('…', '.')
            .Replace('～', '-')
            .Replace("VDC", "V DC", StringComparison.OrdinalIgnoreCase)
            .Replace("VAC", "V AC", StringComparison.OrdinalIgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+", " ");
        normalized = Regex.Replace(normalized, @"\s*\.\.\.\s*", "...");
        normalized = Regex.Replace(normalized, @"\s*([,;:/])\s*", "$1");
        return normalized;
    }

    private static decimal Clamp01(decimal value) => Math.Min(1m, Math.Max(0m, value));

    private sealed record FieldConflict(string Key, IReadOnlyList<string> Values);
}
