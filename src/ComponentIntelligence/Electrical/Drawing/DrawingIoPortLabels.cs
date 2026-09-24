namespace ComponentIntelligence.Electrical.Drawing;

public sealed record DrawingIoPortLabel(string PageId, string EngineeringEndpointId, string Text, long X, long Y);

public static class DrawingIoPortLabels
{
    public static IReadOnlyList<DrawingIoPortLabel> Build(DrawingPlanningInput input, DrawingPlanDocument plan)
    {
        if (input.ProjectId != plan.ProjectId ||
            (input.PlanningInputHash != plan.SourcePlanningInputHash &&
             !DrawingPlanningInputHashCompatibility.IsPresentationMetadataOnlyUpgrade(input, plan.SourcePlanningInputHash)))
            return [];

        var pages = plan.Pages.ToDictionary(p => p.PageId, p => p.Archetype, StringComparer.Ordinal);
        var placements = plan.Placements.ToDictionary(p => p.RepresentationId, StringComparer.Ordinal);
        var modules = input.Representations.Where(r => r.Role == DrawingRepresentationRole.Schematic &&
            r.PhysicalModuleId == r.OwnerId &&
            (r.SourceType == "GeneratedGeneric" || (r.SourceType is null && r.AssetPath is null)));
        var byEndpoint = modules.SelectMany(rep => rep.PortBindings
            .Where(b => b.ConnectionPointId.StartsWith("PORT:", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(b.DisplayLabel))
            .Select(b => (rep.RepresentationId, Binding: b)))
            .GroupBy(x => x.Binding.EngineeringEndpointId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var labels = new List<DrawingIoPortLabel>();
        var seen = new HashSet<(string PageId, string EndpointId)>();

        foreach (var route in plan.Routes.OrderBy(r => r.RouteId, StringComparer.Ordinal))
        {
            if (!pages.TryGetValue(route.PageId, out var archetype) || archetype != "PlcIo" || route.Points.Count < 2)
                continue;
            var endpoints = new[]
            {
                (route.EndpointAId, route.Points[0]),
                (route.EndpointBId, route.RouteId.EndsWith(":DESTINATION", StringComparison.Ordinal)
                    ? route.Points[0] : route.Points[^1])
            };
            foreach (var (endpointId, point) in endpoints)
            {
                if (!byEndpoint.TryGetValue(endpointId, out var match) ||
                    !placements.TryGetValue(match.RepresentationId, out var placement) ||
                    placement.PageId != route.PageId || !seen.Add((route.PageId, endpointId)))
                    continue;
                labels.Add(new DrawingIoPortLabel(route.PageId, endpointId, match.Binding.DisplayLabel!,
                    point.X + 3, point.Y == placement.Y ? point.Y + 3 : point.Y - 14));
            }
        }
        return labels;
    }
}
