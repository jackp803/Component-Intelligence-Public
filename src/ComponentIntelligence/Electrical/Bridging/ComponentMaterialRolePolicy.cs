using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Electrical.Bridging;

public enum BomTopologyDisposition
{
    TopologyNode,
    DeferredConnectionMaterial
}

/// <summary>
/// Deterministic BOM material routing for the topology projection.
///
/// Important: this policy never guesses from manufacturer or model text. It only uses structured
/// Component IR classification/material-role facts. Unknown classification stays visible as a normal
/// topology component so unresolved engineering data is not silently discarded.
/// </summary>
public static class ComponentMaterialRolePolicy
{
    private static readonly string[] ConnectionMaterialTerms =
    [
        "CABLE",
        "CABLE ASSEMBLY",
        "CABLEASSEMBLY",
        "BULK CABLE",
        "WIRE",
        "HARNESS",
        "WIRE HARNESS",
        "CORDSET",
        "CORD SET",
        "PATCH CABLE",
        "PATCHCORD",
        "PATCH CORD",
        "線材",
        "電纜",
        "電線",
        "導線",
        "成品線",
        "成品線組",
        "線組"
    ];

    public static BomTopologyDisposition Classify(ComponentIR component)
    {
        ArgumentNullException.ThrowIfNull(component);

        var structuredRole = string.Join(' ', new[]
        {
            component.Classification.Category,
            component.Classification.Subcategory,
            FindMaterialRoleSpecification(component)
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return IsConnectionMaterial(structuredRole)
            ? BomTopologyDisposition.DeferredConnectionMaterial
            : BomTopologyDisposition.TopologyNode;
    }

    private static string? FindMaterialRoleSpecification(ComponentIR component) =>
        component.Specifications
            .FirstOrDefault(specification =>
                IsMaterialRoleKey(specification.Key) || IsMaterialRoleKey(specification.Name))
            ?.Value;

    private static bool IsMaterialRoleKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim().Replace('_', ' ').Replace('-', ' ');
        return string.Equals(normalized, "Material Role", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "MaterialRole", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "電料角色", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConnectionMaterial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = NormalizeWords(value);
        var padded = $" {normalized} ";
        foreach (var term in ConnectionMaterialTerms)
        {
            if (ContainsCjk(term))
            {
                if (normalized.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
                continue;
            }

            var normalizedTerm = NormalizeWords(term);
            if (padded.Contains($" {normalizedTerm} ", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string NormalizeWords(string value)
    {
        var separators = new[] { '/', '|', ',', ';', ':', '_', '-' };
        var normalized = value.Trim().ToUpperInvariant();
        foreach (var separator in separators)
            normalized = normalized.Replace(separator, ' ');
        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool ContainsCjk(string value) => value.Any(character => character >= '\u2E80');
}
