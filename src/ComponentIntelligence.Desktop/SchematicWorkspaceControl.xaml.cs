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
    private readonly string? _archiveRoot;
    private IReadOnlyList<ComponentIR> _catalog = [];
    private string? _pageId, _selectionId;
    private bool _refreshing, _wireMode, _placeMode, _editModuleMode;
    private SchematicAttachment? _wireStart;
    private List<SchematicPoint> _wirePoints = [];
    private SchematicPoint? _pointer;
    private ElectricalProject? _gestureStart, _gesturePreview;
    private SchematicPoint? _dragStart;
    private Point _dragViewportStart;
    private bool _dragActivated;
    private string? _dragSymbol;
    private string? _dragPortSymbol, _dragPortId;
    private string? _dragPinSymbol, _dragPinId, _dragResizeSymbol;
    private bool _dragPortHasPins;
    private int? _dragVertex;
    private int? _dragSegment;
    private string? _dragMarker;
    private string? _resumeWireId;
    private bool _resumeReversed;
    private readonly Dictionary<string, Canvas> _pageCanvases = new(StringComparer.Ordinal);
    private readonly Canvas _emptyCanvas = new();
    private Canvas? _renderCanvas;
    private Canvas Sheet => _renderCanvas ?? (_pageId is not null && _pageCanvases.TryGetValue(_pageId, out var canvas) ? canvas : _emptyCanvas);
    private bool _allPages;
    private bool _pairMode;
    private (string WireId, bool Start)? _pairFirst;
    private ElectricalProject? _placementPreview;

    public SchematicWorkspaceControl(Func<ElectricalProject> getProject, Action<ElectricalProject, string> commit,
        Func<Task<IReadOnlyList<ComponentIR>>> catalogProvider, Func<string, Task<Uri?>> imageResolver,
        Action undo, Action redo, string? archiveRoot = null)
    {
        _getProject = getProject; _commit = commit; _catalogProvider = catalogProvider; _imageResolver = imageResolver;
        _undo = undo; _redo = redo;
        _archiveRoot = archiveRoot;
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
        FilterCatalog();
        Render(project);
    }

    private bool Apply(Func<ElectricalProject, ElectricalProject> command, string label)
    {
        try { var next = command(_getProject()); _commit(next, label); RefreshWorkspace(); Status.Text = label; return true; }
        catch (Exception e) { Status.Text = "未套用：" + e.Message; return false; }
    }

    private void Render(ElectricalProject project)
    {
        var pages = project.Schematic?.Pages ?? [];
        foreach (var id in _pageCanvases.Keys.Where(id => pages.All(p => p.PageId != id)).ToArray())
        { PageHost.Children.Remove(_pageCanvases[id]); _pageCanvases.Remove(id); }
        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            if (!_pageCanvases.TryGetValue(page.PageId, out var canvas))
            {
                canvas = new Canvas { Background = Brushes.White, Margin = new(12), Tag = page.PageId, Focusable = true };
                var pageId = page.PageId;
                canvas.PreviewMouseLeftButtonDown += (s, _) => { ActivatePage(pageId); Keyboard.Focus((Canvas)s); };
                canvas.PreviewMouseRightButtonDown += (s, _) => { ActivatePage(pageId); Keyboard.Focus((Canvas)s); };
                canvas.MouseLeftButtonDown += Sheet_Down; canvas.MouseMove += Sheet_Move; canvas.MouseLeftButtonUp += Sheet_Up;
                _pageCanvases.Add(pageId, canvas);
            }
            if (PageHost.Children.IndexOf(canvas) != index)
            { PageHost.Children.Remove(canvas); PageHost.Children.Insert(index, canvas); }
            canvas.Visibility = _allPages || page.PageId == _pageId ? Visibility.Visible : Visibility.Collapsed;
            _renderCanvas = canvas;
            try { RenderPage(project, page); } finally { _renderCanvas = null; }
        }
        UpdateSelection(project);
    }

    private void ActivatePage(string pageId)
    {
        if (_pageId == pageId) return;
        if (!FinishPendingDraft()) return;
        CancelGesture(); _pageId = pageId; _selectionId = null;
        _refreshing = true;
        try { PageList.SelectedItem = PageList.Items.Cast<PageItem>().FirstOrDefault(p => p.Id == pageId); }
        finally { _refreshing = false; }
    }

    private void AllPages_Click(object sender, RoutedEventArgs e)
    {
        if (!FinishPendingDraft()) return;
        CancelGesture(); _allPages = AllPagesTool.IsChecked == true; Render(_getProject()); Sheet.BringIntoView();
    }

    private void RenderPage(ElectricalProject project, SchematicPage page)
    {
        Sheet.Children.Clear();
        var doc = project.Schematic!;
        Sheet.Width = page.Width * PixelsPerMm; Sheet.Height = page.Height * PixelsPerMm;
        if (page.TemplateGeometry is not null)
        {
            var geometry = CadCanvas(page.TemplateGeometry); geometry.IsHitTestVisible = false; Sheet.Children.Add(geometry);
            RenderTitleBlock(project, page);
        }
        DrawFrame(page, doc.Pages.IndexOf(page) + 1, project.Name);
        foreach (var binding in page.InlineCableDetails.Concat(page.CableDetail is null ? [] : new[] { page.CableDetail }))
        {
            SchematicCableDetailPresentation? detailView = null;
            try
            {
                var detailService = new SchematicCableDetailService();
                var detail = binding == page.CableDetail ? detailService.Build(project, page) : detailService.BuildArchived(project, page, binding);
                detailView = detail;
                var geometry = CadCanvas(new SchematicCadAsset { SourceSha256 = "", Width = page.Width, Height = page.Height, Primitives = detail.Primitives });
                geometry.IsHitTestVisible = false; Sheet.Children.Add(geometry);
            }
            catch (InvalidOperationException error)
            {
                if (_renderingOutput) throw;
                Text(error.Message, page.Margin + 8, page.Margin + 10, 12, Brushes.Firebrick);
            }
            if (!_renderingOutput && project.Cables.Single(c => c.CableInstanceId == binding.CableInstanceId).ArchivedCable is not null)
                RenderDetailHandles(page, binding, detailView);
        }
        var crossings = SchematicCrossingService.Analyze(doc.Wires.Where(w => w.PageId == page.PageId).ToArray());
        foreach (var wire in doc.Wires.Where(w => w.PageId == page.PageId))
        {
            var specification = SchematicWirePresentation.Resolve(project, wire);
            var evidence = SchematicWireEvidence.Resolve(project, wire);
            var zoom = _renderingOutput ? 1 : Zoom.Value;
            var weight = Math.Max(1.5 / zoom, SchematicWirePresentation.StrokeWidthMm(specification.Awg) * PixelsPerMm);
            var color = (Brush)new BrushConverter().ConvertFromString(evidence.ColorHex)!;
            var line = Path(wire.Points, wire.WireId == _selectionId ? Brushes.DarkCyan : color, wire.WireId == _selectionId ? weight + 1 : weight);
            line.ToolTip = (_wireMode ? "點選既有導線，建立明確分支" : wire.ConnectionId is null ? "待接續導線" : "已建立工程連線") + "\n" + evidence.Description;
            line.ToolTip += "\n" + wire.Designation + (wire.SizingProposal is { } proposal ? "\n線徑待核對：" + proposal.Review : "");
            line.MouseLeftButtonDown += (_, e) =>
            {
                if (_wireMode) return;
                _selectionId = wire.WireId; Render(_getProject()); e.Handled = true; Focus();
            };
            if (evidence.Category == "DC_NEGATIVE" && wire.WireId != _selectionId)
            {
                var outline = CadCanvas(new SchematicCadAsset { SourceSha256 = "", Width = page.Width, Height = page.Height,
                    Primitives = SchematicCrossingService.RoutePrimitives(wire, crossings) }, Brushes.DimGray, weight + 2 / zoom, line.StrokeDashArray);
                outline.IsHitTestVisible = false; Sheet.Children.Add(outline);
            }
            if (crossings.Crossovers.Any(c => c.HorizontalWireId == wire.WireId))
            {
                var geometry = CadCanvas(new SchematicCadAsset { SourceSha256 = "", Width = page.Width, Height = page.Height,
                    Primitives = SchematicCrossingService.RoutePrimitives(wire, crossings) }, line.Stroke, line.StrokeThickness, line.StrokeDashArray);
                Sheet.Children.Add(geometry);
            }
            else Sheet.Children.Add(line);
            if (_wireMode && !_renderingOutput)
            for (var segment = 0; segment < wire.Points.Count - 1; segment++)
            {
                var a = wire.Points[segment]; var b = wire.Points[segment + 1];
                var hit = Path([a, b], Brushes.Transparent, 12);
                hit.Cursor = Cursors.Cross; hit.ToolTip = "點選導線，建立明確分支";
                hit.MouseLeftButtonDown += (_, e) =>
                {
                    var pointer = e.GetPosition(Sheet);
                    var raw = new SchematicPoint(pointer.X / 3, pointer.Y / 3);
                    var junction = a.Y == b.Y
                        ? new SchematicPoint(Math.Clamp(raw.X, Math.Min(a.X, b.X), Math.Max(a.X, b.X)), a.Y)
                        : new SchematicPoint(a.X, Math.Clamp(raw.Y, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)));
                    WireAt(SchematicAttachment.Junction(wire.WireId), junction, false);
                    e.Handled = true;
                };
                Sheet.Children.Add(hit);
            }
            if (!_wireMode && !_renderingOutput)
            for (var segment = 0; segment < wire.Points.Count - 1; segment++)
            {
                var index = segment;
                var hit = Path([wire.Points[index], wire.Points[index + 1]], Brushes.Transparent, 12);
                var horizontal = wire.Points[index].Y == wire.Points[index + 1].Y;
                hit.Cursor = wire.Locked ? Cursors.Arrow : horizontal ? Cursors.SizeNS : Cursors.SizeWE;
                hit.MouseLeftButtonDown += (_, e) =>
                {
                    if (_pairMode) { PairWire(wire, Snap(e.GetPosition(Sheet))); e.Handled = true; return; }
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
            if (wire.ConnectionId is null && !_renderingOutput)
            foreach (var atStart in new[] { true, false })
            {
                var attachment = atStart ? wire.Start : wire.End;
                if (attachment.Kind != SchematicAttachmentKind.Free) continue;
                var point = atStart ? wire.Points[0] : wire.Points[^1];
                var endHandle = new Rectangle { Width = 8, Height = 8, Stroke = Brushes.DarkCyan, Fill = Brushes.White, Cursor = Cursors.Cross, ToolTip = "接續未完成導線" };
                Canvas.SetLeft(endHandle, point.X * 3 - 4); Canvas.SetTop(endHandle, point.Y * 3 - 4); Sheet.Children.Add(endHandle);
                endHandle.MouseLeftButtonDown += (_, e) =>
                {
                    if (_pairMode) { PairWire(wire, point); e.Handled = true; return; }
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
        foreach (var wire in doc.Wires.Where(w => w.PageId == page.PageId))
            if (SchematicCableLabelPresentation.Resolve(project, wire, crossings) is { } cableLabel) RenderCableLabel(cableLabel, page);
        foreach (var point in crossings.Junctions) Marker(point, Brushes.Black, Brushes.Black, 5).IsHitTestVisible = false;
        foreach (var conflict in crossings.Conflicts)
        {
            var warning = Marker(conflict.Position, Brushes.Transparent, Brushes.Firebrick, 12);
            warning.ToolTip = conflict.Code == "COLLINEAR_OVERLAP" ? "不同導線共線重疊；請分開走線通道" : "未連接的接觸／交叉間距不足";
        }
        foreach (var symbol in doc.Symbols.Where(s => s.PageId == page.PageId)) RenderSymbol(project, symbol);
        foreach (var pair in doc.Continuations)
        foreach (var marker in new[] { pair.Source, pair.Destination }.Where(m => m.PageId == page.PageId))
        {
            var point = marker.Position; var arrow = new Polygon
            {
                Points = new PointCollection(SchematicContinuationArrow.Points(doc, marker)
                    .Select(p => new Point(p.X * 3, p.Y * 3))),
                Stroke = Brushes.Black, Fill = Brushes.White, StrokeThickness = 1.3, Cursor = Cursors.Hand
            };
            arrow.MouseLeftButtonDown += (_, e) =>
            {
                if (_wireMode) WireAt(SchematicAttachment.Marker(marker.MarkerId), point, false);
                else if (e.ClickCount >= 2) NavigateToMarker(marker.MarkerId);
                else { _selectionId = marker.MarkerId; BeginGesture(e); _dragMarker = marker.MarkerId; UpdateSelection(_getProject()); Focus(); }
                e.Handled = true;
            };
            if (!_renderingOutput)
            {
                var menu = new ContextMenu();
                var remove = new MenuItem { Header = "刪除這對跨頁符號" };
                remove.Click += (_, _) => DeleteContinuationMarker(marker.MarkerId);
                menu.Items.Add(remove); arrow.ContextMenu = menu;
            }
            Sheet.Children.Add(arrow);
            var caption = new TextBlock { Text = _service.ReferenceCodeFor(doc, marker.MarkerId), FontSize = 10,
                Foreground = Brushes.Black, Cursor = Cursors.Hand,
                ToolTip = _service.ReferenceFor(project, marker.MarkerId) + "\n雙擊前往對端" };
            Canvas.SetLeft(caption, (point.X + 2) * 3); Canvas.SetTop(caption, (point.Y - 5) * 3);
            if (!_renderingOutput) caption.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount >= 2) NavigateToMarker(marker.MarkerId);
                else { _selectionId = marker.MarkerId; UpdateSelection(_getProject()); Focus(); }
                e.Handled = true;
            };
            if (!_renderingOutput) caption.ContextMenu = arrow.ContextMenu;
            Sheet.Children.Add(caption);
        }
        if (page.PageId == _pageId && !_renderingOutput)
        {
            RenderWirePreview();
            if (_placeMode && _pointer is not null && _placementPreview?.Schematic?.Symbols.LastOrDefault() is SchematicSymbol ghost)
            {
                var first = Sheet.Children.Count;
                RenderSymbol(_placementPreview, ghost with { Position = _pointer });
                foreach (UIElement element in Sheet.Children.Cast<UIElement>().Skip(first))
                { element.IsHitTestVisible = false; element.Opacity = .55; }
            }
        }
        Text($"草稿／未完成  |  {doc.Pages.IndexOf(page) + 1} / {doc.Pages.Count}",
            page.Margin, page.Height - 5, 9, Brushes.Black);
        if (!_renderingOutput) UpdateSelection(project);
    }

    private void DrawFrame(SchematicPage page, int number, string? name)
    {
        // Imported frames own their visible labels; grid metadata remains available for references.
        if (page.TemplateGeometry is not null) return;
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
        var owner = SchematicSymbolOwner.Resolve(project, symbol);
        var portEditing = _editModuleMode && !_renderingOutput;
        var compact = SchematicPortPresentation.GenericBodyBounds(project.Schematic!, symbol, owner);
        var compacted = symbol.Geometry is null && (symbol.ManualSize || owner.Ports.Any(port =>
            SchematicPortPresentation.IsRepresented(symbol, port) && SchematicPortPresentation.IsPortCollapsed(symbol, port)));
        var body = new Border { Width = (compacted ? compact.Width : symbol.Width) * 3,
            Height = (compacted ? compact.Height : symbol.Height) * 3, BorderThickness = new(0),
            BorderBrush = symbol.SymbolId == _selectionId ? Brushes.DarkCyan : Brushes.DimGray, Background = Brushes.White,
            Cursor = _wireMode ? Cursors.Cross : portEditing ? Cursors.Arrow : Cursors.SizeAll };
        var content = new Canvas();
        var image = new Image { Stretch = Stretch.Uniform, IsHitTestVisible = false };
        content.Children.Add(image);
        body.Child = content;
        var cad = SchematicSymbolPresentation.GeometryForOwner(symbol, owner);
        if (cad is not null) body.Child = CadCanvas(SchematicSymbolPresentation.BodyGeometry(cad));
        // Selection strokes overlay geometry; their width must not shift the CAD body away from its anchors.
        var drawing = body.Child; body.Child = null;
        var layers = new Grid(); layers.Children.Add(drawing);
        layers.Children.Add(new Border { BorderBrush = body.BorderBrush,
            BorderThickness = new(symbol.SymbolId == _selectionId ? 2 : symbol.Geometry is null ? 1 : 0), IsHitTestVisible = false });
        body.Child = layers;
        if (!compacted)
        {
            var transforms = new TransformGroup(); transforms.Children.Add(new RotateTransform(symbol.Rotation));
            transforms.Children.Add(symbol.Rotation switch
            {
                90 => new TranslateTransform(symbol.Height * 3, 0),
                180 => new TranslateTransform(symbol.Width * 3, symbol.Height * 3),
                270 => new TranslateTransform(0, symbol.Width * 3),
                _ => new TranslateTransform()
            });
            body.RenderTransform = transforms;
        }
        else { image.RenderTransform = new RotateTransform(symbol.Rotation); image.RenderTransformOrigin = new(.5, .5); }
        Canvas.SetLeft(body, compact.X * 3); Canvas.SetTop(body, compact.Y * 3); Sheet.Children.Add(body);
        if (symbol.Geometry is null) _ = LoadImage(owner.DefinitionId, image,
            compacted ? compact.Width : symbol.Width, compacted ? compact.Height : symbol.Height,
            compacted ? symbol.Rotation : 0);
        else
        {
            var upright = SchematicSymbolPresentation.UprightCadText(symbol, cad!);
            var labels = CadCanvas(upright with { Width = Sheet.Width / 3, Height = Sheet.Height / 3 });
            Sheet.Children.Add(labels);
        }
        if (symbol.Geometry is null)
        {
            var label = new TextBlock { Text = SchematicSymbolPresentation.DisplayTitle(symbol, owner), FontSize = 9, Width = (compact.Width - 4) * 3,
                Height = 9 * 3, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brushes.Black, IsHitTestVisible = false };
            Canvas.SetLeft(label, (compact.X + 2) * 3);
            Canvas.SetTop(label, (compact.Y + compact.Height - 10) * 3);
            Sheet.Children.Add(label);
        }
        var referenceY = symbol.Position.Y - 6;
        body.MouseLeftButtonDown += (_, e) =>
        {
            if (_wireMode) return;
            _selectionId = symbol.SymbolId;
            if (portEditing) { Render(project); e.Handled = true; return; }
            if (symbol.Locked) { Render(project); e.Handled = true; return; }
            BeginGesture(e); _dragSymbol = symbol.SymbolId; e.Handled = true; UpdateSelection(project);
        };
        var placedLabels = new List<(double Left, double Top, double Right, double Bottom)>();
        foreach (var anchor in symbol.Anchors)
        {
            if (SchematicPortPresentation.IsCollapsedPin(project.Schematic!, symbol, owner, anchor)) continue;
            var point = SchematicAuthoringService.AnchorPoint(symbol, anchor.EndpointId);
            var pin = Marker(point, anchor.Confirmed ? Brushes.White : Brushes.LightGoldenrodYellow, Brushes.Black, 7);
            pin.ToolTip = $"{anchor.Label}\n{(anchor.Confirmed ? "接點位置已確認" : "接點位置待設定")}";
            pin.Cursor = portEditing && symbol.Geometry is null ? Cursors.SizeAll : Cursors.Cross;
            pin.MouseLeftButtonDown += (_, e) =>
            {
                if (_wireMode) WireAt(SchematicAttachment.Pin(symbol.SymbolId, anchor.EndpointId), point, false);
                else if (portEditing && symbol.Geometry is null && !symbol.Locked)
                {
                    _selectionId = symbol.SymbolId; BeginGesture(e);
                    _dragPinSymbol = symbol.SymbolId; _dragPinId = anchor.EndpointId;
                }
                else { _selectionId = symbol.SymbolId; UpdateSelection(project); }
                e.Handled = true;
            };
            if (SchematicSymbolPresentation.ShowAnchorLabel(symbol, anchor))
            {
                var pose = SchematicSymbolPresentation.AnchorLabel(symbol, anchor);
                var label = new TextBlock { Text = anchor.Label ?? anchor.EndpointId, FontSize = 8.5,
                    Foreground = Brushes.DimGray, IsHitTestVisible = false };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var box = pose.Bounds(label.DesiredSize.Width / 3, label.DesiredSize.Height / 3);
                var step = label.DesiredSize.Height / 3 + 1;
                for (var n = 0; n < 12 && placedLabels.Any(r => box.Left < r.Right && box.Right > r.Left && box.Top < r.Bottom && box.Bottom > r.Top); n++)
                    box = pose.Side switch
                    {
                        SchematicLabelSide.Left => (box.Left - step, box.Top, box.Right - step, box.Bottom),
                        SchematicLabelSide.Right => (box.Left + step, box.Top, box.Right + step, box.Bottom),
                        SchematicLabelSide.Top => (box.Left, box.Top - step, box.Right, box.Bottom - step),
                        _ => (box.Left, box.Top + step, box.Right, box.Bottom + step)
                    };
                placedLabels.Add(box);
                referenceY = Math.Min(referenceY, box.Top - 6);
                if (pose.TextRotation != 0) label.LayoutTransform = new RotateTransform(pose.TextRotation);
                Canvas.SetLeft(label, box.Left * 3); Canvas.SetTop(label, box.Top * 3);
                Sheet.Children.Add(label);
            }
        }
        foreach (var port in owner.Ports.Where(port => SchematicPortPresentation.IsRepresented(symbol, port)))
        {
            var collapsed = SchematicPortPresentation.IsPortCollapsed(symbol, port);
            var portRoute = SchematicPortPresentation.HasPortRoute(project.Schematic!, symbol, port.PortId);
            if ((_renderingOutput || _wireMode) && !portEditing && !collapsed && !portRoute) continue;
            var points = symbol.Anchors.Where(a => port.Pins.Any(p => p.PinId == a.EndpointId))
                .Select(a => SchematicAuthoringService.AnchorPoint(symbol, a.EndpointId)).ToArray();
            var center = points.Length == 0 ? symbol.Position :
                new SchematicPoint(points.Average(p => p.X), points.Average(p => p.Y));
            if (collapsed || portRoute)
            {
                var contact = SchematicPortPresentation.GroupContact(project.Schematic!, symbol, owner, port, compact);
                center = contact.Position;
                var point = Marker(center, Brushes.White, Brushes.Black, 9);
                point.ToolTip = points.Length == 0 ? portEditing
                    ? $"{port.Name}: 拖曳移動 Port；尚無 Pin 明細，不能展開"
                    : $"{port.Name}: 可接線到 Port；尚無 Pin 明細，不能展開"
                    : portEditing ? $"{port.Name}: 點一下展開／收合 Pin；拖曳移動 Port"
                    : collapsed ? $"{port.Name}: 可接線到整個 Port；雙擊展開可改選確切 Pin。Port 接線不代表內部 Pin 互通。"
                    : $"{port.Name}: 既有 Port 接線；新增接線請選確切 Pin。";
                point.Cursor = portEditing ? Cursors.SizeAll : collapsed && _wireMode ? Cursors.Cross : Cursors.Hand;
                point.MouseLeftButtonDown += (_, e) =>
                {
                    _selectionId = symbol.SymbolId;
                    if (_wireMode && !_renderingOutput)
                    {
                        if (collapsed) WireAt(SchematicAttachment.Port(symbol.SymbolId, port.PortId), center, false);
                        else Status.Text = "此 Port 已展開；新增接線請選確切 Pin。";
                    }
                    else if (points.Length == 0 && !portEditing)
                        Status.Text = "此 Port 尚無 Pin 明細；可維持 Port 接線，不能展開成 Pin。";
                    else if (!portEditing && e.ClickCount >= 2)
                        Apply(p => _service.TogglePortCollapsed(p, symbol.SymbolId, port.PortId), "已展開 Port Pin");
                    else if (portEditing && !symbol.Locked)
                    {
                        BeginGesture(e); _dragPortSymbol = symbol.SymbolId; _dragPortId = port.PortId;
                        _dragPortHasPins = points.Length > 0;
                    }
                    else UpdateSelection(project);
                    e.Handled = true;
                };
                var label = SchematicPortPresentation.GroupLabel(center, contact.Side);
                Text(port.Name, label.Position.X, label.Position.Y, 9, Brushes.DimGray, label.Rotation);
                continue;
            }
            var handle = new Border { Width = 17, Height = 17, Background = Brushes.White,
                BorderBrush = portEditing ? Brushes.DarkCyan : Brushes.DimGray, BorderThickness = new(1),
                Cursor = portEditing ? Cursors.SizeAll : Cursors.Hand,
                Child = new TextBlock { Text = "▾", FontSize = 10, TextAlignment = TextAlignment.Center },
                ToolTip = portEditing ? $"{port.Name}：點一下收合 Pin；拖曳到模塊四邊" : $"{port.Name}：雙擊收合 Pin" };
            var handleSide = SchematicPortPresentation.PortSide(symbol, port);
            var handlePosition = handleSide switch
            {
                "Top" => new SchematicPoint(center.X, compact.Y + 5),
                "Bottom" => new SchematicPoint(center.X, compact.Y + compact.Height - 5),
                "Left" => new SchematicPoint(compact.X + 5, center.Y),
                _ => new SchematicPoint(compact.X + compact.Width - 5, center.Y)
            };
            Canvas.SetLeft(handle, handlePosition.X * 3 - 8); Canvas.SetTop(handle, handlePosition.Y * 3 - 8);
            handle.MouseLeftButtonDown += (_, e) =>
            {
                _selectionId = symbol.SymbolId;
                if (!portEditing && e.ClickCount >= 2)
                {
                    CancelGesture(); Apply(p => _service.TogglePortCollapsed(p, symbol.SymbolId, port.PortId), "已切換 Port 接點顯示");
                }
                else if (portEditing && !symbol.Locked)
                {
                    BeginGesture(e); _dragPortSymbol = symbol.SymbolId; _dragPortId = port.PortId;
                    _dragPortHasPins = points.Length > 0;
                    UpdateSelection(project);
                }
                else { Render(project); if (symbol.Locked) Status.Text = "模塊已鎖定，請先解鎖。"; }
                e.Handled = true;
            };
            Sheet.Children.Add(handle);
        }
        if (SchematicSymbolPresentation.ShowReferenceForOwner(symbol, owner))
            Text(owner.Reference ?? "Reference 未設定", symbol.Position.X, referenceY, 11, Brushes.Black);
        var caption = SchematicSymbolPresentation.CableCaption(symbol, owner);
        if (caption.Length > 0)
        {
            var label = new TextBlock { Text = caption, FontSize = 10, TextWrapping = TextWrapping.Wrap,
                Width = (symbol.Rotation % 180 == 0 ? symbol.Width : symbol.Height) * 3, IsHitTestVisible = false };
            Canvas.SetLeft(label, symbol.Position.X * 3);
            Canvas.SetTop(label, (symbol.Position.Y + (symbol.Rotation % 180 == 0 ? symbol.Height : symbol.Width) + 2) * 3);
            Sheet.Children.Add(label);
        }
        if (portEditing && symbol.Geometry is null && !symbol.Locked)
        {
            var resize = new Border { Width = 14, Height = 14, Background = Brushes.White,
                BorderBrush = Brushes.DarkCyan, BorderThickness = new(1), Cursor = Cursors.SizeNWSE,
                Child = new TextBlock { Text = "↘", FontSize = 10, TextAlignment = TextAlignment.Center },
                ToolTip = "拖曳調整模塊大小；接點和導線會跟隨" };
            Canvas.SetLeft(resize, (compact.X + compact.Width) * 3 - 7);
            Canvas.SetTop(resize, (compact.Y + compact.Height) * 3 - 7);
            resize.MouseLeftButtonDown += (_, e) =>
            {
                _selectionId = symbol.SymbolId; BeginGesture(e); _dragResizeSymbol = symbol.SymbolId;
                e.Handled = true;
            };
            Sheet.Children.Add(resize);
        }
    }

    private readonly Dictionary<string, BitmapSource?> _images = new(StringComparer.Ordinal);
    private async Task<BitmapSource?> GetImage(string id)
    {
        if (_images.TryGetValue(id, out var cached)) return cached;
        var uri = await _imageResolver(id); BitmapSource? bitmap = null;
        if (uri is not null)
        {
            var source = new BitmapImage();
            source.BeginInit(); source.CacheOption = BitmapCacheOption.OnLoad; source.UriSource = uri; source.EndInit(); source.Freeze();
            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var stride = checked(bgra.PixelWidth * 4);
            var pixels = new byte[checked(stride * bgra.PixelHeight)];
            bgra.CopyPixels(pixels, stride, 0);
            var bounds = SchematicRasterCrop.FindContentBounds(pixels, bgra.PixelWidth, bgra.PixelHeight, stride);
            bitmap = bounds.Width == bgra.PixelWidth && bounds.Height == bgra.PixelHeight
                ? source : new CroppedBitmap(source, new Int32Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            bitmap.Freeze();
        }
        if (bitmap is not null) _images[id] = bitmap;
        return bitmap;
    }

    private async Task LoadImage(string id, Image target, double width, double height, int rotation)
    {
        try
        {
            var bitmap = await GetImage(id);
            if (bitmap is not null)
            {
                var layout = SchematicCatalogSymbolLayout.Create(width, height, bitmap.PixelWidth, bitmap.PixelHeight, rotation);
                target.Width = layout.ImageWidth * 3; target.Height = layout.ImageHeight * 3;
                Canvas.SetLeft(target, layout.ImageTopLeft.X * 3); Canvas.SetTop(target, layout.ImageTopLeft.Y * 3);
            }
            target.Source = bitmap;
        }
        catch { target.ToolTip = "圖片無法載入"; }
    }

    private void RenderCableLabel(SchematicCableLabel caption, SchematicPage page)
    {
        var label = new TextBlock { Text = caption.Text, FontSize = 10.5, Foreground = Brushes.Black,
            Background = Brushes.White, Padding = new Thickness(2, 0, 2, 0), IsHitTestVisible = false };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var available = caption.AvailableLength * PixelsPerMm;
        if (label.DesiredSize.Width > available)
        {
            label.FontSize = Math.Max(6, (available - 4) / (label.DesiredSize.Width - 4) * label.FontSize);
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }
        var vertical = caption.Rotation != 0;
        var margin = page.Margin * PixelsPerMm;
        var pageWidth = page.Width * PixelsPerMm;
        var pageHeight = page.Height * PixelsPerMm;
        label.MaxWidth = (vertical ? pageHeight : pageWidth) - 2 * margin;
        label.TextWrapping = TextWrapping.Wrap;
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var across = (vertical ? pageWidth : pageHeight) - 2 * margin;
        if (label.DesiredSize.Height > across)
        {
            label.FontSize *= across / label.DesiredSize.Height;
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }
        var offset = label.DesiredSize.Width > available ? label.DesiredSize.Height / 2 + 4 : 0;
        var halfWidth = (vertical ? label.DesiredSize.Height : label.DesiredSize.Width) / 2;
        var halfHeight = (vertical ? label.DesiredSize.Width : label.DesiredSize.Height) / 2;
        var centerX = Math.Clamp(caption.Center.X * PixelsPerMm + (vertical ? offset : 0), margin + halfWidth, pageWidth - margin - halfWidth);
        var centerY = Math.Clamp(caption.Center.Y * PixelsPerMm - (vertical ? 0 : offset), margin + halfHeight, pageHeight - margin - halfHeight);
        label.RenderTransformOrigin = new Point(.5, .5);
        label.RenderTransform = new RotateTransform(caption.Rotation);
        Canvas.SetLeft(label, centerX - label.DesiredSize.Width / 2);
        Canvas.SetTop(label, centerY - label.DesiredSize.Height / 2);
        Sheet.Children.Add(label);
    }

    private void Text(string text, double x, double y, double size, Brush colour, int rotation = 0)
    {
        var label = new TextBlock { Text = text, FontSize = size, Foreground = colour, IsHitTestVisible = false };
        if (rotation != 0) label.RenderTransform = new RotateTransform(rotation);
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
            _wirePoints = SchematicDraftRoute.Append(_wirePoints, point);
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
        var points = _pointer is null ? _wirePoints.ToList() : SchematicDraftRoute.Append(_wirePoints, _pointer);
        var preview = Path(points, Brushes.DarkCyan, 1.5); preview.IsHitTestVisible = false; Sheet.Children.Add(preview);
    }
    private void Sheet_Down(object sender, MouseButtonEventArgs e)
    {
        FocusCanvas(); var point = Snap(e.GetPosition(Sheet));
        if (_placeMode && _pendingCableRepresentation is not null)
        { PlaceAnotherCableRepresentation(point); e.Handled = true; return; }
        if (_placeMode && _pendingCable is not null && _pageId is not null)
        {
            _ = PlacePendingCableAsync(point); e.Handled = true; return;
        }
        if (_placeMode && _pendingRepresentation is not null && _pageId is not null)
        {
            PlacePendingRepresentation(point); e.Handled = true; return;
        }
        if (_placeMode && _pendingBom is SchematicBomPaletteEntry item && _pageId is not null)
        {
            string? warning = null;
            var savedLayoutApplied = false;
            var placed = Apply(p =>
            {
                var result = PlaceBomWithSavedLayout(p, item, _pageId, point);
                warning = result.Warning;
                savedLayoutApplied = result.Applied;
                return result.Project;
            }, "已放置元件");
            if (placed && warning is not null) Status.Text = warning;
            else if (placed && savedLayoutApplied) Status.Text = "已放置元件並套用已保存模塊版型。";
            _placeMode = false; _placementPreview = null; _pendingBom = null; Render(_getProject()); return;
        }
        if (_wireMode)
        {
            var pointer = e.GetPosition(Sheet);
            SchematicAnchorHit? hit;
            try
            {
                hit = SchematicAnchorSnap.FindVisible(_getProject(), _pageId ?? "",
                    new(pointer.X / 3, pointer.Y / 3), 8 / (3 * Zoom.Value));
            }
            catch (InvalidOperationException) { Status.Text = "附近有多個等距接點，請放大並點選指定 Pin。"; e.Handled = true; return; }
            WireAt(hit is null ? SchematicAttachment.Free() : SchematicAttachment.Pin(hit.SymbolId, hit.EndpointId),
                hit?.Position ?? point, e.ClickCount > 1);
            e.Handled = true;
        }
        else { _selectionId = null; Render(_getProject()); }
    }
    private void BeginGesture(MouseButtonEventArgs e)
    {
        _dragViewportStart = e.GetPosition(PageScroll);
        _dragActivated = false;
        Keyboard.Focus(Sheet); _gestureStart = _getProject(); _gesturePreview = null; _dragStart = Snap(e.GetPosition(Sheet)); Sheet.CaptureMouse();
    }
    private void Sheet_Move(object sender, MouseEventArgs e)
    {
        if (sender is Canvas canvas && canvas != Sheet) return;
        _pointer = Snap(e.GetPosition(Sheet));
        if (_gestureStart is not null && _dragStart is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            // Focus can scroll the sheet without pointer movement. A click must remain selection-only.
            var viewportDelta = e.GetPosition(PageScroll) - _dragViewportStart;
            if (!_dragActivated && Math.Abs(viewportDelta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(viewportDelta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragActivated = true;
            try
            {
                if (_dragDetailId is not null) PreviewDetailGesture();
                else if (_dragResizeSymbol is not null)
                {
                    var s = _gestureStart.Schematic!.Symbols.Single(s => s.SymbolId == _dragResizeSymbol);
                    var owner = SchematicSymbolOwner.Resolve(_gestureStart, s);
                    var bounds = SchematicPortPresentation.GenericBodyBounds(_gestureStart.Schematic, s, owner);
                    _gesturePreview = _service.ResizeGenericSymbol(_gestureStart, s.SymbolId,
                        _pointer.X - bounds.X, _pointer.Y - bounds.Y);
                }
                else if (_dragPinSymbol is not null && _dragPinId is not null)
                {
                    var s = _gestureStart.Schematic!.Symbols.Single(s => s.SymbolId == _dragPinSymbol);
                    var owner = SchematicSymbolOwner.Resolve(_gestureStart, s);
                    var bounds = SchematicPortPresentation.GenericBodyBounds(_gestureStart.Schematic, s, owner);
                    var baseProject = s.ManualSize ? _gestureStart : _service.ResizeGenericSymbol(
                        _gestureStart, s.SymbolId, bounds.Width, bounds.Height);
                    var editSymbol = baseProject.Schematic!.Symbols.Single(item => item.SymbolId == s.SymbolId);
                    var editOwner = SchematicSymbolOwner.Resolve(baseProject, editSymbol);
                    var editBounds = SchematicPortPresentation.GenericBodyBounds(baseProject.Schematic, editSymbol, editOwner);
                    var target = SchematicPortPresentation.DropTarget(editSymbol, _pointer, editBounds);
                    _gesturePreview = _service.MovePinToEdge(baseProject, s.SymbolId, _dragPinId, target.Side, target.Coordinate);
                }
                else if (_dragPortSymbol is not null && _dragPortId is not null)
                {
                    var s = _gestureStart.Schematic!.Symbols.Single(s => s.SymbolId == _dragPortSymbol);
                    var owner = SchematicSymbolOwner.Resolve(_gestureStart, s);
                    var bounds = SchematicPortPresentation.GenericBodyBounds(_gestureStart.Schematic, s, owner);
                    var target = s.Geometry is null && (s.ManualSize || s.CollapsedPortIds.Count > 0)
                        ? SchematicPortPresentation.DropTarget(s, _pointer, bounds)
                        : SchematicPortPresentation.DropTarget(s, _pointer);
                    _gesturePreview = _service.MovePortToEdge(_gestureStart, s.SymbolId, _dragPortId, target.Side, target.Coordinate);
                }
                else if (_dragSymbol is not null)
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
        else if ((_wireMode && _wireStart is not null) || _placeMode) Render(_getProject());
    }
    private void Sheet_Up(object sender, MouseButtonEventArgs e)
    {
        if (_gestureStart is null) return;
        var togglePort = _editModuleMode && !_dragActivated && _dragPortHasPins && _dragPortSymbol is not null && _dragPortId is not null
            ? (_dragPortSymbol, _dragPortId) : ((string?, string?)?)null;
        if (_gesturePreview is not null && _pointer != _dragStart) _commit(_gesturePreview, "調整圖面位置／路徑");
        CancelGesture();
        if (togglePort is { } target)
            Apply(p => _service.TogglePortCollapsed(p, target.Item1!, target.Item2!), "已切換 Port 接點顯示");
        RefreshWorkspace(); FocusCanvas();
    }
    private void FocusCanvas()
    {
        FocusManager.SetFocusedElement(Window.GetWindow(this), Sheet);
        Keyboard.Focus(Sheet);
    }
    private void CancelGesture()
    { _gestureStart = null; _gesturePreview = null; _dragStart = null; _dragActivated = false; _dragSymbol = null; _dragPortSymbol = null; _dragPortId = null; _dragPinSymbol = null; _dragPinId = null; _dragResizeSymbol = null; _dragPortHasPins = false; _dragVertex = null; _dragSegment = null; _dragMarker = null; _dragDetailId = null; _dragDetailResize = false; Sheet.ReleaseMouseCapture(); }
    private void CancelCommand()
    {
        CancelGesture(); ClearPendingWire(); _placeMode = false; _pairMode = false; _pairFirst = null;
        _pendingRepresentation = null;
        _pendingCable = null;
        _pendingCableRepresentation = null;
        _pendingBom = null;
        _wireMode = false; _editModuleMode = false; _placementPreview = null;
        SelectTool.IsChecked = true; WireTool.IsChecked = false; EditModuleTool.IsChecked = false;
        _selectionId = null; Render(_getProject()); Status.Text = "已取消目前操作";
    }
    private void Select_Click(object sender, RoutedEventArgs e) { if (!FinishPendingDraft()) return; CancelCommand(); Focus(); }
    private void Wire_Click(object sender, RoutedEventArgs e) { _pairMode = false; _pairFirst = null; _placeMode = false; _editModuleMode = false; EditModuleTool.IsChecked = false; _wireMode = true; SelectTool.IsChecked = false; WireTool.IsChecked = true; Render(_getProject()); Focus(); }
    private void EditModule_Click(object sender, RoutedEventArgs e)
    {
        if (!FinishPendingDraft()) { EditModuleTool.IsChecked = _editModuleMode; return; }
        CancelGesture(); _editModuleMode = EditModuleTool.IsChecked == true;
        _wireMode = false; _placeMode = false; WireTool.IsChecked = false;
        SelectTool.IsChecked = !_editModuleMode;
        Render(_getProject());
        Status.Text = _editModuleMode ? "選取模塊；拖動 Port 到四邊，點一下 Port 展開或收合 Pin。移動後接點位置待確認。" : "已退出編輯模塊";
        FocusCanvas();
    }
    private void FinishWire_Click(object sender, RoutedEventArgs e)
    { if (_wireStart is not null && _wirePoints.Count > 1) WireAt(SchematicAttachment.Free(), _wirePoints[^1], true); }
    private void AddPage_Click(object sender, RoutedEventArgs e)
    {
        if (!FinishPendingDraft()) return;
        Apply(p => _service.AddPage(p, $"工程圖 {(p.Schematic?.Pages.Count ?? 0) + 1}", _pageId), "已新增頁面");
        _pageId = _getProject().Schematic?.Pages.LastOrDefault()?.PageId; _selectionId = null; RefreshWorkspace();
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
    private void Place_Click(object sender, RoutedEventArgs e)
    {
        if (CatalogList.SelectedItem is not CatalogItem item || _pageId is null || !FinishPendingDraft()) return;
        CancelCommand();
        try
        {
            _placementPreview = PlaceBomWithSavedLayout(_getProject(), item.Entry, _pageId, new(20, 20)).Project;
            _pendingBom = item.Entry;
            _placeMode = true; Status.Text = "選定元件待放置"; Focus(); Render(_getProject());
        }
        catch (Exception error) { Status.Text = error.Message; }
    }
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
        var owner = s is null ? null : SchematicSymbolOwner.Resolve(p, s);
        var w = p.Schematic?.Wires.SingleOrDefault(w => w.WireId == _selectionId);
        SelectionLabel.Text = s is null ? (w is not null ? "導線" : "") : SchematicSymbolPresentation.DisplayTitle(s, owner!);
        ReferenceText.Text = owner?.Reference ?? "";
        var coverage = s is null ? default : SchematicSymbolPresentation.ContactCoverage(s, owner!);
        SelectionState.Text = s is not null ? $"{(s.Locked ? "已鎖定" : "可編輯")}\n接點位置：{coverage.Confirmed} / {coverage.Total} 已確認" :
            w is not null ? $"{(w.Locked ? "已鎖定" : "可編輯")}\n{(w.ConnectionId is null ? "待接續" : "工程連線已建立")}" : "";
        if (s?.Geometry is not null)
            SelectionState.Text += "\n" + (s.AssetRevision is null ? "圖塊草稿／未核准" : s.AssetRevision) + "\n" + string.Join("\n", s.Geometry.Diagnostics);
        if (owner?.Cable?.ArchivedCable is { } cable)
            SelectionState.Text += "\n" + (cable.MappingConfirmed ? "內部接法有確認來源" : "內部接法未確認") +
                $"\n模板：{cable.Template.TemplateRevision} / mapping：{cable.Template.MappingRevision ?? "未確認"}";
        if (w is not null)
        {
            var spec = SchematicWirePresentation.Resolve(p, w);
            SelectionState.Text += "\n" + w.Designation;
            SelectionState.Text += "\n" + (spec.Awg is int awg ? $"{(spec.AwgIsApproximate ? "約 " : "")}AWG {awg}" : "AWG 未指定");
            if (spec.AreaMm2 is double area) SelectionState.Text += $" / {area:0.###} mm²";
            var evidence = SchematicWireEvidence.Resolve(p, w);
            SelectionState.Text += "\n接點額定資料\n" + evidence.Description;
            if (evidence.Category == "CONFLICT") SelectionState.Text += "\n兩端分類衝突，需確認";
            if (w.SizingProposal is { } proposal) SelectionState.Text += "\n線徑建議待核對：" + proposal.Review;
        }
    }
    private void Reference_Click(object sender, RoutedEventArgs e)
    {
        var s = _getProject().Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == _selectionId); var reference = ReferenceText.Text;
        if (s is not null) Apply(p => _service.SetSymbolDetails(p, s.SymbolId, reference, s.Anchors), "已更新 Reference");
    }
    private void Zoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (SheetZoom is not null) { SheetZoom.ScaleX = SheetZoom.ScaleY = e.NewValue; Render(_getProject()); } }
    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (_selectionId is not null) NavigateToMarker(_selectionId);
    }

    private void NavigateToMarker(string markerId)
    {
        var pair = _getProject().Schematic?.Continuations.SingleOrDefault(c => c.Source.MarkerId == markerId || c.Destination.MarkerId == markerId);
        if (pair is null || !FinishPendingDraft()) return; var other = pair.Source.MarkerId == markerId ? pair.Destination : pair.Source;
        _pageId = other.PageId; _selectionId = other.MarkerId; RefreshWorkspace(); Sheet.BringIntoView(new Rect(other.Position.X * 3, other.Position.Y * 3, 100, 40));
    }
    private void Continuation_Click(object sender, RoutedEventArgs e)
    {
        var doc = _getProject().Schematic;
        if (doc is null || _pageId is null || doc.Pages.Count < 2) { Status.Text = "跨頁符號需要至少兩頁"; return; }
        if (!FinishPendingDraft()) return;
        CancelGesture(); _pairMode = true; _pairFirst = null; _wireMode = false; _placeMode = false;
        SelectTool.IsChecked = true; WireTool.IsChecked = false;
        Status.Text = "跨頁配對：選取第一條未完成導線"; Render(_getProject()); Focus();
    }

    private void PairWire(SchematicWire wire, SchematicPoint click)
    {
        if (wire.Locked || wire.ConnectionId is not null) { Status.Text = "請選取未鎖定的待接續導線；不能合併既有完整回路"; return; }
        var ends = new[] { true, false }.Where(start => (start ? wire.Start : wire.End).Kind == SchematicAttachmentKind.Free).ToArray();
        if (ends.Length == 0) { Status.Text = "此導線沒有自由端"; return; }
        var atStart = ends.OrderBy(start => { var p = start ? wire.Points[0] : wire.Points[^1]; return Math.Abs(p.X - click.X) + Math.Abs(p.Y - click.Y); }).First();
        if (_pairFirst is null)
        {
            _pairFirst = (wire.WireId, atStart); _selectionId = wire.WireId;
            Status.Text = "跨頁配對：到另一頁選取第二條未完成導線"; Render(_getProject()); return;
        }
        var first = _pairFirst.Value;
        var firstWire = _getProject().Schematic!.Wires.Single(w => w.WireId == first.WireId);
        if (firstWire.PageId == wire.PageId) { Status.Text = "第二條導線必須在另一頁"; return; }
        var dialog = new Window { Title = "跨頁配對", Width = 400, Height = 190, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new(16) }; var signal = new TextBox { Margin = new(0, 4, 0, 12) };
        panel.Children.Add(new TextBlock { Text = "訊號／電位名稱" }); panel.Children.Add(signal);
        var ok = new Button { Content = "建立配對", IsDefault = true, Padding = new(8) };
        ok.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(signal.Text)) dialog.DialogResult = true; };
        panel.Children.Add(ok); dialog.Content = panel;
        dialog.Loaded += (_, _) => signal.Focus();
        if (dialog.ShowDialog() != true) return;
        if (Apply(p => _service.ConnectAcrossPages(p, first.WireId, first.Start, wire.WireId, atStart, signal.Text), "已連接兩頁導線並建立跨頁參照"))
        { _pairMode = false; _pairFirst = null; }
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
        if (s.CableInstanceId is not null) { EditCableBindings(s); return; }
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
            Apply(current => _service.SetSymbolDetails(current, s.SymbolId, SchematicSymbolOwner.Resolve(p, s).Reference,
                rows.Select(r => r.Anchor with { Position = new(r.X, r.Y), Confirmed = r.Confirmed,
                    CadContactId = r.CadContact is { } contact && contact.Position == new SchematicPoint(r.X, r.Y) ? contact.Tag :
                        r.CadContact is null && r.Anchor.Position == new SchematicPoint(r.X, r.Y) ? r.Anchor.CadContactId : null }).ToArray()), "已更新接點位置");
    }
    private sealed record PageItem(string Id, string Label);
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
