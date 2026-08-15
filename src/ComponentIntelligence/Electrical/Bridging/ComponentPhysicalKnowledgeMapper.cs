using System.Globalization;
using System.Text.RegularExpressions;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Bridging;

/// <summary>
/// Converts trusted, normalized physical specifications from Component IR into the project's
/// PhysicalFootprint. It never invents dimensions: the source must explicitly provide a three-axis
/// millimetre Dimensions value. The central-knowledge contract defines dimensions as W × H × D.
/// </summary>
public static class ComponentPhysicalKnowledgeMapper
{
    private static readonly Regex DimensionsMm = new(
        @"(?<w>\d+(?:[.,]\d+)?)\s*[x×X]\s*(?<h>\d+(?:[.,]\d+)?)\s*[x×X]\s*(?<d>\d+(?:[.,]\d+)?)\s*mm\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static PhysicalFootprint? TryCreateFootprint(ComponentIR source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var dimensions = source.Specifications.FirstOrDefault(specification =>
            IsTrusted(specification.Status) &&
            (string.Equals(specification.Key, "dimensions", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(specification.Name, "Dimensions", StringComparison.OrdinalIgnoreCase)));
        if (dimensions is null) return null;

        var candidates = dimensions.Evidence
            .Select(evidence => evidence.RawValue)
            .Concat([dimensions.Value])
            .Where(value => !string.IsNullOrWhiteSpace(value));

        Match? match = null;
        foreach (var candidate in candidates)
        {
            var current = DimensionsMm.Match(candidate!);
            if (!current.Success) continue;
            match = current;
            break;
        }
        if (match is null) return null;

        if (!TryMillimetres(match.Groups["w"].Value, out var width) ||
            !TryMillimetres(match.Groups["h"].Value, out var height) ||
            !TryMillimetres(match.Groups["d"].Value, out var depth))
            return null;

        var installation = source.Specifications.FirstOrDefault(specification =>
            IsTrusted(specification.Status) &&
            (string.Equals(specification.Key, "installation", StringComparison.OrdinalIgnoreCase) ||
             specification.Name.Contains("Installation", StringComparison.OrdinalIgnoreCase)));
        var installationText = string.Join(' ', new[]
        {
            installation?.Value,
            installation?.Evidence.FirstOrDefault()?.RawValue
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return new PhysicalFootprint
        {
            WidthMm = width,
            HeightMm = height,
            DepthMm = depth,
            MountingType = installationText.Contains("DIN-rail", StringComparison.OrdinalIgnoreCase) ||
                           installationText.Contains("DIN rail", StringComparison.OrdinalIgnoreCase)
                ? MountingType.DinRail
                : MountingType.Unknown
        };
    }

    private static bool IsTrusted(VerificationStatus status) =>
        status is VerificationStatus.Verified or VerificationStatus.UserConfirmed or VerificationStatus.SingleSource;

    private static bool TryMillimetres(string raw, out double value)
    {
        raw = raw.Replace(',', '.');
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0;
    }
}
