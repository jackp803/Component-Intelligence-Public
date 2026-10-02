using System.Collections.ObjectModel;
using System.Collections;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public sealed record CableConnectorChoice(string Label, ComponentPort? Source);

public sealed class CableManufacturingTableEditor : UserControl
{
    private sealed record PinChoice(string? Id, string Label);
    private readonly CableManufacturingEditorService _service = new();
    private readonly ObservableCollection<CableManufacturingRow> _rows;
    private readonly DataGrid _grid;
    private readonly ComboBox _ends;
    private readonly ComboBox _count;
    private readonly TextBlock _error;
    private readonly bool _allowInventoryChanges;
    private readonly Dictionary<bool, ComboBox> _usages = [];
    private readonly Func<IReadOnlySet<string>>? _boundPins;
    private readonly Func<string, bool> _confirmRemoval;
    private readonly ComboBox _connectors;
    private readonly ComboBox _coding;
    private readonly ComboBox _gender;
    private readonly TextBox _name;
    private bool _refreshing;
    private string _codingBaselineText = "";
    private bool _syncingUsage;
    private bool _sortFrom = true, _sortDescending;
    private bool _pinSortActive = true;
    public CableManufacturingDraft Draft { get; }
    public event Action<IReadOnlySet<string>>? PinsRemoved;

    public CableManufacturingTableEditor(CableManufacturingDraft draft, IReadOnlyList<CableConnectorChoice> choices,
        bool allowInventoryChanges = true, Func<IReadOnlySet<string>>? boundPins = null, Func<string, bool>? confirmRemoval = null)
    {
        Draft = JsonSerializer.Deserialize<CableManufacturingDraft>(JsonSerializer.Serialize(draft))!;
        _allowInventoryChanges = allowInventoryChanges;
        _boundPins = boundPins;
        _confirmRemoval = confirmRemoval ?? (message => MessageBox.Show(Window.GetWindow(this), message, "確認移除接頭／Pin",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
        _rows = new(CableManufacturingEditorService.SortRows(Draft));
        var root = new DockPanel();
        var top = new StackPanel();
        var endsLine = new WrapPanel { Margin = new(0, 0, 0, 8) };
        _ends = new() { ItemsSource = Draft.Ends, DisplayMemberPath = "Name", Width = 100, Margin = new(0, 0, 8, 0), ToolTip = "接頭端" };
        endsLine.Children.Add(new TextBlock { Text = "接頭端", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0) });
        endsLine.Children.Add(_ends);
        var addEnd = Icon("\uE710", "新增接頭端 (Ctrl+Shift+Insert)", (_, _) => AddEnd()); addEnd.IsEnabled = allowInventoryChanges;
        var deleteEnd = Icon("\uE74D", "移除接頭端 (Ctrl+Shift+Delete)", (_, _) => DeleteEnd()); deleteEnd.IsEnabled = allowInventoryChanges;
        endsLine.Children.Add(addEnd); endsLine.Children.Add(deleteEnd);
        top.Children.Add(endsLine);
        var connectorLine = new WrapPanel { Margin = new(0, 0, 0, 8) };
        var knownFamilies = choices.Select(c => c.Source?.Connector?.Family ?? "Custom").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var preserved = Draft.Ends.Select(p => p.Connector?.Family).Where(f => !string.IsNullOrWhiteSpace(f) && !knownFamilies.Contains(f!))
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(f => new CableConnectorChoice(f!, new() { PortId = "family-" + f, Name = f!,
                Connector = new() { ConnectorId = "family-" + f, Family = f! } }));
        _connectors = new() { ItemsSource = choices.Concat(preserved).ToArray(), DisplayMemberPath = "Label", Width = 160,
            Margin = new(0, 0, 8, 0), ToolTip = "接頭形式" };
        connectorLine.Children.Add(new TextBlock { Text = "形式", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0) });
        connectorLine.Children.Add(_connectors);
        _coding = new() { IsEditable = true, Width = 120, Margin = new(0, 0, 8, 0), ToolTip = "接頭 Coding" };
        connectorLine.Children.Add(new TextBlock { Text = "Coding", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0) });
        connectorLine.Children.Add(_coding);
        _count = new() { ItemsSource = Enumerable.Range(1, 64).Concat(new[] { 78, 96, 100, 128, 256, 512 }).ToArray(),
            IsEditable = true, IsTextSearchEnabled = false, Width = 65, IsEnabled = allowInventoryChanges };
        _count.ToolTip = "Pin 數 (Alt+Enter)";
        connectorLine.Children.Add(new TextBlock { Text = "Pin 數", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0) });
        connectorLine.Children.Add(_count);
        top.Children.Add(connectorLine);
        var details = new WrapPanel { Margin = new(0, 0, 0, 8) };
        _name = new() { Width = 110, Margin = new(0, 0, 8, 0) };
        _gender = new() { ItemsSource = Enum.GetValues<ConnectorGender>(), Width = 110, Margin = new(0, 0, 8, 0) };
        foreach (var (label, field) in new[] { ("名稱", (FrameworkElement)_name), ("公母", _gender) })
        { details.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0) }); details.Children.Add(field); }
        top.Children.Add(details);
        details.Children.Add(Icon("\uE70F", "編輯腳位標示 (Ctrl+Alt+P)", (_, _) => EditPinLabels()));
        var toolbar = new WrapPanel { Margin = new(0, 0, 0, 8) };
        toolbar.Children.Add(Icon("\uE710", "新增接法列 (Ctrl+Insert)", (_, _) => { if (Commit()) _rows.Add(new()); }));
        toolbar.Children.Add(Icon("\uE74D", "移除接法列 (Ctrl+Delete)", (_, _) => RemoveRow()));
        foreach (var (label, from) in new[] { ("P1 狀態", true), ("P2 狀態", false) })
        {
            toolbar.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 6, 0) });
            var usage = new ComboBox { ItemsSource = Enum.GetValues<CablePinUsage>(), Width = 95, SelectedItem = CablePinUsage.Pending };
            _usages[from] = usage;
            usage.SelectionChanged += (_, _) =>
            {
                if (_syncingUsage || _grid?.SelectedItem is not CableManufacturingRow row || usage.SelectedItem is not CablePinUsage value) return;
                if (from) row.FromUsage = value; else row.ToUsage = value;
                _grid.Items.Refresh(); RefreshStatus();
            };
            toolbar.Children.Add(usage);
        }
        top.Children.Add(toolbar); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
        DockPanel.SetDock(_error, Dock.Bottom); root.Children.Add(_error);
        _grid = new() { ItemsSource = _rows, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single, MinHeight = 140, MinColumnWidth = 120 };
        _grid.Columns.Add(TextColumn("P1 FUNCTION", "FromFunction"));
        _grid.Columns.Add(PinColumn("P1 PIN", "FromSourcePinId"));
        _grid.Columns.Add(PinColumn("P2 PIN", "ToSourcePinId"));
        _grid.Columns.Add(TextColumn("P2 FUNCTION", "ToFunction"));
        _grid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(new Action(RefreshStatus));
        _grid.SelectionChanged += (_, _) => SyncUsages();
        _grid.Sorting += (_, e) =>
        {
            if (e.Column.SortMemberPath is not ("FromSourcePinId" or "ToSourcePinId")) { _pinSortActive = false; return; }
            e.Handled = true;
            if (!Commit()) return;
            _pinSortActive = true;
            _sortFrom = e.Column.SortMemberPath == "FromSourcePinId";
            _sortDescending = e.Column.SortDirection == ListSortDirection.Ascending;
            foreach (var column in _grid.Columns) column.SortDirection = null;
            e.Column.SortDirection = _sortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            SortView();
        };
        root.Children.Add(_grid); Content = root;
        _ends.SelectionChanged += (_, _) => RefreshEnd();
        _name.TextChanged += (_, _) => { if (!_refreshing && _ends.SelectedItem is ComponentPort port) { port.Name = _name.Text; RefreshPins(); } };
        _gender.SelectionChanged += (_, _) => { if (!_refreshing && _ends.SelectedItem is ComponentPort port && _gender.SelectedItem is ConnectorGender value) EnsureConnector(port).Gender = value; };
        _coding.SelectionChanged += (_, _) => { if (!_refreshing) ApplyCoding(_coding.SelectedItem as string ?? _coding.Text); };
        _coding.LostKeyboardFocus += (_, _) => { if (!_refreshing) ApplyCoding(_coding.Text); };
        _connectors.SelectionChanged += (_, _) =>
        {
            if (_refreshing || _ends.SelectedItem is not ComponentPort port || _connectors.SelectedItem is not CableConnectorChoice choice) return;
            try
            {
                if (!Commit()) return;
                if (choice.Source is { Pins.Count: > 0 } source)
                {
                    _service.SetConnector(Draft, port.PortId, source, _boundPins?.Invoke(), _allowInventoryChanges);
                    Draft.ConfirmMapping = false;
                    ReloadRows(); RefreshPins(); RefreshEnd(); RefreshStatus();
                    return;
                }
                var connector = EnsureConnector(port); var family = choice.Source?.Connector?.Family ?? "Custom";
                if (!string.Equals(connector.Family, family, StringComparison.OrdinalIgnoreCase))
                { connector.Family = family; connector.Coding = null; Draft.ConfirmMapping = false; }
                connector.PinCount = port.Pins.Count;
                RefreshEnd(); RefreshStatus();
            }
            catch (Exception error) { _error.Text = error.Message; }
        };
        _count.SelectionChanged += (_, _) => { if (!_refreshing && _count.SelectedItem is int count) ApplyCount(count); };
        _count.LostKeyboardFocus += (_, _) => { if (!_refreshing) ApplyCount(); };
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Insert) { if (Commit()) _rows.Add(new()); e.Handled = true; }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Delete) { RemoveRow(); e.Handled = true; }
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Insert) { AddEnd(); e.Handled = true; }
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Delete) { DeleteEnd(); e.Handled = true; }
            if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.Return) { ApplyCount(); e.Handled = true; }
            if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Return && _count.IsKeyboardFocusWithin) { ApplyCount(); e.Handled = true; }
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && e.Key == Key.P) { EditPinLabels(); e.Handled = true; }
        };
        RefreshPins(); _ends.SelectedIndex = 0; RefreshStatus();
    }

    private static ConnectorDefinition EnsureConnector(ComponentPort port) => port.Connector ??=
        new() { ConnectorId = "custom-" + port.PortId, Family = "Custom", PinCount = port.Pins.Count };

    private void RefreshEnd()
    {
        if (_ends.SelectedItem is not ComponentPort port) return;
        _refreshing = true;
        try
        {
            _name.Text = port.Name;
            _count.SelectedItem = _count.Items.Cast<int>().Contains(port.Pins.Count) ? port.Pins.Count : null;
            _count.Text = port.Pins.Count.ToString();
            _gender.SelectedItem = port.Connector?.Gender ?? ConnectorGender.Unknown;
            var family = port.Connector?.Family ?? "Custom";
            _connectors.SelectedItem = _connectors.Items.Cast<CableConnectorChoice>()
                .FirstOrDefault(c => string.Equals(c.Source?.Connector?.Family ?? "Custom", family, StringComparison.OrdinalIgnoreCase));
            var coding = port.Connector?.Coding;
            var label = string.IsNullOrWhiteSpace(coding) ? "未確認" : coding.Length == 1 ? coding + "-code" : coding;
            _coding.ItemsSource = CableConnectorCatalog.CodingOptions(family).Append(label).Distinct().ToArray();
            _coding.SelectedItem = label; _coding.Text = label;
            _codingBaselineText = label;
        }
        finally { _refreshing = false; }
    }

    private void ApplyCoding(string? text)
    {
        if (_ends.SelectedItem is not ComponentPort port) return;
        if (text == _codingBaselineText) return;
        var value = string.IsNullOrWhiteSpace(text) || text == "未確認" ? null : text.Trim();
        if (value?.EndsWith("-code", StringComparison.OrdinalIgnoreCase) == true) value = value[..^5];
        _codingBaselineText = text ?? "";
        if (port.Connector is null && value is null) return;
        var connector = EnsureConnector(port);
        if (connector.Coding != value) { connector.Coding = value; Draft.ConfirmMapping = false; }
    }

    private void SyncUsages()
    {
        _syncingUsage = true;
        try
        {
            var row = _grid.SelectedItem as CableManufacturingRow;
            foreach (var (from, selector) in _usages)
            {
                selector.IsEnabled = row is not null;
                selector.SelectedItem = row is null ? CablePinUsage.Pending : from ? row.FromUsage : row.ToUsage;
            }
        }
        finally { _syncingUsage = false; }
    }

    private void EditPinLabels()
    {
        if (!Commit() || _ends.SelectedItem is not ComponentPort port) return;
        var pins = JsonSerializer.Deserialize<List<ComponentPin>>(JsonSerializer.Serialize(port.Pins))!;
        var grid = new DataGrid { ItemsSource = pins, AutoGenerateColumns = false, CanUserAddRows = false,
            CanUserDeleteRows = false, MinColumnWidth = 140 };
        grid.Columns.Add(new DataGridTextColumn { Header = "來源 Pin ID", Binding = new Binding("PinId"), IsReadOnly = true });
        grid.Columns.Add(TextColumn("腳位號碼", "PinNumber")); grid.Columns.Add(TextColumn("腳位名稱", "PinName"));
        var dialog = new Window { Title = port.Name + " / 腳位標示", Width = 660, Height = 420,
            Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new(12) }; var footer = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right }; var error = new TextBlock { Foreground = Brushes.Firebrick };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new(12, 5, 12, 5) };
        var apply = new Button { Content = "套用", Padding = new(12, 5, 12, 5), Margin = new(8, 0, 0, 0) };
        apply.Click += (_, _) =>
        {
            if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) return;
            if (pins.Any(p => string.IsNullOrWhiteSpace(p.PinNumber)) || pins.Select(p => p.PinNumber.Trim()).Distinct(StringComparer.Ordinal).Count() != pins.Count)
            { error.Text = "腳位號碼不能空白或重複。"; return; }
            for (var i = 0; i < pins.Count; i++) { port.Pins[i].PinNumber = pins[i].PinNumber.Trim(); port.Pins[i].PinName = pins[i].PinName; }
            RefreshPins(); dialog.DialogResult = true;
        };
        footer.Children.Add(cancel); footer.Children.Add(apply); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        DockPanel.SetDock(error, Dock.Bottom); root.Children.Add(error); root.Children.Add(grid); dialog.Content = root; dialog.ShowDialog();
    }

    public bool Commit()
    {
        if (!_grid.CommitEdit(DataGridEditingUnit.Cell, true) || !_grid.CommitEdit(DataGridEditingUnit.Row, true)) return false;
        Draft.Rows = _rows.ToList();
        if (!_refreshing) ApplyCoding(_coding.Text);
        return true;
    }

    private void ApplyCount(int? requested = null)
    {
        if (!_allowInventoryChanges || !Commit() || _ends.SelectedItem is not ComponentPort port) return;
        try
        {
            if (!int.TryParse(requested?.ToString() ?? _count.Text, out var count)) throw new InvalidOperationException("Pin 數請輸入整數。");
            if (count is < 1 or > 512) throw new InvalidOperationException("Pin 數需介於 1 到 512。");
            if (count == port.Pins.Count) { RefreshEnd(); return; }
            var removed = port.Pins.Skip(Math.Max(0, count)).Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
            var bound = _boundPins?.Invoke();
            if (!ConfirmRemoval(port, removed, bound)) { RefreshEnd(); return; }
            _service.SetPinCount(Draft, port.PortId, count, bound, confirmRemoval: true);
            PinsRemoved?.Invoke(removed);
            ReloadRows(); RefreshPins(); RefreshEnd(); RefreshStatus();
        }
        catch (Exception error) { RefreshEnd(); _error.Text = error.Message; }
    }

    private void AddEnd()
    {
        if (!_allowInventoryChanges || !Commit()) return;
        var port = _service.AddEnd(Draft);
        _ends.Items.Refresh(); _ends.SelectedItem = port; ReloadRows(); RefreshPins(); RefreshStatus();
    }

    private void DeleteEnd()
    {
        if (!_allowInventoryChanges || !Commit() || _ends.SelectedItem is not ComponentPort port) return;
        var ids = port.Pins.Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        if (Draft.Ends.Count == 1) { _error.Text = "至少保留一個接頭端。"; return; }
        try
        {
            var bound = _boundPins?.Invoke();
            if (!ConfirmRemoval(port, ids, bound)) return;
            _service.RemoveEnd(Draft, port.PortId, bound, confirmRemoval: true);
            PinsRemoved?.Invoke(ids);
            ReloadRows(); _ends.Items.Refresh(); _ends.SelectedIndex = 0; RefreshPins(); RefreshStatus();
        }
        catch (Exception error) { _error.Text = error.Message; }
    }

    private bool ConfirmRemoval(ComponentPort port, IReadOnlySet<string> pins, IReadOnlySet<string>? bound)
    {
        if (!CableManufacturingEditorService.HasRemovalImpact(Draft, pins, bound)) return true;
        var affectedRows = Draft.Rows.Count(r => r.FromSourcePinId is { } f && pins.Contains(f) || r.ToSourcePinId is { } t && pins.Contains(t));
        return _confirmRemoval($"{port.Name}：移除 {string.Join(", ", port.Pins.Where(p => pins.Contains(p.PinId)).Select(p => p.PinNumber))}\n" +
            $"受影響接法列：{affectedRows}；CAD 綁定：{bound?.Count(pins.Contains) ?? 0}\n" +
            "將解除這些 Pin 的接法、功能、狀態及 CAD 綁定，保留對端資料並改為待確認。確定移除？");
    }

    private void RemoveRow() { if (Commit() && _grid.SelectedItem is CableManufacturingRow row) { _rows.Remove(row); RefreshStatus(); } }
    private void ReloadRows() { _rows.Clear(); foreach (var row in CableManufacturingEditorService.SortRows(Draft)) _rows.Add(row); }
    private void SortView()
    {
        if (!_pinSortActive) return;
        var order = CableManufacturingEditorService.SortRows(Draft, _sortFrom, _sortDescending)
            .Select((row, i) => (row, i)).ToDictionary(p => p.row, p => p.i);
        ((ListCollectionView)CollectionViewSource.GetDefaultView(_rows)).CustomSort = new RowOrderComparer(order);
    }
    private sealed class RowOrderComparer(IReadOnlyDictionary<CableManufacturingRow, int> order) : IComparer
    {
        public int Compare(object? x, object? y) => order.GetValueOrDefault((CableManufacturingRow)x!, int.MaxValue)
            .CompareTo(order.GetValueOrDefault((CableManufacturingRow)y!, int.MaxValue));
    }
    private void RefreshPins()
    {
        var pins = new[] { new PinChoice(null, "未指定") }.Concat(Draft.Ends.SelectMany(p => p.Pins
            .OrderBy(pin => pin.PinNumber, ComponentIntelligence.Electrical.Drawing.DrawingContactDisplayOrder.NumberComparer).Select(pin =>
            new PinChoice(pin.PinId, $"{p.Name} / {pin.PinNumber} {pin.PinName}".Trim())))).ToArray();
        foreach (var column in _grid.Columns.OfType<DataGridComboBoxColumn>()) column.ItemsSource = pins;
    }
    private void RefreshStatus()
    {
        if (!Commit()) return;
        var errors = _service.Validate(Draft);
        SortView();
        _error.Text = errors.Count > 0 ? string.Join("\n", errors) :
            $"接法 {Draft.Rows.Count(r => r.FromSourcePinId is not null && r.ToSourcePinId is not null)}；未完成列 {Draft.Rows.Count(r => r.FromSourcePinId is null || r.ToSourcePinId is null)}";
    }
    private static DataGridTextColumn TextColumn(string header, string path) => new() { Header = header,
        Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new(1, DataGridLengthUnitType.Star) };
    private static DataGridComboBoxColumn PinColumn(string header, string path) => new() { Header = header, DisplayMemberPath = "Label", SelectedValuePath = "Id", SortMemberPath = path,
        SelectedValueBinding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new(1, DataGridLengthUnitType.Star) };
    private static Button Icon(string glyph, string tooltip, RoutedEventHandler handler)
    {
        var button = new Button { FontFamily = new("Segoe MDL2 Assets"), Content = glyph, ToolTip = tooltip, Padding = new(9, 5, 9, 5), Margin = new(4, 0, 0, 0) };
        button.Click += handler; return button;
    }
}
