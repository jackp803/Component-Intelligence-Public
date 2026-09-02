using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private Polyline? _wirePreviewLine;
    private bool _anchoringConnectionLines;

    private void TopologyCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (!ReferenceEquals(source, Surface) && !ReferenceEquals(FindAncestor<Canvas>(source), Surface))
            return;

        // Connector expand/collapse must work even when optional image/Notion visual hooks were never
        // configured by the host window.
        Surface_PreviewPinExpansion(sender, e);
        if (e.Handled) return;

        // Keep keyboard focus on the topology workspace so Esc can always cancel a pending wire.
        Focus();
        if (TryCompleteTerminalToPortWire(e)) e.Handled = true;
    }

    private void TopologyCanvas_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_interactionMode != InteractionMode.Wire) return;

        CancelPendingWire("已取消拉線。左鍵點 Connector / Pin / 端子圓點可重新開始。", render: true);
        e.Handled = true;
    }

    private void TopologyCanvas_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_interactionMode == InteractionMode.Select && e.Key is Key.Delete or Key.Back)
        {
            DeleteSelectedTopologyItems();
            e.Handled = true;
            return;
        }

        if (_interactionMode != InteractionMode.Wire || e.Key != Key.Escape) return;

        CancelPendingWire("已取消拉線。左鍵點 Connector / Pin / 端子圓點可重新開始。", render: true);
        e.Handled = true;
    }

    private void Surface_WirePreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_interactionMode != InteractionMode.Wire || string.IsNullOrWhiteSpace(_pendingWireEndpointId))
        {
            RemoveWirePreview();
            return;
        }

        if (!TryFindTopologyEndpointMarkerCenter(_pendingWireEndpointId, out var start))
        {
            RemoveWirePreview();
            return;
        }

        var current = e.GetPosition(Surface);
        var preview = EnsureWirePreview();
        preview.Points = new PointCollection(BuildPreviewOrthogonalRoute(
            start,
            new Point(Math.Max(0, current.X), Math.Max(0, current.Y))));
    }

    private void Surface_LayoutUpdated(object? sender, EventArgs e)
    {
        if (_anchoringConnectionLines || _project is null || _dragRecorded || _terminalDragRecorded ||
            _dragRouteConnectionId is not null || _dragEndpointHandle is not null) return;

        try
        {
            _anchoringConnectionLines = true;

            // Component Borders rotate separately from their external endpoint markers. Move every
            // Port/Pin to the corresponding rotated physical edge first, then rebuild individually
            // wireable Pin endpoints and their orthogonal routes.
            ApplyRotatedPortVisuals();
            ApplyTerminalJunctionVisuals();
            EnsureEndpointModeVisuals();
            ApplyTerminalComponentGroupingVisuals();

            // Legacy Lines are retained as a compatibility/fallback visual but are hidden by the
            // orthogonal router. Keeping their endpoints correct avoids stale geometry in code paths
            // that still inspect them (labels, older tests, temporary fallbacks).
            foreach (var line in Surface.Children.OfType<Line>())
            {
                if (line.Tag is not string connectionId) continue;
                var connection = _project.Connections.FirstOrDefault(item =>
                    string.Equals(item.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase));
                if (connection is null) continue;

                if (TryFindTopologyEndpointMarkerCenter(connection.FromEndpointId, out var from))
                {
                    line.X1 = from.X;
                    line.Y1 = from.Y;
                }

                if (TryFindTopologyEndpointMarkerCenter(connection.ToEndpointId, out var to))
                {
                    line.X2 = to.X;
                    line.Y2 = to.Y;
                }
            }

            EnsureOrthogonalConnectionVisuals();
        }
        finally
        {
            _anchoringConnectionLines = false;
        }
    }

    private Polyline EnsureWirePreview()
    {
        if (_wirePreviewLine is not null && ReferenceEquals(_wirePreviewLine.Parent, Surface))
            return _wirePreviewLine;

        _wirePreviewLine = new Polyline
        {
            Stroke = Brushes.DarkOrange,
            StrokeThickness = 2.5,
            StrokeDashArray = new DoubleCollection { 5, 3 },
            StrokeLineJoin = PenLineJoin.Round,
            Opacity = 0.9,
            IsHitTestVisible = false
        };
        Panel.SetZIndex(_wirePreviewLine, 10_000);
        Surface.Children.Add(_wirePreviewLine);
        return _wirePreviewLine;
    }

    private static IReadOnlyList<Point> BuildPreviewOrthogonalRoute(Point start, Point end)
    {
        if (Math.Abs(start.X - end.X) < 0.5 || Math.Abs(start.Y - end.Y) < 0.5)
            return [start, end];

        var midX = (start.X + end.X) / 2d;
        return [start, new Point(midX, start.Y), new Point(midX, end.Y), end];
    }

    private bool TryFindTopologyEndpointMarkerCenter(string endpointSelector, out Point center)
    {
        if (TopologyTerminalJunctionService.TryGetTerminalBlockId(endpointSelector, out var terminalBlockId))
            return TryFindTerminalMarkerCenter(terminalBlockId, out center);
        return TryFindEndpointMarkerCenter(endpointSelector, out center);
    }

    private bool TryFindEndpointMarkerCenter(string endpointId, out Point center)
    {
        // Pin-level endpoint markers use the compact 12-14 px endpoint visual contract. Their Tag is
        // the exact PinId, so checking it first anchors the wire to that Pin rather than its parent Port.
        if (TryFindPortMarkerCenter(endpointId, out center)) return true;
        if (_project is null)
        {
            center = default;
            return false;
        }

        foreach (var component in _project.Components)
        foreach (var port in component.Ports)
        {
            if (!port.Pins.Any(pin => string.Equals(pin.PinId, endpointId, StringComparison.OrdinalIgnoreCase)))
                continue;
            return TryFindPortMarkerCenter(port.PortId, out center);
        }

        foreach (var block in _project.TerminalBlocks)
        {
            var ownsEndpoint = block.Positions
                .SelectMany(position => position.Levels)
                .SelectMany(level => level.ConnectionPoints)
                .Any(point => string.Equals(point.ConnectionPointId, endpointId, StringComparison.OrdinalIgnoreCase));
            if (ownsEndpoint) return TryFindTerminalMarkerCenter(block.TerminalBlockId, out center);
        }

        center = default;
        return false;
    }

    private bool TryFindPortMarkerCenter(string endpointId, out Point center)
    {
        foreach (var element in Surface.Children.OfType<FrameworkElement>())
        {
            if (element.Tag is not string tag || !string.Equals(tag, endpointId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsEndpointMarkerVisual(element)) continue;
            if (element.Visibility != Visibility.Visible) continue;

            var left = Canvas.GetLeft(element);
            var top = Canvas.GetTop(element);
            if (double.IsNaN(left) || double.IsNaN(top)) continue;

            center = new Point(left + element.Width / 2, top + element.Height / 2);
            return true;
        }

        center = default;
        return false;
    }

    private void CancelPendingWire(string message, bool render)
    {
        _pendingWireEndpointId = null;
        RemoveWirePreview();
        SelectionText.Text = "Wire cancelled";
        HintBanner.Visibility = Visibility.Visible;
        HintText.Text = message;
        if (render) Render();
    }

    private void RemoveWirePreview()
    {
        if (_wirePreviewLine is not null && ReferenceEquals(_wirePreviewLine.Parent, Surface))
            Surface.Children.Remove(_wirePreviewLine);
        _wirePreviewLine = null;
    }
}
