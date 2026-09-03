using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Drawing;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Desktop;

public partial class DrawingPlanningWorkspaceControl : UserControl
{
    private readonly DrawingPlanningWorkspaceController _controller = new(new DrawingPlanEditService());
    private string? _draggingRepresentationId;
    private Point _dragOffset;
    private double _zoom = 1.0;

    public IDrawingPlannerClient? PlannerClient { get; set; }
    public Func<DrawingPlanningInput>? PlanningInputProvider { get; set; }
    public Func<ElectricalProject>? ProjectProvider { get; set; }
    public Action<ElectricalProject>? ProjectReplaced { get; set; }
    public Func<ProjectRevisionTrigger, string?, Task>? CheckpointAsync { get; set; }
    public Func<Task>? SaveProjectAsync { get; set; }
    public Func<Task<IReadOnlyList<string>>>? HistoryItemsAsync { get; set; }
    public Func<string, Task<ElectricalProject>>? RestoreRevisionAsync { get; set; }

    public DrawingPlanningWorkspaceControl() { InitializeComponent(); }

    public void LoadPlan(DrawingPlanDocument? plan) { _controller.Load(plan); Refresh(); }
    public DrawingPlanDocument? CurrentPlan => _controller.CurrentPlan;

    private async void GeneratePreview_Click(object sender, RoutedEventArgs e)
    {
        if (PlannerClient is null || PlanningInputProvider is null) { StatusText.Text = "Planner runtime is not configured."; return; }
        try
        {
            var input = PlanningInputProvider();
            if (CheckpointAsync is not null) await CheckpointAsync(ProjectRevisionTrigger.GeneratePreview, "Generate Preview");
            var plan = await PlannerClient.GenerateAsync(input, _controller.CurrentPlan, CancellationToken.None);
            _controller.Load(plan);
            if (ProjectProvider is not null && ProjectReplaced is not null)
            {
                var project = ProjectProvider(); project.DrawingPlan = plan; ProjectReplaced(project);
            }
            Refresh(); StatusText.Text = plan.Issues.Any(x => x.Severity == DrawingPlanningIssueSeverity.Blocker) ? "Progressive Preview generated with localized blockers." : "Preview generated.";
        }
        catch (Exception ex) { StatusText.Text = $"Preview failed: {ex.Message}"; }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectProvider is not null && ProjectReplaced is not null && _controller.CurrentPlan is not null)
        { var project = ProjectProvider(); project.DrawingPlan = _controller.CurrentPlan; ProjectReplaced(project); }
        if (CheckpointAsync is not null) await CheckpointAsync(ProjectRevisionTrigger.Save, "Drawing Planning Save");
        if (SaveProjectAsync is not null) await SaveProjectAsync();
        StatusText.Text = "Drawing Plan saved.";
    }

    private void Undo_Click(object sender, RoutedEventArgs e) { if (_controller.Undo()) { PersistPlan(); Refresh(); } }
    private void Redo_Click(object sender, RoutedEventArgs e) { if (_controller.Redo()) { PersistPlan(); Refresh(); } }
    private void AlignLeft_Click(object sender, RoutedEventArgs e) { TryEdit(() => _controller.Align(DrawingAlignment.Left)); }
    private void DistributeHorizontal_Click(object sender, RoutedEventArgs e) { TryEdit(() => _controller.Distribute(DrawingDistribution.Horizontal)); }
    private void ResetPage_Click(object sender, RoutedEventArgs e) { if (_controller.SelectedPageId is { } id) TryEdit(() => _controller.ResetPage(id)); }

    private async void History_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryItemsAsync is null || RestoreRevisionAsync is null) { StatusText.Text = "Revision history service is not configured."; return; }
        var ids = await HistoryItemsAsync(); if (ids.Count == 0) { StatusText.Text = "No revisions."; return; }
        var dialog = new Window { Owner = Window.GetWindow(this), Title = "Drawing Revision History", Width = 620, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var list = new ListBox { ItemsSource = ids, Margin = new Thickness(8) }; var restore = new Button { Content = "Restore selected revision", Margin = new Thickness(8), Padding = new Thickness(10,5,10,5), HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new DockPanel(); DockPanel.SetDock(restore, Dock.Bottom); panel.Children.Add(restore); panel.Children.Add(list); dialog.Content = panel;
        restore.Click += async (_, _) => { if (list.SelectedItem is string revisionId) { var restored = await RestoreRevisionAsync(revisionId); ProjectReplaced?.Invoke(restored); LoadPlan(restored.DrawingPlan); dialog.Close(); } };
        dialog.ShowDialog();
    }

    private void PageList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (PageList.SelectedItem is DrawingPlanPage page) { _controller.SelectPage(page.PageId); RefreshCanvas(); } }
    private void SelectionList_SelectionChanged(object sender, SelectionChangedEventArgs e) { _controller.SelectRepresentations(SelectionList.SelectedItems.Cast<string>()); }
    private void ApplyPlacementState_Click(object sender, RoutedEventArgs e) { var state = StateCombo.SelectedIndex switch { 1 => DrawingPlanControlState.Manual, 2 => DrawingPlanControlState.Locked, _ => DrawingPlanControlState.Auto }; foreach (var id in _controller.SelectedRepresentationIds.ToArray()) TryEdit(() => _controller.SetPlacementState(id, state)); }
    private void Rotate_Click(object sender, RoutedEventArgs e) { foreach (var id in _controller.SelectedRepresentationIds.ToArray()) { var p = _controller.CurrentPlan?.Placements.SingleOrDefault(x => x.RepresentationId == id); if (p is null) continue; var legal = p.AllowedRotations.OrderBy(x => x).ToArray(); var next = legal.FirstOrDefault(x => x > p.RotationDegrees); if (!legal.Contains(next)) next = legal[0]; TryEdit(() => _controller.RotatePlacement(id, next)); } }

    public void MoveRouteSegment(string routeId, int segmentIndex, long delta) => TryEdit(() => _controller.MoveRouteSegment(routeId, segmentIndex, delta));
    public void MoveBendPoint(string routeId, int pointIndex, long x, long y) => TryEdit(() => _controller.MoveBendPoint(routeId, pointIndex, x, y));
    public void AddBendPoint(string routeId, int segmentIndex, long x, long y) => TryEdit(() => _controller.AddBendPoint(routeId, segmentIndex, x, y));
    public void DeleteBendPoint(string routeId, int pointIndex) => TryEdit(() => _controller.DeleteBendPoint(routeId, pointIndex));
    public void SetRouteState(string routeId, DrawingPlanControlState state) => TryEdit(() => _controller.SetRouteState(routeId, state));

    private void DrawingCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.OriginalSource == DrawingCanvas) { _draggingRepresentationId = null; _controller.SelectRepresentations([]); SelectionList.UnselectAll(); } }
    private void Placement_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (sender is not FrameworkElement element || element.Tag is not string id) return; var p = _controller.CurrentPlan?.Placements.Single(x => x.RepresentationId == id); if (p?.State == DrawingPlanControlState.Locked) return; _draggingRepresentationId = id; var point = e.GetPosition(DrawingCanvas); _dragOffset = new Point(point.X - p!.X, point.Y - p.Y); element.CaptureMouse(); e.Handled = true; }
    private void Placement_MouseMove(object sender, MouseEventArgs e) { if (_draggingRepresentationId is null || e.LeftButton != MouseButtonState.Pressed) return; var point = e.GetPosition(DrawingCanvas); var snap = long.TryParse(GridSnapText.Text, out var g) && g > 0 ? g : 10; var x = (long)Math.Round((point.X - _dragOffset.X) / snap) * snap; var y = (long)Math.Round((point.Y - _dragOffset.Y) / snap) * snap; TryEdit(() => _controller.MovePlacement(_draggingRepresentationId, x, y)); }
    private void Placement_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { _draggingRepresentationId = null; if (sender is FrameworkElement element) element.ReleaseMouseCapture(); }
    private void DrawingScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e) { if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return; _zoom = Math.Clamp(_zoom + (e.Delta > 0 ? 0.1 : -0.1), 0.4, 3.0); DrawingCanvas.LayoutTransform = new ScaleTransform(_zoom, _zoom); e.Handled = true; }

    private void TryEdit(Action action) { try { action(); PersistPlan(); Refresh(); } catch (Exception ex) { StatusText.Text = ex.Message; } }
    private void PersistPlan() { if (_controller.CurrentPlan is null || ProjectProvider is null || ProjectReplaced is null) return; var project = ProjectProvider(); project.DrawingPlan = _controller.CurrentPlan; ProjectReplaced(project); }
    private void Refresh() { var plan = _controller.CurrentPlan; PageList.ItemsSource = plan?.Pages.OrderBy(x => x.Order).ToArray() ?? []; SelectionList.ItemsSource = plan?.Placements.Select(x => x.RepresentationId).OrderBy(x => x, StringComparer.Ordinal).ToArray() ?? []; RefreshCanvas(); }

    private void RefreshCanvas()
    {
        DrawingCanvas.Children.Clear(); var plan = _controller.CurrentPlan; if (plan is null) return; var pageId = _controller.SelectedPageId ?? plan.Pages.OrderBy(x => x.Order).FirstOrDefault()?.PageId; if (pageId is null) return;
        foreach (var route in plan.Routes)
        {
            var placements = plan.Placements.Where(p => p.PageId == pageId).Select(p => p.RepresentationId).ToHashSet(StringComparer.Ordinal); if (placements.Count == 0) continue;
            var polyline = new Polyline { Stroke = Brushes.SteelBlue, StrokeThickness = 1.5, IsHitTestVisible = false }; foreach (var point in route.Points) polyline.Points.Add(new Point(point.X, point.Y)); DrawingCanvas.Children.Add(polyline);
        }
        foreach (var placement in plan.Placements.Where(x => x.PageId == pageId))
        {
            var border = new Border { Width = placement.Width, Height = placement.Height, BorderBrush = placement.State == DrawingPlanControlState.Locked ? Brushes.DarkRed : Brushes.DimGray, BorderThickness = new Thickness(1.5), Background = Brushes.WhiteSmoke, Tag = placement.RepresentationId, Child = new TextBlock { Text = placement.RepresentationId, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) } };
            Canvas.SetLeft(border, placement.X); Canvas.SetTop(border, placement.Y); border.RenderTransform = new RotateTransform(placement.RotationDegrees, placement.Width / 2.0, placement.Height / 2.0); border.MouseLeftButtonDown += Placement_MouseLeftButtonDown; border.MouseMove += Placement_MouseMove; border.MouseLeftButtonUp += Placement_MouseLeftButtonUp; DrawingCanvas.Children.Add(border);
        }
    }
}
