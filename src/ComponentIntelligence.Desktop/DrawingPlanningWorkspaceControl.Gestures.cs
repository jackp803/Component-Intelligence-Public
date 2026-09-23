using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Desktop;

public partial class DrawingPlanningWorkspaceControl
{
    private string? _selectedRouteId;
    private (string RouteId, int SegmentIndex, bool Vertical)? _draggingSegment;
    private Point _gestureStart;
    private bool _gestureActive;

    private void BeginCanvasGesture(Point point)
    {
        _controller.BeginGesture(); _gestureActive = true; _gestureStart = point;
        DrawingCanvas.Focus(); DrawingCanvas.CaptureMouse();
    }

    private void DrawEditableRoute(DrawingRoute route)
    {
        var selected = _selectedRouteId == route.RouteId;
        for (var i = 0; i < route.Points.Count - 1; i++)
        {
            var index = i; var a = route.Points[i]; var b = route.Points[i + 1];
            var line = new Line { X1=a.X,Y1=a.Y,X2=b.X,Y2=b.Y, Stroke=selected ? Brushes.RoyalBlue : Brushes.DimGray,
                StrokeThickness=selected ? 2 : 1.2, IsHitTestVisible=false };
            DrawingCanvas.Children.Add(line);
            var hit = new Line { X1=a.X,Y1=a.Y,X2=b.X,Y2=b.Y, Stroke=Brushes.Transparent, StrokeThickness=8,
                Cursor=a.X == b.X ? Cursors.SizeWE : Cursors.SizeNS, ToolTip="調整走線" };
            hit.MouseLeftButtonDown += (_, e) =>
            {
                _selectedRouteId = route.RouteId; _controller.SelectRepresentations([]);
                if (route.State == DrawingPlanControlState.Locked) { StatusText.Text="走線已鎖定，請先解除鎖定。"; RefreshCanvas(); e.Handled=true; return; }
                _draggingSegment = (route.RouteId, index, a.X == b.X);
                BeginCanvasGesture(e.GetPosition(DrawingCanvas)); RefreshCanvas(); e.Handled=true;
            };
            DrawingCanvas.Children.Add(hit);
            if (selected)
            {
                var handle = new Rectangle { Width=6,Height=6,Fill=Brushes.White,Stroke=Brushes.RoyalBlue,IsHitTestVisible=false };
                Canvas.SetLeft(handle,(a.X+b.X)/2.0-3); Canvas.SetTop(handle,(a.Y+b.Y)/2.0-3); DrawingCanvas.Children.Add(handle);
            }
        }
    }

    private void Gesture_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_gestureActive || e.LeftButton != MouseButtonState.Pressed) return;
        var point=e.GetPosition(DrawingCanvas); var snap=long.TryParse(GridSnapText.Text,out var g) && g>0 ? g : 10;
        long Snap(double value) => (long)Math.Round(value/snap)*snap;
        try
        {
            if (_draggingSegment is { } segment)
                _controller.PreviewRouteSegment(segment.RouteId,segment.SegmentIndex,Snap(segment.Vertical ? point.X-_gestureStart.X : point.Y-_gestureStart.Y));
            else if (_draggingRepresentationId is { } id)
                _controller.PreviewPlacement(id,Snap(point.X-_dragOffset.X),Snap(point.Y-_dragOffset.Y));
            RefreshCanvas();
        }
        catch (Exception ex) { CancelCanvasGesture(); StatusText.Text=ex.Message; }
    }

    private void Gesture_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_gestureActive) return;
        try { _controller.CommitGesture(); PersistPlan(); }
        catch (Exception ex) { _controller.CancelGesture(); StatusText.Text=ex.Message; }
        EndCanvasGesture(); RefreshCanvas(); e.Handled=true;
    }

    private void EndCanvasGesture()
    {
        _gestureActive=false; _draggingRepresentationId=null; _draggingSegment=null; DrawingCanvas.ReleaseMouseCapture();
    }

    private void CancelCanvasGesture() { _controller.CancelGesture(); EndCanvasGesture(); RefreshCanvas(); }

    private void Workspace_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _gestureActive) { CancelCanvasGesture(); e.Handled=true; return; }
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || e.OriginalSource is TextBox) return;
        if (e.Key is Key.Z or Key.Y)
        {
            if (_gestureActive) CancelCanvasGesture();
            if (e.Key == Key.Z) Undo_Click(sender,e); else Redo_Click(sender,e);
            e.Handled=true;
        }
    }
}
