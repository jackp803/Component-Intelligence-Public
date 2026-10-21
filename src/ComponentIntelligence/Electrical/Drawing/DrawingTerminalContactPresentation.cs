namespace ComponentIntelligence.Electrical.Drawing;

public sealed record DrawingTerminalContact(string EngineeringEndpointId, DrawingPoint Point);

public sealed record DrawingTerminalContactGroup(
    string RepresentationId, string PageId, DrawingPoint Center,
    IReadOnlyList<DrawingTerminalContact> Contacts,
    IReadOnlyList<DrawingTerminalContact> PotentialAnchors);

public static class DrawingTerminalContactPresentation
{
    public const string SourceType = "ProjectTerminalContact";
    public const string ContactPrefix = "TERMINAL_CONTACT:";

    public static IReadOnlyList<DrawingTerminalContactGroup> Build(DrawingPlanningInput input, DrawingPlanDocument plan)
    {
        var groups = new List<DrawingTerminalContactGroup>();
        foreach (var rep in input.Representations.Where(r => r.SourceType == SourceType))
        {
            var placement = plan.Placements.SingleOrDefault(p => p.RepresentationId == rep.RepresentationId);
            if (placement is null) continue;
            var bindings = rep.PortBindings.ToDictionary(b => b.EngineeringEndpointId, StringComparer.Ordinal);
            var used = new Dictionary<string, DrawingPoint>(StringComparer.Ordinal);
            foreach (var route in plan.Routes.Where(r => r.PageId == placement.PageId))
            {
                if (bindings.ContainsKey(route.EndpointAId)) Add(route.EndpointAId, route.Points[0]);
                if (bindings.ContainsKey(route.EndpointBId))
                    Add(route.EndpointBId, route.RouteId.EndsWith(":DESTINATION", StringComparison.Ordinal)
                        ? route.Points[0] : route.Points[^1]);
            }
            var anchors = used.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new DrawingTerminalContact(pair.Key, pair.Value)).ToArray();
            groups.Add(new DrawingTerminalContactGroup(rep.RepresentationId, placement.PageId,
                new DrawingPoint(placement.X + placement.Width / 2, placement.Y + placement.Height / 2),
                anchors.Where(a => bindings[a.EngineeringEndpointId].ConnectionPointId.StartsWith(ContactPrefix, StringComparison.Ordinal)).ToArray(),
                anchors));

            void Add(string endpointId, DrawingPoint point)
            {
                if (point.X < placement.X || point.X > placement.X + placement.Width ||
                    point.Y < placement.Y || point.Y > placement.Y + placement.Height ||
                    point.X != placement.X && point.X != placement.X + placement.Width &&
                    point.Y != placement.Y && point.Y != placement.Y + placement.Height)
                    return; // The opposite end of a cross-page route is not this terminal.
                if (used.TryGetValue(endpointId, out var existing) && existing != point)
                    throw new InvalidOperationException($"Terminal contact {endpointId} has conflicting route anchors.");
                used[endpointId] = point;
            }
        }
        return groups;
    }
}
