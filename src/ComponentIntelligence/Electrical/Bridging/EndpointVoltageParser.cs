using System.Globalization;
using System.Text.RegularExpressions;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Bridging;

internal static class EndpointVoltageParser
{
    // Entire-cell match: unrecognized qualifiers/TBD must not become a numeric claim.
    private static readonly Regex Value = new(@"^\s*(?<min>[+-]?\d+(?:\.\d+)?)\s*(?:(?:\.\.{1,2}|~|–|…|-)\s*(?<max>[+-]?\d+(?:\.\d+)?))?\s*V\s*(?<type>DC|AC)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ZeroReturn = new(@"^\s*[+-]?0(?:\.0+)?\s*V\s+return\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static VoltageSpecification? Parse(string raw)
    {
        if (ZeroReturn.IsMatch(raw)) return new() { Type = VoltageType.Unknown, NominalVoltage = 0 };
        var match = Value.Match(raw);
        if (!match.Success || !double.TryParse(match.Groups["min"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var min)) return null;
        var max = min;
        if (match.Groups["max"].Success && !double.TryParse(match.Groups["max"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out max)) return null;
        if (!double.IsFinite(min) || !double.IsFinite(max) || min > max) return null;
        return new()
        {
            Type = match.Groups["type"].Value.ToUpperInvariant() switch { "DC" => VoltageType.Dc, "AC" => VoltageType.Ac, _ => VoltageType.Unknown },
            NominalVoltage = min == max ? min : null,
            MinVoltage = min == max ? null : min,
            MaxVoltage = min == max ? null : max
        };
    }
}
