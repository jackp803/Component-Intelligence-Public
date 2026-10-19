using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private void WagoGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!ResolveInspectorDraft()) return;
        var project = _getProject();
        var selected = project.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == _selectionId);
        if (selected is null) return;
        if (selected.WagoVisualGroupId is not null)
        {
            Apply(p => _service.DissolveWagoGroup(p, selected.SymbolId), "已拆開 WAGO 合併顯示");
            return;
        }
        var dialog = new WagoGroupSelectionDialog(project, selected) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        Apply(p => _service.GroupWagoSymbols(p, dialog.SelectedIds), "已合併 WAGO 顯示；各顆接線與 BOM 保持獨立");
    }

    private void RenderWagoGroups(ElectricalProject project, SchematicPage page)
    {
        foreach (var group in SchematicWagoGroups.Build(project.Schematic!, page.PageId))
        {
            var primitives = SchematicWagoGroups.Primitives(project, group);
            var geometry = CadCanvas(new() { SourceSha256 = "", Width = page.Width, Height = page.Height, Primitives = primitives });
            geometry.Tag = "wago-group:" + group.Id;
            Sheet.Children.Add(geometry);
            var photo = new Image { Stretch = Stretch.Uniform, IsHitTestVisible = false, Tag = "wago-group:" + group.Id };
            Sheet.Children.Add(photo);
            var definition = SchematicSymbolOwner.Resolve(project, group.Members[0]).DefinitionId;
            if (_images.TryGetValue(definition, out var cached) && cached is not null) PlaceWagoImage(photo, cached, group);
            else if (!_renderingOutput) _ = LoadWagoImage(definition, photo, group);
            if (_renderingOutput) continue;
            var header = new Border { Width = group.Bounds.Width * 3, Height = group.HeaderHeight * 3,
                Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Tag = "wago-group:" + group.Id,
                ToolTip = "WAGO 221-2411 合併顯示" };
            Canvas.SetLeft(header, group.Bounds.X * 3); Canvas.SetTop(header, group.Bounds.Y * 3);
            header.MouseLeftButtonDown += (_, e) =>
            {
                if (_wireMode) return;
                _selectionId = group.Members[0].SymbolId;
                if (_editModuleMode || group.Members.Any(s => s.Locked)) { Render(project); e.Handled = true; return; }
                BeginGesture(e); _dragSymbol = _selectionId; e.Handled = true; UpdateSelection(project);
            };
            Sheet.Children.Add(header);
            if (_editModuleMode && group.Members.All(s => !s.Locked))
            {
                var resize = new Border { Width = 14, Height = 14, Background = Brushes.White, BorderBrush = Brushes.DarkCyan,
                    BorderThickness = new(1), Cursor = Cursors.SizeNWSE, Tag = "wago-group:" + group.Id,
                    Child = new TextBlock { Text = "↘", FontSize = 10, TextAlignment = TextAlignment.Center },
                    ToolTip = "拖曳調整 WAGO 合併框寬度與列高；接點及導線會跟隨" };
                Canvas.SetLeft(resize, (group.Bounds.X + group.Bounds.Width) * 3 - 7);
                Canvas.SetTop(resize, (group.Bounds.Y + group.Bounds.Height) * 3 - 7);
                resize.MouseLeftButtonDown += (_, e) => {
                    _selectionId = group.Members[0].SymbolId; BeginGesture(e); _dragResizeSymbol = _selectionId; e.Handled = true;
                };
                Sheet.Children.Add(resize);
            }
        }
    }

    private async Task LoadWagoImage(string definitionId, Image target, SchematicWagoGroup group)
    {
        try {
            var bitmap = await GetImage(definitionId);
            if (bitmap is not null) await target.Dispatcher.InvokeAsync(() => PlaceWagoImage(target, bitmap, group));
        }
        catch { await target.Dispatcher.InvokeAsync(() => target.ToolTip = "圖片無法載入"); }
    }

    private static void PlaceWagoImage(Image target, BitmapSource bitmap, SchematicWagoGroup group)
    {
        var fit = SchematicWagoGroups.ImageBounds(group, bitmap.PixelWidth, bitmap.PixelHeight);
        Canvas.SetLeft(target, fit.X * 3); Canvas.SetTop(target, fit.Y * 3);
        target.Width = fit.Width * 3; target.Height = fit.Height * 3; target.Source = bitmap;
    }

    private void WagoFanout_Click(object sender, RoutedEventArgs e)
    {
        var project = _getProject();
        var symbol = project.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == _selectionId);
        if (symbol?.WagoVisualGroupId is null) return;
        EditFanoutSpacing(symbol, SchematicSymbolOwner.Resolve(project, symbol).Ports.Single());
    }
}

internal sealed class WagoGroupSelectionDialog : Window
{
    private readonly List<(string Id, CheckBox Choice)> _choices = [];
    internal string[] SelectedIds => _choices.Where(c => c.Choice.IsChecked == true).Select(c => c.Id).ToArray();

    internal WagoGroupSelectionDialog(ElectricalProject project, SchematicSymbol selected)
    {
        Title = "合併 WAGO 221-2411"; Width = 420; Height = 400; MinWidth = 340; MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new(16) }; Content = panel;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 80, Margin = new(6), Padding = new(8, 5, 8, 5) };
        var accept = new Button { Content = "合併顯示", IsDefault = true, MinWidth = 100, Margin = new(6), Padding = new(8, 5, 8, 5), IsEnabled = false };
        actions.Children.Add(cancel); actions.Children.Add(accept); DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        var list = new StackPanel(); panel.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        foreach (var symbol in project.Schematic!.Symbols.Where(s => s.PageId == selected.PageId && s.WagoVisualGroupId is null && SchematicWagoGroups.IsEligible(project, s))
            .OrderBy(s => s.Position.Y).ThenBy(s => s.Position.X))
        {
            var reference = SchematicSymbolOwner.Resolve(project, symbol).Reference ?? "Reference 未設定";
            var choice = new CheckBox { Content = $"{reference}   ·   X {symbol.Position.X:0.#} / Y {symbol.Position.Y:0.#} mm",
                Margin = new(4, 10, 4, 10), IsChecked = symbol.SymbolId == selected.SymbolId, IsEnabled = !symbol.Locked,
                ToolTip = symbol.Locked ? "請先解鎖元件" : reference };
            _choices.Add((symbol.SymbolId, choice)); list.Children.Add(choice);
            choice.Checked += (_, _) => accept.IsEnabled = SelectedIds.Length >= 2;
            choice.Unchecked += (_, _) => accept.IsEnabled = SelectedIds.Length >= 2;
        }
        accept.Click += (_, _) => { if (SelectedIds.Length >= 2) DialogResult = true; };
    }
}
