using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;
using ShapePath = System.Windows.Shapes.Path;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private const double RoutingObstacleMargin = 24d;
    private const double RoutingEndpointClearance = 48d;
    private const double RoutingLaneClearance = 24d;
    private const double RoutingParallelSpacing = 24d;
    private readonly Dictionary<string, Point> _manualRouteWaypoints = new(StringComparer.OrdinalIgnoreCase);
    private bool _routingVisualsUpdating;
    private string? _selectedRouteConnectionId;
    private string? _hoveredRouteConnectionId;
    private Border? _dragRouteHandle;
    private string? _dragRouteConnectionId;
    private Border? _fromEndpointHandle;
    private Border? _toEndpointHandle;
    private RouteEndpointHandleTag? _dragEndpointHandle;
    private long _lastLiveWireDragTick;
    private IReadOnlyList<TopologyTerminalVisualGroup>? _activeRoutingTerminalGroups;
    private IReadOnlyList<Rect>? _activeRoutingObstacles;
    private readonly HashSet<string> _collapsedCableCoreConnectionIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _primaryCableTrunkConnectionIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Draws formal topology connections as orthogonal 90-degree polylines. Automatic routes score
    /// component intersections as a very high penalty so wires prefer clear lanes around components.
    /// A selected route exposes a draggable bend handle for PPT-like manual adjustment.
    /// </summary>
    private void EnsureOrthogonalConnectionVisuals()
    {
        if (_routingVisualsUpdating || _project is null) return;
        _routingVisualsUpdating = true;
        try
        {
            foreach (var legacy in Surface.Children.OfType<Line>().Where(line => line.Tag is string).ToArray())
                legacy.Visibility = Visibility.Collapsed;

            var liveIds = _project.Connections.Select(connection => connection.ConnectionId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _activeRoutingTerminalGroups = _terminalGroupingPolicy.BuildGroups(_project);
            _activeRoutingObstacles = BuildRoutingObstacles(_activeRoutingTerminalGroups);
            var compactCableRoutes = BuildCompactCableRouteMap();
            RefreshVisuallyMatedConnectionIds();
            _collapsedCableCoreConnectionIds.Clear();
            _primaryCableTrunkConnectionIds.Clear();
            foreach (var (connectionId, info) in compactCableRoutes)
            {
                if (info.Index == 0) _primaryCableTrunkConnectionIds.Add(connectionId);
                else _collapsedCableCoreConnectionIds.Add(connectionId);
            }
            foreach (var obsolete in Surface.Children.OfType<Polyline>()
                         .Where(polyline => string.Equals(polyline.Uid, "CI-ORTHOGONAL-ROUTE", StringComparison.Ordinal) &&
                                            polyline.Tag is string id && !liveIds.Contains(id))
                         .ToArray())
                Surface.Children.Remove(obsolete);

            var completedRoutes = new List<IReadOnlyList<Point>>();
            foreach (var connection in _project.Connections.OrderBy(item => item.ConnectionId, StringComparer.OrdinalIgnoreCase))
            {
                if (!TryFindTopologyEndpointMarkerCenter(connection.FromEndpointId, out var start) ||
                    !TryFindTopologyEndpointMarkerCenter(connection.ToEndpointId, out var end))
                    continue;

                var route = FindOrCreateRoutePolyline(connection);
                IReadOnlyList<Point> points;
                if (compactCableRoutes.TryGetValue(connection.ConnectionId, out var compactCable))
                {
                    points = BuildCollapsedMatedCableRoute(start, end);
                }
                else
                {
                    var escapedStart = CalculateEndpointEscapePoint(connection.FromEndpointId, start, end);
                    var escapedEnd = CalculateEndpointEscapePoint(connection.ToEndpointId, end, start);
                    var routedCore = _manualRouteWaypoints.TryGetValue(connection.ConnectionId, out var waypoint)
                        ? BuildManualOrthogonalRoute(escapedStart, escapedEnd, waypoint)
                        : BuildAutomaticOrthogonalRoute(escapedStart, escapedEnd, completedRoutes);
                    points = CompactOrthogonalPoints(
                        new[] { start }.Concat(routedCore).Append(end));
                }
                route.Points = new PointCollection(points);
                route.Stroke = compactCableRoutes.ContainsKey(connection.ConnectionId)
                    ? Brushes.DimGray
                    : ResolveConnectionBrush(connection);
                route.Visibility = _collapsedCableCoreConnectionIds.Contains(connection.ConnectionId) ||
                                   _visuallyMatedConnectionIds.Contains(connection.ConnectionId)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                route.ToolTip = compactCableRoutes.TryGetValue(connection.ConnectionId, out var cableInfo)
                    ? $"Multi-core cable（多芯電纜）｜{cableInfo.Count} cores\n畫面折疊為單一 M12-F ↔ M12-M 對接線；逐芯資料仍保留在 Pin Mapping。"
                    : $"{BuildEndpointTraceLabel(connection.FromEndpointId)} → {BuildEndpointTraceLabel(connection.ToEndpointId)}\nOrthogonal Route（正交折線）\n單擊：選取並顯示折點與兩端改接把手\n雙擊：線路設定";
                Panel.SetZIndex(route, -20);
                completedRoutes.Add(points);
            }

            ApplyConnectionDecorations();
            ApplyRouteEmphasisVisuals();
            UpdateRouteHandle();
        }
        finally
        {
            _activeRoutingObstacles = null;
            _activeRoutingTerminalGroups = null;
            _routingVisualsUpdating = false;
        }
    }

    private Polyline FindOrCreateRoutePolyline(ElectricalConnection connection)
    {
        var route = Surface.Children.OfType<Polyline>().FirstOrDefault(polyline =>
            string.Equals(polyline.Uid, "CI-ORTHOGONAL-ROUTE", StringComparison.Ordinal) &&
            polyline.Tag is string id &&
            string.Equals(id, connection.ConnectionId, StringComparison.OrdinalIgnoreCase));
        if (route is not null) return route;

        route = new Polyline
        {
            Uid = "CI-ORTHOGONAL-ROUTE",
            Tag = connection.ConnectionId,
            StrokeLineJoin = PenLineJoin.Round,
            Cursor = Cursors.Hand,
            ContextMenu = BuildCableAssemblyContextMenu(connection.ConnectionId),
            ToolTip = "Orthogonal Route（正交折線）\n單擊：選取並顯示折點與兩端改接把手\n雙擊：線路設定"
        };
        route.MouseLeftButtonDown += OrthogonalRoute_MouseLeftButtonDown;
        route.MouseEnter += OrthogonalRoute_MouseEnter;
        route.MouseLeave += OrthogonalRoute_MouseLeave;
        Surface.Children.Add(route);
        return route;
    }

    private void OrthogonalRoute_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string connectionId) return;
        _hoveredRouteConnectionId = connectionId;
        ApplyRouteEmphasisVisuals();
    }

    private void OrthogonalRoute_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string connectionId ||
            !string.Equals(_hoveredRouteConnectionId, connectionId, StringComparison.OrdinalIgnoreCase)) return;
        _hoveredRouteConnectionId = null;
        ApplyRouteEmphasisVisuals();
    }

    private void OrthogonalRoute_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string connectionId) return;
        Edge_MouseLeftButtonDown(sender, e);
        if (e.ClickCount == 1)
        {
            ApplyRouteEmphasisVisuals();
            UpdateRouteHandle();
        }
    }

    private void ApplyRouteEmphasisVisuals()
    {
        foreach (var route in Surface.Children.OfType<Polyline>()
                     .Where(item => string.Equals(item.Uid, "CI-ORTHOGONAL-ROUTE", StringComparison.Ordinal) &&
                                    item.Tag is string))
        {
            var connectionId = (string)route.Tag;
            var hovered = string.Equals(_hoveredRouteConnectionId, connectionId, StringComparison.OrdinalIgnoreCase);
            var selected = _selectedTopologyConnectionIds.Contains(connectionId) ||
                           string.Equals(_selectedRouteConnectionId, connectionId, StringComparison.OrdinalIgnoreCase);
            var cableTrunk = _primaryCableTrunkConnectionIds.Contains(connectionId);
            route.StrokeThickness = hovered
                ? cableTrunk ? 6d : 5d
                : selected ? cableTrunk ? 6d : 4d : cableTrunk ? 5d : 3d;
            route.StrokeDashArray = selected && !cableTrunk ? new DoubleCollection { 8d, 3d } : null;
            route.Opacity = string.IsNullOrWhiteSpace(_hoveredRouteConnectionId) || hovered ? 1d : 0.18d;
        }

        foreach (var label in Surface.Children.OfType<TextBlock>()
                     .Where(item => string.Equals(item.Uid, "CI-ROUTE-IDENTITY", StringComparison.Ordinal) &&
                                    item.Tag is TopologyDecorationTag))
        {
            var tag = (TopologyDecorationTag)label.Tag;
            var hovered = !string.IsNullOrWhiteSpace(_hoveredRouteConnectionId) &&
                          tag.ConnectionIds.Contains(_hoveredRouteConnectionId, StringComparer.OrdinalIgnoreCase);
            label.Opacity = string.IsNullOrWhiteSpace(_hoveredRouteConnectionId) || hovered ? 1d : 0.18d;
        }
    }

    private IReadOnlyList<Point> BuildAutomaticOrthogonalRoute(
        Point start,
        Point end,
        IReadOnlyList<IReadOnlyList<Point>> completedRoutes)
    {
        var obstacles = _activeRoutingObstacles ?? BuildRoutingObstacles();

        const double routingGrid = 12d;
        static double SnapToGrid(double value) => Math.Round(value / routingGrid) * routingGrid;

        var xLanes = obstacles.SelectMany(rect => new[] { rect.Left - RoutingLaneClearance, rect.Right + RoutingLaneClearance })
            .Append((start.X + end.X) / 2d)
            .Select(SnapToGrid)
            .Where(x => x >= 0d && x <= Surface.Width)
            .Distinct()
            .ToArray();
        var yLanes = obstacles.SelectMany(rect => new[] { rect.Top - RoutingLaneClearance, rect.Bottom + RoutingLaneClearance })
            .Append((start.Y + end.Y) / 2d)
            .Select(SnapToGrid)
            .Where(y => y >= 0d && y <= Surface.Height)
            .Distinct()
            .ToArray();

        var candidates = new List<IReadOnlyList<Point>>
        {
            CompactOrthogonalPoints([start, new Point(end.X, start.Y), end]),
            CompactOrthogonalPoints([start, new Point(start.X, end.Y), end])
        };
        foreach (var x in xLanes)
            candidates.Add(CompactOrthogonalPoints([start, new Point(x, start.Y), new Point(x, end.Y), end]));
        foreach (var y in yLanes)
            candidates.Add(CompactOrthogonalPoints([start, new Point(start.X, y), new Point(end.X, y), end]));

        // Two-lane doglegs solve the common schematic case where a single Z-shaped route cannot
        // avoid both a component and an existing wire bundle. Limit to nearby grid lanes so routing
        // remains fast even for large BOMs.
        var nearbyXLanes = xLanes.OrderBy(x => Math.Abs(x - (start.X + end.X) / 2d)).Take(12).ToArray();
        var nearbyYLanes = yLanes.OrderBy(y => Math.Abs(y - (start.Y + end.Y) / 2d)).Take(12).ToArray();
        foreach (var x in nearbyXLanes)
        foreach (var y in nearbyYLanes)
        {
            candidates.Add(CompactOrthogonalPoints([
                start,
                new Point(x, start.Y),
                new Point(x, y),
                new Point(end.X, y),
                end
            ]));
            candidates.Add(CompactOrthogonalPoints([
                start,
                new Point(start.X, y),
                new Point(x, y),
                new Point(x, end.Y),
                end
            ]));
        }

        return candidates
            .OrderBy(candidate => ScoreRoute(candidate, obstacles, completedRoutes))
            .ThenBy(candidate => string.Join(";", candidate.Select(point => $"{point.X:F2},{point.Y:F2}")), StringComparer.Ordinal)
            .First();
    }

    private void AutoRouteConnections_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null) return;
        MutationStarting?.Invoke(this, new TopologyMutationEventArgs("Auto layout topology and route all connections"));
        var arrangement = _projection.ArrangeConnectedPlacements(_project);
        Surface.Width = Math.Max(3200d, arrangement.RequiredWidth + 160d);
        Surface.Height = Math.Max(2000d, arrangement.RequiredHeight + 160d);
        _manualRouteWaypoints.Clear();
        _selectedRouteConnectionId = null;
        _hoveredRouteConnectionId = null;
        RemoveRouteHandle();
        Render();
        SelectionText.Text = $"已排版 {arrangement.NodeCount} 個元件 / {_project.Connections.Count} 條線路";
        HintBanner.Visibility = Visibility.Visible;
        HintText.Text = $"自動排版完成：{arrangement.GraphGroupCount} 個連線群組、{arrangement.LayerCount} 層。元件已依接線方向排列，線路已重新配置水平／垂直通道；不滿意可按 Undo 復原。";
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<Point> BuildManualOrthogonalRoute(Point start, Point end, Point waypoint) =>
        CompactOrthogonalPoints([
            start,
            new Point(waypoint.X, start.Y),
            waypoint,
            new Point(end.X, waypoint.Y),
            end
        ]);

    private Dictionary<string, CompactCableRouteInfo> BuildCompactCableRouteMap()
    {
        if (_project is null) return [];
        var result = new Dictionary<string, CompactCableRouteInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in _project.Connections
                     .Where(connection => !string.IsNullOrWhiteSpace(connection.CableInstanceId) &&
                                          !string.IsNullOrWhiteSpace(connection.CableCoreId))
                     .GroupBy(connection => connection.CableInstanceId!, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group
                .OrderBy(connection => int.TryParse(connection.CableCoreId, out var core) ? core : int.MaxValue)
                .ThenBy(connection => connection.CableCoreId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(connection => connection.ConnectionId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (ordered.Length < 2) continue;

            var fromOwners = ordered.Select(connection => FindTopologyEndpointOwner(connection.FromEndpointId))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var toOwners = ordered.Select(connection => FindTopologyEndpointOwner(connection.ToEndpointId))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (fromOwners.Length != 1 || toOwners.Length != 1 ||
                string.IsNullOrWhiteSpace(fromOwners[0]) || string.IsNullOrWhiteSpace(toOwners[0]))
                continue;
            var fromComponent = _project.Components.FirstOrDefault(component =>
                string.Equals(component.ComponentInstanceId, fromOwners[0], StringComparison.OrdinalIgnoreCase));
            var toComponent = _project.Components.FirstOrDefault(component =>
                string.Equals(component.ComponentInstanceId, toOwners[0], StringComparison.OrdinalIgnoreCase));
            if (!string.Equals(fromComponent?.TypeKey, "INLINE_CONNECTOR", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(toComponent?.TypeKey, "INLINE_CONNECTOR", StringComparison.OrdinalIgnoreCase))
                continue;

            for (var index = 0; index < ordered.Length; index++)
                result[ordered[index].ConnectionId] = new CompactCableRouteInfo(index, ordered.Length);
        }
        return result;
    }

    private static IReadOnlyList<Point> BuildCollapsedMatedCableRoute(Point start, Point end)
    {
        if (Math.Abs(end.X - start.X) >= Math.Abs(end.Y - start.Y))
        {
            var middleX = (start.X + end.X) / 2d;
            return CompactOrthogonalPoints([
                start,
                new Point(middleX, start.Y),
                new Point(middleX, end.Y),
                end
            ]);
        }

        var middleY = (start.Y + end.Y) / 2d;
        return CompactOrthogonalPoints([
            start,
            new Point(start.X, middleY),
            new Point(end.X, middleY),
            end
        ]);
    }

    private List<Rect> BuildRoutingObstacles(
        IReadOnlyList<TopologyTerminalVisualGroup>? knownTerminalGroups = null)
    {
        if (_project is null) return [];

        var terminalGroups = knownTerminalGroups ?? _terminalGroupingPolicy.BuildGroups(_project);
        var groupedIds = terminalGroups
            .SelectMany(group => group.ComponentInstanceIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var obstacles = _project.TopologyPlacements
            .Where(placement => !groupedIds.Contains(placement.ObjectId))
            .Select(TopologyPortGeometry.CalculateVisualBounds)
            .Select(bounds => ExpandRoutingObstacle(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height)))
            .ToList();
        obstacles.AddRange(terminalGroups.Select(group => ExpandRoutingObstacle(new Rect(
            group.Bounds.X,
            group.Bounds.Y,
            group.Bounds.Width,
            group.Bounds.Height))));
        return obstacles;
    }

    private static Rect ExpandRoutingObstacle(Rect bounds) => new(
        bounds.X - RoutingObstacleMargin,
        bounds.Y - RoutingObstacleMargin,
        bounds.Width + RoutingObstacleMargin * 2,
        bounds.Height + RoutingObstacleMargin * 2);

    private Point CalculateEndpointEscapePoint(string endpointId, Point anchor, Point otherAnchor)
    {
        if (_project is null) return anchor;
        var ownerId = FindTopologyEndpointOwner(endpointId);
        var placement = _project.TopologyPlacements.FirstOrDefault(item =>
            string.Equals(item.ObjectId, ownerId, StringComparison.OrdinalIgnoreCase));
        if (placement is null) return anchor;

        var placementBounds = TopologyPortGeometry.CalculateVisualBounds(placement);
        var visualBounds = new Rect(
            placementBounds.X,
            placementBounds.Y,
            placementBounds.Width,
            placementBounds.Height);
        var terminalGroup = (_activeRoutingTerminalGroups ?? _terminalGroupingPolicy.BuildGroups(_project))
            .FirstOrDefault(group => group.ComponentInstanceIds.Contains(ownerId, StringComparer.OrdinalIgnoreCase));
        var bounds = terminalGroup is null
            ? visualBounds
            : new Rect(
                terminalGroup.Bounds.X,
                terminalGroup.Bounds.Y,
                terminalGroup.Bounds.Width,
                terminalGroup.Bounds.Height);
        var left = bounds.X;
        var right = bounds.X + bounds.Width;
        var top = bounds.Y;
        var bottom = bounds.Y + bounds.Height;
        var distances = new[]
        {
            (Distance: Math.Abs(anchor.X - left), Side: EndpointEscapeSide.Left),
            (Distance: Math.Abs(anchor.X - right), Side: EndpointEscapeSide.Right),
            (Distance: Math.Abs(anchor.Y - top), Side: EndpointEscapeSide.Top),
            (Distance: Math.Abs(anchor.Y - bottom), Side: EndpointEscapeSide.Bottom)
        };
        var nearest = distances.OrderBy(item => item.Distance).First();
        var side = nearest.Distance <= 10d
            ? nearest.Side
            : Math.Abs(otherAnchor.X - anchor.X) >= Math.Abs(otherAnchor.Y - anchor.Y)
                ? otherAnchor.X >= anchor.X ? EndpointEscapeSide.Right : EndpointEscapeSide.Left
                : otherAnchor.Y >= anchor.Y ? EndpointEscapeSide.Bottom : EndpointEscapeSide.Top;

        return side switch
        {
            EndpointEscapeSide.Left => new Point(left - RoutingEndpointClearance, anchor.Y),
            EndpointEscapeSide.Right => new Point(right + RoutingEndpointClearance, anchor.Y),
            EndpointEscapeSide.Top => new Point(anchor.X, top - RoutingEndpointClearance),
            EndpointEscapeSide.Bottom => new Point(anchor.X, bottom + RoutingEndpointClearance),
            _ => anchor
        };
    }

    private string? FindTopologyEndpointOwner(string endpointId)
    {
        if (_project is null) return null;
        foreach (var component in _project.Components)
        foreach (var port in component.Ports)
        {
            if (string.Equals(port.PortId, endpointId, StringComparison.OrdinalIgnoreCase))
                return component.ComponentInstanceId;
            if (port.Pins.Any(pin => string.Equals(pin.PinId, endpointId, StringComparison.OrdinalIgnoreCase)))
                return component.ComponentInstanceId;
        }

        foreach (var block in _project.TerminalBlocks)
        foreach (var point in block.Positions.SelectMany(position => position.Levels).SelectMany(level => level.ConnectionPoints))
            if (string.Equals(point.ConnectionPointId, endpointId, StringComparison.OrdinalIgnoreCase))
                return block.TerminalBlockId;
        return null;
    }

    private static double ScoreRoute(
        IReadOnlyList<Point> points,
        IReadOnlyList<Rect> obstacles,
        IReadOnlyList<IReadOnlyList<Point>> completedRoutes)
    {
        var obstacleIntersections = 0;
        var routeCrossings = 0;
        var length = 0d;
        for (var index = 0; index < points.Count - 1; index++)
        {
            var a = points[index];
            var b = points[index + 1];
            length += Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
            obstacleIntersections += obstacles.Count(obstacle => OrthogonalSegmentIntersectsRect(a, b, obstacle));
            routeCrossings += completedRoutes.Sum(route => CountRouteConflicts(a, b, route));
        }
        var bends = Math.Max(0, points.Count - 2);
        return obstacleIntersections * 1_000_000_000d + routeCrossings * 1_000_000d + length * 10d + bends * 25d;
    }

    private static int CountRouteConflicts(Point a, Point b, IReadOnlyList<Point> route)
    {
        var count = 0;
        for (var index = 0; index < route.Count - 1; index++)
        {
            var c = route[index];
            var d = route[index + 1];
            if (TryGetProperOrthogonalIntersection(a, b, c, d, out _)) count++;
            else if (HasCollinearOverlap(a, b, c, d)) count += 8;
            else if (HasNearbyParallelOverlap(a, b, c, d)) count += 2;
        }
        return count;
    }

    private static bool HasCollinearOverlap(Point a, Point b, Point c, Point d)
    {
        const double epsilon = 0.5;
        const double minimumOverlap = 8d;
        var bothHorizontal = Math.Abs(a.Y - b.Y) < epsilon && Math.Abs(c.Y - d.Y) < epsilon &&
                             Math.Abs(a.Y - c.Y) < epsilon;
        if (bothHorizontal)
            return Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) -
                   Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) > minimumOverlap;

        var bothVertical = Math.Abs(a.X - b.X) < epsilon && Math.Abs(c.X - d.X) < epsilon &&
                           Math.Abs(a.X - c.X) < epsilon;
        return bothVertical && Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) -
               Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) > minimumOverlap;
    }

    private static bool HasNearbyParallelOverlap(Point a, Point b, Point c, Point d)
    {
        const double epsilon = 0.5d;
        const double minimumOverlap = 8d;
        var bothHorizontal = Math.Abs(a.Y - b.Y) < epsilon && Math.Abs(c.Y - d.Y) < epsilon;
        if (bothHorizontal && Math.Abs(a.Y - c.Y) < RoutingParallelSpacing)
        {
            var overlap = Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) -
                          Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
            return overlap > minimumOverlap;
        }

        var bothVertical = Math.Abs(a.X - b.X) < epsilon && Math.Abs(c.X - d.X) < epsilon;
        if (!bothVertical || Math.Abs(a.X - c.X) >= RoutingParallelSpacing) return false;
        return Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) -
               Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) > minimumOverlap;
    }

    private static bool OrthogonalSegmentIntersectsRect(Point a, Point b, Rect rect)
    {
        const double epsilon = 0.01;
        if (Math.Abs(a.X - b.X) < epsilon)
        {
            var minY = Math.Min(a.Y, b.Y);
            var maxY = Math.Max(a.Y, b.Y);
            return a.X > rect.Left + epsilon && a.X < rect.Right - epsilon &&
                   maxY > rect.Top + epsilon && minY < rect.Bottom - epsilon;
        }

        if (Math.Abs(a.Y - b.Y) < epsilon)
        {
            var minX = Math.Min(a.X, b.X);
            var maxX = Math.Max(a.X, b.X);
            return a.Y > rect.Top + epsilon && a.Y < rect.Bottom - epsilon &&
                   maxX > rect.Left + epsilon && minX < rect.Right - epsilon;
        }
        return true;
    }

    private static IReadOnlyList<Point> CompactOrthogonalPoints(IEnumerable<Point> source)
    {
        var points = new List<Point>();
        foreach (var point in source)
        {
            if (points.Count > 0 && Distance(points[^1], point) < 0.5) continue;
            points.Add(point);
        }

        for (var index = points.Count - 2; index > 0; index--)
        {
            var previous = points[index - 1];
            var current = points[index];
            var next = points[index + 1];
            var sameX = Math.Abs(previous.X - current.X) < 0.5 && Math.Abs(current.X - next.X) < 0.5;
            var sameY = Math.Abs(previous.Y - current.Y) < 0.5 && Math.Abs(current.Y - next.Y) < 0.5;
            if (sameX || sameY) points.RemoveAt(index);
        }
        return points;
    }

    private static double Distance(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private Brush ResolveConnectionBrush(ElectricalConnection connection)
    {
        if (_project is null) return Brushes.Gray;
        var potential = TopologyConnectionPotentialClassifier.Classify(_project, connection);
        if (potential == TopologyPotentialClass.PositiveDc) return Brushes.Firebrick;
        if (potential == TopologyPotentialClass.NegativeOrReturnDc) return Brushes.RoyalBlue;
        if (potential == TopologyPotentialClass.ProtectiveOrFunctionalEarth) return Brushes.ForestGreen;

        if (!string.IsNullOrWhiteSpace(connection.NetId))
        {
            var net = _project.Nets.FirstOrDefault(candidate =>
                string.Equals(candidate.NetId, connection.NetId, StringComparison.OrdinalIgnoreCase));
            if (net is not null) return LayerBrush(net.Layer);
        }

        return LayerBrush(TopologyElectricalContinuity.ResolveLayer(_project, connection));
    }

    private void ApplyConnectionDecorations()
    {
        if (_project is null) return;
        foreach (var decoration in Surface.Children.OfType<FrameworkElement>()
                     .Where(element => element.Uid.StartsWith("CI-CROSSING-", StringComparison.Ordinal) ||
                                       string.Equals(element.Uid, "CI-JUNCTION-DOT", StringComparison.Ordinal) ||
                                       string.Equals(element.Uid, "CI-ROUTE-IDENTITY", StringComparison.Ordinal))
                     .ToArray())
            Surface.Children.Remove(decoration);

        var routes = Surface.Children.OfType<Polyline>()
            .Where(route => string.Equals(route.Uid, "CI-ORTHOGONAL-ROUTE", StringComparison.Ordinal) &&
                            route.Tag is string id &&
                            !_collapsedCableCoreConnectionIds.Contains(id) &&
                            !_visuallyMatedConnectionIds.Contains(id) &&
                            route.Points.Count >= 2)
            .OrderBy(route => (string)route.Tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        for (var firstIndex = 0; firstIndex < routes.Length; firstIndex++)
        for (var secondIndex = firstIndex + 1; secondIndex < routes.Length; secondIndex++)
        {
            var first = routes[firstIndex];
            var second = routes[secondIndex];
            for (var firstSegment = 0; firstSegment < first.Points.Count - 1; firstSegment++)
            for (var secondSegment = 0; secondSegment < second.Points.Count - 1; secondSegment++)
            {
                var a = first.Points[firstSegment];
                var b = first.Points[firstSegment + 1];
                var c = second.Points[secondSegment];
                var d = second.Points[secondSegment + 1];
                if (!TryGetProperOrthogonalIntersection(a, b, c, d, out var crossing)) continue;
                var key = $"{Math.Round(crossing.X, 2):F2}:{Math.Round(crossing.Y, 2):F2}";
                if (!emitted.Add(key)) continue;

                var firstIsHorizontal = Math.Abs(a.Y - b.Y) < 0.5;
                var overRoute = firstIsHorizontal ? first : second;
                AddCrossingBridge(crossing, overRoute, first, second);
            }
        }

        AddJunctionDots(routes);
        AddRouteIdentityLabels(routes);
    }

    private void AddRouteIdentityLabels(IReadOnlyList<Polyline> routes)
    {
        if (_project is null) return;
        var occupiedLabels = new List<Rect>();
        var emittedCableLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var componentBounds = _project.TopologyPlacements
            .Select(placement => new Rect(
                placement.X - 4d,
                placement.Y - 4d,
                placement.Width + 8d,
                placement.Height + 8d))
            .ToArray();
        foreach (var route in routes)
        {
            var connectionId = (string)route.Tag;
            var connection = _project.Connections.FirstOrDefault(item =>
                string.Equals(item.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase));
            if (connection is null) continue;
            IReadOnlyList<string> labelConnectionIds = [connectionId];
            if (!string.IsNullOrWhiteSpace(connection.CableInstanceId))
            {
                var cableConnectionIds = _project.Connections
                    .Where(item => string.Equals(
                        item.CableInstanceId,
                        connection.CableInstanceId,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.ConnectionId)
                    .ToArray();
                if (cableConnectionIds.Length > 1)
                {
                    if (!emittedCableLabels.Add(connection.CableInstanceId)) continue;
                    labelConnectionIds = cableConnectionIds;
                }
            }
            var text = BuildRouteIdentityLabel(connection);
            var segments = Enumerable.Range(0, route.Points.Count - 1)
                .Select(index => new
                {
                    A = route.Points[index],
                    B = route.Points[index + 1],
                    Length = Distance(route.Points[index], route.Points[index + 1]),
                    Horizontal = Math.Abs(route.Points[index].Y - route.Points[index + 1].Y) < 0.5
                })
                .OrderByDescending(segment => segment.Horizontal)
                .ThenByDescending(segment => segment.Length)
                .ToArray();

            // Labels deliberately sit beside the longest clear segment, never on a wire.  They are
            // transparent too, so an unusually dense drawing still leaves every conductor visible.
            const double labelHeight = 13d;
            var labelWidth = Math.Clamp(text.Length * 5.5d, 38d, 112d);
            var candidates = new List<Rect>();
            foreach (var segment in segments)
            {
                var midpoint = new Point((segment.A.X + segment.B.X) / 2d, (segment.A.Y + segment.B.Y) / 2d);
                if (segment.Horizontal)
                {
                    candidates.Add(ClampRouteLabelBounds(new Rect(midpoint.X - labelWidth / 2d, midpoint.Y - labelHeight - 5d, labelWidth, labelHeight)));
                    candidates.Add(ClampRouteLabelBounds(new Rect(midpoint.X - labelWidth / 2d, midpoint.Y + 5d, labelWidth, labelHeight)));
                }
                else
                {
                    candidates.Add(ClampRouteLabelBounds(new Rect(midpoint.X + 5d, midpoint.Y - labelHeight / 2d, labelWidth, labelHeight)));
                    candidates.Add(ClampRouteLabelBounds(new Rect(midpoint.X - labelWidth - 5d, midpoint.Y - labelHeight / 2d, labelWidth, labelHeight)));
                }
            }

            var bounds = candidates
                .OrderBy(candidate => ScoreRouteLabelBounds(candidate, routes, componentBounds, occupiedLabels))
                .ThenBy(candidate => candidate.Top)
                .ThenBy(candidate => candidate.Left)
                .FirstOrDefault();
            if (bounds.IsEmpty)
                bounds = new Rect(0, 0, labelWidth, labelHeight);

            var label = new TextBlock
            {
                Uid = "CI-ROUTE-IDENTITY",
                Tag = new TopologyDecorationTag(labelConnectionIds, RequireAllConnectionsVisible: true),
                Text = text,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = route.Stroke,
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                Opacity = route.Opacity,
                IsHitTestVisible = false,
                ToolTip = $"{BuildEndpointTraceLabel(connection.FromEndpointId)} → {BuildEndpointTraceLabel(connection.ToEndpointId)}"
            };
            Canvas.SetLeft(label, bounds.Left);
            Canvas.SetTop(label, bounds.Top);
            Panel.SetZIndex(label, -7);
            Surface.Children.Add(label);
            occupiedLabels.Add(bounds);
        }
    }

    private string BuildRouteIdentityLabel(ElectricalConnection connection)
    {
        if (_project is not null && !string.IsNullOrWhiteSpace(connection.CableInstanceId))
        {
            var cable = _project.Cables.FirstOrDefault(item =>
                string.Equals(item.CableInstanceId, connection.CableInstanceId, StringComparison.OrdinalIgnoreCase));
            if (cable is not null && !string.IsNullOrWhiteSpace(cable.DisplayName))
                return cable.DisplayName;

            if (cable is not null &&
                !string.Equals(cable.CableDefinitionId, "UNRESOLVED-CABLE", StringComparison.OrdinalIgnoreCase))
            {
                var material = _availableCableMaterials.FirstOrDefault(item =>
                    string.Equals(item.CableDefinitionId, cable.CableDefinitionId, StringComparison.OrdinalIgnoreCase));
                if (material is not null) return $"{material.Manufacturer} {material.Model}".Trim();
            }
        }

        return $"{BuildEndpointShortLabel(connection.FromEndpointId)} → {BuildEndpointShortLabel(connection.ToEndpointId)}";
    }

    private string BuildEndpointShortLabel(string endpointId)
    {
        if (_project is null) return endpointId;
        foreach (var component in _project.Components)
        foreach (var port in component.Ports)
        {
            if (string.Equals(port.PortId, endpointId, StringComparison.OrdinalIgnoreCase))
                return port.Name;
            var pin = port.Pins.FirstOrDefault(item =>
                string.Equals(item.PinId, endpointId, StringComparison.OrdinalIgnoreCase));
            if (pin is not null)
                return pin.PinName ?? pin.Function ?? pin.PinNumber;
        }

        foreach (var block in _project.TerminalBlocks)
        {
            var owns = block.Positions.SelectMany(position => position.Levels)
                .SelectMany(level => level.ConnectionPoints)
                .Any(point => string.Equals(point.ConnectionPointId, endpointId, StringComparison.OrdinalIgnoreCase));
            if (owns) return block.ReferenceDesignator;
        }
        return endpointId;
    }

    private Rect ClampRouteLabelBounds(Rect bounds)
    {
        var width = Surface.Width;
        var height = Surface.Height;
        var x = bounds.Left;
        var y = bounds.Top;
        if (!double.IsNaN(width) && !double.IsInfinity(width))
            x = Math.Clamp(x, 0d, Math.Max(0d, width - bounds.Width));
        if (!double.IsNaN(height) && !double.IsInfinity(height))
            y = Math.Clamp(y, 0d, Math.Max(0d, height - bounds.Height));
        return new Rect(x, y, bounds.Width, bounds.Height);
    }

    private static int ScoreRouteLabelBounds(
        Rect candidate,
        IReadOnlyList<Polyline> routes,
        IReadOnlyList<Rect> componentBounds,
        IReadOnlyList<Rect> occupiedLabels)
    {
        var score = componentBounds.Count(bound => bound.IntersectsWith(candidate)) * 10_000;
        score += occupiedLabels.Count(bound => bound.IntersectsWith(candidate)) * 1_000;
        foreach (var route in routes)
        for (var index = 0; index < route.Points.Count - 1; index++)
            if (OrthogonalSegmentIntersectsRect(route.Points[index], route.Points[index + 1], candidate))
                score += 100;
        return score;
    }

    private string BuildEndpointTraceLabel(string endpointId)
    {
        if (_project is null) return endpointId;
        foreach (var component in _project.Components)
        foreach (var port in component.Ports)
        {
            var owner = component.ReferenceDesignator ?? component.EquipmentTag ?? component.DisplayName ?? component.ComponentInstanceId;
            if (string.Equals(port.PortId, endpointId, StringComparison.OrdinalIgnoreCase))
                return $"{owner}.{port.Name}";
            var pin = port.Pins.FirstOrDefault(item =>
                string.Equals(item.PinId, endpointId, StringComparison.OrdinalIgnoreCase));
            if (pin is not null)
                return $"{owner}.{pin.PinName ?? pin.Function ?? pin.PinNumber}";
        }

        foreach (var block in _project.TerminalBlocks)
        {
            var owns = block.Positions.SelectMany(position => position.Levels)
                .SelectMany(level => level.ConnectionPoints)
                .Any(point => string.Equals(point.ConnectionPointId, endpointId, StringComparison.OrdinalIgnoreCase));
            if (owns) return block.ReferenceDesignator;
        }
        return endpointId;
    }

    private void AddCrossingBridge(Point crossing, Polyline overRoute, Polyline first, Polyline second)
    {
        var ids = new[] { (string)first.Tag, (string)second.Tag };
        var tag = new TopologyDecorationTag(ids, RequireAllConnectionsVisible: true);
        var gap = new Border
        {
            Uid = "CI-CROSSING-GAP",
            Tag = tag,
            Width = 16,
            Height = 10,
            Background = Brushes.White,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(gap, crossing.X - gap.Width / 2d);
        Canvas.SetTop(gap, crossing.Y - gap.Height / 2d);
        Panel.SetZIndex(gap, -10);
        Surface.Children.Add(gap);

        // Put the under-passing conductor back over the small white clearing.  The arc is then the
        // only visual interruption, which makes this read as a non-connected crossing rather than a cut wire.
        var underRoute = ReferenceEquals(overRoute, first) ? second : first;
        var underpass = new Line
        {
            Uid = "CI-CROSSING-UNDERPASS",
            Tag = tag,
            X1 = crossing.X,
            Y1 = crossing.Y - 8d,
            X2 = crossing.X,
            Y2 = crossing.Y + 8d,
            Stroke = underRoute.Stroke,
            StrokeThickness = underRoute.StrokeThickness,
            IsHitTestVisible = false
        };
        Panel.SetZIndex(underpass, -9);
        Surface.Children.Add(underpass);

        var figure = new PathFigure { StartPoint = new Point(crossing.X - 7, crossing.Y) };
        figure.Segments.Add(new ArcSegment(
            new Point(crossing.X + 7, crossing.Y),
            new Size(7, 7),
            0,
            false,
            SweepDirection.Counterclockwise,
            true));
        var bridge = new ShapePath
        {
            Uid = "CI-CROSSING-BRIDGE",
            Tag = tag,
            Data = new PathGeometry([figure]),
            Stroke = overRoute.Stroke,
            StrokeThickness = overRoute.StrokeThickness,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false
        };
        Panel.SetZIndex(bridge, -8);
        Surface.Children.Add(bridge);
    }

    private void AddJunctionDots(IReadOnlyList<Polyline> routes)
    {
        if (_project is null) return;
        foreach (var block in _project.TerminalBlocks)
        {
            var pointIds = block.Positions.SelectMany(position => position.Levels)
                .SelectMany(level => level.ConnectionPoints)
                .Select(point => point.ConnectionPointId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var connectionIds = _project.Connections
                .Where(connection => pointIds.Contains(connection.FromEndpointId) || pointIds.Contains(connection.ToEndpointId))
                .Select(connection => connection.ConnectionId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (connectionIds.Length < 2 || !TryFindTerminalMarkerCenter(block.TerminalBlockId, out var center)) continue;

            var dot = new Ellipse
            {
                Uid = "CI-JUNCTION-DOT",
                Tag = new TopologyDecorationTag(connectionIds, RequireAllConnectionsVisible: false),
                Width = 7,
                Height = 7,
                Fill = Brushes.DarkSlateGray,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(dot, center.X - dot.Width / 2d);
            Canvas.SetTop(dot, center.Y - dot.Height / 2d);
            Panel.SetZIndex(dot, 5_001);
            Surface.Children.Add(dot);
        }
    }

    private static bool TryGetProperOrthogonalIntersection(Point a, Point b, Point c, Point d, out Point intersection)
    {
        const double endpointClearance = 8d;
        var firstHorizontal = Math.Abs(a.Y - b.Y) < 0.5;
        var firstVertical = Math.Abs(a.X - b.X) < 0.5;
        var secondHorizontal = Math.Abs(c.Y - d.Y) < 0.5;
        var secondVertical = Math.Abs(c.X - d.X) < 0.5;
        if ((!firstHorizontal || !secondVertical) && (!firstVertical || !secondHorizontal))
        {
            intersection = default;
            return false;
        }

        var horizontalA = firstHorizontal ? a : c;
        var horizontalB = firstHorizontal ? b : d;
        var verticalA = firstHorizontal ? c : a;
        var verticalB = firstHorizontal ? d : b;
        intersection = new Point(verticalA.X, horizontalA.Y);
        var insideHorizontal = intersection.X > Math.Min(horizontalA.X, horizontalB.X) + endpointClearance &&
                               intersection.X < Math.Max(horizontalA.X, horizontalB.X) - endpointClearance;
        var insideVertical = intersection.Y > Math.Min(verticalA.Y, verticalB.Y) + endpointClearance &&
                             intersection.Y < Math.Max(verticalA.Y, verticalB.Y) - endpointClearance;
        return insideHorizontal && insideVertical;
    }

    private sealed record TopologyDecorationTag(
        IReadOnlyList<string> ConnectionIds,
        bool RequireAllConnectionsVisible);

    private enum EndpointEscapeSide
    {
        Left,
        Right,
        Top,
        Bottom
    }

    private sealed record CompactCableRouteInfo(int Index, int Count);

    private void UpdateRouteHandle()
    {
        if (_project is null || string.IsNullOrWhiteSpace(_selectedRouteConnectionId))
        {
            RemoveRouteHandle();
            return;
        }

        var route = Surface.Children.OfType<Polyline>().FirstOrDefault(polyline =>
            string.Equals(polyline.Uid, "CI-ORTHOGONAL-ROUTE", StringComparison.Ordinal) &&
            polyline.Tag is string id &&
            string.Equals(id, _selectedRouteConnectionId, StringComparison.OrdinalIgnoreCase));
        if (route is null || route.Points.Count < 2)
        {
            RemoveRouteHandle();
            return;
        }

        var point = _manualRouteWaypoints.TryGetValue(_selectedRouteConnectionId, out var manual)
            ? manual
            : route.Points[route.Points.Count / 2];

        if (_dragRouteHandle is null || !ReferenceEquals(_dragRouteHandle.Parent, Surface))
        {
            _dragRouteHandle = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(2),
                Background = Brushes.White,
                BorderBrush = Brushes.DarkOrange,
                BorderThickness = new Thickness(2),
                Cursor = Cursors.SizeAll,
                ToolTip = "拖曳以調整 90° 折線路徑（Manual Bend / 手動折點）"
            };
            _dragRouteHandle.MouseLeftButtonDown += RouteHandle_MouseLeftButtonDown;
            _dragRouteHandle.MouseMove += RouteHandle_MouseMove;
            _dragRouteHandle.MouseLeftButtonUp += RouteHandle_MouseLeftButtonUp;
            Surface.Children.Add(_dragRouteHandle);
        }

        _dragRouteHandle.Tag = _selectedRouteConnectionId;
        Canvas.SetLeft(_dragRouteHandle, Math.Max(0, point.X - 6));
        Canvas.SetTop(_dragRouteHandle, Math.Max(0, point.Y - 6));
        Panel.SetZIndex(_dragRouteHandle, 20_000);
        UpdateEndpointReconnectHandles(route);
    }

    private void UpdateEndpointReconnectHandles(Polyline route)
    {
        if (route.Tag is not string connectionId || route.Points.Count < 2) return;
        _fromEndpointHandle = EnsureEndpointReconnectHandle(_fromEndpointHandle, connectionId, reconnectFrom: true);
        _toEndpointHandle = EnsureEndpointReconnectHandle(_toEndpointHandle, connectionId, reconnectFrom: false);
        PositionEndpointReconnectHandle(_fromEndpointHandle, route.Points[0]);
        PositionEndpointReconnectHandle(_toEndpointHandle, route.Points[^1]);
    }

    private Border EnsureEndpointReconnectHandle(Border? handle, string connectionId, bool reconnectFrom)
    {
        if (handle is null || !ReferenceEquals(handle.Parent, Surface))
        {
            handle = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = Brushes.White,
                BorderBrush = reconnectFrom ? Brushes.Teal : Brushes.MediumVioletRed,
                BorderThickness = new Thickness(3),
                Cursor = Cursors.Cross,
                ToolTip = "拖曳到另一個 Pin / Port，即可改接這一端（線材設定會保留）"
            };
            handle.MouseLeftButtonDown += EndpointReconnectHandle_MouseLeftButtonDown;
            handle.MouseMove += EndpointReconnectHandle_MouseMove;
            handle.MouseLeftButtonUp += EndpointReconnectHandle_MouseLeftButtonUp;
            Surface.Children.Add(handle);
        }
        handle.Tag = new RouteEndpointHandleTag(connectionId, reconnectFrom);
        return handle;
    }

    private static void PositionEndpointReconnectHandle(FrameworkElement handle, Point point)
    {
        Canvas.SetLeft(handle, Math.Max(0, point.X - handle.Width / 2));
        Canvas.SetTop(handle, Math.Max(0, point.Y - handle.Height / 2));
        Panel.SetZIndex(handle, 20_100);
    }

    private void EndpointReconnectHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border handle || handle.Tag is not RouteEndpointHandleTag tag) return;
        _dragEndpointHandle = tag;
        _lastLiveWireDragTick = 0;
        handle.CaptureMouse();
        SelectionText.Text = tag.ReconnectFrom ? "正在拖曳線路 A 端" : "正在拖曳線路 B 端";
        HintText.Text = "把圓形把手放到另一個 Pin / Port 上；放開滑鼠即可改接，Esc 或放在空白處則取消。";
        e.Handled = true;
    }

    private void EndpointReconnectHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragEndpointHandle is not RouteEndpointHandleTag drag || sender is not Border handle ||
            e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(Surface);
        PositionEndpointReconnectHandle(handle, point);

        var now = Environment.TickCount64;
        if (now - _lastLiveWireDragTick >= 16)
        {
            _lastLiveWireDragTick = now;
            UpdateEndpointReconnectPreview(drag, point);
        }
        e.Handled = true;
    }

    private void UpdateEndpointReconnectPreview(RouteEndpointHandleTag drag, Point point)
    {
        var route = FindRoutePolyline(drag.ConnectionId);
        if (route is null || route.Points.Count < 2) return;

        var bounded = new Point(Math.Max(0, point.X), Math.Max(0, point.Y));
        var fixedPoint = drag.ReconnectFrom ? route.Points[^1] : route.Points[0];
        route.Points = new PointCollection(drag.ReconnectFrom
            ? BuildPreviewOrthogonalRoute(bounded, fixedPoint)
            : BuildPreviewOrthogonalRoute(fixedPoint, bounded));
    }

    private void EndpointReconnectHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border handle || _dragEndpointHandle is not RouteEndpointHandleTag drag) return;
        handle.ReleaseMouseCapture();
        _dragEndpointHandle = null;
        var dropPoint = e.GetPosition(Surface);
        try
        {
            if (_project is null || !TryFindReconnectTarget(dropPoint, out var targetSelector, out var targetLabel))
            {
                HintText.Text = "未放在有效 Pin / Port 上，線路沒有變更。";
                EnsureOrthogonalConnectionVisuals();
                e.Handled = true;
                return;
            }

            MutationStarting?.Invoke(this, new TopologyMutationEventArgs(
                $"Reconnect {(drag.ReconnectFrom ? "A" : "B")} endpoint of {drag.ConnectionId} to {targetSelector}"));
            var updated = _terminalJunctions.ReconnectEndpoint(
                _project,
                drag.ConnectionId,
                drag.ReconnectFrom,
                targetSelector);
            _manualRouteWaypoints.Remove(drag.ConnectionId);
            SelectionText.Text = $"Line: {updated.FromEndpointId} → {updated.ToEndpointId}";
            HintText.Text = $"改接完成：{targetLabel}。原線材、線號與其他線路設定均已保留。";
            Render();
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            MessageBox.Show(Window.GetWindow(this), exception.Message, "無法改接線路", MessageBoxButton.OK, MessageBoxImage.Information);
            EnsureOrthogonalConnectionVisuals();
        }
        e.Handled = true;
    }

    private bool TryFindReconnectTarget(Point dropPoint, out string selector, out string label)
    {
        selector = string.Empty;
        label = string.Empty;
        if (_project is null) return false;

        var candidates = new List<(double Distance, string Selector, string Label)>();
        foreach (var marker in Surface.Children.OfType<Border>().Where(item => item.Visibility == Visibility.Visible))
        {
            if (marker.Tag is not string id) continue;
            var left = Canvas.GetLeft(marker);
            var top = Canvas.GetTop(marker);
            if (double.IsNaN(left) || double.IsNaN(top)) continue;
            var center = new Point(left + marker.Width / 2, top + marker.Height / 2);
            var distance = Math.Sqrt(Math.Pow(center.X - dropPoint.X, 2) + Math.Pow(center.Y - dropPoint.Y, 2));
            if (distance > 28) continue;

            if (IsEndpointMarkerVisual(marker) && _endpointConnectionService.IsKnownEndpoint(_project, id))
                candidates.Add((distance, id, DescribeEndpoint(id)));
            else if (Math.Abs(marker.Width - 16) < 0.1 && Math.Abs(marker.Height - 16) < 0.1)
            {
                var block = _project.TerminalBlocks.FirstOrDefault(item =>
                    string.Equals(item.TerminalBlockId, id, StringComparison.OrdinalIgnoreCase));
                if (block is not null)
                    candidates.Add((distance, TopologyTerminalJunctionService.Selector(id), $"{block.ReferenceDesignator} Terminal"));
            }
        }

        var nearest = candidates.OrderBy(item => item.Distance).FirstOrDefault();
        if (nearest.Selector is null) return false;
        selector = nearest.Selector;
        label = nearest.Label;
        return true;
    }

    private void RouteHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border handle || handle.Tag is not string connectionId) return;
        _dragRouteConnectionId = connectionId;
        _lastLiveWireDragTick = 0;
        handle.CaptureMouse();
        MutationStarting?.Invoke(this, new TopologyMutationEventArgs($"Adjust topology route {connectionId}"));
        e.Handled = true;
    }

    private void RouteHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragRouteConnectionId is null || sender is not Border handle || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(Surface);
        var waypoint = new Point(Math.Max(0, point.X), Math.Max(0, point.Y));
        _manualRouteWaypoints[_dragRouteConnectionId] = waypoint;
        PositionRouteHandle(handle, waypoint);

        var now = Environment.TickCount64;
        if (now - _lastLiveWireDragTick >= 16)
        {
            _lastLiveWireDragTick = now;
            UpdateManualRoutePreview(_dragRouteConnectionId, waypoint);
        }
        e.Handled = true;
    }

    private void UpdateManualRoutePreview(string connectionId, Point waypoint)
    {
        if (_project is null) return;
        var connection = _project.Connections.FirstOrDefault(item =>
            string.Equals(item.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase));
        var route = FindRoutePolyline(connectionId);
        if (connection is null || route is null ||
            !TryFindTopologyEndpointMarkerCenter(connection.FromEndpointId, out var start) ||
            !TryFindTopologyEndpointMarkerCenter(connection.ToEndpointId, out var end))
            return;

        route.Points = new PointCollection(BuildManualOrthogonalRoute(start, end, waypoint));
    }

    private Polyline? FindRoutePolyline(string connectionId) =>
        Surface.Children.OfType<Polyline>().FirstOrDefault(polyline =>
            string.Equals(polyline.Uid, "CI-ORTHOGONAL-ROUTE", StringComparison.Ordinal) &&
            polyline.Tag is string id &&
            string.Equals(id, connectionId, StringComparison.OrdinalIgnoreCase));

    private static void PositionRouteHandle(FrameworkElement handle, Point point)
    {
        Canvas.SetLeft(handle, Math.Max(0, point.X - handle.Width / 2));
        Canvas.SetTop(handle, Math.Max(0, point.Y - handle.Height / 2));
        Panel.SetZIndex(handle, 20_000);
    }

    private void RouteHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border handle) handle.ReleaseMouseCapture();
        var changed = _dragRouteConnectionId is not null;
        _dragRouteConnectionId = null;
        if (changed)
        {
            EnsureOrthogonalConnectionVisuals();
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
        e.Handled = true;
    }

    private void RemoveRouteHandle()
    {
        if (_dragRouteHandle is not null && ReferenceEquals(_dragRouteHandle.Parent, Surface))
            Surface.Children.Remove(_dragRouteHandle);
        _dragRouteHandle = null;
        _dragRouteConnectionId = null;
        if (_fromEndpointHandle is not null && ReferenceEquals(_fromEndpointHandle.Parent, Surface))
            Surface.Children.Remove(_fromEndpointHandle);
        if (_toEndpointHandle is not null && ReferenceEquals(_toEndpointHandle.Parent, Surface))
            Surface.Children.Remove(_toEndpointHandle);
        _fromEndpointHandle = null;
        _toEndpointHandle = null;
        _dragEndpointHandle = null;
    }

    private void SyncSelectedRouteHandleVisibility()
    {
        var route = string.IsNullOrWhiteSpace(_selectedRouteConnectionId)
            ? null
            : Surface.Children.OfType<Polyline>().FirstOrDefault(polyline =>
                string.Equals(polyline.Uid, "CI-ORTHOGONAL-ROUTE", StringComparison.Ordinal) &&
                polyline.Tag is string id &&
                string.Equals(id, _selectedRouteConnectionId, StringComparison.OrdinalIgnoreCase));
        var visibility = route?.Visibility ?? Visibility.Collapsed;
        if (_dragRouteHandle is not null) _dragRouteHandle.Visibility = visibility;
        if (_fromEndpointHandle is not null) _fromEndpointHandle.Visibility = visibility;
        if (_toEndpointHandle is not null) _toEndpointHandle.Visibility = visibility;
    }

    private sealed record RouteEndpointHandleTag(string ConnectionId, bool ReconnectFrom);
}
