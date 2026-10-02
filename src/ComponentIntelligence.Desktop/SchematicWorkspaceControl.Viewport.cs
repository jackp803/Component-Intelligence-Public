using System.Windows;
using System.Windows.Input;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private Point? _panStart;
    private double _panHorizontalOffset;
    private double _panVerticalOffset;

    private void PageScroll_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        var oldZoom = Zoom.Value;
        var nextZoom = Math.Clamp(oldZoom * Math.Pow(1.12, e.Delta / 120d), Zoom.Minimum, Zoom.Maximum);
        var pointer = e.GetPosition(PageScroll);
        Zoom.Value = nextZoom;
        PageScroll.UpdateLayout();
        PageScroll.ScrollToHorizontalOffset(SchematicViewportMath.OffsetAfterZoom(
            PageScroll.HorizontalOffset, pointer.X, oldZoom, nextZoom));
        PageScroll.ScrollToVerticalOffset(SchematicViewportMath.OffsetAfterZoom(
            PageScroll.VerticalOffset, pointer.Y, oldZoom, nextZoom));
        e.Handled = true;
    }

    private void PageScroll_Down(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        _panStart = e.GetPosition(PageScroll);
        _panHorizontalOffset = PageScroll.HorizontalOffset;
        _panVerticalOffset = PageScroll.VerticalOffset;
        PageScroll.Cursor = Cursors.SizeAll;
        PageScroll.CaptureMouse();
        e.Handled = true;
    }

    private void PageScroll_Move(object sender, MouseEventArgs e)
    {
        if (_panStart is not Point start) return;
        var pointer = e.GetPosition(PageScroll);
        PageScroll.ScrollToHorizontalOffset(SchematicViewportMath.OffsetAfterPan(
            _panHorizontalOffset, start.X, pointer.X));
        PageScroll.ScrollToVerticalOffset(SchematicViewportMath.OffsetAfterPan(
            _panVerticalOffset, start.Y, pointer.Y));
        e.Handled = true;
    }

    private void PageScroll_Up(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _panStart is null) return;
        _panStart = null;
        PageScroll.Cursor = null;
        PageScroll.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void PageScroll_LostCapture(object sender, MouseEventArgs e)
    {
        _panStart = null;
        PageScroll.Cursor = null;
    }
}
