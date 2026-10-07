namespace ComponentIntelligence.Electrical.Drawing;

public static class DrawingPlanningInputHashCompatibility
{
    public static bool IsPresentationMetadataOnlyUpgrade(DrawingPlanningInput current, string priorHash)
    {
        if (string.IsNullOrWhiteSpace(current.PlanningInputHash) || current.PlanningInputHash == priorHash ||
            !current.Representations.Any(r => r.DisplayLabel is not null ||
                r.PortBindings.Any(b => b.DisplayLabel is not null || b.PhysicalSide is not null)) ||
            DrawingPlanningJson.ComputeHash(current) != current.PlanningInputHash)
            return false;

        foreach (var (removeLabels, removeSides) in new[] { (true, false), (false, true), (true, true) })
        {
            var withoutMetadata = current with
            {
                Representations = current.Representations.Select(r => r with
                {
                    DisplayLabel = removeLabels ? null : r.DisplayLabel,
                    PortBindings = r.PortBindings.Select(b => b with
                    {
                        DisplayLabel = removeLabels ? null : b.DisplayLabel,
                        PhysicalSide = removeSides ? null : b.PhysicalSide
                    }).ToArray()
                }).ToList()
            };
            if (DrawingPlanningJson.ComputeHash(withoutMetadata) == priorHash) return true;
        }
        return false;
    }

    public static bool IsDisplayLabelOnlyUpgrade(DrawingPlanningInput current, string priorHash)
    {
        if (string.IsNullOrWhiteSpace(current.PlanningInputHash) ||
            current.PlanningInputHash == priorHash ||
            !current.Representations.Any(r => r.DisplayLabel is not null || r.PortBindings.Any(b => b.DisplayLabel is not null)) ||
            DrawingPlanningJson.ComputeHash(current) != current.PlanningInputHash)
            return false;

        var withoutLabels = current with
        {
            Representations = current.Representations.Select(r => r with
            {
                DisplayLabel = null,
                PortBindings = r.PortBindings.Select(b => b with { DisplayLabel = null }).ToArray()
            }).ToList()
        };
        return DrawingPlanningJson.ComputeHash(withoutLabels) == priorHash;
    }
}
