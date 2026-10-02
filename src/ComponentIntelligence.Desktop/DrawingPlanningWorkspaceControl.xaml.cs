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
    private readonly DrawingPlanEditService _edits = new();
    private readonly DrawingPlanningWorkspaceController _controller;
    private string? _draggingRepresentationId;
    private Point _dragOffset;
    private double _zoom = 1.0;
    private bool _refreshingSelection;
    private DrawingPlanningInput? _previewInput;
    private IReadOnlyDictionary<string, DrawingRepresentationDecision> _previewCatalog = new Dictionary<string, DrawingRepresentationDecision>();

    public IDrawingPlannerClient? PlannerClient { get; set; }
    public Func<DrawingPlanningInput>? PlanningInputProvider { get; set; }
    public Func<ElectricalProject>? ProjectProvider { get; set; }
    public Action<ElectricalProject>? ProjectReplaced { get; set; }
    public Func<ProjectRevisionTrigger, string?, Task>? CheckpointAsync { get; set; }
    public Func<Task>? SaveProjectAsync { get; set; }
    public Func<Task<IReadOnlyList<string>>>? HistoryItemsAsync { get; set; }
    public Func<string, Task<ElectricalProject>>? RestoreRevisionAsync { get; set; }

    public DrawingPlanningWorkspaceControl() { _controller = new(_edits); InitializeComponent(); }


    public void LoadPlan(DrawingPlanDocument? plan, DrawingPlanningInput? previewInput = null)
    {
        var input = previewInput ?? (plan is null ? null : PlanningInputProvider?.Invoke());
        _previewInput = input;
        _previewCatalog = input is null ? new Dictionary<string, DrawingRepresentationDecision>()
            : DrawingPreviewCatalog.Build(input).ToDictionary(r => r.RepresentationId, StringComparer.Ordinal);
        _edits.SetEndpointBindings(_previewCatalog.ToDictionary(p => p.Key,
            p => (IReadOnlySet<string>)p.Value.PortBindings.Select(b => b.EngineeringEndpointId).ToHashSet(StringComparer.Ordinal)));
        _selectedRouteId = null;
        _controller.Load(plan); Refresh();
        StatusText.Text = plan is null ? "Drawing Plan not loaded." : $"Drawing Plan loaded: {plan.Pages.Count} pages.";
    }
    public DrawingPlanDocument? CurrentPlan => _controller.CurrentPlan;

    private async void GeneratePreview_Click(object sender, RoutedEventArgs e)
    {
        if (PlannerClient is null || PlanningInputProvider is null) { StatusText.Text = "Planner runtime is not configured."; return; }
        try
        {
            var input = PlanningInputProvider();
            if (CheckpointAsync is not null) await CheckpointAsync(ProjectRevisionTrigger.GeneratePreview, "Generate Preview");
            var plan = await PlannerClient.GenerateAsync(input, _controller.CurrentPlan, CancellationToken.None);
            LoadPlan(plan, input);
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

    private void PageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageList.SelectedItem is not DrawingPlanPage page) return;
        if (_controller.SelectedPageId != page.PageId)
        {
            if (_gestureActive) CancelCanvasGesture();
            _controller.SelectPage(page.PageId); _selectedRouteId = null;
        }
        RefreshSelection(); RefreshCanvas();
    }
    private void SelectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingSelection || CurrentPlan is null) return;
        _selectedRouteId = null;
        _controller.SelectRepresentations(SelectionList.SelectedItems.Cast<PreviewSelection>().Select(x => x.Id));
        RefreshSelectedState();
        RefreshCanvas();
    }
    private void ApplyPlacementState_Click(object sender, RoutedEventArgs e)
    {
        if (StateCombo.SelectedIndex < 0)
        {
            StatusText.Text = "請先選擇要套用的狀態。";
            return;
        }
        var state = StateCombo.SelectedIndex switch { 1 => DrawingPlanControlState.Manual, 2 => DrawingPlanControlState.Locked, _ => DrawingPlanControlState.Auto };
        if (_selectedRouteId is { } route) { SetRouteState(route, state); return; }
        TryEdit(() => _controller.SetSelectedPlacementState(state));
    }
    private void Rotate_Click(object sender, RoutedEventArgs e) { foreach (var id in _controller.SelectedRepresentationIds.ToArray()) { var p = _controller.CurrentPlan?.Placements.SingleOrDefault(x => x.RepresentationId == id); if (p is null) continue; var legal = p.AllowedRotations.OrderBy(x => x).ToArray(); var next = legal.FirstOrDefault(x => x > p.RotationDegrees); if (!legal.Contains(next)) next = legal[0]; TryEdit(() => _controller.RotatePlacement(id, next)); } }

    public void MoveRouteSegment(string routeId, int segmentIndex, long delta) => TryEdit(() => _controller.MoveRouteSegment(routeId, segmentIndex, delta));
    public void MoveBendPoint(string routeId, int pointIndex, long x, long y) => TryEdit(() => _controller.MoveBendPoint(routeId, pointIndex, x, y));
    public void AddBendPoint(string routeId, int segmentIndex, long x, long y) => TryEdit(() => _controller.AddBendPoint(routeId, segmentIndex, x, y));
    public void DeleteBendPoint(string routeId, int pointIndex) => TryEdit(() => _controller.DeleteBendPoint(routeId, pointIndex));
    public void SetRouteState(string routeId, DrawingPlanControlState state) => TryEdit(() => _controller.SetRouteState(routeId, state));

    private void DrawingCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.OriginalSource == DrawingCanvas) { _draggingRepresentationId = null; _controller.SelectRepresentations([]); SelectionList.UnselectAll(); } }
    private void Placement_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string id) return;
        var p = _controller.CurrentPlan?.Placements.Single(x => x.RepresentationId == id); if (p is null) return;
        _selectedRouteId = null; _controller.SelectRepresentations([id]);
        RefreshSelection();
        if (p.State == DrawingPlanControlState.Locked) { StatusText.Text = "位置已鎖定，請先解除鎖定。"; e.Handled = true; return; }
        _draggingRepresentationId = id; var point = e.GetPosition(DrawingCanvas); _dragOffset = new Point(point.X - p.X, point.Y - p.Y);
        BeginCanvasGesture(point); e.Handled = true;
    }
    private void DrawingScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e) { if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return; _zoom = Math.Clamp(_zoom + (e.Delta > 0 ? 0.1 : -0.1), 0.4, 3.0); DrawingCanvas.LayoutTransform = new ScaleTransform(_zoom, _zoom); e.Handled = true; }

    private void TryEdit(Action action) { try { action(); PersistPlan(); Refresh(); } catch (Exception ex) { StatusText.Text = ex.Message; } }
    private void PersistPlan() { if (_controller.CurrentPlan is null || ProjectProvider is null || ProjectReplaced is null) return; var project = ProjectProvider(); project.DrawingPlan = _controller.CurrentPlan; ProjectReplaced(project); }
    private sealed record PreviewSelection(string Id, string Label)
    {
        public override string ToString() => Label.Replace('\n', ' ');
    }

    private IReadOnlyDictionary<string, string> PreviewLabels()
    {
        var labels = ProjectProvider is null ? new Dictionary<string, string>() : DrawingPreviewLabels.Build(ProjectProvider()).ToDictionary(p => p.Key, p => p.Value);
        foreach (var rep in _previewCatalog.Values)
            if (!labels.ContainsKey(rep.RepresentationId) && labels.TryGetValue($"REP:{rep.OwnerId}:Schematic", out var label))
                labels[rep.RepresentationId] = label + "\n接點續頁";
        return labels;
    }

    private void Refresh()
    {
        var plan = _controller.CurrentPlan;
        var selected = _controller.SelectedPageId;
        PageList.ItemsSource = plan?.Pages.OrderBy(x => x.Order).ToArray() ?? [];
        PageList.SelectedItem = plan?.Pages.FirstOrDefault(x => x.PageId == selected);
        RefreshSelection();
        RefreshCanvas();
    }

    private void RefreshSelection()
    {
        var selected = _controller.SelectedRepresentationIds.ToHashSet(StringComparer.Ordinal);
        var labels = PreviewLabels();
        var items = CurrentPlan?.Placements.Where(p => p.PageId == _controller.SelectedPageId)
            .Select(p => new PreviewSelection(p.RepresentationId, labels.GetValueOrDefault(p.RepresentationId, "名稱待確認"))).ToArray() ?? [];
        _refreshingSelection = true;
        try
        {
            SelectionList.ItemsSource = items;
            foreach (var item in items.Where(i => selected.Contains(i.Id))) SelectionList.SelectedItems.Add(item);
        }
        finally { _refreshingSelection = false; }
        RefreshSelectedState();
    }

    private void RefreshSelectedState() => StateCombo.SelectedIndex = _controller.SelectionState(_selectedRouteId) is { } state ? (int)state : -1;

    private void RefreshCanvas()
    {
        DrawingCanvas.Children.Clear(); var plan = _controller.DisplayPlan; if (plan is null) return; var pageId = _controller.SelectedPageId ?? plan.Pages.OrderBy(x => x.Order).FirstOrDefault()?.PageId; if (pageId is null) return;
        var labels = PreviewLabels();
        var project = ProjectProvider?.Invoke();
        var page = plan.Pages.Single(x => x.PageId == pageId);
        DrawingCanvas.Width = page.Bounds.Width;
        DrawingCanvas.Height = page.Bounds.Height;
        foreach (var route in plan.Routes.Where(r => r.PageId == pageId)) DrawEditableRoute(route);
        foreach (var placement in plan.Placements.Where(x => x.PageId == pageId))
        {
            var label = labels.GetValueOrDefault(placement.RepresentationId, "名稱待確認");
            var border = new Border { Width = placement.Width, Height = placement.Height, BorderBrush = placement.State == DrawingPlanControlState.Locked ? Brushes.DarkRed : Brushes.DimGray, BorderThickness = new Thickness(1.5), Background = Brushes.WhiteSmoke, Tag = placement.RepresentationId, ToolTip = label,
                Child = CablePreview(placement, project, label) ?? HeavyDutyPreview(placement, project, label) ?? (placement.Height < 40
                    ? new TextBlock { Text = label.Replace('\n', ' '), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 2, 4, 2) }
                    : new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(5),
                    Child = new TextBlock { Width = Math.Max(30, placement.Width - 12), Text = label, TextWrapping = TextWrapping.Wrap, FontSize = 13 } }) };
            Canvas.SetLeft(border, placement.X); Canvas.SetTop(border, placement.Y); border.RenderTransform = new RotateTransform(placement.RotationDegrees, placement.Width / 2.0, placement.Height / 2.0); border.MouseLeftButtonDown += Placement_MouseLeftButtonDown; DrawingCanvas.Children.Add(border);
        }
        if (_previewInput is not null)
            foreach (var port in DrawingIoPortLabels.Build(_previewInput, plan).Where(label => label.PageId == pageId))
            {
                var text = new TextBlock { Text = port.Text, FontSize = 11, Foreground = Brushes.Black, Background = Brushes.White,
                    ToolTip = port.EngineeringEndpointId };
                Canvas.SetLeft(text, port.X); Canvas.SetTop(text, port.Y); DrawingCanvas.Children.Add(text);
            }
        DrawContinuationLabels(plan, pageId);
    }

    private void DrawContinuationLabels(DrawingPlanDocument plan, string pageId)
    {
        if (_previewInput is null) return;
        var labels = DrawingContinuationPresentation.Build(_previewInput, plan, PreviewLabels());
        foreach (var placed in DrawingContinuationLayout.Place(plan, labels, pageId))
        {
            var label = placed.Label;
            var relation = plan.CrossPageRelations.Single(r => r.RelationId == label.RelationId);
            var routeId = relation.SourcePageId == pageId ? relation.SourceRouteId : relation.DestinationRouteId;
            var route = plan.Routes.Single(r => r.RouteId == routeId);
            var p = label.Anchor;
            var previous = p == route.Points[0] ? route.Points[1] : route.Points[^2];
            var dx = Math.Sign(p.X - previous.X); var dy = Math.Sign(p.Y - previous.Y);
            var arrow = new Polygon { Stroke = Brushes.DimGray, Fill = Brushes.White, StrokeThickness = 1,
                Points = new PointCollection { new(p.X, p.Y), new(p.X - dx * 7 - dy * 3, p.Y - dy * 7 + dx * 3), new(p.X - dx * 7 + dy * 3, p.Y - dy * 7 - dx * 3) } };
            DrawingCanvas.Children.Add(arrow);
            var box = placed.Box;
            void Navigate(object? _, MouseButtonEventArgs e)
            {
                _controller.SelectPage(label.PeerPageId); _selectedRouteId = null; Refresh();
                DrawingScroll.UpdateLayout();
                DrawingScroll.ScrollToHorizontalOffset(Math.Max(0, label.PeerAnchor.X - DrawingScroll.ViewportWidth / 2));
                DrawingScroll.ScrollToVerticalOffset(Math.Max(0, label.PeerAnchor.Y - DrawingScroll.ViewportHeight / 2));
                e.Handled = true;
            }
            var marker = new TextBlock { Text = $"↗{placed.Index}", FontSize = 9, Foreground = Brushes.Black,
                Background = Brushes.White, Cursor = Cursors.Hand, ToolTip = "跨頁參照索引，非線號。" };
            marker.MouseLeftButtonDown += Navigate;
            Canvas.SetLeft(marker, Math.Clamp(p.X + (dx < 0 ? -25 : 5), 0, DrawingCanvas.Width - 30));
            Canvas.SetTop(marker, Math.Clamp(p.Y - 15, 0, DrawingCanvas.Height - 16));
            Canvas.SetZIndex(marker, 3);
            DrawingCanvas.Children.Add(marker);
            var caption = new Border { Width = box.Width, Height = box.Height, Background = Brushes.White,
                BorderBrush = placed.FitsWithoutOverlap ? Brushes.LightGray : Brushes.DarkOrange,
                BorderThickness = new Thickness(0.5), Padding = new Thickness(3), Cursor = Cursors.Hand,
                ToolTip = $"跨頁參照 {placed.Index}（非線號）/ 前往 {label.PeerPageId} / 對端位置 ({label.PeerAnchor.X}, {label.PeerAnchor.Y})",
                Child = new TextBlock { Text = $"↗{placed.Index}  {label.Text}", FontSize = 10, LineHeight = 12, TextWrapping = TextWrapping.Wrap } };
            caption.MouseLeftButtonDown += Navigate;
            Canvas.SetLeft(caption, box.X); Canvas.SetTop(caption, box.Y);
            Canvas.SetZIndex(caption, 2);
            DrawingCanvas.Children.Add(caption);
        }
    }
}
