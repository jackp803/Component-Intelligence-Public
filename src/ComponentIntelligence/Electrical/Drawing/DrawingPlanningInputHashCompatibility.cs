namespace ComponentIntelligence.Electrical.Drawing;

public static class DrawingPlanningInputHashCompatibility
{
    public static bool IsPresentationMetadataOnlyUpgrade(DrawingPlanningInput current, string priorHash)
    {
        if (string.IsNullOrWhiteSpace(current.PlanningInputHash) || current.PlanningInputHash == priorHash ||
            !current.Representations.Any(r => r.SourceType == DrawingTerminalContactPresentation.SourceType ||
                r.DisplayLabel is not null || r.PortBindings.Any(b => b.DisplayLabel is not null || b.PhysicalSide is not null)) ||
            DrawingPlanningJson.ComputeHash(current) != current.PlanningInputHash)
            return false;

        for (var mask = 1; mask < 8; mask++)
        {
            var removeLabels = (mask & 1) != 0;
            var removeSides = (mask & 2) != 0;
            var removeTerminal = (mask & 4) != 0;
            var withoutMetadata = current with
            {
                Representations = current.Representations.Select(r => r with
                {
                    SourceType = removeTerminal && r.SourceType == DrawingTerminalContactPresentation.SourceType ? null : r.SourceType,
                    DisplayLabel = removeLabels ? null : r.DisplayLabel,
                    PortBindings = r.PortBindings.Select(b => b with
                    {
                        ConnectionPointId = removeTerminal && r.SourceType == DrawingTerminalContactPresentation.SourceType &&
                            b.ConnectionPointId.StartsWith(DrawingTerminalContactPresentation.ContactPrefix, StringComparison.Ordinal)
                            ? $"PIN:{b.EngineeringEndpointId}" : b.ConnectionPointId,
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
