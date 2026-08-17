using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private readonly Dictionary<string, Point[]> _stableRoutePoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Point> _stableManualRouteWaypoints = new(StringComparer.OrdinalIgnoreCase);
    private ElectricalProject? _routeIsolationProject;
    private bool _routeIsolationConfigured;
    private bool _routeIsolationApplying;
    private bool _routeIsolationReconcileScheduled;
    private int _globalReroutePassesRemaining;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += TopologyRouteIsolation_Loaded;
    }

    private void TopologyRouteIsolation_Loaded(object sender, RoutedEventArgs e)
    {
        if (_routeIsolationConfigured) return;
        _routeIsolationConfigured = true;

        // The normal WPF MouseMove handler on a route/node marks the event handled. handledEventsToo
        // lets this parent run immediately after the child handler so unrelated routes are restored
        // before WPF paints the next frame; this removes the visible "other wires are moving" effect.
        Surface.AddHandler(
            Mouse.MouseMoveEvent,
            new MouseEventHandler(RouteIsolation_SurfaceMouseMove),
            handledEventsToo: true);
        Surface.LayoutUpdated += RouteIsolation_SurfaceLayoutUpdated;
        ProjectChanged += RouteIsolation_ProjectChanged;
        MutationStarting += RouteIsolation_MutationStarting;

        ScheduleRouteIsolationReconcile();
    }

    private void RouteIsolation_MutationStarting(object? sender, TopologyMutationEventArgs e)
    {
        // Only explicit automatic commands receive global routing authority. Ordinary Render,
        // Refresh, line creation, bend dragging, node movement and rotation stay local.
        if (e.Description.StartsWith("Auto arrange topology", StringComparison.OrdinalIgnoreCase) ||
            e.Description.StartsWith("Auto layout topology and route all connections", StringComparison.OrdinalIgnoreCase))
        {
            // Keep the permission alive for a few layout passes because WPF may perform an initial
            // layout while the command is still mutating placements, followed by the final Render.
            _globalReroutePassesRemaining = Math.Max(_globalReroutePassesRemaining, 3);
        }
    }

    private void RouteIsolation_ProjectChanged(object? sender, EventArgs e) =>
        ScheduleRouteIsolationReconcile();

    private void RouteIsolation_SurfaceLayoutUpdated(object? sender, EventArgs e) =>
        ScheduleRouteIsolationReconcile();

    private void RouteIsolation_SurfaceMouseMove(object sender, MouseEventArgs e)
    {
        if (_routingVisualsUpdating || _routeIsolationApplying) return;

        // Synchronous reconciliation is important during dragging. The legacy/full router has
        // already run in the child MouseMove handler; restore every unaffected route now, before
        // the frame is presented to the user.
        if (_dragRouteConnectionId is not null || _dragRecorded || _terminalDragRecorded)
            ReconcileRouteIsolation();
    }

    private void ScheduleRouteIsolationReconcile()
    {
        if (_routeIsolationReconcileScheduled || !IsLoaded) return;
        _routeIsolationReconcileScheduled = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _routeIsolationReconcileScheduled = false;
            ReconcileRouteIsolation();
        }));
    }

    private void ReconcileRouteIsolation()
    {
        if (_routeIsolationApplying || _routingVisualsUpdating || _project is null || Surface is null) return;
        BindRouteIsolationProject();

        var liveConnections = _project.Connections.ToDictionary(
            connection => connection.ConnectionId,
            StringComparer.OrdinalIgnoreCase);
        PruneRouteIsolationState(liveConnections.Keys);

        var routeVisuals = Surface.Children.OfType<Polyline>()
            .Where(route =>
                string.Equals(route.Uid, "CI-ORTHOGONAL-ROUTE", StringComparison.Ordinal) &&
                route.Tag is string id &&
                liveConnections.ContainsKey(id))
            .ToDictionary(route => (string)route.Tag, StringComparer.OrdinalIgnoreCase);
        if (routeVisuals.Count == 0) return;

        _routeIsolationApplying = true;
        try
        {
            var forceGlobal = _globalReroutePassesRemaining > 0;
            var fixedRoutes = new List<IReadOnlyList<Point>>();
            var recompute = new List<(ElectricalConnection Connection, Polyline Route)>();
            var geometryChanged = false;

            foreach (var connection in liveConnections.Values.OrderBy(item => item.ConnectionId, StringComparer.OrdinalIgnoreCase))
            {
                if (!routeVisuals.TryGetValue(connection.ConnectionId, out var route) || route.Points.Count < 2)
                    continue;
                if (!TryFindTopologyEndpointMarkerCenter(connection.FromEndpointId, out var currentStart) ||
                    !TryFindTopologyEndpointMarkerCenter(connection.ToEndpointId, out var currentEnd))
                    continue;

                var hasCachedRoute = _stableRoutePoints.TryGetValue(connection.ConnectionId, out var stable) &&
                                     stable is { Length: >= 2 };
                var manualChanged = HasManualWaypointChanged(connection.ConnectionId);
                var decision = TopologyRouteIsolationPolicy.Decide(
                    forceGlobal,
                    hasCachedRoute,
                    manualChanged,
                    hasCachedRoute ? stable![0].X : 0d,
                    hasCachedRoute ? stable![0].Y : 0d,
                    hasCachedRoute ? stable![^1].X : 0d,
                    hasCachedRoute ? stable![^1].Y : 0d,
                    currentStart.X,
                    currentStart.Y,
                    currentEnd.X,
                    currentEnd.Y);

                switch (decision.Action)
                {
                    case TopologyRouteIsolationAction.Preserve:
                        geometryChanged |= SetRoutePoints(route, stable!);
                        fixedRoutes.Add(stable!);
                        break;

                    case TopologyRouteIsolationAction.Translate:
                    {
                        var translated = stable!
                            .Select(point => new Point(
                                point.X + decision.TranslationX,
                                point.Y + decision.TranslationY))
                            .ToArray();
                        geometryChanged |= SetRoutePoints(route, translated);
                        _stableRoutePoints[connection.ConnectionId] = translated;
                        fixedRoutes.Add(translated);
                        break;
                    }

                    case TopologyRouteIsolationAction.Recompute:
                        recompute.Add((connection, route));
                        break;
                }
            }

            // Recompute only the permitted routes. They are scored against all fixed routes, so a
            // newly drawn line or an incident line moves around the engineer's locked existing wires
            // instead of forcing those existing wires to find new lanes.
            foreach (var item in recompute.OrderBy(item => item.Connection.ConnectionId, StringComparer.OrdinalIgnoreCase))
            {
                var points = BuildIsolatedRoute(item.Connection, fixedRoutes);
                if (points is null) continue;

                geometryChanged |= SetRoutePoints(item.Route, points);
                _stableRoutePoints[item.Connection.ConnectionId] = points;
                fixedRoutes.Add(points);
            }

            foreach (var connectionId in liveConnections.Keys)
                SyncStableManualWaypoint(connectionId);

            if (_globalReroutePassesRemaining > 0)
                _globalReroutePassesRemaining--;

            if (geometryChanged)
            {
                // Crossing bridges and labels are derived visuals. Rebuild them after route isolation
                // has restored the authoritative route geometry so decorations cannot remain at the
                // temporary all-route recalculation positions.
                ApplyConnectionDecorations();
                ApplyRouteEmphasisVisuals();
                UpdateRouteHandle();
            }
        }
        finally
        {
            _routeIsolationApplying = false;
        }
    }

    private Point[]? BuildIsolatedRoute(
        ElectricalConnection connection,
        IReadOnlyList<IReadOnlyList<Point>> fixedRoutes)
    {
        if (!TryFindTopologyEndpointMarkerCenter(connection.FromEndpointId, out var start) ||
            !TryFindTopologyEndpointMarkerCenter(connection.ToEndpointId, out var end))
            return null;

        var escapedStart = CalculateEndpointEscapePoint(connection.FromEndpointId, start, end);
        var escapedEnd = CalculateEndpointEscapePoint(connection.ToEndpointId, end, start);
        var routedCore = _manualRouteWaypoints.TryGetValue(connection.ConnectionId, out var waypoint)
            ? BuildManualOrthogonalRoute(escapedStart, escapedEnd, waypoint)
            : BuildAutomaticOrthogonalRoute(escapedStart, escapedEnd, fixedRoutes);
        return CompactOrthogonalPoints(new[] { start }.Concat(routedCore).Append(end)).ToArray();
    }

    private bool HasManualWaypointChanged(string connectionId)
    {
        var hasCurrent = _manualRouteWaypoints.TryGetValue(connectionId, out var current);
        var hasStable = _stableManualRouteWaypoints.TryGetValue(connectionId, out var stable);
        if (hasCurrent != hasStable) return true;
        if (!hasCurrent) return false;
        return Math.Abs(current.X - stable.X) > 0.5d || Math.Abs(current.Y - stable.Y) > 0.5d;
    }

    private void SyncStableManualWaypoint(string connectionId)
    {
        if (_manualRouteWaypoints.TryGetValue(connectionId, out var waypoint))
            _stableManualRouteWaypoints[connectionId] = waypoint;
        else
            _stableManualRouteWaypoints.Remove(connectionId);
    }

    private static bool SetRoutePoints(Polyline route, IReadOnlyList<Point> points)
    {
        if (route.Points.Count == points.Count)
        {
            var same = true;
            for (var index = 0; index < points.Count; index++)
            {
                if (Math.Abs(route.Points[index].X - points[index].X) <= 0.1d &&
                    Math.Abs(route.Points[index].Y - points[index].Y) <= 0.1d)
                    continue;
                same = false;
                break;
            }
            if (same) return false;
        }

        route.Points = new PointCollection(points);
        return true;
    }

    private void BindRouteIsolationProject()
    {
        if (ReferenceEquals(_routeIsolationProject, _project)) return;

        _routeIsolationProject = _project;
        _stableRoutePoints.Clear();
        _stableManualRouteWaypoints.Clear();
        _manualRouteWaypoints.Clear();
        _globalReroutePassesRemaining = 0;
    }

    private void PruneRouteIsolationState(IEnumerable<string> liveConnectionIds)
    {
        var live = liveConnectionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var obsolete in _stableRoutePoints.Keys.Where(id => !live.Contains(id)).ToArray())
            _stableRoutePoints.Remove(obsolete);
        foreach (var obsolete in _stableManualRouteWaypoints.Keys.Where(id => !live.Contains(id)).ToArray())
            _stableManualRouteWaypoints.Remove(obsolete);
        foreach (var obsolete in _manualRouteWaypoints.Keys.Where(id => !live.Contains(id)).ToArray())
            _manualRouteWaypoints.Remove(obsolete);
    }
}
