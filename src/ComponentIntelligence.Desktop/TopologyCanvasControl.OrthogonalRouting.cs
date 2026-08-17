using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private readonly Dictionary<string, Point> _manualRouteWaypoints = new(StringComparer.OrdinalIgnoreCase);
    private bool _routingVisualsUpdating;
    private string? _selectedRouteConnectionId;
    private Border? _dragRouteHandle;
    private string? _dragRouteConnectionId;

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
            foreach (var obsolete in Surface.Children.OfType<Polyline>()
                         .Where(polyline => string.Equals(polyline.Uid, "CI-ORTHOGONAL-ROUTE", StringComparison.Ordinal) &&
                                            polyline.Tag is string id && !liveIds.Contains(id))
                         .ToArray())
                Surface.Children.Remove(obsolete);

            foreach (var connection in _project.Connections)
            {
                if (!TryFindTopologyEndpointMarkerCenter(connection.FromEndpointId, out var start) ||
                    !TryFindTopologyEndpointMarkerCenter(connection.ToEndpointId, out var end))
                    continue;

                var route = FindOrCreateRoutePolyline(connection);
                var points = _manualRouteWaypoints.TryGetValue(connection.ConnectionId, out var waypoint)
                    ? BuildManualOrthogonalRoute(start, end, waypoint)
                    : BuildAutomaticOrthogonalRoute(connection, start, end);
                route.Points = new PointCollection(points);
                route.Stroke = ResolveConnectionBrush(connection);
                route.StrokeThickness = string.Equals(_selectedRouteConnectionId, connection.ConnectionId, StringComparison.OrdinalIgnoreCase)
                    ? 4d
                    : 3d;
                Panel.SetZIndex(route, -20);
            }

            UpdateRouteHandle();
        }
        finally
        {
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
            ToolTip = "Orthogonal Route（正交折線）\n單擊：選取並顯示拖曳折點\n雙擊：線路設定"
        };
        route.MouseLeftButtonDown += OrthogonalRoute_MouseLeftButtonDown;
        Surface.Children.Add(route);
        return route;
    }

    private void OrthogonalRoute_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string connectionId) return;
        _selectedRouteConnectionId = connectionId;
        Edge_MouseLeftButtonDown(sender, e);
        if (e.ClickCount == 1)
            EnsureOrthogonalConnectionVisuals();
    }

    private IReadOnlyList<Point> BuildAutomaticOrthogonalRoute(
        ElectricalConnection connection,
        Point start,
        Point end)
    {
        if (Math.Abs(start.X - end.X) < 0.5 || Math.Abs(start.Y - end.Y) < 0.5)
            return [start, end];

        var obstacles = BuildRoutingObstacles(connection);
        var minX = obstacles.Count == 0 ? Math.Min(start.X, end.X) : obstacles.Min(rect => rect.Left);
        var maxX = obstacles.Count == 0 ? Math.Max(start.X, end.X) : obstacles.Max(rect => rect.Right);
        var minY = obstacles.Count == 0 ? Math.Min(start.Y, end.Y) : obstacles.Min(rect => rect.Top);
        var maxY = obstacles.Count == 0 ? Math.Max(start.Y, end.Y) : obstacles.Max(rect => rect.Bottom);
        const double clearance = 26d;

        var xLanes = new[]
        {
            (start.X + end.X) / 2d,
            minX - clearance,
            maxX + clearance
        }.Distinct().ToArray();
        var yLanes = new[]
        {
            (start.Y + end.Y) / 2d,
            minY - clearance,
            maxY + clearance
        }.Distinct().ToArray();

        var candidates = new List<IReadOnlyList<Point>>();
        foreach (var x in xLanes)
            candidates.Add(CompactOrthogonalPoints([start, new Point(x, start.Y), new Point(x, end.Y), end]));
        foreach (var y in yLanes)
            candidates.Add(CompactOrthogonalPoints([start, new Point(start.X, y), new Point(end.X, y), end]));

        return candidates
            .OrderBy(candidate => ScoreRoute(candidate, obstacles))
            .First();
    }

    private static IReadOnlyList<Point> BuildManualOrthogonalRoute(Point start, Point end, Point waypoint) =>
        CompactOrthogonalPoints([
            start,
            new Point(waypoint.X, start.Y),
            waypoint,
            new Point(end.X, waypoint.Y),
            end
        ]);

    private List<Rect> BuildRoutingObstacles(ElectricalConnection connection)
    {
        if (_project is null) return [];
        var fromOwner = FindTopologyEndpointOwner(connection.FromEndpointId);
        var toOwner = FindTopologyEndpointOwner(connection.ToEndpointId);
        const double margin = 12d;

        return _project.TopologyPlacements
            .Where(placement =>
                !string.Equals(placement.ObjectId, fromOwner, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(placement.ObjectId, toOwner, StringComparison.OrdinalIgnoreCase))
            .Select(placement => new Rect(
                placement.X - margin,
                placement.Y - margin,
                placement.Width + margin * 2,
                placement.Height + margin * 2))
            .ToList();
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

    private static double ScoreRoute(IReadOnlyList<Point> points, IReadOnlyList<Rect> obstacles)
    {
        var intersections = 0;
        var length = 0d;
        for (var index = 0; index < points.Count - 1; index++)
        {
            var a = points[index];
            var b = points[index + 1];
            length += Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
            intersections += obstacles.Count(obstacle => OrthogonalSegmentIntersectsRect(a, b, obstacle));
        }
        return intersections * 1_000_000d + length;
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
        if (!string.IsNullOrWhiteSpace(connection.NetId))
        {
            var net = _project.Nets.FirstOrDefault(candidate =>
                string.Equals(candidate.NetId, connection.NetId, StringComparison.OrdinalIgnoreCase));
            if (net is not null) return LayerBrush(net.Layer);
        }

        var pinLayer = _project.Components.SelectMany(component => component.Ports)
            .SelectMany(port => port.Pins)
            .Where(pin =>
                string.Equals(pin.PinId, connection.FromEndpointId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pin.PinId, connection.ToEndpointId, StringComparison.OrdinalIgnoreCase))
            .Select(pin => pin.Layer)
            .FirstOrDefault(layer => layer != ElectricalLayer.Unknown);
        return LayerBrush(pinLayer);
    }

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
    }

    private void RouteHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border handle || handle.Tag is not string connectionId) return;
        _dragRouteConnectionId = connectionId;
        handle.CaptureMouse();
        MutationStarting?.Invoke(this, new TopologyMutationEventArgs($"Adjust topology route {connectionId}"));
        e.Handled = true;
    }

    private void RouteHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragRouteConnectionId is null || sender is not Border handle || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(Surface);
        _manualRouteWaypoints[_dragRouteConnectionId] = new Point(Math.Max(0, point.X), Math.Max(0, point.Y));
        EnsureOrthogonalConnectionVisuals();
        e.Handled = true;
    }

    private void RouteHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border handle) handle.ReleaseMouseCapture();
        var changed = _dragRouteConnectionId is not null;
        _dragRouteConnectionId = null;
        if (changed) ProjectChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void RemoveRouteHandle()
    {
        if (_dragRouteHandle is not null && ReferenceEquals(_dragRouteHandle.Parent, Surface))
            Surface.Children.Remove(_dragRouteHandle);
        _dragRouteHandle = null;
        _dragRouteConnectionId = null;
    }
}
