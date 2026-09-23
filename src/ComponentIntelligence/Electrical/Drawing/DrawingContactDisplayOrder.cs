using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ComponentIntelligence.Electrical.Drawing;

public static class DrawingContactDisplayOrder
{
    // Only display text is sorted numerically. Endpoint IDs are never parsed.
    public static IComparer<string> NumberComparer { get; } = Comparer<string>.Create((a, b) =>
    {
        var left = Regex.Split(a ?? "", "([0-9]+)"); var right = Regex.Split(b ?? "", "([0-9]+)");
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var value = BigInteger.TryParse(left[i], out var x) && BigInteger.TryParse(right[i], out var y)
                ? x.CompareTo(y) : StringComparer.Ordinal.Compare(left[i], right[i]);
            if (value != 0) return value;
        }
        return left.Length.CompareTo(right.Length);
    });

    public static IReadOnlyDictionary<string, int> Read(DrawingPlanningInput input)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rule in input.WiringRules.Where(r => r.RuleKind == "ContactDisplayOrder" && r.Source == "ElectricalProject.ComponentPin.PinNumber"))
        foreach (var item in JsonSerializer.SerializeToElement(rule.Value).EnumerateArray())
            result.Add(item.GetProperty("endpointId").GetString()!, item.GetProperty("order").GetInt32());
        return result;
    }
}
