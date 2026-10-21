using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public sealed class CableWiringDiagramEditor : UserControl
{
    private const double PixelsPerMm = 4;
    private readonly CableWiringEditorService _service = new();
    private readonly Canvas _canvas = new() { Background = Brushes.White };
    private readonly Grid _viewport = new() { Background = Brushes.White, ClipToBounds = true, MinHeight = 200 };
    private readonly TextBlock _status = new() { Margin = new(4), TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _usage = new() { ItemsSource = Enum.GetValues<CablePinUsage>(), Width = 100 };
    private readonly Stack<string> _undo = new(), _redo = new();
    private readonly ToggleButton _select, _wire;
    private readonly Button _undoButton, _redoButton, _mirrorButton;
    private string? _selectedPin, _selectedEdge, _selectedPort, _pendingPin, _dragId, _dragBefore;
    private bool _wireMode, _pinDrag, _pan, _dragMoved, _syncUsage;
    private Point _mouseStart;
    private SchematicPoint? _objectStart;
    private int? _dragSegment;
    private readonly List<SchematicPoint> _waypoints = [];
    private CableWiringDiagramPresentation _presentation = null!;
    public CableManufacturingDraft Draft { get; }
    public double Zoom { get; private set; } = 1;
    public Vector PanOffset { get; private set; }
    public event EventHandler? Changed;

    public CableWiringDiagramEditor(CableManufacturingDraft draft)
    {
        Draft = draft;
        if (!draft.InventoryOnly) throw new InvalidOperationException("請先明確進入線材接線圖模式。");
        _service.EnsureLayout(draft);
        var root = new DockPanel { Background = Brushes.White };
        var toolbar = new WrapPanel { Margin = new(0, 0, 0, 6) };
        _select = Toggle("\uE762", "選取／移動", () => SetMode(false));
        _wire = Toggle("\uE71B", "接線：選擇起點 Pin，再選擇對端 Pin；點導線可建立分支", () => SetMode(true));
        toolbar.Children.Add(_select); toolbar.Children.Add(_wire);
        _mirrorButton = Icon("\uE8AB", "接點左右對調", () => { if (_selectedPort is { } id) MirrorPort(id); });
        toolbar.Children.Add(_mirrorButton);
        toolbar.Children.Add(Icon("\uE74D", "刪除所選導線 (Delete)", DeleteSelection));
        _undoButton = Icon("\uE7A7", "復原 (Ctrl+Z)", Undo); _redoButton = Icon("\uE7A6", "重做 (Ctrl+Y)", Redo);
        toolbar.Children.Add(_undoButton); toolbar.Children.Add(_redoButton);
        toolbar.Children.Add(Icon("\uE740", "符合畫面", FitToView));
        toolbar.Children.Add(Icon("\uE8A3", "放大 (Ctrl+滾輪)", () => ZoomBy(1.2, new(_viewport.ActualWidth / 2, _viewport.ActualHeight / 2))));
        toolbar.Children.Add(Icon("\uE71F", "縮小 (Ctrl+滾輪)", () => ZoomBy(1 / 1.2, new(_viewport.ActualWidth / 2, _viewport.ActualHeight / 2))));
        _usage.ToolTip = "所選 Pin 的狀態"; toolbar.Children.Add(_usage);
        _usage.SelectionChanged += (_, _) => { if (!_syncUsage && _selectedPin is { } id && _usage.SelectedItem is CablePinUsage value) SetPinUsage(id, value); };
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        _viewport.Children.Add(_canvas); root.Children.Add(_viewport); Content = root;
        _viewport.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            ZoomBy(e.Delta > 0 ? 1.15 : 1 / 1.15, e.GetPosition(_viewport)); e.Handled = true;
        };
        _viewport.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            _pan = true; _mouseStart = e.GetPosition(_viewport); _viewport.CaptureMouse(); e.Handled = true;
        };
        _viewport.MouseMove += DragMove;
        _viewport.PreviewMouseUp += EndDrag;
        _viewport.LostMouseCapture += (_, _) => { if (_dragId is not null || _dragSegment is not null) FinishDrag(); _pan = false; };
        _canvas.MouseLeftButtonDown += (_, e) =>
        {
            if (_wireMode && _pendingPin is not null)
            { _waypoints.Add(Point(e.GetPosition(_canvas))); Render(); }
            else { _selectedPin = null; _selectedEdge = null; _selectedPort = null; Render(); }
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { _pendingPin = null; _waypoints.Clear(); Render(); e.Handled = true; }
            else if (e.Key == Key.Delete) { DeleteSelection(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z) { Undo(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y) { Redo(); e.Handled = true; }
        };
        Loaded += (_, _) => FitToView();
        SetMode(false); Render();
    }

    public static void CopyDraft(CableManufacturingDraft from, CableManufacturingDraft to)
    {
        var copy = JsonSerializer.Deserialize<CableManufacturingDraft>(JsonSerializer.Serialize(from))!;
        to.Ends = copy.Ends; to.Rows = copy.Rows; to.FromSourcePortId = copy.FromSourcePortId; to.ToSourcePortId = copy.ToSourcePortId;
        to.InventoryOnly = copy.InventoryOnly; to.ExplicitMapping = copy.ExplicitMapping; to.WiringDiagram = copy.WiringDiagram;
        to.MappingRevision = copy.MappingRevision; to.MappingEvidence = copy.MappingEvidence; to.ConfirmMapping = copy.ConfirmMapping;
        to.ExplicitlyRemovedPinIds = copy.ExplicitlyRemovedPinIds;
        to.FunctionLinks = copy.FunctionLinks; to.FunctionOverrides = copy.FunctionOverrides;
    }
    private void SetMode(bool wire) { _wireMode = wire; _select.IsChecked = !wire; _wire.IsChecked = wire; _pendingPin = null; _waypoints.Clear(); }
    public void ConnectPins(string from, string to) => Change(() => _service.Connect(Draft, from, to));
    public void MovePort(string id, SchematicPoint point) => Change(() => _service.MovePort(Draft, id, point));
    public void MovePin(string id, SchematicPoint point) => Change(() => _service.MovePin(Draft, id, point));
    public void MirrorPort(string id) => Change(() => _service.MirrorPort(Draft, id));
    public void SelectPort(string id) { _selectedPort = id; _selectedPin = null; _selectedEdge = null; Render(); }
    public void SetPinUsage(string id, CablePinUsage usage) => Change(() => _service.SetUsage(Draft, id, usage));
    public void SelectEdge(string key) { _selectedEdge = key; _selectedPin = null; _selectedPort = null; Render(); }
    public void DeleteSelection()
    {
        if (_selectedEdge is null) return;
        var edge = CableWiringEditorService.Mapping(Draft).Single(p => CableWiringEditorService.EdgeKey(p) == _selectedEdge);
        if (!Change(() => _service.Disconnect(Draft, edge.FromSourcePinId, edge.ToSourcePinId))) return;
        _selectedEdge = null; Render();
    }
    private bool Change(Action action)
    {
        var before = JsonSerializer.Serialize(Draft);
        try
        {
            action();
            if (before != JsonSerializer.Serialize(Draft)) { _undo.Push(before); _redo.Clear(); Changed?.Invoke(this, EventArgs.Empty); }
            Render(); return true;
        }
        catch (InvalidOperationException e)
        { CopyDraft(JsonSerializer.Deserialize<CableManufacturingDraft>(before)!, Draft); Render(); _status.Text = e.Message; return false; }
    }
    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);
    private void Restore(Stack<string> source, Stack<string> target)
    {
        if (!source.TryPop(out var state)) return;
        target.Push(JsonSerializer.Serialize(Draft)); CopyDraft(JsonSerializer.Deserialize<CableManufacturingDraft>(state)!, Draft);
        _selectedEdge = null; _selectedPin = null; _selectedPort = null; _pendingPin = null; _waypoints.Clear();
        Render(); Changed?.Invoke(this, EventArgs.Empty);
    }
    public void ZoomBy(double factor, Point pivot)
    {
        var zoom = Math.Clamp(Zoom * factor, .05, 8); var ratio = zoom / Zoom;
        PanOffset = new(pivot.X - (pivot.X - PanOffset.X) * ratio, pivot.Y - (pivot.Y - PanOffset.Y) * ratio);
        Zoom = zoom; Transform();
    }
    public void PanBy(Vector delta) { PanOffset += delta; Transform(); }
    public void FitToView()
    {
        var b = _presentation.Bounds;
        var width = Math.Max(200, _viewport.ActualWidth); var height = Math.Max(160, _viewport.ActualHeight);
        Zoom = Math.Clamp(Math.Min((width - 30) / (b.Width * PixelsPerMm), (height - 30) / (b.Height * PixelsPerMm)), .05, 2);
        PanOffset = new(15 - b.X * PixelsPerMm * Zoom, 15 - b.Y * PixelsPerMm * Zoom); Transform();
    }
    private void Transform() => _canvas.RenderTransform = new TransformGroup
    { Children = new() { new ScaleTransform(Zoom, Zoom), new TranslateTransform(PanOffset.X, PanOffset.Y) } };

    private void Render()
    {
        _presentation = _service.Build(Draft); _canvas.Children.Clear();
        CableWiringVisualRenderer.DrawDiagram(_canvas, _presentation);
        foreach (var (key, path) in _presentation.EdgePaths)
        {
            if (key == _selectedEdge && _presentation.EdgePrimitives.TryGetValue(key, out var primitives))
                CableWiringVisualRenderer.Draw(_canvas, primitives, stroke: Brushes.DarkCyan);
            var hit = new Polyline { Points = new(path.Select(p => new Point(p.X * PixelsPerMm, p.Y * PixelsPerMm))),
                Stroke = Brushes.Transparent, StrokeThickness = 12,
                Cursor = _wireMode ? Cursors.Cross : Cursors.SizeAll, ToolTip = "導線：選取後 Delete 刪除；拖曳調整彎折位置" };
            hit.MouseLeftButtonDown += (_, e) =>
            {
                var point = Point(e.GetPosition(_canvas));
                if (_wireMode && _pendingPin is not null) Branch(key, point);
                else
                {
                    SelectEdge(key); _dragBefore = JsonSerializer.Serialize(Draft); _dragSegment = ClosestSegment(path, point);
                    _mouseStart = e.GetPosition(_viewport); _dragMoved = false; _viewport.CaptureMouse();
                }
                e.Handled = true;
            };
            _canvas.Children.Add(hit);
        }
        foreach (var port in Draft.Ends)
        {
            var b = _presentation.PortBounds[port.PortId];
            if (_selectedPort == port.PortId)
            {
                var outline = new Rectangle { Width = b.Width * PixelsPerMm, Height = b.Height * PixelsPerMm,
                    Stroke = Brushes.DarkCyan, StrokeThickness = 1.5, IsHitTestVisible = false };
                Canvas.SetLeft(outline, b.X * PixelsPerMm); Canvas.SetTop(outline, b.Y * PixelsPerMm); _canvas.Children.Add(outline);
            }
            var header = new Rectangle { Width = b.Width * PixelsPerMm, Height = 17 * PixelsPerMm, Fill = Brushes.Transparent,
                Cursor = Cursors.SizeAll, ToolTip = port.Name + " / " + (port.Connector?.Family ?? "待確認") };
            Canvas.SetLeft(header, b.X * PixelsPerMm); Canvas.SetTop(header, b.Y * PixelsPerMm);
            header.MouseLeftButtonDown += (_, e) =>
            { if (!_wireMode) { StartDrag(port.PortId, false, new(b.X, b.Y), e); SelectPort(port.PortId); } e.Handled = true; };
            _canvas.Children.Add(header);
            foreach (var pin in port.Pins)
            {
                var p = _presentation.PinPositions[pin.PinId];
                var hit = new Ellipse { Width = 12, Height = 12, Fill = Brushes.White,
                    Stroke = pin.PinId == _selectedPin || pin.PinId == _pendingPin ? Brushes.DarkCyan : Brushes.Black,
                    StrokeThickness = 1.5, Cursor = _wireMode ? Cursors.Cross : Cursors.Hand, ToolTip = $"{port.Name} / {pin.PinNumber} {pin.Function}" };
                Canvas.SetLeft(hit, p.X * PixelsPerMm - 6); Canvas.SetTop(hit, p.Y * PixelsPerMm - 6);
                hit.MouseLeftButtonDown += (_, e) =>
                {
                    _selectedPin = pin.PinId; _selectedEdge = null; _selectedPort = port.PortId;
                    if (_wireMode) PickPin(pin.PinId);
                    else { Render(); StartDrag(pin.PinId, true, p, e); }
                    e.Handled = true;
                };
                _canvas.Children.Add(hit);
            }
        }
        if (_pendingPin is { } id && _waypoints.Count > 0)
        {
            var preview = new List<SchematicPoint> { _presentation.PinPositions[id] };
            foreach (var p in _waypoints) preview = SchematicDraftRoute.Append(preview, p);
            _canvas.Children.Add(new Polyline { Points = new(preview.Select(p => new Point(p.X * PixelsPerMm, p.Y * PixelsPerMm))), Stroke = Brushes.DarkCyan, StrokeThickness = 1.5, IsHitTestVisible = false });
        }
        _syncUsage = true;
        _usage.IsEnabled = _selectedPin is not null;
        _usage.SelectedItem = Draft.Rows.SelectMany(CableManufacturingCells.All).FirstOrDefault(c => c.SourcePinId == _selectedPin)?.Usage ?? CablePinUsage.Pending;
        _syncUsage = false;
        _undoButton.IsEnabled = _undo.Count > 0; _redoButton.IsEnabled = _redo.Count > 0;
        _mirrorButton.IsEnabled = _selectedPort is not null;
        _status.Text = _selectedPin is { } selected ? Draft.Ends.SelectMany(p => p.Pins.Select(pin => (p, pin))).Where(x => x.pin.PinId == selected)
            .Select(x => $"{x.p.Name} / {x.pin.PinNumber} {x.pin.Function}").FirstOrDefault() : $"{Draft.Ends.Count} Port / {Draft.Ends.Sum(p => p.Pins.Count)} Pin / {CableWiringEditorService.Mapping(Draft).Count} 接線";
        var conflicts = CableFunctionAssociationService.Conflicts(Draft);
        if (conflicts.Count > 0) _status.Text += "\n" + string.Join("\n", conflicts);
        Transform();
    }

    private void PickPin(string id)
    {
        if (_pendingPin is null) { _pendingPin = id; _waypoints.Clear(); Render(); return; }
        var from = _pendingPin; var waypoints = _waypoints.ToArray();
        if (!Change(() =>
        {
            _service.Connect(Draft, from, id);
            if (waypoints.Length > 0) _service.SetRoute(Draft, from, id,
                new[] { Draft.WiringDiagram!.PinPositions[from] }.Concat(waypoints).Append(Draft.WiringDiagram.PinPositions[id]).ToArray());
        })) return;
        _pendingPin = null; _waypoints.Clear(); Render();
    }
    private void Branch(string key, SchematicPoint point)
    {
        var from = _pendingPin!;
        var edge = CableWiringEditorService.Mapping(Draft).Single(p => CableWiringEditorService.EdgeKey(p) == key);
        var path = _presentation.EdgePaths[key]; var index = ClosestSegment(path, point);
        var a = path[index]; var b = path[index + 1];
        var join = a.X == b.X ? new SchematicPoint(a.X, Math.Clamp(point.Y, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y))) :
            new SchematicPoint(Math.Clamp(point.X, Math.Min(a.X, b.X), Math.Max(a.X, b.X)), a.Y);
        if (!Change(() =>
        {
            _service.Connect(Draft, from, edge.FromSourcePinId);
            _service.SetRoute(Draft, from, edge.FromSourcePinId, new[] { Draft.WiringDiagram!.PinPositions[from] }
                .Concat(_waypoints).Append(join).Concat(path.Take(index + 1).Reverse()).ToArray());
        })) return;
        _pendingPin = null; _waypoints.Clear(); Render();
    }
    private void StartDrag(string id, bool pin, SchematicPoint start, MouseButtonEventArgs e)
    {
        _dragId = id; _pinDrag = pin; _objectStart = start; _mouseStart = e.GetPosition(_viewport);
        _dragBefore = JsonSerializer.Serialize(Draft); _dragMoved = false; _viewport.CaptureMouse();
    }
    private void DragMove(object sender, MouseEventArgs e)
    {
        var mouse = e.GetPosition(_viewport); var delta = mouse - _mouseStart;
        if (_pan) { PanBy(delta); _mouseStart = mouse; return; }
        if (_dragBefore is null || delta.Length < 3 && !_dragMoved) return;
        try
        {
            var original = JsonSerializer.Deserialize<CableManufacturingDraft>(_dragBefore)!;
            if (_dragId is { } id && _objectStart is { } start)
            {
                var point = new SchematicPoint(start.X + delta.X / (Zoom * PixelsPerMm), start.Y + delta.Y / (Zoom * PixelsPerMm));
                if (_pinDrag) _service.MovePin(Draft, id, point); else _service.MovePort(Draft, id, point);
            }
            else if (_dragSegment is { } index && _selectedEdge is { } key)
            {
                var pair = CableWiringEditorService.Mapping(Draft).Single(p => CableWiringEditorService.EdgeKey(p) == key);
                var path = _service.Build(original).EdgePaths[key]; var a = path[index]; var b = path[index + 1];
                var dx = a.X == b.X ? delta.X / (Zoom * PixelsPerMm) : 0;
                var dy = a.Y == b.Y ? delta.Y / (Zoom * PixelsPerMm) : 0;
                _service.SetRoute(Draft, pair.FromSourcePinId, pair.ToSourcePinId, path.Take(index + 1)
                    .Append(new(a.X + dx, a.Y + dy)).Append(new(b.X + dx, b.Y + dy)).Concat(path.Skip(index + 1)).ToArray());
            }
            _dragMoved = true; Render();
        }
        catch (InvalidOperationException error) { _status.Text = error.Message; }
    }
    private void EndDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _pan) { _pan = false; _viewport.ReleaseMouseCapture(); e.Handled = true; return; }
        if (e.ChangedButton != MouseButton.Left || _dragBefore is null) return;
        FinishDrag(); _viewport.ReleaseMouseCapture(); e.Handled = true;
    }
    private void FinishDrag()
    {
        if (_dragMoved && _dragBefore is { } before && before != JsonSerializer.Serialize(Draft))
        { _undo.Push(before); _redo.Clear(); Changed?.Invoke(this, EventArgs.Empty); }
        _dragBefore = null; _dragId = null; _dragSegment = null; _dragMoved = false; Render();
    }
    private static SchematicPoint Point(Point p) => new(p.X / PixelsPerMm, p.Y / PixelsPerMm);
    private static int ClosestSegment(IReadOnlyList<SchematicPoint> path, SchematicPoint point) => Enumerable.Range(0, path.Count - 1)
        .MinBy(i => Math.Pow(point.X - Math.Clamp(point.X, Math.Min(path[i].X, path[i + 1].X), Math.Max(path[i].X, path[i + 1].X)), 2) +
            Math.Pow(point.Y - Math.Clamp(point.Y, Math.Min(path[i].Y, path[i + 1].Y), Math.Max(path[i].Y, path[i + 1].Y)), 2));
    private static Button Icon(string glyph, string tip, Action action)
    { var button = new Button { Content = glyph, FontFamily = new("Segoe MDL2 Assets"), ToolTip = tip, Padding = new(9, 5, 9, 5), Margin = new(0, 0, 5, 0) }; button.Click += (_, _) => action(); return button; }
    private static ToggleButton Toggle(string glyph, string tip, Action action)
    { var button = new ToggleButton { Content = glyph, FontFamily = new("Segoe MDL2 Assets"), ToolTip = tip, Padding = new(9, 5, 9, 5), Margin = new(0, 0, 5, 0) }; button.Click += (_, _) => action(); return button; }
}
