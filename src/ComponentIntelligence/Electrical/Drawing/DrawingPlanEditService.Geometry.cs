namespace ComponentIntelligence.Electrical.Drawing;

public sealed partial class DrawingPlanEditService
{
    private IReadOnlyDictionary<string, IReadOnlySet<string>> _endpointBindings = new Dictionary<string, IReadOnlySet<string>>();

    public void SetEndpointBindings(IReadOnlyDictionary<string, IReadOnlySet<string>> bindings) =>
        _endpointBindings = bindings.ToDictionary(x => x.Key, x => (IReadOnlySet<string>)x.Value.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

    private DrawingPlanDocument ReconnectPlacements(DrawingPlanDocument plan, IReadOnlyList<DrawingPlacement> placements)
    {
        var moved = placements.Where(p => plan.Placements.Any(old => old.RepresentationId == p.RepresentationId &&
            (old.X != p.X || old.Y != p.Y || old.RotationDegrees != p.RotationDegrees))).ToArray();
        var routes = plan.Routes.ToArray();
        foreach (var placement in moved)
        {
            var old = plan.Placements.Single(p => p.RepresentationId == placement.RepresentationId);
            if (!_endpointBindings.TryGetValue(placement.RepresentationId, out var endpoints))
                throw new InvalidOperationException("缺少此表示的端點綁定，不能安全移動連線。請重新產生預覽。");
            for (var index = 0; index < routes.Length; index++)
            {
                var route = routes[index];
                if (route.PageId != old.PageId) continue;
                var continuation = plan.CrossPageRelations.SingleOrDefault(r => r.RelationKind == "ElectricalConnectionContinuation" &&
                    (r.SourceRouteId == route.RouteId || r.DestinationRouteId == route.RouteId));
                var start = continuation is null ? endpoints.Contains(route.EndpointAId) :
                    (continuation.SourceRouteId == route.RouteId ? continuation.SourceRepresentationId : continuation.DestinationRepresentationId) == old.RepresentationId;
                var end = continuation is null && endpoints.Contains(route.EndpointBId);
                if (!start && !end) continue;
                EnsureEditable(route);
                if (continuation is not null && route.State == DrawingPlanControlState.Auto)
                {
                    routes[index] = route with { Points = route.Points.Select(p => Transform(p, old, placement)).ToArray() };
                    continue;
                }
                var points = route.Points.ToList();
                if (start) points = ReconnectStart(points, Transform(points[0], old, placement));
                if (end) { points.Reverse(); points = ReconnectStart(points, Transform(points[0], old, placement)); points.Reverse(); }
                ValidateOrthogonal(points);
                routes[index] = route with { Points = points };
            }
        }
        return DrawingPlanJson.Rehash(plan with { Placements = placements, Routes = routes });
    }

    // Preserve existing endpoint geometry under the symbol transform. This does
    // not manufacture missing approved-asset anchor coordinates.
    private static DrawingPoint Transform(DrawingPoint point, DrawingPlacement old, DrawingPlacement next)
    {
        var x = point.X - old.X - old.Width / 2.0;
        var y = point.Y - old.Y - old.Height / 2.0;
        var delta = ((next.RotationDegrees - old.RotationDegrees) % 360 + 360) % 360;
        (x, y) = delta switch { 90 => (-y, x), 180 => (-x, -y), 270 => (y, -x), _ => (x, y) };
        return new((long)Math.Round(next.X + next.Width / 2.0 + x), (long)Math.Round(next.Y + next.Height / 2.0 + y));
    }

    private static List<DrawingPoint> ReconnectStart(IReadOnlyList<DrawingPoint> points, DrawingPoint anchor)
    {
        if (points[0] == anchor) return points.ToList();
        var next = points[1];
        var elbow = points[0].X == next.X ? new DrawingPoint(anchor.X, next.Y) : new DrawingPoint(next.X, anchor.Y);
        var result = new List<DrawingPoint> { anchor };
        if (elbow != anchor && elbow != next) result.Add(elbow);
        foreach (var point in points.Skip(1)) if (result[^1] != point) result.Add(point);
        if (result.Count < 2) throw new InvalidOperationException("移動後路線長度為零，請選擇其他位置。");
        return result;
    }
}
