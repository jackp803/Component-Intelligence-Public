using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    public event EventHandler? SaveRequested;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        var gesture = string.Join("+", new[]
        {
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl" : null,
            Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt" : null,
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? "Shift" : null,
            key == Key.Return ? "Enter" : key.ToString()
        }.Where(s => s is not null));
        var command = SchematicShortcutCatalog.All.SingleOrDefault(s => s.Gesture == gesture)?.Command;
        if (command is null) return;
        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox &&
            command is not ("Save" or "Cancel" or "ApplyReference" or "Help")) return;
        var args = new RoutedEventArgs();
        switch (command)
        {
            case "Select": Select_Click(this, args); break;
            case "EditModule": EditModuleTool.IsChecked = !EditModuleTool.IsChecked; EditModule_Click(this, args); break;
            case "Wire": Wire_Click(this, args); break;
            case "Finish": FinishWire_Click(this, args); break;
            case "Place": Place_Click(this, args); break;
            case "AnotherRepresentation": AnotherRepresentation_Click(this, args); break;
            case "SplitRepresentation": SplitRepresentation_Click(this, args); break;
            case "ReviewDraft": ReviewDraft_Click(this, args); break;
            case "Continuation": Continuation_Click(this, args); break;
            case "Bindings": Bindings_Click(this, args); break;
            case "Import": ImportSymbol_Click(this, args); break;
            case "Template": ImportTemplate_Click(this, args); break;
            case "Rotate": Rotate_Click(this, args); break;
            case "Lock": Lock_Click(this, args); break;
            case "Peer": Navigate_Click(this, args); break;
            case "MovePage": MoveSymbolPage_Click(this, args); break;
            case "Cable": CableSettings_Click(this, args); break;
            case "CableLibrary": CableLibrary_Click(this, args); break;
            case "CableArchive": CableArchive_Click(this, args); break;
            case "CableDetail": CableDetail_Click(this, args); break;
            case "Gauge": Gauge_Click(this, args); break;
            case "ExportDraft": ExportDraft_Click(this, args); break;
            case "ExportPdf": ExportPdf_Click(this, args); break;
            case "Print": Print_Click(this, args); break;
            case "AllPages": AllPagesTool.IsChecked = !AllPagesTool.IsChecked; AllPages_Click(this, args); break;
            case "AddPage": AddPage_Click(this, args); break;
            case "PagePrevious": SelectAdjacentPage(-1); break;
            case "PageNext": SelectAdjacentPage(1); break;
            case "OrderPrevious": MovePage(-1); break;
            case "OrderNext": MovePage(1); break;
            case "Rename": RenamePage_Click(this, args); break;
            case "ApplyReference": Reference_Click(this, args); break;
            case "Search": Search.Focus(); Search.SelectAll(); break;
            case "Save": if (FinishPendingDraft()) SaveRequested?.Invoke(this, EventArgs.Empty); break;
            case "Undo": CancelCommand(); _undo(); RefreshWorkspace(); break;
            case "Redo": CancelCommand(); _redo(); RefreshWorkspace(); break;
            case "ZoomIn": Zoom.Value = Math.Min(Zoom.Maximum, Zoom.Value + .1); break;
            case "ZoomOut": Zoom.Value = Math.Max(Zoom.Minimum, Zoom.Value - .1); break;
            case "Help": Shortcuts_Click(this, args); break;
            case "Cancel": CancelCommand(); break;
            case "Delete": DeleteSelectedObject(); break;
        }
        e.Handled = true;
    }

    private void SelectAdjacentPage(int delta)
    {
        var index = PageList.SelectedIndex + delta;
        if (index >= 0 && index < PageList.Items.Count) PageList.SelectedIndex = index;
        Sheet.BringIntoView();
    }

    private void DeleteSelectedObject()
    {
        if (SelectedCableDetail() is { } detail && _pageId is not null)
        {
            var part = SelectedDetailPart();
            var label = part == SchematicCableDetailPart.Table ? "接法表" : part == SchematicCableDetailPart.Sketch ? "製作示意圖" : "製作明細";
            if (MessageBox.Show(Window.GetWindow(this), "只刪除此" + label + "？實體線材與所有接線保留。", "刪除" + label,
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var page = _getProject().Schematic!.Pages.Single(p => p.PageId == _pageId);
            if (Apply(p => part is { } selectedPart ? new SchematicCableDetailService().RemovePart(p, page.PageId, detail.DetailId, selectedPart) :
                new SchematicCableDetailService().RemoveDetail(p, page.PageId, detail.DetailId), "已刪除" + label + "；實體線材保留"))
                _selectionId = null;
            return;
        }
        var doc = _getProject().Schematic;
        var symbol = doc?.Symbols.SingleOrDefault(s => s.SymbolId == _selectionId);
        if (symbol is not null)
        {
            if (symbol.Locked) { Status.Text = "請先解鎖此圖面表示。"; return; }
            if (doc!.Wires.Any(w => w.Start.SymbolId == symbol.SymbolId || w.End.SymbolId == symbol.SymbolId))
            { Status.Text = "此表示仍有接線，請先明確處理接線；未刪除任何資料。"; return; }
            if (MessageBox.Show(Window.GetWindow(this), "只刪除此圖面表示？實體元件、Reference 與其他表示會保留。",
                "刪除圖面表示", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            if (Apply(p => _service.DeleteRepresentation(p, symbol.SymbolId), "已刪除圖面表示；實體元件保留，可復原"))
            { _selectionId = null; RefreshWorkspace(); }
            return;
        }
        if (doc?.Continuations.Any(c => c.Source.MarkerId == _selectionId || c.Destination.MarkerId == _selectionId) == true)
        {
            DeleteContinuationMarker(_selectionId!);
            return;
        }
        if (_getProject().Schematic?.Wires.Any(w => w.WireId == _selectionId) != true) return;
        if (MessageBox.Show(Window.GetWindow(this), "刪除此導線及對應工程連線？", "刪除導線",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        Apply(p => _service.DeleteWire(p, _selectionId!), "已刪除導線");
    }

    private void DeleteContinuationMarker(string markerId)
    {
        if (_wireStart is not null) { Status.Text = "請先完成目前導線，或按 Esc 取消畫線，再刪除跨頁符號。"; return; }
        var doc = _getProject().Schematic;
        var pair = doc?.Continuations.SingleOrDefault(c => c.Source.MarkerId == markerId || c.Destination.MarkerId == markerId);
        if (pair is null) return;
        var connected = doc!.Wires.Any(w => w.ConnectionId is not null &&
            (w.Start.MarkerId == pair.Source.MarkerId || w.End.MarkerId == pair.Source.MarkerId ||
             w.Start.MarkerId == pair.Destination.MarkerId || w.End.MarkerId == pair.Destination.MarkerId));
        var message = connected
            ? "這對跨頁符號已建立工程連線。刪除會同時移除整條工程連線及其所有頁面線段；元件不會刪除。確定嗎？"
            : "刪除這一對跨頁符號？已畫的導線保留在原位，兩端恢復為未完成線尾。";
        if (MessageBox.Show(Window.GetWindow(this), message, "刪除跨頁符號",
            MessageBoxButton.YesNo, connected ? MessageBoxImage.Warning : MessageBoxImage.Question,
            MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (Apply(p => _service.DeleteContinuation(p, markerId, deleteCompletedCircuit: connected),
            connected ? "已刪除跨頁符號與工程連線；可復原" : "已刪除成對跨頁符號；導線保留為未完成"))
        { _selectionId = null; RefreshWorkspace(); }
    }

    private void Shortcuts_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window { Title = "快捷鍵", Width = 430, Height = 640,
            Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.Content = new DataGrid { ItemsSource = SchematicShortcutCatalog.All, IsReadOnly = true,
            AutoGenerateColumns = false, Margin = new(12), Columns =
            {
                new DataGridTextColumn { Header = "操作", Binding = new System.Windows.Data.Binding("Label"), Width = new(1, DataGridLengthUnitType.Star) },
                new DataGridTextColumn { Header = "快捷鍵", Binding = new System.Windows.Data.Binding("Gesture"), Width = 160 }
            } };
        dialog.ShowDialog();
    }

    private void Gauge_Click(object sender, RoutedEventArgs e)
    {
        var wire = _getProject().Schematic?.Wires.SingleOrDefault(w => w.WireId == _selectionId);
        if (wire is null) { Status.Text = "請先選取導線"; return; }
        var dialog = new Window { Title = "導線規格", Width = 360, Height = 220,
            Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new(16) };
        panel.Children.Add(new TextBlock { Text = "AWG", Margin = new(0, 0, 0, 8) });
        var choices = new ComboBox { ItemsSource = new[] { "未指定" }.Concat(Enumerable.Range(0, 41).Select(i => $"AWG {i}")).ToArray(),
            SelectedIndex = wire.Awg is int awg ? awg + 1 : 0 };
        panel.Children.Add(choices);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new(12, 5, 12, 5) };
        var apply = new Button { Content = "套用", IsDefault = true, Padding = new(12, 5, 12, 5), Margin = new(8, 0, 0, 0) };
        apply.Click += (_, _) =>
        {
            if (Apply(p => _service.SetWireAwg(p, wire.WireId, choices.SelectedIndex > 0 ? choices.SelectedIndex - 1 : null), "已更新導線規格"))
                dialog.DialogResult = true;
        };
        row.Children.Add(cancel); row.Children.Add(apply); panel.Children.Add(row); dialog.Content = panel; dialog.ShowDialog();
    }
}
