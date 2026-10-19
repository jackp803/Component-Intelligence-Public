using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private SchematicProjectSearchResult? _searchHighlightResult;
    private ElectricalProject? _searchHighlightProject;
    private SchematicDocument? _searchHighlightDocument;
    private object? _searchHighlightTarget;
    private readonly List<Canvas> _searchHighlightOverlays = [];
    private DispatcherOperation? _searchCenterOperation;

    private void ProjectSearch_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SchematicProjectSearchDialog(_getProject) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.SelectedResult is { } result) NavigateSearchResult(result);
    }

    private bool NavigateSearchResult(SchematicProjectSearchResult result)
    {
        if (!ResolveInspectorDraft()) return false;
        var project = _getProject();
        var current = SchematicProjectSearch.Resolve(project, result);
        if (current is null) { Status.Text = "搜尋項目已變更或不存在，請重新搜尋。"; return false; }
        CancelCommand();
        _panStart = null; PageScroll.Cursor = null;
        if (PageScroll.IsMouseCaptured) PageScroll.ReleaseMouseCapture();
        ClearSearchHighlights();
        _pageId = current.PageId; _selectionId = current.ObjectId;
        if (current.Kind == SchematicProjectSearchKind.Wire) _selectedWires.Add(current.ObjectId);
        _searchHighlightResult = current; _searchHighlightProject = project; _searchHighlightDocument = project.Schematic;
        _searchHighlightTarget = current.Kind == SchematicProjectSearchKind.Wire
            ? project.Schematic!.Wires.First(w => w.WireId == current.ObjectId)
            : project.Schematic!.Symbols.First(s => s.SymbolId == current.ObjectId);
        LayoutUpdated += SearchHighlightStateChanged;
        CompositionTarget.Rendering += SearchHighlightStateChanged;
        Unloaded += SearchHighlightsUnloaded;
        RefreshWorkspace();
        _searchCenterOperation = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _searchCenterOperation = null;
            if (!SearchHighlightIsCurrent(_getProject())) return;
            PageScroll.UpdateLayout();
            var bounds = current.FocusBounds;
            var fit = Math.Min(PageScroll.ViewportWidth / (bounds.Width * PixelsPerMm + 24),
                PageScroll.ViewportHeight / (bounds.Height * PixelsPerMm + 24));
            if (fit > 0 && fit < Zoom.Value)
            {
                Zoom.Value = Math.Max(Zoom.Minimum, fit);
                PageScroll.UpdateLayout();
                RenderSearchHighlights(project, project.Schematic!.Pages.First(p => p.PageId == current.PageId));
            }
            var center = Sheet.TransformToAncestor(PageScroll).Transform(new Point(
                (bounds.X + bounds.Width / 2) * PixelsPerMm, (bounds.Y + bounds.Height / 2) * PixelsPerMm));
            PageScroll.ScrollToHorizontalOffset(PageScroll.HorizontalOffset + center.X - PageScroll.ViewportWidth / 2);
            PageScroll.ScrollToVerticalOffset(PageScroll.VerticalOffset + center.Y - PageScroll.ViewportHeight / 2);
        }));
        Status.Text = $"{current.PageNumber:00} {current.PageTitle} / {current.Name}";
        FocusCanvas();
        return true;
    }

    private bool SearchHighlightIsCurrent(ElectricalProject project)
    {
        if (_searchHighlightResult is not { } result || !ReferenceEquals(project, _searchHighlightProject) ||
            !ReferenceEquals(project.Schematic, _searchHighlightDocument) || result.PageId != _pageId || result.ObjectId != _selectionId)
            return false;
        return result.Kind == SchematicProjectSearchKind.Wire
            ? _selectedWires.Count == 1 && _selectedWires.Contains(result.ObjectId) &&
                ReferenceEquals(project.Schematic?.Wires.FirstOrDefault(w => w.WireId == result.ObjectId), _searchHighlightTarget)
            : _selectedWires.Count == 0 &&
                ReferenceEquals(project.Schematic?.Symbols.FirstOrDefault(s => s.SymbolId == result.ObjectId), _searchHighlightTarget);
    }

    private void SearchHighlightStateChanged(object? sender, EventArgs e)
    {
        if (!_renderingOutput && !SearchHighlightIsCurrent(_getProject())) ClearSearchHighlights();
    }

    private void SearchHighlightsUnloaded(object sender, RoutedEventArgs e) => ClearSearchHighlights();

    private void ClearSearchHighlights()
    {
        LayoutUpdated -= SearchHighlightStateChanged;
        CompositionTarget.Rendering -= SearchHighlightStateChanged;
        Unloaded -= SearchHighlightsUnloaded;
        _searchCenterOperation?.Abort(); _searchCenterOperation = null;
        _searchHighlightResult = null; _searchHighlightProject = null; _searchHighlightDocument = null; _searchHighlightTarget = null;
        foreach (var overlay in _searchHighlightOverlays)
            if (overlay.Parent is Panel parent) parent.Children.Remove(overlay);
        _searchHighlightOverlays.Clear();
    }

    private void RenderSearchHighlights(ElectricalProject project, SchematicPage page)
    {
        if (_renderingOutput || _searchHighlightResult is not { } result) return;
        if (!SearchHighlightIsCurrent(project)) { ClearSearchHighlights(); return; }
        if (page.PageId != result.PageId) return;
        foreach (var old in _searchHighlightOverlays.ToArray())
        {
            if (old.Parent is Panel parent) parent.Children.Remove(old);
            _searchHighlightOverlays.Remove(old);
        }
        var overlay = new Canvas { Tag = "ProjectSearchHighlights", IsHitTestVisible = false,
            Width = page.Width * PixelsPerMm, Height = page.Height * PixelsPerMm };
        Panel.SetZIndex(overlay, 10000);
        var doc = project.Schematic!;
        var wire = result.Kind == SchematicProjectSearchKind.Wire ? doc.Wires.FirstOrDefault(w => w.WireId == result.ObjectId) : null;
        var zoom = Math.Max(.01, Zoom.Value);
        if (wire is not null)
        {
            foreach (var (brush, thickness) in new[] { (Brushes.Gold, 7d), (Brushes.DarkCyan, 2.5) })
                overlay.Children.Add(new Polyline { Points = new PointCollection(wire.Points.Select(p => new Point(p.X * PixelsPerMm, p.Y * PixelsPerMm))),
                    Stroke = brush, StrokeThickness = thickness / zoom, StrokeLineJoin = PenLineJoin.Round,
                    Tag = wire.WireId, IsHitTestVisible = false });
        }
        var symbolIds = wire is null ? new[] { result.ObjectId } : result.EndpointSymbolIds;
        foreach (var id in symbolIds)
        {
            var symbol = doc.Symbols.FirstOrDefault(s => s.SymbolId == id && s.PageId == page.PageId);
            if (symbol is null) continue;
            var bounds = SchematicPortPresentation.GenericBodyBounds(doc, symbol, SchematicSymbolOwner.Resolve(project, symbol));
            var rectangle = new Rectangle { Width = bounds.Width * PixelsPerMm + 8 / zoom, Height = bounds.Height * PixelsPerMm + 8 / zoom,
                Stroke = Brushes.DarkOrange, StrokeThickness = 2.5 / zoom, Fill = new SolidColorBrush(Color.FromArgb(25, 255, 185, 0)),
                Tag = symbol.SymbolId, IsHitTestVisible = false };
            Canvas.SetLeft(rectangle, bounds.X * PixelsPerMm - 4 / zoom);
            Canvas.SetTop(rectangle, bounds.Y * PixelsPerMm - 4 / zoom);
            overlay.Children.Add(rectangle);
        }
        Sheet.Children.Add(overlay); _searchHighlightOverlays.Add(overlay);
    }
}

internal sealed class SchematicProjectSearchDialog : Window
{
    private readonly Func<ElectricalProject> _getProject;
    private readonly TextBox _query;
    private readonly TextBlock _state;
    private readonly DataGrid _results;
    private readonly Button _go;
    internal SchematicProjectSearchResult? SelectedResult { get; private set; }

    internal SchematicProjectSearchDialog(Func<ElectricalProject> getProject)
    {
        _getProject = getProject;
        NameScope.SetNameScope(this, new NameScope());
        Title = "搜尋工程圖"; Width = 980; Height = 560; MinWidth = 580; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new(12) }; Content = panel;
        var queryPanel = new DockPanel { Margin = new(0, 0, 0, 8) };
        queryPanel.Children.Add(new Label { Content = "搜尋", VerticalAlignment = VerticalAlignment.Center, Padding = new(0, 0, 10, 0) });
        _query = new TextBox { Name = "ProjectSearchQuery", MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new(6, 3, 6, 3) }; queryPanel.Children.Add(_query);
        DockPanel.SetDock(queryPanel, Dock.Top); panel.Children.Add(queryPanel);
        _state = new TextBlock { Name = "ProjectSearchState", Margin = new(0, 0, 0, 8) };
        DockPanel.SetDock(_state, Dock.Top); panel.Children.Add(_state);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 80, Padding = new(10, 5, 10, 5), Margin = new(8, 0, 0, 0) };
        _go = new Button { Content = "前往", MinWidth = 80, Padding = new(10, 5, 10, 5), IsEnabled = false };
        actions.Children.Add(_go); actions.Children.Add(cancel);
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        _results = new DataGrid { Name = "ProjectSearchResults", AutoGenerateColumns = false, IsReadOnly = true,
            CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow, HeadersVisibility = DataGridHeadersVisibility.Column,
            EnableRowVirtualization = true, EnableColumnVirtualization = true, RowHeaderWidth = 0 };
        _results.Columns.Add(new DataGridTextColumn { Header = "頁碼", Binding = new Binding("PageNumber") { StringFormat = "00" }, Width = 48 });
        _results.Columns.Add(new DataGridTextColumn { Header = "頁面", Binding = new Binding("PageTitle"), Width = 140 });
        _results.Columns.Add(new DataGridTextColumn { Header = "類型", Binding = new Binding("KindLabel"), Width = 90 });
        var wrap = new Style(typeof(TextBlock)) { Setters = { new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap) } };
        _results.Columns.Add(new DataGridTextColumn { Header = "名稱", Binding = new Binding("Name"), MinWidth = 130,
            Width = new(1, DataGridLengthUnitType.Star), ElementStyle = wrap });
        _results.Columns.Add(new DataGridTextColumn { Header = "導線端點", Binding = new Binding("EndpointDescription"), MinWidth = 160,
            Width = new(1.5, DataGridLengthUnitType.Star), ElementStyle = wrap });
        panel.Children.Add(_results);
        RegisterName(_query.Name, _query); RegisterName(_state.Name, _state); RegisterName(_results.Name, _results);
        _query.TextChanged += (_, _) => RefreshResults();
        _results.SelectionChanged += (_, _) => _go.IsEnabled = _results.SelectedItem is SchematicProjectSearchResult;
        _go.Click += (_, _) => Accept();
        _results.MouseDoubleClick += (_, e) => {
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(_results, source) is DataGridRow)
            { Accept(); e.Handled = true; }
        };
        PreviewKeyDown += (_, e) => {
            if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
            else if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
            else if (e.Key == Key.Down && _query.IsKeyboardFocusWithin && _results.Items.Count > 0)
            { _results.Focus(); _results.SelectedIndex = Math.Max(0, _results.SelectedIndex); e.Handled = true; }
        };
        Loaded += (_, _) => _query.Focus();
        RefreshResults();
    }

    private void RefreshResults()
    {
        var rows = SchematicProjectSearch.Find(_getProject(), _query.Text);
        _results.ItemsSource = rows;
        _results.SelectedIndex = rows.Count > 0 ? 0 : -1;
        _state.Text = string.IsNullOrWhiteSpace(_query.Text) ? "輸入搜尋文字" : $"{rows.Count} 筆結果";
        _go.IsEnabled = rows.Count > 0;
    }

    private void Accept()
    {
        if (_results.SelectedItem is not SchematicProjectSearchResult result) return;
        SelectedResult = result; DialogResult = true;
    }
}
