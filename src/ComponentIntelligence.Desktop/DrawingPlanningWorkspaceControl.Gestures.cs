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
    private (string RouteId, int PointIndex)? _draggingBend;
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
                RefreshSelection();
                if (route.State == DrawingPlanControlState.Locked) { StatusText.Text="走線已鎖定，請先解除鎖定。"; RefreshCanvas(); e.Handled=true; return; }
                _draggingSegment = (route.RouteId, index, a.X == b.X);
                BeginCanvasGesture(e.GetPosition(DrawingCanvas)); RefreshCanvas(); e.Handled=true;
            };
            DrawingCanvas.Children.Add(hit);
            var menu = new ContextMenu();
            var addBend = new MenuItem { Header = "新增折點", IsEnabled = route.State != DrawingPlanControlState.Locked };
            Point menuPoint = default;
            hit.MouseRightButtonDown += (_, e) => menuPoint = e.GetPosition(DrawingCanvas);
            addBend.Click += (_, _) =>
            {
                var x = a.X == b.X ? a.X : (long)Math.Clamp(menuPoint.X, Math.Min(a.X, b.X), Math.Max(a.X, b.X));
                var y = a.Y == b.Y ? a.Y : (long)Math.Clamp(menuPoint.Y, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));
                if (new DrawingPoint(x, y) == a || new DrawingPoint(x, y) == b) return;
                _selectedRouteId = route.RouteId; _controller.SelectRepresentations([]);
                AddBendPoint(route.RouteId, index, x, y);
            };
            menu.Items.Add(addBend); hit.ContextMenu = menu;
            if (selected)
            {
                var handle = new Rectangle { Width=6,Height=6,Fill=Brushes.White,Stroke=Brushes.RoyalBlue,IsHitTestVisible=false };
                Canvas.SetLeft(handle,(a.X+b.X)/2.0-3); Canvas.SetTop(handle,(a.Y+b.Y)/2.0-3); DrawingCanvas.Children.Add(handle);
            }
        }
        if (selected) DrawBendHandles(route);
    }

    private void DrawBendHandles(DrawingRoute route)
    {
        for (var i = 1; i < route.Points.Count - 1; i++)
        {
            var index = i; var point = route.Points[i];
            var handle = new Ellipse { Width = 9, Height = 9, Fill = Brushes.White, Stroke = Brushes.RoyalBlue,
                Cursor = Cursors.SizeAll, ToolTip = "移動折點" };
            Canvas.SetLeft(handle, point.X - 4.5); Canvas.SetTop(handle, point.Y - 4.5);
            handle.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (route.State == DrawingPlanControlState.Locked) { StatusText.Text = "走線已鎖定，請先解除鎖定。"; return; }
                _draggingBend = (route.RouteId, index);
                BeginCanvasGesture(e.GetPosition(DrawingCanvas));
            };
            var menu = new ContextMenu();
            var remove = new MenuItem { Header = "刪除折點", IsEnabled = route.State != DrawingPlanControlState.Locked };
            remove.Click += (_, _) => DeleteBendPoint(route.RouteId, index);
            menu.Items.Add(remove); handle.ContextMenu = menu;
            DrawingCanvas.Children.Add(handle);
        }
    }

    private void Gesture_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_gestureActive || e.LeftButton != MouseButtonState.Pressed) return;
        var point=e.GetPosition(DrawingCanvas); var snap=long.TryParse(GridSnapText.Text,out var g) && g>0 ? g : 10;
        long Snap(double value) => (long)Math.Round(value/snap)*snap;
        try
        {
            if (_draggingBend is { } bend)
                _controller.PreviewBendPoint(bend.RouteId, bend.PointIndex, Snap(point.X), Snap(point.Y));
            else if (_draggingSegment is { } segment)
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
        EndCanvasGesture(); RefreshSelectedState(); RefreshCanvas(); e.Handled=true;
    }

    private void EndCanvasGesture()
    {
        _gestureActive=false; _draggingRepresentationId=null; _draggingSegment=null; _draggingBend=null; DrawingCanvas.ReleaseMouseCapture();
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
