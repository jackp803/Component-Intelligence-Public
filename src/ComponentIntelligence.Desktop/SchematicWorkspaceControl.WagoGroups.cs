using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
            if (_renderingOutput) continue;
            var header = new Border { Width = group.Bounds.Width * 3, Height = SchematicWagoGroups.HeaderHeight * 3,
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
        }
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
