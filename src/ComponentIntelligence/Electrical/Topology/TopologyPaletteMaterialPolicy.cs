namespace ComponentIntelligence.Electrical.Topology;

public enum TopologyPaletteMaterialKind
{
    Standard,
    TerminalBlock,
    ShortingJumper
}

/// <summary>
/// Routes structured BOM material categories into opt-in topology palette groups. Classification
/// uses the normalized TypeKey produced from Component IR category/subcategory; manufacturer and
/// model names are deliberately ignored so ordinary devices are never hidden by a fuzzy match.
/// </summary>
public static class TopologyPaletteMaterialPolicy
{
    private static readonly string[] TerminalTerms =
    [
        "TERMINAL BLOCK", "TERMINALBLOCK", "DIN TERMINAL", "DIN RAIL TERMINAL", "RAIL TERMINAL",
        "FEED THROUGH TERMINAL", "端子台", "端子排", "軌道端子"
    ];

    private static readonly string[] JumperTerms =
    [
        "SHORTING JUMPER", "SHORTINGJUMPER", "JUMPER BAR", "JUMPER COMB",
        "CROSS CONNECT", "SHORTING BAR", "PLUG IN BRIDGE", "BRIDGE JUMPER", "JUMPER",
        "短路片", "短接片", "橋接片"
    ];

    public static TopologyPaletteMaterialKind Classify(string? typeKey)
    {
        if (string.IsNullOrWhiteSpace(typeKey)) return TopologyPaletteMaterialKind.Standard;
        var normalized = Normalize(typeKey);
        if (JumperTerms.Any(term => ContainsTerm(normalized, term)))
            return TopologyPaletteMaterialKind.ShortingJumper;
        if (TerminalTerms.Any(term => ContainsTerm(normalized, term)))
            return TopologyPaletteMaterialKind.TerminalBlock;
        return TopologyPaletteMaterialKind.Standard;
    }

    private static bool ContainsTerm(string normalizedValue, string term)
    {
        var normalizedTerm = Normalize(term);
        if (normalizedTerm.Any(character => character >= '\u2E80'))
            return normalizedValue.Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase);
        return $" {normalizedValue} ".Contains($" {normalizedTerm} ", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        foreach (var separator in new[] { '/', '|', ',', ';', ':', '_', '-' })
            normalized = normalized.Replace(separator, ' ');
        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
