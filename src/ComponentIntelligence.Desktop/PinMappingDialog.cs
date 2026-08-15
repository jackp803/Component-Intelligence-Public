using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public sealed class PinMappingDialog : Window
{
    private readonly ComponentPort _from;
    private readonly ComponentPort _to;
    private readonly ObservableCollection<MappingRow> _rows = new();
    private readonly DataGrid _grid = new();

    public PinMappingDialog(ComponentPort from, ComponentPort to, IReadOnlyList<PinMappingEntry> existing)
    {
        _from = from ?? throw new ArgumentNullException(nameof(from));
        _to = to ?? throw new ArgumentNullException(nameof(to));

        Title = "Pin Mapping / 腳位映射";
        Width = 760;
        Height = 580;
        MinWidth = 650;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        foreach (var mapping in existing)
        {
            _rows.Add(new MappingRow
            {
                FromPinNumber = PinNumber(_from, mapping.FromPinId),
                ToPinNumber = PinNumber(_to, mapping.ToPinId),
                CoreId = mapping.CoreId,
                Signal = mapping.Signal,
                Layer = mapping.Layer
            });
        }

        var root = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = $"A: {_from.Name}  →  B: {_to.Name}",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "只建立有證據或人工確認的映射。系統不會因 Pin 號相同而自動假設 1→1、2→2。留空即代表 Unknown（未知）。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "取消", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var apply = new Button { Content = "套用 Pin Mapping", Padding = new Thickness(16, 7, 16, 7), IsDefault = true };
        apply.Click += Apply_Click;
        footer.Children.Add(cancel);
        footer.Children.Add(apply);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var add = new Button { Content = "+ Mapping", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        var remove = new Button { Content = "移除選取", Padding = new Thickness(12, 5, 12, 5) };
        add.Click += (_, _) => _rows.Add(new MappingRow());
        remove.Click += (_, _) => { if (_grid.SelectedItem is MappingRow row) _rows.Remove(row); };
        tools.Children.Add(add);
        tools.Children.Add(remove);
        DockPanel.SetDock(tools, Dock.Top);
        root.Children.Add(tools);

        _grid.ItemsSource = _rows;
        _grid.AutoGenerateColumns = false;
        _grid.CanUserAddRows = false;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = $"A Pin ({_from.Name})",
            ItemsSource = _from.Pins.Select(pin => pin.PinNumber).ToArray(),
            SelectedItemBinding = new System.Windows.Data.Binding(nameof(MappingRow.FromPinNumber)) { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
            Width = 130
        });
        _grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = $"B Pin ({_to.Name})",
            ItemsSource = _to.Pins.Select(pin => pin.PinNumber).ToArray(),
            SelectedItemBinding = new System.Windows.Data.Binding(nameof(MappingRow.ToPinNumber)) { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
            Width = 130
        });
        _grid.Columns.Add(TextColumn("Core / 芯線", nameof(MappingRow.CoreId), 110));
        _grid.Columns.Add(TextColumn("Signal / 訊號", nameof(MappingRow.Signal), 160));
        _grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Layer / 圖層",
            ItemsSource = Enum.GetValues<ElectricalLayer>(),
            SelectedItemBinding = new System.Windows.Data.Binding(nameof(MappingRow.Layer)) { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
            Width = 130
        });
        root.Children.Add(_grid);
        Content = root;
    }

    public IReadOnlyList<PinMappingEntry> ResultMappings { get; private set; } = [];

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        _grid.CommitEdit(DataGridEditingUnit.Cell, true);
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        var completed = _rows.Where(row => !string.IsNullOrWhiteSpace(row.FromPinNumber) || !string.IsNullOrWhiteSpace(row.ToPinNumber)).ToArray();
        if (completed.Any(row => string.IsNullOrWhiteSpace(row.FromPinNumber) || string.IsNullOrWhiteSpace(row.ToPinNumber)))
        {
            MessageBox.Show(this, "每一列 Mapping 都必須同時選 A Pin 與 B Pin；不需要的列請移除。", "Pin Mapping 未完成", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var mappings = new List<PinMappingEntry>();
        foreach (var row in completed)
        {
            var fromPin = _from.Pins.First(pin => string.Equals(pin.PinNumber, row.FromPinNumber, StringComparison.OrdinalIgnoreCase));
            var toPin = _to.Pins.First(pin => string.Equals(pin.PinNumber, row.ToPinNumber, StringComparison.OrdinalIgnoreCase));
            mappings.Add(new PinMappingEntry(fromPin.PinId, toPin.PinId, Blank(row.CoreId), Blank(row.Signal), row.Layer));
        }
        ResultMappings = mappings;
        DialogResult = true;
    }

    private static string? PinNumber(ComponentPort port, string pinId) =>
        port.Pins.FirstOrDefault(pin => string.Equals(pin.PinId, pinId, StringComparison.OrdinalIgnoreCase))?.PinNumber;

    private static DataGridTextColumn TextColumn(string header, string path, double width) => new()
    {
        Header = header,
        Binding = new System.Windows.Data.Binding(path) { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
        Width = new DataGridLength(width)
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class MappingRow
    {
        public string? FromPinNumber { get; set; }
        public string? ToPinNumber { get; set; }
        public string? CoreId { get; set; }
        public string? Signal { get; set; }
        public ElectricalLayer Layer { get; set; } = ElectricalLayer.Unknown;
    }
}
