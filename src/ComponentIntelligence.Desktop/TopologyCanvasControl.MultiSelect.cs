using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private readonly HashSet<string> _selectedTopologyObjectIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedTopologyConnectionIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly TopologyTerminalDeletionService _terminalDeletionService = new();
    private Dictionary<string, Point> _terminalDragStartSelectionPositions = new(StringComparer.OrdinalIgnoreCase);
    private Point _marqueeStart;
    private bool _marqueePointerCaptured;
    private bool _marqueeAdditive;
    private Rectangle? _marqueeRectangle;
    private long _lastLiveRouteRefreshTick;

    private void ConfigureMarqueeSelection()
    {
        Surface.PreviewMouseLeftButtonDown += Surface_MarqueeMouseLeftButtonDown;
        Surface.PreviewMouseMove += Surface_MarqueeMouseMove;
        Surface.PreviewMouseLeftButtonUp += Surface_MarqueeMouseLeftButtonUp;
    }

    private void Surface_MarqueeMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_interactionMode != InteractionMode.Select || _project is null || !ReferenceEquals(e.OriginalSource, Surface))
            return;

        _marqueeStart = e.GetPosition(Surface);
        _marqueePointerCaptured = true;
        _marqueeAdditive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (!_marqueeAdditive)
        {
            _selectedTopologyObjectIds.Clear();
            _selectedTopologyConnectionIds.Clear();
            _selectedRouteConnectionId = null;
        }
        RemoveMarqueeRectangle();
        ApplyTopologySelectionVisuals();
        Surface.CaptureMouse();
        e.Handled = true;
    }

    private void Surface_MarqueeMouseMove(object sender, MouseEventArgs e)
    {
        if (!_marqueePointerCaptured || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(Surface);
        if (_marqueeRectangle is null &&
            Math.Abs(current.X - _marqueeStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _marqueeStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _marqueeRectangle ??= CreateMarqueeRectangle();
        var bounds = NormalizeSelectionBounds(_marqueeStart, current);
        _marqueeRectangle.Width = bounds.Width;
        _marqueeRectangle.Height = bounds.Height;
        Canvas.SetLeft(_marqueeRectangle, bounds.Left);
        Canvas.SetTop(_marqueeRectangle, bounds.Top);
        e.Handled = true;
    }

    private void Surface_MarqueeMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_marqueePointerCaptured || _project is null) return;
        var current = e.GetPosition(Surface);
        var bounds = NormalizeSelectionBounds(_marqueeStart, current);
        var madeBox = _marqueeRectangle is not null && bounds.Width >= 2d && bounds.Height >= 2d;

        Surface.ReleaseMouseCapture();
        _marqueePointerCaptured = false;
        RemoveMarqueeRectangle();

        if (madeBox)
        {
            foreach (var placement in _project.TopologyPlacements)
            {
                if (FindTopologyNodeVisual(placement.ObjectId) is null) continue;
                var objectBounds = new Rect(placement.X, placement.Y, placement.Width, placement.Height);
                // A narrow box drawn around wires may cross a large component body. Only treat a
                // component as selected when the marquee contains the entire component rectangle.
                if (bounds.Contains(objectBounds))
                    _selectedTopologyObjectIds.Add(placement.ObjectId);
            }

            foreach (var route in Surface.Children.OfType<Polyline>().Where(route =>
                         string.Equals(route.Uid, "CI-ORTHOGONAL-ROUTE", StringComparison.Ordinal) &&
                         route.Tag is string))
            {
                if (RouteIntersectsSelectionBounds(route.Points, bounds))
                    _selectedTopologyConnectionIds.Add((string)route.Tag);
            }
            _selectedRouteConnectionId = _selectedTopologyConnectionIds.LastOrDefault();
        }
        else if (!_marqueeAdditive)
        {
            _selectedTopologyObjectIds.Clear();
            _selectedTopologyConnectionIds.Clear();
            _selectedRouteConnectionId = null;
        }

        ApplyTopologySelectionVisuals();
        var selectedCount = _selectedTopologyObjectIds.Count + _selectedTopologyConnectionIds.Count;
        SelectionText.Text = selectedCount == 0
            ? "未選取"
            : $"已選取 {_selectedTopologyObjectIds.Count} 個物件、{_selectedTopologyConnectionIds.Count} 條線";
        HintText.Text = _selectedTopologyConnectionIds.Count > 0
            ? "已框選線路與元件。按 Del 可刪除線路；一般元件移回清單，端子台會從專案刪除。"
            : _selectedTopologyObjectIds.Count > 1
                ? "已完成框選。拖曳可整組移動；按 Del 將一般元件移回清單，端子台則直接刪除。"
                : "在空白處按住左鍵拖曳，可框選元件與線路。";
        e.Handled = true;
    }

    private Rectangle CreateMarqueeRectangle()
    {
        var rectangle = new Rectangle
        {
            Uid = "CI-MARQUEE-SELECTION",
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(32, 30, 144, 255)),
            IsHitTestVisible = false
        };
        Panel.SetZIndex(rectangle, 30_000);
        Surface.Children.Add(rectangle);
        return rectangle;
    }

    private void RemoveMarqueeRectangle()
    {
        if (_marqueeRectangle is null) return;
        Surface.Children.Remove(_marqueeRectangle);
        _marqueeRectangle = null;
    }

    private bool SelectTopologyObjectForDrag(string objectId)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl)
        {
            if (!_selectedTopologyObjectIds.Add(objectId))
            {
                _selectedTopologyObjectIds.Remove(objectId);
                ApplyTopologySelectionVisuals();
                SelectionText.Text = _selectedTopologyObjectIds.Count == 0
                    ? "未選取"
                    : $"已選取 {_selectedTopologyObjectIds.Count} 個物件";
                return false;
            }
        }
        else if (!_selectedTopologyObjectIds.Contains(objectId))
        {
            _selectedTopologyObjectIds.Clear();
            _selectedTopologyConnectionIds.Clear();
            _selectedRouteConnectionId = null;
            _selectedTopologyObjectIds.Add(objectId);
        }

        ApplyTopologySelectionVisuals();
        return true;
    }

    private void SelectTopologyConnection(string connectionId)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (!ctrl)
        {
            _selectedTopologyObjectIds.Clear();
            _selectedTopologyConnectionIds.Clear();
            _selectedTopologyConnectionIds.Add(connectionId);
        }
        else if (!_selectedTopologyConnectionIds.Add(connectionId))
        {
            _selectedTopologyConnectionIds.Remove(connectionId);
        }

        _selectedRouteConnectionId = _selectedTopologyConnectionIds.Contains(connectionId)
            ? connectionId
            : _selectedTopologyConnectionIds.LastOrDefault();
        ApplyTopologySelectionVisuals();
    }

    private void DeleteSelectedTopologyItems()
    {
        if (_project is null) return;
        var connectionIds = _selectedTopologyConnectionIds.ToArray();
        var unplaceComponentIds = _selectedTopologyObjectIds
            .Where(id => _project.Components.Any(component =>
                string.Equals(component.ComponentInstanceId, id, StringComparison.OrdinalIgnoreCase) &&
                TopologyPaletteMaterialPolicy.Classify(component.TypeKey) != TopologyPaletteMaterialKind.TerminalBlock))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var terminalIds = _selectedTopologyObjectIds
            .Where(id =>
                _project.TerminalBlocks.Any(block => string.Equals(block.TerminalBlockId, id, StringComparison.OrdinalIgnoreCase)) ||
                _project.Components.Any(component =>
                    string.Equals(component.ComponentInstanceId, id, StringComparison.OrdinalIgnoreCase) &&
                    TopologyPaletteMaterialPolicy.Classify(component.TypeKey) == TopologyPaletteMaterialKind.TerminalBlock))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (connectionIds.Length == 0 && unplaceComponentIds.Count == 0 && terminalIds.Count == 0)
        {
            SelectionText.Text = "未選取可刪除項目";
            HintText.Text = "請先框選線路或元件，再按 Del。";
            return;
        }

        MutationStarting?.Invoke(this, new TopologyMutationEventArgs(
            $"Delete {connectionIds.Length} topology connection(s), unplace {unplaceComponentIds.Count} component(s), and remove {terminalIds.Count} terminal(s)"));
        var terminalResult = _terminalDeletionService.Delete(_project, terminalIds);
        var removedConnections = terminalResult.RemovedConnections +
                                 _connectionEditor.DeleteConnections(_project, connectionIds);
        var removedComponents = _project.TopologyPlacements.RemoveAll(placement =>
            unplaceComponentIds.Contains(placement.ObjectId));
        foreach (var terminalId in terminalIds)
            _baseTopologyPlacementSizes.Remove(terminalId);

        _selectedTopologyConnectionIds.Clear();
        _selectedTopologyObjectIds.Clear();
        _selectedRouteConnectionId = null;
        _hoveredRouteConnectionId = null;
        RemoveRouteHandle();
        PruneRouteIsolationState(_project.Connections.Select(connection => connection.ConnectionId));
        Render();
        SelectionText.Text = $"已刪除 {removedConnections} 條線、{terminalResult.RemovedTerminals} 個端子台；移回清單 {removedComponents} 個一般元件";
        HintText.Text = terminalResult.RemovedTerminals > 0
            ? "端子台已從專案與 Layout 移除，不會回到 BOM 元件清單；可使用 Undo 復原。"
            : "一般元件已回到左側清單；線路已刪除。可使用 Undo 復原。";
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    private Dictionary<string, Point> CaptureSelectedTopologyPositions()
    {
        if (_project is null) return new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        return _project.TopologyPlacements
            .Where(placement => _selectedTopologyObjectIds.Contains(placement.ObjectId))
            .ToDictionary(
                placement => placement.ObjectId,
                placement => new Point(placement.X, placement.Y),
                StringComparer.OrdinalIgnoreCase);
    }

    private void MoveSelectedTopologyObjects(
        IReadOnlyDictionary<string, Point> origins,
        double requestedDx,
        double requestedDy)
    {
        if (_project is null || origins.Count == 0) return;
        var placements = _project.TopologyPlacements
            .Where(placement => origins.ContainsKey(placement.ObjectId))
            .ToArray();
        if (placements.Length == 0) return;

        var minX = placements.Min(placement => origins[placement.ObjectId].X);
        var minY = placements.Min(placement => origins[placement.ObjectId].Y);
        var maxRight = placements.Max(placement => origins[placement.ObjectId].X + placement.Width);
        var maxBottom = placements.Max(placement => origins[placement.ObjectId].Y + placement.Height);
        var shift = ExpandCanvasForBounds(
            minX + requestedDx,
            minY + requestedDy,
            maxRight + requestedDx,
            maxBottom + requestedDy);
        minX += shift.X;
        minY += shift.Y;
        maxRight += shift.X;
        maxBottom += shift.Y;
        var dx = Math.Clamp(requestedDx, -minX, Math.Max(-minX, Surface.Width - maxRight));
        var dy = Math.Clamp(requestedDy, -minY, Math.Max(-minY, Surface.Height - maxBottom));

        foreach (var placement in placements)
        {
            var origin = origins[placement.ObjectId];
            placement.X = origin.X + dx;
            placement.Y = origin.Y + dy;
        }

        // A manually dragged bend stores an absolute canvas coordinate. Once either endpoint owner
        // moves that coordinate is stale, so return the affected connection to automatic routing.
        var movedIds = origins.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in _project.Connections)
        {
            var fromOwner = FindTopologyEndpointOwner(connection.FromEndpointId);
            var toOwner = FindTopologyEndpointOwner(connection.ToEndpointId);
            if ((fromOwner is not null && movedIds.Contains(fromOwner)) ||
                (toOwner is not null && movedIds.Contains(toOwner)))
                _manualRouteWaypoints.Remove(connection.ConnectionId);
        }

        UpdateSelectedTopologyVisualPositions(placements);
        RefreshRoutesDuringGroupDrag();
    }

    private void RefreshRoutesDuringGroupDrag()
    {
        var now = Environment.TickCount64;
        if (now - _lastLiveRouteRefreshTick < 50) return;
        _lastLiveRouteRefreshTick = now;

        // About 20 fps keeps endpoints and wires visibly attached without rebuilding the whole
        // project view on every mouse pixel. The normal mouse-up Render performs the final exact pass.
        _hoveredRouteConnectionId = null;
        ApplyRotatedPortVisuals();
        ApplyTerminalJunctionVisuals();
        EnsureEndpointModeVisuals();
        EnsureOrthogonalConnectionVisuals();
        ApplyTopologySelectionVisuals();
    }

    private void UpdateSelectedTopologyVisualPositions(IEnumerable<ComponentIntelligence.Electrical.Domain.TopologyPlacement> placements)
    {
        if (_project is null) return;
        foreach (var placement in placements)
        {
            var visual = FindTopologyNodeVisual(placement.ObjectId);
            if (visual is null) continue;
            var isTerminal = _project.TerminalBlocks.Any(block =>
                string.Equals(block.TerminalBlockId, placement.ObjectId, StringComparison.OrdinalIgnoreCase));
            Canvas.SetLeft(visual, isTerminal ? placement.X + placement.Width / 2d - visual.Width / 2d : placement.X);
            Canvas.SetTop(visual, isTerminal ? placement.Y + placement.Height / 2d - visual.Height / 2d : placement.Y);
        }
    }

    private void ApplyTopologySelectionVisuals()
    {
        if (_project is null || Surface is null) return;
        _selectedTopologyObjectIds.RemoveWhere(id =>
            _project.TopologyPlacements.All(placement =>
                !string.Equals(placement.ObjectId, id, StringComparison.OrdinalIgnoreCase)));
        var liveConnections = _project.Connections.Select(connection => connection.ConnectionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedTopologyConnectionIds.RemoveWhere(id => !liveConnections.Contains(id));
        if (_selectedRouteConnectionId is not null && !liveConnections.Contains(_selectedRouteConnectionId))
            _selectedRouteConnectionId = _selectedTopologyConnectionIds.LastOrDefault();

        foreach (var placement in _project.TopologyPlacements)
        {
            var visual = FindTopologyNodeVisual(placement.ObjectId);
            if (visual is null) continue;
            visual.Effect = _selectedTopologyObjectIds.Contains(placement.ObjectId)
                ? new DropShadowEffect
                {
                    Color = Colors.DodgerBlue,
                    BlurRadius = 14,
                    ShadowDepth = 0,
                    Opacity = 0.95
                }
                : null;
        }
        ApplyRouteEmphasisVisuals();
    }

    private Border? FindTopologyNodeVisual(string objectId) =>
        Surface.Children.OfType<Border>().FirstOrDefault(element =>
            element.Tag is string tag &&
            string.Equals(tag, objectId, StringComparison.OrdinalIgnoreCase));

    private static Rect NormalizeSelectionBounds(Point first, Point second) =>
        new(
            Math.Min(first.X, second.X),
            Math.Min(first.Y, second.Y),
            Math.Abs(second.X - first.X),
            Math.Abs(second.Y - first.Y));

    private static bool RouteIntersectsSelectionBounds(IList<Point> points, Rect selection)
    {
        for (var index = 1; index < points.Count; index++)
        {
            var first = points[index - 1];
            var second = points[index];
            var segment = new Rect(
                Math.Min(first.X, second.X) - 4d,
                Math.Min(first.Y, second.Y) - 4d,
                Math.Max(8d, Math.Abs(second.X - first.X) + 8d),
                Math.Max(8d, Math.Abs(second.Y - first.Y) + 8d));
            if (selection.IntersectsWith(segment)) return true;
        }
        return false;
    }
}
