using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private Line? _wirePreviewLine;
    private bool _anchoringConnectionLines;

    private void TopologyCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Keep keyboard focus on the topology workspace so Esc can always cancel a pending wire.
        Focus();
        if (TryCompleteTerminalToPortWire(e)) e.Handled = true;
    }

    private void TopologyCanvas_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_interactionMode != InteractionMode.Wire) return;

        CancelPendingWire("已取消拉線。左鍵點 Port 或端子圓點可重新開始。", render: true);
        e.Handled = true;
    }

    private void TopologyCanvas_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_interactionMode != InteractionMode.Wire || e.Key != Key.Escape) return;

        CancelPendingWire("已取消拉線。左鍵點 Port 或端子圓點可重新開始。", render: true);
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
        preview.X1 = start.X;
        preview.Y1 = start.Y;
        preview.X2 = Math.Max(0, current.X);
        preview.Y2 = Math.Max(0, current.Y);
    }

    private void Surface_LayoutUpdated(object? sender, EventArgs e)
    {
        if (_anchoringConnectionLines || _project is null) return;

        try
        {
            _anchoringConnectionLines = true;

            // The component Border uses a RotateTransform, while Port markers are separate Canvas children.
            // Apply the same rotation geometry to those markers before anchoring formal/preview wires.
            ApplyRotatedPortVisuals();
            ApplyTerminalJunctionVisuals();

            foreach (var line in Surface.Children.OfType<Line>())
            {
                if (line.Tag is not string connectionId) continue;
                var connection = _project.Connections.FirstOrDefault(item =>
                    string.Equals(item.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase));
                if (connection is null) continue;

                if (TryFindEndpointMarkerCenter(connection.FromEndpointId, out var from))
                {
                    line.X1 = from.X;
                    line.Y1 = from.Y;
                }

                if (TryFindEndpointMarkerCenter(connection.ToEndpointId, out var to))
                {
                    line.X2 = to.X;
                    line.Y2 = to.Y;
                }
            }
        }
        finally
        {
            _anchoringConnectionLines = false;
        }
    }

    private Line EnsureWirePreview()
    {
        if (_wirePreviewLine is not null && ReferenceEquals(_wirePreviewLine.Parent, Surface))
            return _wirePreviewLine;

        _wirePreviewLine = new Line
        {
            Stroke = Brushes.DarkOrange,
            StrokeThickness = 2.5,
            StrokeDashArray = new DoubleCollection { 5, 3 },
            Opacity = 0.9,
            IsHitTestVisible = false
        };
        Panel.SetZIndex(_wirePreviewLine, 10_000);
        Surface.Children.Add(_wirePreviewLine);
        return _wirePreviewLine;
    }

    private bool TryFindTopologyEndpointMarkerCenter(string endpointSelector, out Point center)
    {
        if (TopologyTerminalJunctionService.TryGetTerminalBlockId(endpointSelector, out var terminalBlockId))
            return TryFindTerminalMarkerCenter(terminalBlockId, out center);
        return TryFindEndpointMarkerCenter(endpointSelector, out center);
    }

    private bool TryFindEndpointMarkerCenter(string endpointId, out Point center)
    {
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

    private bool TryFindPortMarkerCenter(string portId, out Point center)
    {
        foreach (var element in Surface.Children.OfType<FrameworkElement>())
        {
            if (element.Tag is not string tag || !string.Equals(tag, portId, StringComparison.OrdinalIgnoreCase))
                continue;

            // Port markers are the small 14 × 14 Border elements rendered by RenderPorts().
            if (element is not Border || Math.Abs(element.Width - 14) > 0.1 || Math.Abs(element.Height - 14) > 0.1)
                continue;

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
