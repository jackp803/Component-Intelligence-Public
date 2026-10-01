using System.Collections.ObjectModel;
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
    public CableManufacturingDraft Draft { get; }

    public CableManufacturingTableEditor(CableManufacturingDraft draft, IReadOnlyList<CableConnectorChoice> choices,
        bool allowInventoryChanges = true)
    {
        Draft = JsonSerializer.Deserialize<CableManufacturingDraft>(JsonSerializer.Serialize(draft))!;
        _allowInventoryChanges = allowInventoryChanges;
        _rows = new(Draft.Rows);
        var root = new DockPanel();
        var top = new StackPanel();
        var endsLine = new WrapPanel { Margin = new(0, 0, 0, 8) };
        _ends = new() { ItemsSource = Draft.Ends, DisplayMemberPath = "Name", Width = 100, Margin = new(0, 0, 8, 0) };
        endsLine.Children.Add(new TextBlock { Text = "接頭", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0) });
        endsLine.Children.Add(_ends);
        var connectors = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Label", Width = 310, Margin = new(0, 0, 8, 0) };
        endsLine.Children.Add(connectors);
        _count = new() { ItemsSource = Enumerable.Range(1, 64).Concat(new[] { 78, 96, 100, 128, 256, 512 }).ToArray(),
            IsEditable = true, Width = 65, IsEnabled = allowInventoryChanges };
        endsLine.Children.Add(new TextBlock { Text = "Pin", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0) });
        endsLine.Children.Add(_count);
        var applyCount = Icon("\uE72C", "套用 Pin 數 (Alt+Enter)", (_, _) => ApplyCount()) ;
        applyCount.IsEnabled = allowInventoryChanges; endsLine.Children.Add(applyCount);
        var addEnd = Icon("\uE710", "新增接頭 (Ctrl+Shift+Insert)", (_, _) => AddEnd()); addEnd.IsEnabled = allowInventoryChanges;
        var deleteEnd = Icon("\uE74D", "移除接頭 (Ctrl+Shift+Delete)", (_, _) => DeleteEnd()); deleteEnd.IsEnabled = allowInventoryChanges;
        endsLine.Children.Add(addEnd); endsLine.Children.Add(deleteEnd);
        top.Children.Add(endsLine);
        var details = new WrapPanel { Margin = new(0, 0, 0, 8) };
        var name = new TextBox { Width = 110, Margin = new(0, 0, 8, 0) };
        var gender = new ComboBox { ItemsSource = Enum.GetValues<ConnectorGender>(), Width = 110, Margin = new(0, 0, 8, 0) };
        var coding = new TextBox { Width = 90, Margin = new(0, 0, 8, 0) };
        foreach (var (label, field) in new[] { ("名稱", (FrameworkElement)name), ("公母", gender), ("Coding", coding) })
        { details.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0) }); details.Children.Add(field); }
        top.Children.Add(details);
        var toolbar = new WrapPanel { Margin = new(0, 0, 0, 8) };
        toolbar.Children.Add(Icon("\uE710", "新增接法列 (Ctrl+Insert)", (_, _) => { if (Commit()) _rows.Add(new()); }));
        toolbar.Children.Add(Icon("\uE74D", "移除接法列 (Ctrl+Delete)", (_, _) => RemoveRow()));
        foreach (var (label, from) in new[] { ("P1 狀態", true), ("P2 狀態", false) })
        {
            toolbar.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 6, 0) });
            var usage = new ComboBox { ItemsSource = Enum.GetValues<CablePinUsage>(), Width = 95, SelectedItem = CablePinUsage.Pending };
            usage.SelectionChanged += (_, _) =>
            {
                if (_grid?.SelectedItem is not CableManufacturingRow row || usage.SelectedItem is not CablePinUsage value) return;
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
        root.Children.Add(_grid); Content = root;
        var refreshing = false;
        _ends.SelectionChanged += (_, _) =>
        {
            if (_ends.SelectedItem is not ComponentPort port) return;
            refreshing = true;
            name.Text = port.Name; _count.Text = port.Pins.Count.ToString(); gender.SelectedItem = port.Connector?.Gender ?? ConnectorGender.Unknown;
            coding.Text = port.Connector?.Coding ?? "";
            connectors.SelectedItem = choices.FirstOrDefault(c => c.Source?.Connector?.ConnectorId == port.Connector?.ConnectorId) ?? choices.FirstOrDefault();
            refreshing = false;
        };
        name.TextChanged += (_, _) => { if (!refreshing && _ends.SelectedItem is ComponentPort port) { port.Name = name.Text; RefreshPins(); } };
        gender.SelectionChanged += (_, _) => { if (!refreshing && _ends.SelectedItem is ComponentPort { Connector: { } connector } && gender.SelectedItem is ConnectorGender value) connector.Gender = value; };
        coding.TextChanged += (_, _) => { if (!refreshing && _ends.SelectedItem is ComponentPort { Connector: { } connector }) connector.Coding = string.IsNullOrWhiteSpace(coding.Text) ? null : coding.Text.Trim(); };
        connectors.SelectionChanged += (_, _) =>
        {
            if (refreshing || _ends.SelectedItem is not ComponentPort port || connectors.SelectedItem is not CableConnectorChoice choice) return;
            try
            {
                if (!Commit()) return;
                var targetCount = choice.Source?.Connector?.PinCount ?? choice.Source?.Pins.Count ?? port.Pins.Count;
                if (targetCount <= 0) targetCount = port.Pins.Count;
                if (!_allowInventoryChanges && targetCount != port.Pins.Count) throw new InvalidOperationException("變更 Pin 數需建立新模板修訂，不能替換已放入線材的接點。");
                _service.SetPinCount(Draft, port.PortId, targetCount);
                var connector = choice.Source?.Connector is { } source
                    ? JsonSerializer.Deserialize<ConnectorDefinition>(JsonSerializer.Serialize(source))!
                    : new() { ConnectorId = "custom-" + port.PortId, Family = "Custom", PinCount = port.Pins.Count };
                port.Connector = connector;
                refreshing = true;
                gender.SelectedItem = connector.Gender; coding.Text = connector.Coding ?? "";
                refreshing = false;
                _count.Text = port.Pins.Count.ToString();
                ReloadRows(); RefreshPins(); RefreshStatus();
            }
            catch (Exception error) { _error.Text = error.Message; }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Insert) { if (Commit()) _rows.Add(new()); e.Handled = true; }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Delete) { RemoveRow(); e.Handled = true; }
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Insert) { AddEnd(); e.Handled = true; }
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Delete) { DeleteEnd(); e.Handled = true; }
            if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.Return) { ApplyCount(); e.Handled = true; }
        };
        RefreshPins(); _ends.SelectedIndex = 0; RefreshStatus();
    }

    public bool Commit()
    {
        if (!_grid.CommitEdit(DataGridEditingUnit.Cell, true) || !_grid.CommitEdit(DataGridEditingUnit.Row, true)) return false;
        Draft.Rows = _rows.ToList(); return true;
    }

    private void ApplyCount()
    {
        if (!_allowInventoryChanges || !Commit() || _ends.SelectedItem is not ComponentPort port) return;
        try
        {
            if (!int.TryParse(_count.Text, out var count)) throw new InvalidOperationException("Pin 數請輸入整數。");
            _service.SetPinCount(Draft, port.PortId, count); ReloadRows(); RefreshPins(); RefreshStatus();
        }
        catch (Exception error) { _error.Text = error.Message; }
    }

    private void AddEnd()
    {
        if (!_allowInventoryChanges || !Commit()) return;
        var id = "end-" + Guid.NewGuid().ToString("N");
        var port = new ComponentPort { PortId = id, Name = "P" + (Draft.Ends.Count + 1), Pins = [new() { PinId = id + ".1", PinNumber = "1" }] };
        Draft.Ends.Add(port); Draft.Rows.Add(new() { FromSourcePinId = port.Pins[0].PinId });
        _ends.Items.Refresh(); _ends.SelectedItem = port; ReloadRows(); RefreshPins();
    }

    private void DeleteEnd()
    {
        if (!_allowInventoryChanges || !Commit() || _ends.SelectedItem is not ComponentPort port) return;
        var ids = port.Pins.Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        if (Draft.Ends.Count == 1 || Draft.Rows.Any(r => r.FromSourcePinId is not null && ids.Contains(r.FromSourcePinId) || r.ToSourcePinId is not null && ids.Contains(r.ToSourcePinId)))
        { _error.Text = "接頭仍有表格接點，或已是最後一端；請先明確處理接法列。"; return; }
        Draft.Ends.Remove(port);
        if (Draft.FromSourcePortId == port.PortId) Draft.FromSourcePortId = Draft.Ends.First().PortId;
        if (Draft.ToSourcePortId == port.PortId) Draft.ToSourcePortId = Draft.Ends.Skip(1).FirstOrDefault()?.PortId;
        _ends.Items.Refresh(); _ends.SelectedIndex = 0; RefreshPins();
    }

    private void RemoveRow() { if (Commit() && _grid.SelectedItem is CableManufacturingRow row) { _rows.Remove(row); RefreshStatus(); } }
    private void ReloadRows() { _rows.Clear(); foreach (var row in Draft.Rows) _rows.Add(row); }
    private void RefreshPins()
    {
        var pins = new[] { new PinChoice(null, "未指定") }.Concat(Draft.Ends.SelectMany(p => p.Pins.Select(pin =>
            new PinChoice(pin.PinId, $"{p.Name} / {pin.PinNumber} {pin.PinName}".Trim())))).ToArray();
        foreach (var column in _grid.Columns.OfType<DataGridComboBoxColumn>()) column.ItemsSource = pins;
    }
    private void RefreshStatus()
    {
        if (!Commit()) return;
        var errors = _service.Validate(Draft);
        _error.Text = errors.Count > 0 ? string.Join("\n", errors) :
            $"接法 {Draft.Rows.Count(r => r.FromSourcePinId is not null && r.ToSourcePinId is not null)}；未完成列 {Draft.Rows.Count(r => r.FromSourcePinId is null || r.ToSourcePinId is null)}";
    }
    private static DataGridTextColumn TextColumn(string header, string path) => new() { Header = header,
        Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new(1, DataGridLengthUnitType.Star) };
    private static DataGridComboBoxColumn PinColumn(string header, string path) => new() { Header = header, DisplayMemberPath = "Label", SelectedValuePath = "Id",
        SelectedValueBinding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new(1, DataGridLengthUnitType.Star) };
    private static Button Icon(string glyph, string tooltip, RoutedEventHandler handler)
    {
        var button = new Button { FontFamily = new("Segoe MDL2 Assets"), Content = glyph, ToolTip = tooltip, Padding = new(9, 5, 9, 5), Margin = new(4, 0, 0, 0) };
        button.Click += handler; return button;
    }
}
