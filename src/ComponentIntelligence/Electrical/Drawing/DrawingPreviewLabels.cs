using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Drawing;

public static class DrawingPreviewLabels
{
    public static IReadOnlyDictionary<string, string> Build(ElectricalProject project)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var component in project.Components)
            labels[$"REP:{component.ComponentInstanceId}:Schematic"] = Join(component.ReferenceDesignator, component.DisplayName, "元件名稱待確認");
        foreach (var cable in project.Cables)
        {
            var label = Join(cable.ReferenceDesignator, cable.DisplayName, "線材名稱待確認");
            labels[$"REP:{cable.CableInstanceId}:CableFunctional"] = label;
            labels[$"REP:{cable.CableInstanceId}:CableDetail"] = label + "\n線材明細";
        }
        return labels;
    }

    private static string Join(string? reference, string? name, string fallback)
    {
        var parts = new[] { reference, name }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
        return parts.Length == 0 ? fallback : string.Join("\n", parts);
    }
}
