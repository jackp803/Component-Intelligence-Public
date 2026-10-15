using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private ContextMenu PortFanoutMenu(SchematicSymbol symbol, ComponentPort port)
    {
        var menu = new ContextMenu();
        var enabled = symbol.EndpointDisplayPlacements.Any(p => p.PortId == port.PortId);
        var expand = new MenuItem { Header = enabled ? "收回接點引線" : "展開接點引線", IsEnabled = !symbol.Locked && port.Pins.Count > 0 };
        expand.Click += (_, _) => { if (ResolveInspectorDraft()) Apply(p => _service.SetPortFanout(p, symbol.SymbolId, port.PortId, !enabled), "已調整 Port 引線；Pin 針號與接法不變"); };
        menu.Items.Add(expand);
        var align = new MenuItem { Header = "依接線對齊接點", IsEnabled = !symbol.Locked && port.Pins.Count > 0 };
        align.Click += (_, _) => { if (ResolveInspectorDraft()) Apply(p => _service.AlignPortFanout(p, symbol.SymbolId, port.PortId),
            "已展開並對齊兩端接點；Pin 針號、CAD 原始接點與接法不變"); };
        menu.Items.Add(align);
        var settings = new MenuItem { Header = "引線間距與長度", IsEnabled = !symbol.Locked && port.Pins.Count > 0 };
        settings.Click += (_, _) => EditFanoutSpacing(symbol, port);
        menu.Items.Add(settings); return menu;
    }

    private void EditFanoutSpacing(SchematicSymbol symbol, ComponentPort port)
    {
        if (!ResolveInspectorDraft()) return;
        var panel = new StackPanel { Margin = new(16) };
        panel.Children.Add(new TextBlock { Text = "Pin 間距 (mm)" });
        var placements = symbol.EndpointDisplayPlacements.Where(p => p.PortId == port.PortId).ToArray();
        var direction = placements.Length == 0 ? "Right" : symbol.Anchors.Single(a => a.EndpointId == placements[0].EndpointId).Direction;
        var coordinates = placements.Where(p => symbol.Anchors.Single(a => a.EndpointId == p.EndpointId).Direction == direction)
            .Select(p => direction is "Left" or "Right" ? p.Position.Y : p.Position.X).Order().ToArray();
        var currentPitch = coordinates.Length < 2 ? SchematicAuthoringService.DefaultFanoutPitch : coordinates[1] - coordinates[0];
        var currentLead = placements.Length == 0 ? SchematicAuthoringService.DefaultFanoutLead : direction switch { "Left" => -placements[0].Position.X,
            "Right" => placements[0].Position.X - symbol.Width, "Top" => -placements[0].Position.Y, _ => placements[0].Position.Y - symbol.Height };
        var pitch = new TextBox { Text = currentPitch.ToString("0.###", CultureInfo.CurrentCulture) }; panel.Children.Add(pitch);
        panel.Children.Add(new TextBlock { Text = "引線長度 (mm)", Margin = new(0, 8, 0, 0) });
        var lead = new TextBox { Text = currentLead.ToString("0.###", CultureInfo.CurrentCulture) }; panel.Children.Add(lead);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) }; panel.Children.Add(error);
        var ok = new Button { Content = "套用", Margin = new(0, 8, 0, 0) }; panel.Children.Add(ok);
        var dialog = new Window { Title = port.Name + " / 引線配置", Width = 380, Height = 310, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ok.Click += (_, _) => { try { if (Apply(p => _service.SetPortFanout(p, symbol.SymbolId, port.PortId, true,
            double.Parse(pitch.Text, CultureInfo.CurrentCulture), double.Parse(lead.Text, CultureInfo.CurrentCulture)), "已調整 Port 引線；接法保留")) dialog.DialogResult = true;
            else error.Text = Status.Text; } catch (Exception exception) { error.Text = exception.Message; } };
        dialog.Content = panel; dialog.ShowDialog();
    }

    private readonly HashSet<string> _selectedWires = new(StringComparer.Ordinal);
    private Point? _marqueeStart;
    private Rectangle? _marquee;
    private bool IsWireSelected(string id) => !_renderingOutput && (id == _selectionId || _selectedWires.Contains(id));

    private void SelectWire(string id, bool additive)
    {
        if (!ResolveInspectorDraft()) return;
        if (!additive) _selectedWires.Clear();
        if (additive && _selectedWires.Contains(id)) _selectedWires.Remove(id); else _selectedWires.Add(id);
        _selectionId = _selectedWires.Contains(id) ? id : _selectedWires.LastOrDefault();
        Render(_getProject()); FocusCanvas();
    }

    private void BeginWireMarquee(MouseButtonEventArgs e)
    {
        if (!ResolveInspectorDraft()) return;
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) _selectedWires.Clear();
        _selectionId = null; Render(_getProject());
        _marqueeStart = e.GetPosition(Sheet);
        _marquee = new Rectangle { Stroke = Brushes.DarkCyan, StrokeThickness = 1,
            Fill = new SolidColorBrush(Color.FromArgb(25, 0, 130, 140)), IsHitTestVisible = false };
        Sheet.Children.Add(_marquee); Sheet.CaptureMouse(); e.Handled = true;
    }

    private void UpdateWireMarquee(Point point)
    {
        if (_marqueeStart is not { } start || _marquee is null) return;
        var bounds = new Rect(start, point);
        Canvas.SetLeft(_marquee, bounds.X); Canvas.SetTop(_marquee, bounds.Y);
        _marquee.Width = bounds.Width; _marquee.Height = bounds.Height;
    }

    private void FinishWireMarquee(Point point)
    {
        if (_marqueeStart is not { } start) return;
        var bounds = new Rect(start, point);
        if (bounds.Width > SystemParameters.MinimumHorizontalDragDistance || bounds.Height > SystemParameters.MinimumVerticalDragDistance)
            foreach (var wire in _getProject().Schematic?.Wires.Where(w => w.PageId == _pageId) ?? [])
                if (wire.Points.Zip(wire.Points.Skip(1)).Any(e => bounds.IntersectsWith(
                    new Rect(new Point(e.First.X * 3, e.First.Y * 3), new Point(e.Second.X * 3, e.Second.Y * 3)))))
                    _selectedWires.Add(wire.WireId);
        CancelWireMarquee(); _selectionId = _selectedWires.LastOrDefault(); Render(_getProject()); FocusCanvas();
    }

    private void CancelWireMarquee()
    {
        if (_marquee is not null) Sheet.Children.Remove(_marquee);
        _marqueeStart = null; _marquee = null;
        if (_gestureStart is null) Sheet.ReleaseMouseCapture();
    }

    private void BatchGauge()
    {
        if (!ResolveInspectorDraft()) return;
        var project = _getProject(); var ids = _selectedWires.ToArray();
        var panel = new StackPanel { Margin = new(16) };
        var mode = new ComboBox { ItemsSource = new[] { "手動 AWG", "手動截面積 mm²", "重新自動建議（待核對）", "沿用已核對導線規格" }, SelectedIndex = 0 };
        var awg = new ComboBox { ItemsSource = Enumerable.Range(0, 41).ToArray(), SelectedItem = 20, Margin = new(0, 8, 0, 0) };
        var area = new TextBox { Text = "0.5", Margin = new(0, 8, 0, 0) };
        var sources = new ComboBox { ItemsSource = project.Schematic!.Wires.Where(w => SchematicWireLegendPresentation.IsOrdinary(project, w) &&
            w.SizingProposal is null && SchematicWirePresentation.Resolve(project, w).AreaMm2 is not null).ToArray(),
            DisplayMemberPath = "Designation", Margin = new(0, 8, 0, 0) };
        var review = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) };
        foreach (var field in new FrameworkElement[] { mode, awg, area, sources, review }) panel.Children.Add(field);
        void State() { awg.Visibility = mode.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            area.Visibility = mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            sources.Visibility = mode.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            review.Text = string.Join("\n", project.Schematic.Wires.Where(w => ids.Contains(w.WireId)).Select(w =>
                (w.Designation ?? w.WireId) + "：" + SchematicWireLegendPresentation.Entry(project, w).Specification.Text)); }
        mode.SelectionChanged += (_, _) => State(); State();
        var dialog = new Window { Title = "批次線徑 / " + ids.Length + " 條", Width = 480, Height = 560,
            Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var ok = new Button { Content = "套用整組", Margin = new(0, 12, 0, 0) }; panel.Children.Add(ok);
        ok.Click += (_, _) =>
        {
            try
            {
                double? value = mode.SelectedIndex == 1 ? double.Parse(area.Text, CultureInfo.CurrentCulture) : null;
                if (mode.SelectedIndex == 3 && sources.SelectedItem is not SchematicWire) throw new InvalidOperationException("請選擇已核對來源。");
                if (Apply(p => ReferenceEquals(p, project) ? _service.SetWireSpecifications(p, ids, mode.SelectedIndex == 0 ? (int)awg.SelectedItem : null,
                    value, mode.SelectedIndex == 2, (sources.SelectedItem as SchematicWire)?.WireId is { } source && mode.SelectedIndex == 3 ? source : null) :
                    throw new InvalidOperationException("核對期間專案已變更，請重開批次設定。"),
                    "已套用整組線徑；可復原")) dialog.DialogResult = true;
                else review.Text = Status.Text;
            }
            catch (Exception error) { review.Text = error.Message; }
        };
        dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; dialog.ShowDialog();
    }

    private bool ReviewedEdit(Func<ElectricalProject, ElectricalProject> edit, string title, string summary)
    {
        try
        {
            var current = _getProject(); var next = edit(current);
            var before = current.Schematic!; var after = next.Schematic!;
            var impact = summary + $"\n移除 {before.Symbols.Count - after.Symbols.Count} 個圖面表示、{before.Wires.Count - after.Wires.Count} 個線段、" +
                $"{current.Connections.Count - next.Connections.Count} 條工程連線、{before.Continuations.Count - after.Continuations.Count} 對跨頁符號。" +
                "\n實體元件與線材庫存保留，可復原。";
            if (MessageBox.Show(Window.GetWindow(this), impact, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return false;
            return Apply(p => ReferenceEquals(p, current) ? next : throw new InvalidOperationException("專案已變更，請重新核對。"), title + "；可復原");
        }
        catch (Exception error) { Status.Text = "未套用：" + error.Message; return false; }
    }

    private void DeletePage_Click(object sender, RoutedEventArgs e)
    {
        if (!FinishPendingDraft()) return;
        var project = _getProject(); var doc = project.Schematic;
        var page = doc?.Pages.SingleOrDefault(p => p.PageId == _pageId); if (page is null) return;
        if (doc!.Pages.Count < 2) { Status.Text = "專案至少保留一頁。"; return; }
        var populated = doc.Symbols.Any(s => s.PageId == page.PageId) || doc.Wires.Any(w => w.PageId == page.PageId) ||
            doc.Continuations.Any(c => c.Source.PageId == page.PageId || c.Destination.PageId == page.PageId) || page.CableDetail is not null || page.InlineCableDetails.Count > 0;
        if (!populated) { Apply(p => _service.DeletePage(p, page.PageId), "已刪除空白頁；可復原"); return; }
        var panel = new StackPanel { Margin = new(16) };
        var mode = new ComboBox { ItemsSource = new[] { "內容搬到其他頁", "刪除此頁內容與相關接線" }, SelectedIndex = 0 };
        var destination = new ComboBox { ItemsSource = doc.Pages.Where(p => p.PageId != page.PageId).ToArray(), DisplayMemberPath = "Title", SelectedIndex = 0, Margin = new(0, 8, 0, 0) };
        panel.Children.Add(mode); panel.Children.Add(destination);
        mode.SelectionChanged += (_, _) => destination.IsEnabled = mode.SelectedIndex == 0;
        var dialog = new Window { Title = "刪除頁面 / " + page.Title, Width = 440, Height = 260, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var ok = new Button { Content = "核對影響", Margin = new(0, 12, 0, 0) }; panel.Children.Add(ok);
        ok.Click += (_, _) => { if (ReviewedEdit(p => _service.DeletePage(p, page.PageId,
            mode.SelectedIndex == 0 ? ((SchematicPage)destination.SelectedItem).PageId : null, mode.SelectedIndex == 1),
            "刪除頁面", mode.SelectedIndex == 0 ? "內容搬到「" + ((SchematicPage)destination.SelectedItem).Title + "」。" : "刪除此頁內容。")) dialog.DialogResult = true; };
        dialog.Content = panel; dialog.ShowDialog();
    }

    private async void RefreshCad_Click(object sender, RoutedEventArgs e)
    {
        if (!ResolveInspectorDraft()) return;
        var current = _getProject(); var symbol = current.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == _selectionId);
        if (symbol?.Geometry is null) { Status.Text = "請選取已匯入 CAD 的圖塊。"; return; }
        try
        {
            var imported = await PickCadAsset(symbol.AssetPath); if (imported is null) return;
            var choice = MessageBox.Show(Window.GetWindow(this), "是：更新相同來源的所有已放置圖塊\n否：只更新目前圖塊\n取消：保持原圖", "更新圖塊", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
            if (choice == MessageBoxResult.Cancel) return;
            var ids = current.Schematic!.Symbols.Where(s => s.SymbolId == symbol.SymbolId || choice == MessageBoxResult.Yes &&
                s.AssetSha256 == symbol.AssetSha256 && s.AssetPath == symbol.AssetPath && s.Role == symbol.Role).Select(s => s.SymbolId).ToArray();
            var cableIds = current.Schematic.Symbols.Where(s => ids.Contains(s.SymbolId) && s.CableInstanceId is not null).Select(s => s.CableInstanceId!).ToHashSet(StringComparer.Ordinal);
            ids = ids.Concat(current.Schematic.Symbols.Where(s => s.CableInstanceId is not null && cableIds.Contains(s.CableInstanceId)).Select(s => s.SymbolId)).Distinct(StringComparer.Ordinal).ToArray();
            if (MessageBox.Show(Window.GetWindow(this), "更新 " + ids.Length + " 個圖面表示，按原 CAD 接點標籤比對；缺少或重複的標籤將停止更新。\n" +
                string.Join("\n", imported.Value.Asset.Diagnostics), "核對更新範圍", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            Apply(p => ReferenceEquals(p, current) ? _service.RefreshCad(p, ids, imported.Value.Asset, imported.Value.Path) :
                throw new InvalidOperationException("讀取期間專案已變更，請重新選取。"), "已更新圖塊；接點身分與工程連線保留，可復原");
        }
        catch (Exception error) { Status.Text = "未更新：" + error.Message; }
    }
}
