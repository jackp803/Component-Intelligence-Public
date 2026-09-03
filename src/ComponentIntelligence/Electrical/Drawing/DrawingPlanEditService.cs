namespace ComponentIntelligence.Electrical.Drawing;

public sealed class DrawingPlanEditService
{
    public DrawingPlanDocument MovePlacement(DrawingPlanDocument plan, string representationId, long x, long y) => UpdatePlacement(plan, representationId, p => EnsureEditable(p) with { X = x, Y = y, State = DrawingPlanControlState.Manual });

    public DrawingPlanDocument SetPlacementState(DrawingPlanDocument plan, string representationId, DrawingPlanControlState state) => UpdatePlacement(plan, representationId, p => p with { State = state });

    public DrawingPlanDocument MovePage(DrawingPlanDocument plan, string pageId, int targetIndex)
    {
        var pages = plan.Pages.OrderBy(x => x.Order).ThenBy(x => x.PageId, StringComparer.Ordinal).ToList();
        var sourceIndex = pages.FindIndex(x => x.PageId == pageId);
        if (sourceIndex < 0) throw new InvalidOperationException("Page not found.");
        if (pages[sourceIndex].OrderState == DrawingPlanControlState.Locked) throw new InvalidOperationException("Locked page order cannot be moved.");
        if (targetIndex < 0 || targetIndex >= pages.Count) throw new InvalidOperationException("Page target index is invalid.");
        var item = pages[sourceIndex]; pages.RemoveAt(sourceIndex); pages.Insert(targetIndex, item with { OrderState = DrawingPlanControlState.Manual });
        pages = pages.Select((page, index) => page with { Order = index }).ToList();
        return DrawingPlanJson.Rehash(plan with { Pages = pages });
    }

    public DrawingPlanDocument SetPageOrderState(DrawingPlanDocument plan, string pageId, DrawingPlanControlState state)
    {
        var found = false;
        var pages = plan.Pages.Select(page => page.PageId == pageId ? Mark(page, state, ref found) : page).ToArray();
        if (!found) throw new InvalidOperationException("Page not found.");
        return DrawingPlanJson.Rehash(plan with { Pages = pages });
    }

    public DrawingPlanDocument MoveRouteSegment(DrawingPlanDocument plan, string routeId, int segmentIndex, long delta)
    {
        return UpdateRoute(plan, routeId, route =>
        {
            EnsureEditable(route);
            if (segmentIndex < 0 || segmentIndex >= route.Points.Count - 1) throw new InvalidOperationException("Route segment index is invalid.");
            var points = route.Points.ToArray(); var a = points[segmentIndex]; var b = points[segmentIndex + 1];
            if (a.X == b.X) { points[segmentIndex] = a with { X = a.X + delta }; points[segmentIndex + 1] = b with { X = b.X + delta }; }
            else if (a.Y == b.Y) { points[segmentIndex] = a with { Y = a.Y + delta }; points[segmentIndex + 1] = b with { Y = b.Y + delta }; }
            else throw new InvalidOperationException("Only orthogonal route segments can move.");
            return route with { Points = points, State = DrawingPlanControlState.Manual };
        });
    }

    public DrawingPlanDocument MoveBendPoint(DrawingPlanDocument plan, string routeId, int pointIndex, long x, long y) => UpdateRoute(plan, routeId, route =>
    {
        EnsureEditable(route);
        if (pointIndex <= 0 || pointIndex >= route.Points.Count - 1) throw new InvalidOperationException("Only interior bend points can move.");
        var points = route.Points.ToArray(); points[pointIndex] = new DrawingPoint(x, y); ValidateOrthogonal(points); return route with { Points = points, State = DrawingPlanControlState.Manual };
    });

    public DrawingPlanDocument AddBendPoint(DrawingPlanDocument plan, string routeId, int segmentIndex, long x, long y) => UpdateRoute(plan, routeId, route =>
    {
        EnsureEditable(route);
        if (segmentIndex < 0 || segmentIndex >= route.Points.Count - 1) throw new InvalidOperationException("Route segment index is invalid.");
        var points = route.Points.ToList(); points.Insert(segmentIndex + 1, new DrawingPoint(x, y)); ValidateOrthogonal(points); return route with { Points = points, State = DrawingPlanControlState.Manual };
    });

    public DrawingPlanDocument DeleteBendPoint(DrawingPlanDocument plan, string routeId, int pointIndex) => UpdateRoute(plan, routeId, route =>
    {
        EnsureEditable(route);
        if (pointIndex <= 0 || pointIndex >= route.Points.Count - 1) throw new InvalidOperationException("Only interior bend points can be deleted.");
        var points = route.Points.ToList(); points.RemoveAt(pointIndex); ValidateOrthogonal(points); return route with { Points = points, State = DrawingPlanControlState.Manual };
    });

    public DrawingPlanDocument SetRouteState(DrawingPlanDocument plan, string routeId, DrawingPlanControlState state) => UpdateRoute(plan, routeId, route => route with { State = state });
    public DrawingPlanDocument ResetPlacementToAuto(DrawingPlanDocument plan, string representationId) => SetPlacementState(plan, representationId, DrawingPlanControlState.Auto);
    public DrawingPlanDocument ResetRouteToAuto(DrawingPlanDocument plan, string routeId) => SetRouteState(plan, routeId, DrawingPlanControlState.Auto);

    public DrawingPlanDocument AlignPlacements(DrawingPlanDocument plan, IReadOnlyList<string> representationIds, DrawingAlignment alignment)
    {
        var selected = SelectPlacements(plan, representationIds); var target = alignment switch
        {
            DrawingAlignment.Left => selected.Min(x => x.X), DrawingAlignment.Right => selected.Max(x => x.X + x.Width),
            DrawingAlignment.Top => selected.Min(x => x.Y), DrawingAlignment.Bottom => selected.Max(x => x.Y + x.Height),
            DrawingAlignment.HorizontalCenter => selected.Sum(x => x.X + x.Width / 2) / selected.Count,
            DrawingAlignment.VerticalCenter => selected.Sum(x => x.Y + x.Height / 2) / selected.Count, _ => 0
        };
        var ids = representationIds.ToHashSet(StringComparer.Ordinal);
        var placements = plan.Placements.Select(p => !ids.Contains(p.RepresentationId) ? p : Align(EnsureEditable(p), alignment, target)).ToArray();
        return DrawingPlanJson.Rehash(plan with { Placements = placements });
    }

    public DrawingPlanDocument DistributePlacements(DrawingPlanDocument plan, IReadOnlyList<string> representationIds, DrawingDistribution distribution)
    {
        var selected = SelectPlacements(plan, representationIds); if (selected.Count < 3) return plan;
        var ordered = distribution == DrawingDistribution.Horizontal ? selected.OrderBy(x => x.X).ToList() : selected.OrderBy(x => x.Y).ToList();
        var start = distribution == DrawingDistribution.Horizontal ? ordered.First().X : ordered.First().Y;
        var end = distribution == DrawingDistribution.Horizontal ? ordered.Last().X : ordered.Last().Y;
        var step = (end - start) / (ordered.Count - 1); var positions = ordered.Select((p, i) => (p.RepresentationId, Value: start + step * i)).ToDictionary(x => x.RepresentationId, x => x.Value, StringComparer.Ordinal);
        var placements = plan.Placements.Select(p => positions.TryGetValue(p.RepresentationId, out var value) ? (distribution == DrawingDistribution.Horizontal ? EnsureEditable(p) with { X = value, State = DrawingPlanControlState.Manual } : EnsureEditable(p) with { Y = value, State = DrawingPlanControlState.Manual }) : p).ToArray();
        return DrawingPlanJson.Rehash(plan with { Placements = placements });
    }

    public DrawingPlanDocument RotatePlacement(DrawingPlanDocument plan, string representationId, int rotationDegrees) => UpdatePlacement(plan, representationId, p =>
    {
        EnsureEditable(p); if (!p.AllowedRotations.Contains(rotationDegrees)) throw new InvalidOperationException("Rotation is not allowed by representation metadata."); return p with { RotationDegrees = rotationDegrees, State = DrawingPlanControlState.Manual };
    });

    public DrawingPlanDocument ResetGroupToAuto(DrawingPlanDocument plan, string groupId)
    {
        if (!plan.Groups.Any(x => x.GroupId == groupId)) throw new InvalidOperationException("Group not found.");
        return DrawingPlanJson.Rehash(plan with { Groups = plan.Groups.Select(g => g.GroupId == groupId ? g with { State = DrawingPlanControlState.Auto } : g).ToArray(), Placements = plan.Placements.Select(p => p.GroupId == groupId ? p with { State = DrawingPlanControlState.Auto } : p).ToArray() });
    }

    public DrawingPlanDocument ResetPageToAuto(DrawingPlanDocument plan, string pageId)
    {
        if (!plan.Pages.Any(x => x.PageId == pageId)) throw new InvalidOperationException("Page not found.");
        var groupIds = plan.Groups.Where(x => x.PageId == pageId).Select(x => x.GroupId).ToHashSet(StringComparer.Ordinal);
        return DrawingPlanJson.Rehash(plan with { Pages = plan.Pages.Select(p => p.PageId == pageId ? p with { OrderState = DrawingPlanControlState.Auto } : p).ToArray(), Groups = plan.Groups.Select(g => groupIds.Contains(g.GroupId) ? g with { State = DrawingPlanControlState.Auto } : g).ToArray(), Placements = plan.Placements.Select(p => p.PageId == pageId ? p with { State = DrawingPlanControlState.Auto } : p).ToArray() });
    }

    private static DrawingPlacement Align(DrawingPlacement p, DrawingAlignment alignment, long target) => alignment switch
    {
        DrawingAlignment.Left => p with { X = target, State = DrawingPlanControlState.Manual }, DrawingAlignment.Right => p with { X = target - p.Width, State = DrawingPlanControlState.Manual },
        DrawingAlignment.Top => p with { Y = target, State = DrawingPlanControlState.Manual }, DrawingAlignment.Bottom => p with { Y = target - p.Height, State = DrawingPlanControlState.Manual },
        DrawingAlignment.HorizontalCenter => p with { X = target - p.Width / 2, State = DrawingPlanControlState.Manual }, DrawingAlignment.VerticalCenter => p with { Y = target - p.Height / 2, State = DrawingPlanControlState.Manual }, _ => p
    };

    private static IReadOnlyList<DrawingPlacement> SelectPlacements(DrawingPlanDocument plan, IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) throw new InvalidOperationException("At least one placement is required."); var set = ids.ToHashSet(StringComparer.Ordinal); var selected = plan.Placements.Where(x => set.Contains(x.RepresentationId)).ToList(); if (selected.Count != set.Count) throw new InvalidOperationException("Placement selection contains unknown ID."); if (selected.Any(x => x.State == DrawingPlanControlState.Locked)) throw new InvalidOperationException("Locked placement cannot be edited."); return selected;
    }

    private static DrawingPlanPage Mark(DrawingPlanPage page, DrawingPlanControlState state, ref bool found) { found = true; return page with { OrderState = state }; }
    private static DrawingPlacement EnsureEditable(DrawingPlacement p) { if (p.State == DrawingPlanControlState.Locked) throw new InvalidOperationException("Locked placement cannot be edited."); return p; }
    private static DrawingRoute EnsureEditable(DrawingRoute r) { if (r.State == DrawingPlanControlState.Locked) throw new InvalidOperationException("Locked route cannot be edited."); return r; }
    private static void ValidateOrthogonal(IReadOnlyList<DrawingPoint> points) { for (var i = 1; i < points.Count; i++) if (points[i - 1].X != points[i].X && points[i - 1].Y != points[i].Y) throw new InvalidOperationException("Route geometry must remain orthogonal."); }

    private static DrawingPlanDocument UpdatePlacement(DrawingPlanDocument plan, string id, Func<DrawingPlacement, DrawingPlacement> update) { var found = false; var items = plan.Placements.Select(p => p.RepresentationId == id ? Apply(p, update, ref found) : p).ToArray(); if (!found) throw new InvalidOperationException("Placement not found."); return DrawingPlanJson.Rehash(plan with { Placements = items }); }
    private static DrawingPlanDocument UpdateRoute(DrawingPlanDocument plan, string id, Func<DrawingRoute, DrawingRoute> update) { var found = false; var items = plan.Routes.Select(r => r.RouteId == id ? Apply(r, update, ref found) : r).ToArray(); if (!found) throw new InvalidOperationException("Route not found."); return DrawingPlanJson.Rehash(plan with { Routes = items }); }
    private static T Apply<T>(T value, Func<T, T> update, ref bool found) { found = true; return update(value); }
}
