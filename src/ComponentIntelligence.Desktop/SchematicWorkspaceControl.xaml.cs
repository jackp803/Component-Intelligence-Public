using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl : UserControl
{
    private const double PixelsPerMm = 3;
    private readonly SchematicAuthoringService _service = new();
    private readonly Func<ElectricalProject> _getProject;
    private readonly Action<ElectricalProject, string> _commit;
    private readonly Func<Task<IReadOnlyList<ComponentIR>>> _catalogProvider;
    private readonly Func<string, Task<Uri?>> _imageResolver;
    private readonly Action _undo;
    private readonly Action _redo;
    private IReadOnlyList<ComponentIR> _catalog = [];
    private string? _pageId, _selectionId;
    private bool _refreshing, _wireMode, _placeMode;
    private SchematicAttachment? _wireStart;
    private List<SchematicPoint> _wirePoints = [];
    private SchematicPoint? _pointer;
    private ElectricalProject? _gestureStart, _gesturePreview;
    private SchematicPoint? _dragStart;
    private string? _dragSymbol;
    private int? _dragVertex;
    private int? _dragSegment;
    private string? _dragMarker;
    private string? _resumeWireId;
    private bool _resumeReversed;

    public SchematicWorkspaceControl(Func<ElectricalProject> getProject, Action<ElectricalProject, string> commit,
        Func<Task<IReadOnlyList<ComponentIR>>> catalogProvider, Func<string, Task<Uri?>> imageResolver,
        Action undo, Action redo)
    {
        _getProject = getProject; _commit = commit; _catalogProvider = catalogProvider; _imageResolver = imageResolver;
        _undo = undo; _redo = redo;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try { _catalog = await _catalogProvider(); FilterCatalog(); RefreshWorkspace(); }
            catch (Exception e) { Status.Text = "資料中心讀取失敗：" + e.Message; }
        };
    }

    public void RefreshWorkspace()
    {
        var project = _getProject(); var doc = project.Schematic;
        _refreshing = true;
        try
        {
            var pages = doc?.Pages.Select((p, i) => new PageItem(p.PageId, $"{i + 1:00}  {p.Title}")).ToArray() ?? [];
            if (!pages.Any(p => p.Id == _pageId)) _pageId = pages.FirstOrDefault()?.Id;
            PageList.ItemsSource = pages;
            PageList.SelectedItem = pages.FirstOrDefault(p => p.Id == _pageId);
        }
        finally { _refreshing = false; }
        Render(project);
    }

    private bool Apply(Func<ElectricalProject, ElectricalProject> command, string label)
    {
        try { var next = command(_getProject()); _commit(next, label); RefreshWorkspace(); Status.Text = label; return true; }
        catch (Exception e) { Status.Text = "未套用：" + e.Message; return false; }
    }

    private void Render(ElectricalProject project)
    {
        Sheet.Children.Clear();
        var doc = project.Schematic; var page = doc?.Pages.SingleOrDefault(p => p.PageId == _pageId);
        if (doc is null || page is null) { Status.Text = "尚無工程圖頁面"; return; }
        Sheet.Width = page.Width * PixelsPerMm; Sheet.Height = page.Height * PixelsPerMm;
        if (page.TemplateGeometry is not null)
        {
            var geometry = CadCanvas(page.TemplateGeometry); geometry.IsHitTestVisible = false; Sheet.Children.Add(geometry);
        }
        DrawFrame(page, doc.Pages.IndexOf(page) + 1, project.Name);
        var crossings = SchematicCrossingService.Analyze(doc.Wires.Where(w => w.PageId == _pageId).ToArray());
        foreach (var wire in doc.Wires.Where(w => w.PageId == _pageId))
        {
            var line = Path(wire.Points, wire.WireId == _selectionId ? Brushes.DarkCyan : Brushes.Black, wire.WireId == _selectionId ? 2.5 : 1.3);
            if (wire.ConnectionId is null) line.StrokeDashArray = new DoubleCollection([5, 3]);
            line.ToolTip = wire.ConnectionId is null ? "待接續導線" : "已建立工程連線";
            line.MouseLeftButtonDown += (_, e) =>
            {
                if (_wireMode) return;
                _selectionId = wire.WireId; Render(_getProject()); e.Handled = true; Focus();
            };
            if (crossings.Crossovers.Any(c => c.HorizontalWireId == wire.WireId))
            {
                var geometry = CadCanvas(new SchematicCadAsset { SourceSha256 = "", Width = page.Width, Height = page.Height,
                    Primitives = SchematicCrossingService.RoutePrimitives(wire, crossings) }, line.Stroke, line.StrokeThickness, line.StrokeDashArray);
                Sheet.Children.Add(geometry);
            }
            else Sheet.Children.Add(line);
            if (!_wireMode)
            for (var segment = 0; segment < wire.Points.Count - 1; segment++)
            {
                var index = segment;
                var hit = Path([wire.Points[index], wire.Points[index + 1]], Brushes.Transparent, 12);
                var horizontal = wire.Points[index].Y == wire.Points[index + 1].Y;
                hit.Cursor = wire.Locked ? Cursors.Arrow : horizontal ? Cursors.SizeNS : Cursors.SizeWE;
                hit.MouseLeftButtonDown += (_, e) =>
                {
                    _selectionId = wire.WireId;
                    if (!wire.Locked) { BeginGesture(e); _dragSegment = index; }
                    UpdateSelection(_getProject()); e.Handled = true;
                };
                var menu = new ContextMenu();
                var detour = new MenuItem { Header = "增加正交繞行", IsEnabled = !wire.Locked };
                detour.Click += (_, _) =>
                {
                    var a = wire.Points[index]; var b = wire.Points[index + 1];
                    var target = horizontal ? new SchematicPoint((a.X + b.X) / 2, a.Y + 10) : new(a.X + 10, (a.Y + b.Y) / 2);
                    Apply(p => _service.AddDetour(p, wire.WireId, index, target, 2.5), "已增加正交繞行");
                };
                menu.Items.Add(detour);
                var delete = new MenuItem { Header = "刪除這條連線", IsEnabled = !wire.Locked };
                delete.Click += (_, _) =>
                {
                    var message = wire.ConnectionId is null ? "刪除此未完成導線？" : "刪除此工程連線及其所有跨頁線段？元件與跨頁符號會保留。";
                    if (MessageBox.Show(Window.GetWindow(this), message, "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                    if (Apply(p => _service.DeleteWire(p, wire.WireId), "已刪除連線；可復原")) { _selectionId = null; RefreshWorkspace(); }
                };
                menu.Items.Add(delete); hit.ContextMenu = menu; Sheet.Children.Add(hit);
            }
            if (wire.ConnectionId is null)
            foreach (var atStart in new[] { true, false })
            {
                var attachment = atStart ? wire.Start : wire.End;
                if (attachment.Kind != SchematicAttachmentKind.Free) continue;
                var point = atStart ? wire.Points[0] : wire.Points[^1];
                var endHandle = new Rectangle { Width = 8, Height = 8, Stroke = Brushes.DarkCyan, Fill = Brushes.White, Cursor = Cursors.Cross, ToolTip = "接續未完成導線" };
                Canvas.SetLeft(endHandle, point.X * 3 - 4); Canvas.SetTop(endHandle, point.Y * 3 - 4); Sheet.Children.Add(endHandle);
                endHandle.MouseLeftButtonDown += (_, e) =>
                {
                    if (wire.Locked) { Status.Text = "導線已鎖定"; e.Handled = true; return; }
                    _wireMode = true; WireTool.IsChecked = true; SelectTool.IsChecked = false;
                    _resumeWireId = wire.WireId; _resumeReversed = atStart;
                    _wireStart = atStart ? wire.End : wire.Start;
                    _wirePoints = atStart ? wire.Points.AsEnumerable().Reverse().ToList() : wire.Points.ToList();
                    Render(_getProject()); e.Handled = true;
                };
            }
            if (wire.WireId == _selectionId && !_wireMode)
                for (var i = 1; i < wire.Points.Count - 1; i++)
                {
                    var index = i; var handle = Marker(wire.Points[i], Brushes.White, Brushes.DarkCyan, 9);
                    handle.Cursor = Cursors.SizeAll;
                    var menu = new ContextMenu(); var remove = new MenuItem { Header = "刪除折點", IsEnabled = !wire.Locked };
                    remove.Click += (_, _) => Apply(p => _service.RemoveBend(p, wire.WireId, index), "已刪除折點");
                    menu.Items.Add(remove); handle.ContextMenu = menu;
                    handle.MouseLeftButtonDown += (_, e) =>
                    {
                        if (wire.Locked) { Status.Text = "導線已鎖定"; e.Handled = true; return; }
                        BeginGesture(e); _dragVertex = index; e.Handled = true;
                    };
                }
        }
        foreach (var point in crossings.Junctions) Marker(point, Brushes.Black, Brushes.Black, 5).IsHitTestVisible = false;
        foreach (var conflict in crossings.Conflicts)
        {
            var warning = Marker(conflict.Position, Brushes.Transparent, Brushes.Firebrick, 12);
            warning.ToolTip = conflict.Code == "COLLINEAR_OVERLAP" ? "不同導線共線重疊；請分開走線通道" : "未連接的接觸／交叉間距不足";
        }
        foreach (var symbol in doc.Symbols.Where(s => s.PageId == _pageId)) RenderSymbol(project, symbol);
        foreach (var pair in doc.Continuations)
        foreach (var marker in new[] { pair.Source, pair.Destination }.Where(m => m.PageId == _pageId))
        {
            var point = marker.Position; var arrow = new Polygon
            {
                Points = new PointCollection([new(point.X * 3, point.Y * 3), new(point.X * 3 - 9, point.Y * 3 - 5), new(point.X * 3 - 9, point.Y * 3 + 5)]),
                Stroke = Brushes.Black, Fill = Brushes.White, StrokeThickness = 1.3, Cursor = Cursors.Hand
            };
            arrow.MouseLeftButtonDown += (_, e) =>
            {
                if (_wireMode) WireAt(SchematicAttachment.Marker(marker.MarkerId), point, false);
                else { _selectionId = marker.MarkerId; BeginGesture(e); _dragMarker = marker.MarkerId; UpdateSelection(_getProject()); }
                e.Handled = true;
            };
            Sheet.Children.Add(arrow);
            Text(_service.ReferenceFor(project, marker.MarkerId), point.X + 2, point.Y - 5, 10, Brushes.Black);
        }
        RenderWirePreview();
        UpdateSelection(project);
    }

    private void DrawFrame(SchematicPage page, int number, string? name)
    {
        var border = new Rectangle { Width = (page.Width - 2 * page.Margin) * 3, Height = (page.Height - 2 * page.Margin) * 3,
            Stroke = Brushes.DimGray, StrokeThickness = 1, IsHitTestVisible = false };
        Canvas.SetLeft(border, page.Margin * 3); Canvas.SetTop(border, page.Margin * 3);
        if (page.TemplateGeometry is null) Sheet.Children.Add(border);
        for (var c = 0; c < page.GridColumns; c++)
            Text((c + 1).ToString(), page.Margin + (c + .5) * (page.Width - 2 * page.Margin) / page.GridColumns, 3, 10, Brushes.Gray);
        for (var r = 0; r < page.GridRows; r++)
            Text(((char)('A' + r)).ToString(), 3, page.Margin + (r + .5) * (page.Height - 2 * page.Margin) / page.GridRows, 10, Brushes.Gray);
        Text($"{name}   |   {page.Title}   |   {number}", page.Margin + 2, page.Height - page.Margin - 8, 12, Brushes.DimGray);
        Text(page.TemplatePath is null ? "A3 · 公司模板尚未套用" : System.IO.Path.GetFileName(page.TemplatePath), page.Margin + 2, page.Height - 6, 10, Brushes.Gray);
    }

    private void RenderSymbol(ElectricalProject project, SchematicSymbol symbol)
    {
        var component = project.Components.Single(c => c.ComponentInstanceId == symbol.ComponentInstanceId);
        var body = new Border { Width = symbol.Width * 3, Height = symbol.Height * 3, BorderThickness = new(symbol.SymbolId == _selectionId ? 2 : 1),
            BorderBrush = symbol.SymbolId == _selectionId ? Brushes.DarkCyan : Brushes.DimGray, Background = Brushes.White,
            Cursor = _wireMode ? Cursors.Cross : Cursors.SizeAll };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var image = new Image { Height = Math.Min(symbol.Height * 1.3, 75), Stretch = Stretch.Uniform, IsHitTestVisible = false };
        stack.Children.Add(image);
        stack.Children.Add(new TextBlock { Text = component.DisplayName ?? component.ComponentDefinitionId, FontSize = 11,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new(5), IsHitTestVisible = false });
        body.Child = stack;
        if (symbol.Geometry is not null) body.Child = CadCanvas(symbol.Geometry);
        var transforms = new TransformGroup(); transforms.Children.Add(new RotateTransform(symbol.Rotation));
        transforms.Children.Add(symbol.Rotation switch
        {
            90 => new TranslateTransform(symbol.Height * 3, 0),
            180 => new TranslateTransform(symbol.Width * 3, symbol.Height * 3),
            270 => new TranslateTransform(0, symbol.Width * 3),
            _ => new TranslateTransform()
        });
        body.RenderTransform = transforms;
        Canvas.SetLeft(body, symbol.Position.X * 3); Canvas.SetTop(body, symbol.Position.Y * 3); Sheet.Children.Add(body);
        if (symbol.Geometry is null) _ = LoadImage(component.ComponentDefinitionId, image);
        Text(component.ReferenceDesignator ?? "Reference 未設定", symbol.Position.X, symbol.Position.Y - 6, 11, Brushes.Black);
        body.MouseLeftButtonDown += (_, e) =>
        {
            if (_wireMode) return;
            _selectionId = symbol.SymbolId;
            if (symbol.Locked) { Render(project); e.Handled = true; return; }
            BeginGesture(e); _dragSymbol = symbol.SymbolId; e.Handled = true; UpdateSelection(project);
        };
        foreach (var anchor in symbol.Anchors)
        {
            var point = SchematicAuthoringService.AnchorPoint(symbol, anchor.EndpointId);
            var pin = Marker(point, anchor.Confirmed ? Brushes.White : Brushes.LightGoldenrodYellow, Brushes.Black, 7);
            pin.ToolTip = $"{anchor.Label}\n{(anchor.Confirmed ? "接點位置已確認" : "接點位置待設定")}";
            pin.Cursor = Cursors.Cross;
            pin.MouseLeftButtonDown += (_, e) =>
            {
                if (_wireMode) WireAt(SchematicAttachment.Pin(symbol.SymbolId, anchor.EndpointId), point, false);
                else { _selectionId = symbol.SymbolId; UpdateSelection(project); }
                e.Handled = true;
            };
            Text(anchor.Label ?? anchor.EndpointId, point.X + 2, point.Y - 3.8, 8.5, Brushes.DimGray);
        }
    }

    private readonly Dictionary<string, BitmapImage?> _images = new(StringComparer.Ordinal);
    private async Task LoadImage(string id, Image target)
    {
        try
        {
            if (!_images.TryGetValue(id, out var bitmap))
            {
                var uri = await _imageResolver(id); bitmap = null;
                if (uri is not null) { bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = uri; bitmap.EndInit(); bitmap.Freeze(); }
                _images[id] = bitmap;
            }
            target.Source = bitmap;
        }
        catch { target.ToolTip = "圖片無法載入"; }
    }

    private void Text(string text, double x, double y, double size, Brush colour)
    {
        var label = new TextBlock { Text = text, FontSize = size, Foreground = colour, IsHitTestVisible = false };
        Canvas.SetLeft(label, x * 3); Canvas.SetTop(label, y * 3); Sheet.Children.Add(label);
    }
    private Ellipse Marker(SchematicPoint p, Brush fill, Brush stroke, double diameter)
    {
        var marker = new Ellipse { Width = diameter, Height = diameter, Fill = fill, Stroke = stroke, StrokeThickness = 1.3 };
        Canvas.SetLeft(marker, p.X * 3 - diameter / 2); Canvas.SetTop(marker, p.Y * 3 - diameter / 2); Sheet.Children.Add(marker); return marker;
    }
    private static Polyline Path(IEnumerable<SchematicPoint> points, Brush colour, double width) => new()
    { Points = new PointCollection(points.Select(p => new Point(p.X * 3, p.Y * 3))), Stroke = colour, StrokeThickness = width, Cursor = Cursors.Hand };
    private static SchematicPoint Snap(Point point) => new(Math.Round(point.X / 3 / 2.5) * 2.5, Math.Round(point.Y / 3 / 2.5) * 2.5);
    private static void AppendOrthogonal(List<SchematicPoint> points, SchematicPoint end)
    {
        if (points.Count == 0) { points.Add(end); return; }
        var last = points[^1]; if (last == end) return;
        if (last.X != end.X && last.Y != end.Y) points.Add(new(end.X, last.Y));
        points.Add(end);
    }

    private void WireAt(SchematicAttachment attachment, SchematicPoint point, bool finish)
    {
        if (_pageId is null) return;
        if (_wireStart is null) { _wireStart = attachment; _wirePoints = [point]; }
        else
        {
            AppendOrthogonal(_wirePoints, point);
            if (attachment.Kind != SchematicAttachmentKind.Free || finish)
            {
                var start = _wireStart; var points = _wirePoints.ToArray(); var page = _pageId;
                if (!Apply(p => SaveWire(p, page, start, attachment, points), "導線已保存")) return;
                ClearPendingWire();
            }
        }
        Render(_getProject());
    }
    private ElectricalProject SaveWire(ElectricalProject project, string page, SchematicAttachment start,
        SchematicAttachment end, IReadOnlyList<SchematicPoint> points) => _resumeWireId is null
        ? _service.DrawWire(project, page, start, end, points)
        : _resumeReversed ? _service.CompleteDraft(project, _resumeWireId, end, start, points.Reverse().ToArray())
        : _service.CompleteDraft(project, _resumeWireId, start, end, points);
    private void ClearPendingWire() { _wireStart = null; _wirePoints.Clear(); _resumeWireId = null; _resumeReversed = false; }
    public bool FinishPendingDraft()
    {
        if (_wireStart is null || _wirePoints.Count < 2 || _pageId is null) return true;
        if (!Apply(p => SaveWire(p, _pageId, _wireStart, SchematicAttachment.Free(), _wirePoints.ToArray()), "未完成導線已保存")) return false;
        ClearPendingWire(); Render(_getProject()); return true;
    }
    private void RenderWirePreview()
    {
        if (_wireStart is null) return;
        var points = _wirePoints.ToList(); if (_pointer is not null) AppendOrthogonal(points, _pointer);
        var preview = Path(points, Brushes.DarkCyan, 1.5); preview.IsHitTestVisible = false; Sheet.Children.Add(preview);
    }
    private void Sheet_Down(object sender, MouseButtonEventArgs e)
    {
        Focus(); var point = Snap(e.GetPosition(Sheet));
        if (_placeMode && CatalogList.SelectedItem is CatalogItem item && _pageId is not null)
        {
            Apply(p => _service.AddCatalogComponent(p, item.Component, _pageId, point), "已放置元件；接點位置待確認");
            _placeMode = false; return;
        }
        if (_wireMode) { WireAt(SchematicAttachment.Free(), point, e.ClickCount > 1); e.Handled = true; }
        else { _selectionId = null; Render(_getProject()); }
    }
    private void BeginGesture(MouseButtonEventArgs e)
    {
        Focus(); _gestureStart = _getProject(); _gesturePreview = null; _dragStart = Snap(e.GetPosition(Sheet)); Sheet.CaptureMouse();
    }
    private void Sheet_Move(object sender, MouseEventArgs e)
    {
        _pointer = Snap(e.GetPosition(Sheet));
        if (_gestureStart is not null && _dragStart is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                if (_dragSymbol is not null)
                {
                    var s = _gestureStart.Schematic!.Symbols.Single(s => s.SymbolId == _dragSymbol);
                    _gesturePreview = _service.TransformSymbol(_gestureStart, s.SymbolId,
                        new(s.Position.X + _pointer.X - _dragStart.X, s.Position.Y + _pointer.Y - _dragStart.Y), s.Rotation);
                }
                else if (_dragVertex is int vertex && _selectionId is not null)
                {
                    var w = _gestureStart.Schematic!.Wires.Single(w => w.WireId == _selectionId);
                    var points = w.Points.Take(vertex).ToList(); AppendOrthogonal(points, _pointer);
                    foreach (var p in w.Points.Skip(vertex + 1)) AppendOrthogonal(points, p);
                    _gesturePreview = _service.ReplaceRoute(_gestureStart, w.WireId, points);
                }
                else if (_dragSegment is int segment && _selectionId is not null)
                    _gesturePreview = _service.MoveSegment(_gestureStart, _selectionId, segment, _pointer);
                else if (_dragMarker is not null)
                {
                    var marker = _gestureStart.Schematic!.Continuations.SelectMany(c => new[] { c.Source, c.Destination }).Single(m => m.MarkerId == _dragMarker);
                    _gesturePreview = _service.MoveContinuationMarker(_gestureStart, marker.MarkerId,
                        new(marker.Position.X + _pointer.X - _dragStart.X, marker.Position.Y + _pointer.Y - _dragStart.Y));
                }
                if (_gesturePreview is not null) Render(_gesturePreview);
            }
            catch (Exception error) { _gesturePreview = null; Status.Text = error.Message; }
        }
        else if (_wireMode && _wireStart is not null) Render(_getProject());
    }
    private void Sheet_Up(object sender, MouseButtonEventArgs e)
    {
        if (_gestureStart is null) return;
        if (_gesturePreview is not null && _pointer != _dragStart) _commit(_gesturePreview, "調整圖面位置／路徑");
        CancelGesture(); RefreshWorkspace();
    }
    private void CancelGesture()
    { _gestureStart = null; _gesturePreview = null; _dragStart = null; _dragSymbol = null; _dragVertex = null; _dragSegment = null; _dragMarker = null; Sheet.ReleaseMouseCapture(); }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CancelGesture(); ClearPendingWire(); _placeMode = false; Render(_getProject()); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Z or Key.Y)
        {
            if (e.OriginalSource is TextBox) return;
            CancelGesture(); ClearPendingWire(); if (e.Key == Key.Z) _undo(); else _redo(); RefreshWorkspace(); e.Handled = true;
        }
    }
    private void Select_Click(object sender, RoutedEventArgs e) { if (!FinishPendingDraft()) return; _wireMode = false; SelectTool.IsChecked = true; WireTool.IsChecked = false; ClearPendingWire(); Render(_getProject()); }
    private void Wire_Click(object sender, RoutedEventArgs e) { _wireMode = true; SelectTool.IsChecked = false; WireTool.IsChecked = true; Focus(); }
    private void FinishWire_Click(object sender, RoutedEventArgs e)
    { if (_wireStart is not null && _wirePoints.Count > 1) WireAt(SchematicAttachment.Free(), _wirePoints[^1], true); }
    private void AddPage_Click(object sender, RoutedEventArgs e)
    {
        if (!FinishPendingDraft()) return;
        Apply(p => _service.AddPage(p, $"工程圖 {(p.Schematic?.Pages.Count ?? 0) + 1}"), "已新增頁面");
        _pageId = _getProject().Schematic?.Pages.LastOrDefault()?.PageId; RefreshWorkspace();
    }
    private void Page_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || PageList.SelectedItem is not PageItem item) return;
        if (!FinishPendingDraft()) { RefreshWorkspace(); return; }
        ClearPendingWire(); CancelGesture(); _pageId = item.Id; _selectionId = null; RefreshWorkspace();
    }
    private void PageUp_Click(object sender, RoutedEventArgs e) => MovePage(-1);
    private void PageDown_Click(object sender, RoutedEventArgs e) => MovePage(1);
    private void MovePage(int delta)
    {
        var ids = _getProject().Schematic?.Pages.Select(p => p.PageId).ToList(); if (ids is null) return;
        var index = ids.IndexOf(_pageId!); var next = index + delta; if (index < 0 || next < 0 || next >= ids.Count) return;
        (ids[index], ids[next]) = (ids[next], ids[index]); Apply(p => _service.ReorderPages(p, ids), "已調整頁序與跨頁參照");
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) => FilterCatalog();
    private void FilterCatalog()
    {
        if (CatalogList is null) return; var search = Search.Text.Trim();
        CatalogList.ItemsSource = _catalog.Select(c => new CatalogItem(c, $"{c.Identity.Manufacturer} {c.Identity.Model}"))
            .Where(c => c.Label.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    private void Place_Click(object sender, RoutedEventArgs e) { if (CatalogList.SelectedItem is CatalogItem) { _placeMode = true; _wireMode = false; Status.Text = "選定元件待放置"; Focus(); } }
    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        var s = _getProject().Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == _selectionId);
        if (s is not null) Apply(p => _service.TransformSymbol(p, s.SymbolId, s.Position, (s.Rotation + 90) % 360), "已旋轉元件");
    }
    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        var doc = _getProject().Schematic; if (doc is null || _selectionId is null) return;
        var locked = doc.Symbols.SingleOrDefault(s => s.SymbolId == _selectionId)?.Locked ?? doc.Wires.SingleOrDefault(w => w.WireId == _selectionId)?.Locked;
        if (locked is not null) Apply(p => _service.SetLocked(p, _selectionId, !locked.Value), locked.Value ? "已解鎖" : "已鎖定");
    }
    private void UpdateSelection(ElectricalProject p)
    {
        var s = p.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == _selectionId);
        var component = p.Components.SingleOrDefault(c => c.ComponentInstanceId == s?.ComponentInstanceId);
        var w = p.Schematic?.Wires.SingleOrDefault(w => w.WireId == _selectionId);
        SelectionLabel.Text = component?.DisplayName ?? (w is not null ? "導線" : "");
        ReferenceText.Text = component?.ReferenceDesignator ?? "";
        SelectionState.Text = s is not null ? $"{(s.Locked ? "已鎖定" : "可編輯")}\n接點位置：{s.Anchors.Count(a => a.Confirmed)} / {s.Anchors.Count} 已確認" :
            w is not null ? $"{(w.Locked ? "已鎖定" : "可編輯")}\n{(w.ConnectionId is null ? "待接續" : "工程連線已建立")}" : "";
        if (s?.Geometry is not null)
            SelectionState.Text += "\n" + (s.AssetRevision is null ? "圖塊草稿／未核准" : s.AssetRevision) + "\n" + string.Join("\n", s.Geometry.Diagnostics);
    }
    private void Reference_Click(object sender, RoutedEventArgs e)
    {
        var s = _getProject().Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == _selectionId); var reference = ReferenceText.Text;
        if (s is not null) Apply(p => _service.SetSymbolDetails(p, s.SymbolId, reference, s.Anchors), "已更新 Reference");
    }
    private void Zoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (SheetZoom is not null) SheetZoom.ScaleX = SheetZoom.ScaleY = e.NewValue; }
    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        var pair = _getProject().Schematic?.Continuations.SingleOrDefault(c => c.Source.MarkerId == _selectionId || c.Destination.MarkerId == _selectionId);
        if (pair is null || !FinishPendingDraft()) return; var other = pair.Source.MarkerId == _selectionId ? pair.Destination : pair.Source;
        _pageId = other.PageId; _selectionId = other.MarkerId; RefreshWorkspace();
    }
    private void Continuation_Click(object sender, RoutedEventArgs e)
    {
        var doc = _getProject().Schematic;
        if (doc is null || _pageId is null || doc.Pages.Count < 2) { Status.Text = "跨頁符號需要至少兩頁"; return; }
        var dialog = new Window { Title = "跨頁符號", Width = 400, Height = 230, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new(16) }; var signal = new TextBox { Margin = new(0, 4, 0, 12) };
        var destination = new ComboBox { ItemsSource = doc.Pages.Where(p => p.PageId != _pageId).ToArray(), DisplayMemberPath = "Title", SelectedIndex = 0 };
        panel.Children.Add(new TextBlock { Text = "訊號／電位名稱" }); panel.Children.Add(signal);
        panel.Children.Add(new TextBlock { Text = "接收頁面" }); panel.Children.Add(destination);
        var ok = new Button { Content = "建立配對", Margin = new(0, 14, 0, 0), Padding = new(8) };
        ok.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(signal.Text)) dialog.DialogResult = true; };
        panel.Children.Add(ok); dialog.Content = panel;
        if (dialog.ShowDialog() != true || destination.SelectedItem is not SchematicPage target) return;
        var sourcePoint = _wirePoints.LastOrDefault() ?? _pointer ?? new(80, 40);
        var applied = Apply(p =>
        {
            var next = _service.AddContinuation(p, signal.Text, _pageId, sourcePoint, target.PageId, new(20, 40));
            if (_wireStart is not null && _wirePoints.Count > 1)
                next = SaveWire(next, _pageId, _wireStart, SchematicAttachment.Marker(next.Schematic!.Continuations.Last().Source.MarkerId), _wirePoints);
            return next;
        }, "已建立跨頁配對");
        if (applied) ClearPendingWire(); Render(_getProject());
    }

    private void RenamePage_Click(object sender, RoutedEventArgs e)
    {
        var page = _getProject().Schematic?.Pages.SingleOrDefault(p => p.PageId == _pageId);
        if (page is null) return;
        var dialog = new Window { Title = "頁面名稱", Width = 400, Height = 170, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new(16) };
        var title = new TextBox { Text = page.Title, Margin = new(0, 0, 0, 12) };
        panel.Children.Add(title);
        var ok = new Button { Content = "套用", IsDefault = true, Padding = new(8) };
        ok.Click += (_, _) =>
        {
            if (Apply(p => _service.RenamePage(p, page.PageId, title.Text), "已更新頁面名稱")) dialog.DialogResult = true;
        };
        panel.Children.Add(ok); dialog.Content = panel;
        dialog.Loaded += (_, _) => { title.Focus(); title.SelectAll(); };
        dialog.ShowDialog();
    }

    private void MoveSymbolPage_Click(object sender, RoutedEventArgs e)
    {
        var doc = _getProject().Schematic;
        var symbol = doc?.Symbols.SingleOrDefault(s => s.SymbolId == _selectionId);
        if (doc is null || symbol is null) { Status.Text = "請先選取要搬頁的元件"; return; }
        if (!FinishPendingDraft()) return;
        var dialog = new Window { Title = "搬移元件至其他頁", Width = 380, Height = 180, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new(16) };
        var choices = new ComboBox { ItemsSource = doc.Pages.Where(p => p.PageId != symbol.PageId).ToArray(), DisplayMemberPath = "Title" };
        panel.Children.Add(choices); var ok = new Button { Content = "搬移", Margin = new(0, 16, 0, 0), Padding = new(8) };
        ok.Click += (_, _) => { if (choices.SelectedItem is SchematicPage) dialog.DialogResult = true; }; panel.Children.Add(ok); dialog.Content = panel;
        if (dialog.ShowDialog() != true || choices.SelectedItem is not SchematicPage target) return;
        if (Apply(p => _service.MoveSymbolsToPage(p, [symbol.SymbolId], target.PageId), "已搬頁；接線與跨頁參照同步更新"))
        { _pageId = target.PageId; RefreshWorkspace(); }
    }
    private void Bindings_Click(object sender, RoutedEventArgs e)
    {
        var p = _getProject(); var s = p.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == _selectionId); if (s is null) return;
        var rows = s.Anchors.Select(a => new AnchorRow(a)).ToList();
        var dialog = new Window { Title = "接點位置與綁定", Width = 850, Height = 480, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new(12) };
        var ok = new Button { Content = "套用", Padding = new(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 8, 0, 0) };
        DockPanel.SetDock(ok, Dock.Bottom); panel.Children.Add(ok);
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false };
        grid.Columns.Add(new DataGridTextColumn { Header = "Port / Pin", Binding = new Binding("Label"), IsReadOnly = true, Width = new(1, DataGridLengthUnitType.Star) });
        if (s.Geometry?.ConnectionPoints.Count > 0)
            grid.Columns.Add(new DataGridComboBoxColumn { Header = "明確選取 CAD 接點", ItemsSource = s.Geometry.ConnectionPoints,
                DisplayMemberPath = "Tag", SelectedItemBinding = new Binding("CadContact") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "X (mm)", Binding = new Binding("X"), Width = 90 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Y (mm)", Binding = new Binding("Y"), Width = 90 });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "位置已確認", Binding = new Binding("Confirmed"), Width = 100 });
        panel.Children.Add(grid); ok.Click += (_, _) => { if (grid.CommitEdit(DataGridEditingUnit.Cell, true) && grid.CommitEdit(DataGridEditingUnit.Row, true)) dialog.DialogResult = true; };
        dialog.Content = panel;
        if (dialog.ShowDialog() == true)
            Apply(current => _service.SetSymbolDetails(current, s.SymbolId, p.Components.Single(c => c.ComponentInstanceId == s.ComponentInstanceId).ReferenceDesignator,
                rows.Select(r => r.Anchor with { Position = new(r.X, r.Y), Confirmed = r.Confirmed }).ToArray()), "已更新接點位置");
    }
    private sealed record PageItem(string Id, string Label);
    private sealed record CatalogItem(ComponentIR Component, string Label);
    private sealed class AnchorRow(SchematicAnchor anchor) : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void Changed(string property) => PropertyChanged?.Invoke(this, new(property));
        public SchematicAnchor Anchor { get; } = anchor;
        public string Label => Anchor.Label ?? Anchor.EndpointId;
        private double _x = anchor.Position.X;
        private double _y = anchor.Position.Y;
        private bool _confirmed = anchor.Confirmed;
        public double X { get => _x; set { if (_x == value) return; _x = value; Confirmed = false; Changed(nameof(X)); } }
        public double Y { get => _y; set { if (_y == value) return; _y = value; Confirmed = false; Changed(nameof(Y)); } }
        public bool Confirmed { get => _confirmed; set { if (_confirmed == value) return; _confirmed = value; Changed(nameof(Confirmed)); } }
        private SchematicCadContact? _cadContact;
        public SchematicCadContact? CadContact
        {
            get => _cadContact;
            set { _cadContact = value; if (value is not null) { X = value.Position.X; Y = value.Position.Y; Confirmed = false; } Changed(nameof(CadContact)); }
        }
    }
}
