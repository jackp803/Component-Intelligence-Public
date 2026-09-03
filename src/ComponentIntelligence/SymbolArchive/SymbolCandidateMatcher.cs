using System.Text;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Resolution;

namespace ComponentIntelligence.SymbolArchive;

public sealed record SymbolComponentMatch(
    string ComponentId,
    string Manufacturer,
    string Model,
    int Score,
    IReadOnlyList<string> Signals);

public sealed class SymbolCandidateMatcher
{
    public IReadOnlyList<SymbolComponentMatch> Rank(
        BlockArchiveCandidate candidate,
        IReadOnlyList<ComponentIR> components)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(components);
        var fileStem = NormalizeSearchText(Path.GetFileNameWithoutExtension(candidate.FileName));
        var relative = NormalizeSearchText(candidate.RelativePath);
        var deep = NormalizeSearchText(BuildDeepText(candidate.DeepMetadata));

        return components.Select(component => Score(component, fileStem, relative, deep))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.ComponentId, StringComparer.Ordinal)
            .ToArray();
    }

    private static SymbolComponentMatch Score(ComponentIR component, string fileStem, string relative, string deep)
    {
        var signals = new List<string>();
        var score = 0;
        var componentId = CompactToken(component.Identity.ComponentId);
        var model = ModelNormalizer.Normalize(component.Identity.Model)?.SearchKey ?? string.Empty;
        var manufacturer = ManufacturerNormalizer.NormalizeKey(component.Identity.Manufacturer) ?? string.Empty;

        if (!string.IsNullOrEmpty(componentId) && CompactToken(fileStem + " " + relative + " " + deep).Contains(componentId, StringComparison.Ordinal))
        {
            score += 120;
            signals.Add("component-id-token:+120");
        }
        if (!string.IsNullOrEmpty(model) && ContainsNormalized(fileStem, model))
        {
            score += 100;
            signals.Add("model-file-stem:+100");
        }
        if (!string.IsNullOrEmpty(model) && ContainsNormalized(relative, model))
        {
            score += 80;
            signals.Add("model-relative-path:+80");
        }
        if (!string.IsNullOrEmpty(model) && ContainsNormalized(deep, model))
        {
            score += 60;
            signals.Add("model-deep-metadata:+60");
        }
        if (!string.IsNullOrEmpty(manufacturer) &&
            (ContainsNormalized(relative, manufacturer) || ContainsNormalized(deep, manufacturer) || ContainsNormalized(fileStem, manufacturer)))
        {
            score += 20;
            signals.Add("manufacturer:+20");
        }

        return new SymbolComponentMatch(
            component.Identity.ComponentId,
            component.Identity.Manufacturer,
            component.Identity.Model,
            score,
            signals);
    }

    private static bool ContainsNormalized(string haystack, string needle) =>
        !string.IsNullOrWhiteSpace(needle) && haystack.Contains(NormalizeSearchText(needle), StringComparison.Ordinal);

    private static string CompactToken(string? value) =>
        new string(NormalizeSearchText(value).Where(char.IsLetterOrDigit).ToArray());

    private static string BuildDeepText(BlockDeepInspectionMetadata? metadata)
    {
        if (metadata is null) return string.Empty;
        return string.Join(" ", metadata.BlockNames
            .Concat(metadata.TextLabels)
            .Concat(metadata.Attributes.SelectMany(attribute => new[] { attribute.Name, attribute.Value })));
    }

    private static string NormalizeSearchText(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
}
