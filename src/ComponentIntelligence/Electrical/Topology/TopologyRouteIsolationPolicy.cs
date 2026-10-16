namespace ComponentIntelligence.Electrical.Topology;

public enum TopologyRouteIsolationAction
{
    Preserve,
    Translate,
    Recompute
}

public readonly record struct TopologyRouteIsolationDecision(
    TopologyRouteIsolationAction Action,
    double TranslationX = 0d,
    double TranslationY = 0d);

/// <summary>
/// Decides whether one existing topology route is allowed to change during an editor refresh.
/// Normal editing is intentionally local: an unrelated route is preserved byte-for-byte at the
/// geometry level; a route whose two endpoints moved together is translated without changing its
/// shape; only a new/edited/incident route is recomputed. Global auto-layout/auto-routing is the
/// explicit escape hatch that permits every route to be recomputed.
/// </summary>
public static class TopologyRouteIsolationPolicy
{
    public static TopologyRouteIsolationDecision Decide(
        bool forceGlobalReroute,
        bool hasCachedRoute,
        bool manualWaypointChanged,
        double cachedStartX,
        double cachedStartY,
        double cachedEndX,
        double cachedEndY,
        double currentStartX,
        double currentStartY,
        double currentEndX,
        double currentEndY,
        double tolerance = 0.5d)
    {
        if (tolerance < 0d) throw new ArgumentOutOfRangeException(nameof(tolerance));

        if (forceGlobalReroute || !hasCachedRoute || manualWaypointChanged)
            return new TopologyRouteIsolationDecision(TopologyRouteIsolationAction.Recompute);

        var startDx = currentStartX - cachedStartX;
        var startDy = currentStartY - cachedStartY;
        var endDx = currentEndX - cachedEndX;
        var endDy = currentEndY - cachedEndY;
        var startMoved = Math.Abs(startDx) > tolerance || Math.Abs(startDy) > tolerance;
        var endMoved = Math.Abs(endDx) > tolerance || Math.Abs(endDy) > tolerance;

        if (!startMoved && !endMoved)
            return new TopologyRouteIsolationDecision(TopologyRouteIsolationAction.Preserve);

        // If both endpoint owners moved by the same delta (for example group movement or a leading
        // canvas expansion), keep the engineer's exact bend geometry and translate the whole route.
        if (startMoved && endMoved &&
            Math.Abs(startDx - endDx) <= tolerance &&
            Math.Abs(startDy - endDy) <= tolerance)
        {
            return new TopologyRouteIsolationDecision(
                TopologyRouteIsolationAction.Translate,
                startDx,
                startDy);
        }

        // One endpoint moved, or the endpoints moved by different deltas. This is an incident route
        // and is the only ordinary case where automatic routing is allowed to choose a new shape.
        return new TopologyRouteIsolationDecision(TopologyRouteIsolationAction.Recompute);
    }
}
