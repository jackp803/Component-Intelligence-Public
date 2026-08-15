using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private readonly TopologyTerminalJunctionService _terminalJunctions = new();
    private bool _applyingTerminalVisuals;
    private string? _terminalDragBlockId;
    private Point _terminalDragStartMouse;
    private Point _terminalDragStartPlacement;
    private bool _terminalDragRecorded;

    private void ApplyTerminalJunctionVisuals()
    {
        if (_applyingTerminalVisuals || _project is null) return;

        try
        {
            _applyingTerminalVisuals = true;
            foreach (var block in _project.TerminalBlocks)
            {
                var marker = Surface.Children.OfType<Border>().FirstOrDefault(element =>
                    element.Tag is string tag && string.Equals(tag, block.TerminalBlockId, StringComparison.OrdinalIgnoreCase));
                if (marker is null) continue;

                var placement = _project.TopologyPlacements.FirstOrDefault(item =>
                    string.Equals(item.ObjectId, block.TerminalBlockId, StringComparison.OrdinalIgnoreCase));
                if (placement is null) continue;

                const double diameter = 16;
                marker.Width = diameter;
                marker.Height = diameter;
                marker.CornerRadius = new CornerRadius(diameter / 2);
                marker.Background = Brushes.White;
                marker.BorderBrush = Brushes.DarkSlateGray;
                marker.BorderThickness = new Thickness(2);
                marker.Child = null;
                marker.Cursor = Cursors.Cross;
                marker.RenderTransform = Transform.Identity;
                marker.ToolTip = $"{block.ReferenceDesignator}{(string.IsNullOrWhiteSpace(block.FunctionTag) ? string.Empty : " | " + block.FunctionTag)}\nTerminal / Junction（端子／分岔點）\n拉線模式：可從這個圓點拉支線；雙擊線路可插入新的端子圓點。";

                Canvas.SetLeft(marker, placement.X + placement.Width / 2 - diameter / 2);
                Canvas.SetTop(marker, placement.Y + placement.Height / 2 - diameter / 2);
                Panel.SetZIndex(marker, 5000);

                marker.PreviewMouseLeftButtonDown -= TerminalMarker_PreviewMouseLeftButtonDown;
                marker.PreviewMouseLeftButtonDown += TerminalMarker_PreviewMouseLeftButtonDown;
                marker.PreviewMouseMove -= TerminalMarker_PreviewMouseMove;
                marker.PreviewMouseMove += TerminalMarker_PreviewMouseMove;
                marker.PreviewMouseLeftButtonUp -= TerminalMarker_PreviewMouseLeftButtonUp;
                marker.PreviewMouseLeftButtonUp += TerminalMarker_PreviewMouseLeftButtonUp;
            }
        }
        finally
        {
            _applyingTerminalVisuals = false;
        }
    }

    private void TerminalMarker_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_project is null || sender is not Border marker || marker.Tag is not string blockId) return;
        var block = _project.TerminalBlocks.FirstOrDefault(item =>
            string.Equals(item.TerminalBlockId, blockId, StringComparison.OrdinalIgnoreCase));
        if (block is null) return;

        if (_interactionMode == InteractionMode.Wire)
        {
            var selector = TopologyTerminalJunctionService.Selector(blockId);
            if (_pendingWireEndpointId is null)
            {
                _pendingWireEndpointId = selector;
                SelectionText.Text = $"A: {block.ReferenceDesignator} Terminal";
                HintText.Text = "已選端子圓點（A 點）。移動滑鼠，再左鍵點另一個 Port 或端子圓點完成；Esc / 右鍵取消。";
                Render();
                e.Handled = true;
                return;
            }

            if (string.Equals(_pendingWireEndpointId, selector, StringComparison.OrdinalIgnoreCase))
            {
                CancelPendingWire("已取消拉線。左鍵點 Port 或端子圓點可重新開始。", render: true);
                e.Handled = true;
                return;
            }

            CompleteTopologyWire(_pendingWireEndpointId, selector, $"Connect {_pendingWireEndpointId} -> {selector}");
            e.Handled = true;
            return;
        }

        var placement = _project.TopologyPlacements.FirstOrDefault(item =>
            string.Equals(item.ObjectId, blockId, StringComparison.OrdinalIgnoreCase));
        if (placement is null) return;

        _terminalDragBlockId = blockId;
        _terminalDragStartMouse = e.GetPosition(Surface);
        _terminalDragStartPlacement = new Point(placement.X, placement.Y);
        _terminalDragRecorded = false;
        marker.CaptureMouse();
        SelectionText.Text = $"{block.ReferenceDesignator} | Terminal / Junction";
        HintText.Text = "端子在 Topology 只顯示小圓圈；拖曳可移動位置。實際端子料號、Position / Jumper 留到實體配置決定。";
        e.Handled = true;
    }

    private void TerminalMarker_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_project is null || _terminalDragBlockId is null || sender is not Border marker || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(Surface);
        var dx = current.X - _terminalDragStartMouse.X;
        var dy = current.Y - _terminalDragStartMouse.Y;
        if (!_terminalDragRecorded && Math.Abs(dx) + Math.Abs(dy) >= 2)
        {
            MutationStarting?.Invoke(this, new TopologyMutationEventArgs($"Move terminal junction {_terminalDragBlockId}"));
            _terminalDragRecorded = true;
        }
        if (!_terminalDragRecorded) return;

        var placement = _project.TopologyPlacements.First(item =>
            string.Equals(item.ObjectId, _terminalDragBlockId, StringComparison.OrdinalIgnoreCase));
        placement.X = Math.Max(0, _terminalDragStartPlacement.X + dx);
        placement.Y = Math.Max(0, _terminalDragStartPlacement.Y + dy);
        Canvas.SetLeft(marker, placement.X + placement.Width / 2 - marker.Width / 2);
        Canvas.SetTop(marker, placement.Y + placement.Height / 2 - marker.Height / 2);
        e.Handled = true;
    }

    private void TerminalMarker_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_terminalDragBlockId is null) return;
        var moved = _terminalDragRecorded;
        if (sender is Border marker) marker.ReleaseMouseCapture();
        _terminalDragBlockId = null;
        _terminalDragRecorded = false;
        if (moved)
        {
            Render();
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
        e.Handled = true;
    }

    private bool TryCompleteTerminalToPortWire(MouseButtonEventArgs e)
    {
        if (_project is null || _interactionMode != InteractionMode.Wire ||
            string.IsNullOrWhiteSpace(_pendingWireEndpointId) ||
            !TopologyTerminalJunctionService.TryGetTerminalBlockId(_pendingWireEndpointId, out _))
            return false;
        if (!TryGetPortMarkerFromOriginalSource(e.OriginalSource as DependencyObject, out var portId)) return false;

        CompleteTopologyWire(_pendingWireEndpointId, portId, $"Connect {_pendingWireEndpointId} -> {portId}");
        return true;
    }

    private void CompleteTopologyWire(string fromSelector, string toSelector, string mutationDescription)
    {
        if (_project is null) return;
        try
        {
            MutationStarting?.Invoke(this, new TopologyMutationEventArgs(mutationDescription));
            _terminalJunctions.Connect(_project, fromSelector, toSelector);
            _pendingWireEndpointId = null;
            RemoveWirePreview();
            SelectionText.Text = "連線已建立";
            HintText.Text = "連線完成。端子圓點可繼續拉出更多支線；底層會建立新的 Terminal Connection Point，不會把線路交叉自動視為導通。";
            Render();
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            MessageBox.Show(Window.GetWindow(this), exception.Message, "無法建立端子分岔連線", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private bool TryFindTerminalMarkerCenter(string terminalBlockId, out Point center)
    {
        foreach (var marker in Surface.Children.OfType<Border>())
        {
            if (marker.Tag is not string tag || !string.Equals(tag, terminalBlockId, StringComparison.OrdinalIgnoreCase)) continue;
            if (Math.Abs(marker.Width - 16) > 0.1 || Math.Abs(marker.Height - 16) > 0.1) continue;
            var left = Canvas.GetLeft(marker);
            var top = Canvas.GetTop(marker);
            if (double.IsNaN(left) || double.IsNaN(top)) continue;
            center = new Point(left + marker.Width / 2, top + marker.Height / 2);
            return true;
        }
        center = default;
        return false;
    }

    private static bool TryGetPortMarkerFromOriginalSource(DependencyObject? source, out string portId)
    {
        var current = source;
        while (current is not null)
        {
            if (current is Border border && border.Tag is string tag &&
                Math.Abs(border.Width - 14) < 0.1 && Math.Abs(border.Height - 14) < 0.1)
            {
                portId = tag;
                return true;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        portId = string.Empty;
        return false;
    }
}
