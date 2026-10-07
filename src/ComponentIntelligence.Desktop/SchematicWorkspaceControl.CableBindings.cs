using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private sealed class CableContactRow
    {
        public required string SourcePinId { get; init; }
        public required string Label { get; init; }
        public string Kind { get; init; } = "Pin";
        public string? ContactId { get; set; }
    }

    private void EditCableBindings(SchematicSymbol symbol)
    {
        if (symbol.Locked) { Status.Text = "請先解鎖線材表示。"; return; }
        var cable = SchematicSymbolOwner.Resolve(_getProject(), symbol).Cable!;
        var rows = cable.ArchivedCable!.Ports.SelectMany(p => new[] { new CableContactRow {
            SourcePinId = p.SourcePortId!, Kind = "Port", Label = $"Port: {p.Name} ({p.Pins.Count} Pin)",
            ContactId = symbol.CadPortBindings.SingleOrDefault(b => b.PortId == p.PortId)?.CadContactId } }.Concat(p.Pins.Select(pin => new CableContactRow {
            SourcePinId = pin.SourcePinId!, Label = $"{p.Name} / {pin.PinNumber} {pin.PinName}".Trim(),
            ContactId = symbol.Anchors.SingleOrDefault(a => a.EndpointId == pin.PinId)?.CadContactId }))).ToList();
        var dialog = new Window { Title = "線材對外接點綁定", Width = 720, Height = 440, MinWidth = 550, MinHeight = 300,
            Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new(14) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 8, 0, 0) };
        var clear = new Button { Content = "清除所選綁定", Padding = new(10, 5, 10, 5) };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new(10, 5, 10, 5), Margin = new(8, 0, 0, 0) };
        var apply = new Button { Content = "套用", Padding = new(10, 5, 10, 5), Margin = new(8, 0, 0, 0) };
        footer.Children.Add(clear); footer.Children.Add(cancel); footer.Children.Add(apply);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick };
        DockPanel.SetDock(error, Dock.Bottom); root.Children.Add(error);
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false };
        var kind = new ComboBox { ItemsSource = new[] { "Port", "Pin", "全部" }, SelectedIndex = 0, Width = 110, Margin = new(0, 0, 0, 8) };
        void Filter() { CollectionViewSource.GetDefaultView(rows).Filter = item => kind.SelectedItem?.ToString() == "全部" ||
            item is CableContactRow row && row.Kind == kind.SelectedItem?.ToString(); }
        kind.SelectionChanged += (_, _) => { if (grid.CommitEdit(DataGridEditingUnit.Cell, true) && grid.CommitEdit(DataGridEditingUnit.Row, true)) Filter(); };
        DockPanel.SetDock(kind, Dock.Top); root.Children.Add(kind); Filter();
        grid.Columns.Add(new DataGridTextColumn { Header = "Port / Pin", Binding = new Binding("Label"), IsReadOnly = true, Width = new(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "CAD 接點（未選取即待綁定）", ItemsSource = symbol.Geometry!.ConnectionPoints,
            DisplayMemberPath = "Tag", SelectedValuePath = "Tag", SelectedValueBinding = new Binding("ContactId") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 270 });
        clear.Click += (_, _) =>
        {
            if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) return;
            if (grid.SelectedItem is CableContactRow row) { row.ContactId = null; grid.Items.Refresh(); }
        };
        apply.Click += (_, _) =>
        {
            if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) return;
            try
            {
                var bindings = rows.Where(r => r.ContactId is not null).ToDictionary(r => r.SourcePinId, r => r.ContactId!, StringComparer.Ordinal);
                var next = _service.SetCableContactBindings(_getProject(), symbol.SymbolId, bindings);
                if (Apply(_ => next, "已更新線材對外接點；內部 mapping 不變")) dialog.DialogResult = true;
            }
            catch (Exception exception) { error.Text = exception.Message; }
        };
        root.Children.Add(grid); dialog.Content = root; dialog.ShowDialog();
    }
}
