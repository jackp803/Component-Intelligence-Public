using System.Windows;
using System.Windows.Input;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private const double MinimumCanvasWidth = 3200d;
    private const double MinimumCanvasHeight = 2000d;
    private const double MinimumCanvasZoom = 0.25d;
    private const double MaximumCanvasZoom = 3d;
    private const double CanvasContentMargin = 160d;
    private double _canvasZoom = 1d;

    private void TopologyScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;

        var previousZoom = _canvasZoom;
        var factor = e.Delta > 0 ? 1.1d : 1d / 1.1d;
        var nextZoom = Math.Clamp(previousZoom * factor, MinimumCanvasZoom, MaximumCanvasZoom);
        if (Math.Abs(nextZoom - previousZoom) < 0.0001d)
        {
            e.Handled = true;
            return;
        }

        var pointer = e.GetPosition(TopologyScrollViewer);
        var logicalX = (TopologyScrollViewer.HorizontalOffset + pointer.X) / previousZoom;
        var logicalY = (TopologyScrollViewer.VerticalOffset + pointer.Y) / previousZoom;
        _canvasZoom = nextZoom;
        TopologyZoomTransform.ScaleX = nextZoom;
        TopologyZoomTransform.ScaleY = nextZoom;
        TopologyScrollViewer.UpdateLayout();
        TopologyScrollViewer.ScrollToHorizontalOffset(logicalX * nextZoom - pointer.X);
        TopologyScrollViewer.ScrollToVerticalOffset(logicalY * nextZoom - pointer.Y);

        SelectionText.Text = $"畫布縮放：{nextZoom:P0}";
        HintText.Text = "Ctrl + 滾輪可繼續縮放；普通滾輪仍用來上下捲動畫布。";
        e.Handled = true;
    }

    private void ResetCanvasBoundsForProject()
    {
        Surface.Width = MinimumCanvasWidth;
        Surface.Height = MinimumCanvasHeight;
        EnsureCanvasContainsProjectContent();
    }

    private void EnsureCanvasContainsProjectContent()
    {
        if (_project is null || _project.TopologyPlacements.Count == 0) return;
        var requiredWidth = _project.TopologyPlacements.Max(item => item.X + item.Width) + CanvasContentMargin;
        var requiredHeight = _project.TopologyPlacements.Max(item => item.Y + item.Height) + CanvasContentMargin;
        Surface.Width = Math.Max(Surface.Width, Math.Max(MinimumCanvasWidth, requiredWidth));
        Surface.Height = Math.Max(Surface.Height, Math.Max(MinimumCanvasHeight, requiredHeight));
    }

    private Point FindAvailablePointInVisibleTopologyViewport()
    {
        const double nodeWidth = 150d;
        const double nodeHeight = 76d;
        const double margin = 36d;
        const double columnStep = 190d;
        const double rowStep = 116d;

        var zoom = _canvasZoom > 0d && double.IsFinite(_canvasZoom) ? _canvasZoom : 1d;
        var viewportWidth = TopologyScrollViewer.ViewportWidth > 0d
            ? TopologyScrollViewer.ViewportWidth / zoom
            : Math.Max(600d, ActualWidth / zoom);
        var viewportHeight = TopologyScrollViewer.ViewportHeight > 0d
            ? TopologyScrollViewer.ViewportHeight / zoom
            : Math.Max(400d, ActualHeight / zoom);
        var left = Math.Max(0d, TopologyScrollViewer.HorizontalOffset / zoom);
        var top = Math.Max(0d, TopologyScrollViewer.VerticalOffset / zoom);
        var maxX = Math.Max(left + margin, left + viewportWidth - nodeWidth - margin);
        var maxY = Math.Max(top + margin, top + viewportHeight - nodeHeight - margin);

        for (var y = top + margin; y <= maxY + 0.1d; y += rowStep)
        for (var x = left + margin; x <= maxX + 0.1d; x += columnStep)
        {
            var candidate = new Rect(x, y, nodeWidth, nodeHeight);
            var overlaps = _project?.TopologyPlacements.Any(placement =>
                candidate.IntersectsWith(new Rect(
                    placement.X - 16d,
                    placement.Y - 16d,
                    placement.Width + 32d,
                    placement.Height + 32d))) == true;
            if (!overlaps) return new Point(x, y);
        }

        return new Point(
            Math.Clamp(left + viewportWidth / 2d - nodeWidth / 2d, left, maxX),
            Math.Clamp(top + viewportHeight / 2d - nodeHeight / 2d, top, maxY));
    }

    private Point ExpandCanvasForBounds(double left, double top, double right, double bottom)
    {
        if (_project is null) return new Point();
        var expansion = TopologyCanvasBoundsPolicy.Calculate(
            Surface.Width,
            Surface.Height,
            left,
            top,
            right,
            bottom);
        if (expansion.ShiftX == 0d && expansion.ShiftY == 0d &&
            Math.Abs(expansion.Width - Surface.Width) < 0.1d &&
            Math.Abs(expansion.Height - Surface.Height) < 0.1d)
            return new Point();

        if (expansion.ShiftX != 0d || expansion.ShiftY != 0d)
            ShiftTopologyContent(expansion.ShiftX, expansion.ShiftY);

        Surface.Width = expansion.Width;
        Surface.Height = expansion.Height;
        if (expansion.ShiftX != 0d || expansion.ShiftY != 0d)
        {
            TopologyScrollViewer.UpdateLayout();
            TopologyScrollViewer.ScrollToHorizontalOffset(
                TopologyScrollViewer.HorizontalOffset + expansion.ShiftX * _canvasZoom);
            TopologyScrollViewer.ScrollToVerticalOffset(
                TopologyScrollViewer.VerticalOffset + expansion.ShiftY * _canvasZoom);
        }

        return new Point(expansion.ShiftX, expansion.ShiftY);
    }

    private void ShiftTopologyContent(double shiftX, double shiftY)
    {
        if (_project is null) return;
        foreach (var placement in _project.TopologyPlacements)
        {
            placement.X += shiftX;
            placement.Y += shiftY;
        }

        foreach (var connectionId in _manualRouteWaypoints.Keys.ToArray())
        {
            var point = _manualRouteWaypoints[connectionId];
            _manualRouteWaypoints[connectionId] = new Point(point.X + shiftX, point.Y + shiftY);
        }

        ShiftCapturedPositions(_dragStartSelectionPositions, shiftX, shiftY);
        ShiftCapturedPositions(_terminalDragStartSelectionPositions, shiftX, shiftY);
        _dragStartMouse = new Point(_dragStartMouse.X + shiftX, _dragStartMouse.Y + shiftY);
        _terminalDragStartMouse = new Point(
            _terminalDragStartMouse.X + shiftX,
            _terminalDragStartMouse.Y + shiftY);
        _dragStartObject = new Point(_dragStartObject.X + shiftX, _dragStartObject.Y + shiftY);
        _terminalDragStartPlacement = new Point(
            _terminalDragStartPlacement.X + shiftX,
            _terminalDragStartPlacement.Y + shiftY);

        // Leading-edge expansion changes every model coordinate. Move all currently rendered node
        // bodies immediately as well, so unselected components do not appear to jump during the
        // active drag before the normal mouse-up Render performs the final exact refresh.
        UpdateSelectedTopologyVisualPositions(_project.TopologyPlacements);
    }

    private static void ShiftCapturedPositions(IDictionary<string, Point> positions, double shiftX, double shiftY)
    {
        foreach (var objectId in positions.Keys.ToArray())
        {
            var point = positions[objectId];
            positions[objectId] = new Point(point.X + shiftX, point.Y + shiftY);
        }
    }
}
