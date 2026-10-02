using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.SymbolArchive;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace ComponentIntelligence.Desktop;

internal sealed class CableCadRoleEditor : UserControl
{
    private readonly TextBox _file = new() { IsReadOnly = true, MinWidth = 180 };
    private readonly TextBox _units = new() { Text = "1", Width = 70 };
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "完整圖面", "區域", "圖塊" }, SelectedIndex = 0, Width = 100 };
    private readonly ComboBox _blocks = new() { Width = 170 };
    private readonly TextBox[] _region = Enumerable.Range(0, 4).Select(_ => new TextBox { Width = 70, Text = "0", Margin = new(0, 0, 6, 0) }).ToArray();
    private readonly Viewbox _preview = new() { Stretch = Stretch.Uniform, Margin = new(4) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private SchematicCadAsset? _full;
    private SchematicPoint? _start;
    private bool _restoring;
    public string? SourcePath { get; private set; }
    public SchematicCadAsset? Geometry { get; private set; }
    public CableCadSelection? Selection { get; private set; }
    public double Units => double.Parse(_units.Text, CultureInfo.CurrentCulture);
    public event EventHandler? Changed;

    public void ValidateCommitted()
    {
        if (Geometry is null) return;
        if (Units != Geometry.MillimetresPerUnit)
            throw new InvalidOperationException("圖面單位已變更，請重新載入原圖。");
        var mode = Selection?.Bounds is not null ? 1 : Selection?.BlockName is not null ? 2 : 0;
        if (_mode.SelectedIndex != mode)
            throw new InvalidOperationException("請先套用選取範圍。");
        if (Selection?.Bounds is { } b && !new[] { b.X, b.Y, b.Width, b.Height }.SequenceEqual(
            _region.Select(f => double.Parse(f.Text, CultureInfo.CurrentCulture))))
            throw new InvalidOperationException("範圍已變更，請先套用。");
        if (Selection?.BlockName is { } name && (string?)_blocks.SelectedItem != name)
            throw new InvalidOperationException("圖塊已變更，請先套用。");
    }

    public CableCadRoleEditor()
    {
        var root = new DockPanel();
        var top = new StackPanel();
        var fileLine = new DockPanel { Margin = new(0, 0, 0, 8) };
        var browse = Button("\uE8E5", "選取 CAD 檔 (Ctrl+O)", async (_, _) => await BrowseAsync());
        DockPanel.SetDock(browse, Dock.Right); fileLine.Children.Add(browse);
        var reload = Button("\uE72C", "重新載入原圖 (Ctrl+R)", async (_, _) => await ReloadAsync());
        DockPanel.SetDock(reload, Dock.Right); fileLine.Children.Add(reload); fileLine.Children.Add(_file); top.Children.Add(fileLine);
        var settings = new WrapPanel { Margin = new(0, 0, 0, 8) };
        settings.Children.Add(new TextBlock { Text = "mm／單位", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0) });
        settings.Children.Add(_units); settings.Children.Add(_mode); settings.Children.Add(_blocks);
        settings.Children.Add(Button("\uE73E", "套用範圍 (Ctrl+Enter)", (_, _) => ApplySelection())); top.Children.Add(settings);
        var bounds = new WrapPanel();
        foreach (var (label, i) in new[] { ("X", 0), ("Y", 1), ("寬", 2), ("高", 3) })
        { bounds.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0) }); bounds.Children.Add(_region[i]); }
        top.Children.Add(bounds); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        root.Children.Add(new Border { Background = Brushes.White, BorderBrush = Brushes.Gainsboro, BorderThickness = new(1), Child = _preview });
        Content = root;
        _mode.SelectionChanged += (_, _) => { if (!_restoring) UpdatePreview(); };
        PreviewKeyDown += async (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Key == Key.O) { e.Handled = true; await BrowseAsync(); }
            else if (e.Key == Key.R) { e.Handled = true; await ReloadAsync(); }
            else if (e.Key == Key.Return) { e.Handled = true; ApplySelection(); }
        };
    }

    public void Restore(string source, SchematicCadAsset? geometry, CableCadSelection? selection, double units)
    {
        _restoring = true;
        SourcePath = source; _file.Text = Path.GetFileName(source); _file.ToolTip = source;
        _units.Text = units.ToString(CultureInfo.CurrentCulture); Geometry = geometry; Selection = selection;
        _full = selection is null ? geometry : null;
        _mode.SelectedIndex = selection?.Bounds is not null ? 1 : selection?.BlockName is not null ? 2 : 0;
        if (selection?.Bounds is { } bounds) SetBounds(bounds);
        if (selection?.BlockName is { } block) { _blocks.ItemsSource = new[] { block }; _blocks.SelectedItem = block; }
        _restoring = false; UpdatePreview();
    }

    private async Task BrowseAsync()
    {
        var dialog = new OpenFileDialog { Filter = "CAD 圖檔|*.dxf;*.dwg;*.dwt", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await LoadAsync(dialog.FileName, resetSelection: true);
    }
    private async Task ReloadAsync() { if (SourcePath is not null) await LoadAsync(SourcePath, resetSelection: false); }

    private async Task LoadAsync(string path, bool resetSelection)
    {
        try
        {
            if (!double.TryParse(_units.Text, out var units) || !double.IsFinite(units) || units <= 0) throw new InvalidOperationException("圖面單位請輸入正數。");
            IsEnabled = false;
            var asset = await new SchematicCadFileLoader().ReadAsync(path, units);
            SourcePath = path; _file.Text = Path.GetFileName(path); _file.ToolTip = path; _full = asset;
            if (resetSelection) { Selection = null; _mode.SelectedIndex = 0; }
            _blocks.ItemsSource = asset.Primitives.SelectMany(p => p.SourceBlocks).Distinct(StringComparer.Ordinal).ToArray();
            if (Selection?.BlockName is { } block) _blocks.SelectedItem = block;
            Geometry = asset;
            if (Selection is not null) ApplySelection(); else { UpdatePreview(); Changed?.Invoke(this, EventArgs.Empty); }
        }
        catch (Exception error) { _status.Text = error.Message; }
        finally { IsEnabled = true; }
    }

    private void ApplySelection()
    {
        if (_full is null) { _status.Text = "請先載入原圖，才能更改選取範圍。"; return; }
        try
        {
            CableCadSelection? selection = null;
            if (_mode.SelectedIndex == 1)
            {
                var values = _region.Select(f => double.Parse(f.Text, CultureInfo.CurrentCulture)).ToArray();
                selection = new() { Bounds = new(values[0], values[1], values[2], values[3]), GeometrySha256 = new('0', 64) };
            }
            else if (_mode.SelectedIndex == 2)
            {
                if (_blocks.SelectedItem is not string block) throw new InvalidOperationException("請選取圖塊。");
                selection = new() { BlockName = block, GeometrySha256 = new('0', 64) };
            }
            var geometry = selection is null ? _full : SchematicCadSelectionService.Select(_full, selection);
            Geometry = geometry; Selection = selection is null ? null : selection with { GeometrySha256 = geometry.SelectionSha256! };
            UpdatePreview(); Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    private void UpdatePreview()
    {
        var asset = _mode.SelectedIndex == 1 ? _full ?? Geometry : Geometry;
        if (asset is null) { _preview.Child = null; _status.Text = "尚未匯入"; return; }
        var canvas = new Canvas { Width = asset.Width * 3, Height = asset.Height * 3, Background = Brushes.Transparent };
        canvas.Children.Add(SchematicWorkspaceControl.CadCanvas(asset));
        foreach (var contact in asset.ConnectionPoints)
        {
            var dot = new Ellipse { Width = 6, Height = 6, Stroke = Brushes.Teal, Fill = Brushes.White, StrokeThickness = 1, ToolTip = contact.Tag };
            Canvas.SetLeft(dot, contact.Position.X * 3 - 3); Canvas.SetTop(dot, contact.Position.Y * 3 - 3); canvas.Children.Add(dot);
        }
        if (_mode.SelectedIndex == 1 && Selection?.Bounds is { } selected && _full is not null) AddBounds(canvas, selected);
        canvas.MouseLeftButtonDown += (_, e) => { if (_mode.SelectedIndex != 1 || _full is null) return; var p = e.GetPosition(canvas); _start = new(p.X / 3, p.Y / 3); canvas.CaptureMouse(); e.Handled = true; };
        canvas.MouseLeftButtonUp += (_, e) =>
        {
            canvas.ReleaseMouseCapture(); if (_start is not { } start || _full is null) return; _start = null;
            var p = e.GetPosition(canvas); var x = Math.Clamp(p.X / 3, 0, _full.Width); var y = Math.Clamp(p.Y / 3, 0, _full.Height);
            SetBounds(new(Math.Min(start.X, x), Math.Min(start.Y, y), Math.Abs(start.X - x), Math.Abs(start.Y - y)));
            ApplySelection(); e.Handled = true;
        };
        _preview.Child = canvas;
        _status.Text = $"{Geometry?.Width:0.##} × {Geometry?.Height:0.##} mm；接點 {Geometry?.ConnectionPoints.Count ?? 0}" +
            (Geometry?.Diagnostics.Count > 0 ? "\n" + string.Join("\n", Geometry.Diagnostics) : "");
    }
    private void SetBounds(SchematicGridBounds bounds) { var values = new[] { bounds.X, bounds.Y, bounds.Width, bounds.Height }; for (var i = 0; i < 4; i++) _region[i].Text = values[i].ToString("R", CultureInfo.CurrentCulture); }
    private static void AddBounds(Canvas canvas, SchematicGridBounds bounds)
    {
        var rectangle = new Rectangle { Width = bounds.Width * 3, Height = bounds.Height * 3, Stroke = Brushes.Teal, StrokeThickness = 2, IsHitTestVisible = false };
        Canvas.SetLeft(rectangle, bounds.X * 3); Canvas.SetTop(rectangle, bounds.Y * 3); canvas.Children.Add(rectangle);
    }
    private static Button Button(string glyph, string tooltip, RoutedEventHandler handler)
    {
        var button = new Button { FontFamily = new("Segoe MDL2 Assets"), Content = glyph, ToolTip = tooltip, Padding = new(9, 5, 9, 5), Margin = new(4, 0, 0, 0) };
        button.Click += handler; return button;
    }
}
