using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Desktop;

/// <summary>
/// Human correction editor for reusable component knowledge.
/// Project/runtime fields (reference, topology X/Y, current connections) are intentionally absent.
/// </summary>
public sealed class ComponentManualEditorDialog : Window
{
    private readonly ComponentIR _original;
    private readonly TextBox _category = new();
    private readonly TextBox _subcategory = new();
    private readonly TextBox _connectorFamily = new();
    private readonly TextBox _connectorCoding = new();
    private readonly TextBox _connectorPins = new();
    private readonly TextBox _imageUrl = new();
    private readonly TextBox _productUrl = new();
    private readonly TextBox _datasheetUrl = new();
    private readonly TextBox _cadUrl = new();
    private readonly ObservableCollection<PortEditRow> _ports;
    private readonly ObservableCollection<PinEditRow> _pins;
    private readonly ObservableCollection<SpecificationEditRow> _specifications;

    public ComponentManualEditorDialog(ComponentIR component)
    {
        _original = component ?? throw new ArgumentNullException(nameof(component));
        _ports = new ObservableCollection<PortEditRow>(component.Ports.Select(PortEditRow.From));
        _pins = new ObservableCollection<PinEditRow>(component.Pins.Select(PinEditRow.From));
        _specifications = new ObservableCollection<SpecificationEditRow>(component.Specifications.Select(SpecificationEditRow.From));

        Title = $"人工修正元件資料 / Edit Component - {component.Identity.Manufacturer} {component.Identity.Model}";
        Width = 980;
        Height = 760;
        MinWidth = 820;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _category.Text = component.Classification.Category ?? string.Empty;
        _subcategory.Text = component.Classification.Subcategory ?? string.Empty;
        _connectorFamily.Text = component.Connector.Family ?? string.Empty;
        _connectorCoding.Text = component.Connector.Coding ?? string.Empty;
        _connectorPins.Text = component.Connector.Pins?.ToString() ?? string.Empty;
        _imageUrl.Text = component.Assets.ImageUrl?.AbsoluteUri ?? string.Empty;
        _productUrl.Text = component.Assets.ProductPageUrl?.AbsoluteUri ?? string.Empty;
        _datasheetUrl.Text = component.Assets.DatasheetUrl?.AbsoluteUri ?? string.Empty;
        _cadUrl.Text = component.Assets.CadUrl?.AbsoluteUri ?? string.Empty;

        var root = new DockPanel { Margin = new Thickness(16) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "取消 / Cancel", Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var save = new Button { Content = "儲存本機修正 / Save", Padding = new Thickness(16, 7, 16, 7), IsDefault = true };
        save.Click += Save_Click;
        footer.Children.Add(cancel);
        footer.Children.Add(save);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = $"{component.Identity.Manufacturer}  {component.Identity.Model}",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "只編輯跨專案可重用的元件知識。Reference、Topology 位置、目前專案接線不會在此修改。人工修改的 Pin / Specification 會留下 UserConfirmed（使用者確認）證據；同步 Notion 時若撞到 Verified（已驗證）資料會進 Conflict，不會直接覆蓋。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "基本資料 / Basic", Content = BuildBasicPanel() });
        tabs.Items.Add(new TabItem { Header = "Ports / 接口", Content = BuildPortsPanel() });
        tabs.Items.Add(new TabItem { Header = "Pins / 腳位", Content = BuildPinsPanel() });
        tabs.Items.Add(new TabItem { Header = "Specifications / 規格", Content = BuildSpecificationsPanel() });
        root.Children.Add(tabs);
        Content = root;
    }

    public ComponentIR? EditedComponent { get; private set; }

    private UIElement BuildBasicPanel()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var fields = new (string Label, TextBox Box)[]
        {
            ("Category（類別）", _category),
            ("Subcategory（子類別）", _subcategory),
            ("Connector Family（接頭家族）", _connectorFamily),
            ("Connector Coding（接頭編碼）", _connectorCoding),
            ("Connector Pin Count（腳位數）", _connectorPins),
            ("Product Image URL（產品圖片）", _imageUrl),
            ("Product URL（產品頁）", _productUrl),
            ("Datasheet URL（規格書）", _datasheetUrl),
            ("CAD URL", _cadUrl)
        };
        for (var row = 0; row < fields.Length; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = fields[row].Label, Margin = new Thickness(0, 7, 10, 7), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, row);
            grid.Children.Add(label);
            fields[row].Box.Margin = new Thickness(0, 4, 0, 4);
            fields[row].Box.MinHeight = 30;
            fields[row].Box.VerticalContentAlignment = VerticalAlignment.Center;
            Grid.SetRow(fields[row].Box, row);
            Grid.SetColumn(fields[row].Box, 1);
            grid.Children.Add(fields[row].Box);
        }
        scroll.Content = grid;
        return scroll;
    }

    private UIElement BuildPortsPanel()
    {
        var root = new DockPanel { Margin = new Thickness(8) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var add = new Button { Content = "+ Port", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        var remove = new Button { Content = "移除選取 / Remove", Padding = new Thickness(12, 5, 12, 5) };
        buttons.Children.Add(add);
        buttons.Children.Add(remove);
        DockPanel.SetDock(buttons, Dock.Top);
        root.Children.Add(buttons);

        var grid = GridFor(_ports);
        grid.Columns.Add(TextColumn("Port ID", nameof(PortEditRow.PortId), 120));
        grid.Columns.Add(TextColumn("Type", nameof(PortEditRow.PortType), 110));
        grid.Columns.Add(TextColumn("Connector", nameof(PortEditRow.ConnectorFamily), 110));
        grid.Columns.Add(TextColumn("Signal", nameof(PortEditRow.SignalType), 110));
        grid.Columns.Add(TextColumn("Direction", nameof(PortEditRow.Direction), 100));
        grid.Columns.Add(TextColumn("Voltage", nameof(PortEditRow.VoltageDomain), 100));
        grid.Columns.Add(TextColumn("Protocol", nameof(PortEditRow.Protocol), 110));
        add.Click += (_, _) => _ports.Add(new PortEditRow { PortId = $"PORT-{_ports.Count + 1}" });
        remove.Click += (_, _) => { if (grid.SelectedItem is PortEditRow row) _ports.Remove(row); };
        root.Children.Add(grid);
        return root;
    }

    private UIElement BuildPinsPanel()
    {
        var root = new DockPanel { Margin = new Thickness(8) };
        var hint = new TextBlock
        {
            Text = "Port ID（所屬接口）決定 Pin 掛在哪個接口下。例如 PWR 的兩個腳位可填：PWR | 1 | 24V，以及 PWR | 2 | 0V。若不知道所屬 Port，請留空，不要猜。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var add = new Button { Content = "+ Pin", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        var remove = new Button { Content = "移除選取 / Remove", Padding = new Thickness(12, 5, 12, 5) };
        buttons.Children.Add(add);
        buttons.Children.Add(remove);
        DockPanel.SetDock(buttons, Dock.Top);
        root.Children.Add(buttons);

        var grid = GridFor(_pins);
        grid.Columns.Add(TextColumn("Port ID", nameof(PinEditRow.PortId), 90));
        grid.Columns.Add(TextColumn("Pin", nameof(PinEditRow.PinNumber), 70));
        grid.Columns.Add(TextColumn("Function", nameof(PinEditRow.Function), 150));
        grid.Columns.Add(TextColumn("Signal", nameof(PinEditRow.SignalType), 110));
        grid.Columns.Add(TextColumn("Direction", nameof(PinEditRow.Direction), 95));
        grid.Columns.Add(TextColumn("Voltage", nameof(PinEditRow.VoltageDomain), 105));
        grid.Columns.Add(TextColumn("Description", nameof(PinEditRow.Description), 210));
        add.Click += (_, _) => _pins.Add(new PinEditRow
        {
            PortId = _ports.Count == 1 ? _ports[0].PortId : null,
            PinNumber = (_pins.Count + 1).ToString()
        });
        remove.Click += (_, _) => { if (grid.SelectedItem is PinEditRow row) _pins.Remove(row); };
        root.Children.Add(grid);
        return root;
    }

    private UIElement BuildSpecificationsPanel()
    {
        var root = new DockPanel { Margin = new Thickness(8) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var add = new Button { Content = "+ Specification", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        var remove = new Button { Content = "移除選取 / Remove", Padding = new Thickness(12, 5, 12, 5) };
        buttons.Children.Add(add);
        buttons.Children.Add(remove);
        DockPanel.SetDock(buttons, Dock.Top);
        root.Children.Add(buttons);

        var grid = GridFor(_specifications);
        grid.Columns.Add(TextColumn("Key", nameof(SpecificationEditRow.Key), 150));
        grid.Columns.Add(TextColumn("Name", nameof(SpecificationEditRow.Name), 190));
        grid.Columns.Add(TextColumn("Section", nameof(SpecificationEditRow.Section), 150));
        grid.Columns.Add(TextColumn("Value", nameof(SpecificationEditRow.Value), 300));
        add.Click += (_, _) => _specifications.Add(new SpecificationEditRow { Name = "New specification" });
        remove.Click += (_, _) => { if (grid.SelectedItem is SpecificationEditRow row) _specifications.Remove(row); };
        root.Children.Add(grid);
        return root;
    }

    private static DataGrid GridFor<T>(IEnumerable<T> items) => new()
    {
        ItemsSource = items,
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        SelectionMode = DataGridSelectionMode.Single,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(NullIfBlank(_connectorPins), out var pinCount)) pinCount = 0;
            var editedPins = _pins.Where(row => !string.IsNullOrWhiteSpace(row.PinNumber)).Select(BuildPin).ToArray();
            var editedPorts = _ports
                .Where(row => !string.IsNullOrWhiteSpace(row.PortId))
                .Select(row => new ComponentPort
                {
                    PortId = row.PortId.Trim(),
                    PortType = NullIfBlank(row.PortType),
                    ConnectorFamily = NullIfBlank(row.ConnectorFamily),
                    SignalType = NullIfBlank(row.SignalType),
                    Direction = NullIfBlank(row.Direction),
                    VoltageDomain = NullIfBlank(row.VoltageDomain),
                    Protocol = NullIfBlank(row.Protocol),
                    AllowedConnections = _original.Ports.FirstOrDefault(port => string.Equals(port.PortId, row.PortId, StringComparison.OrdinalIgnoreCase))?.AllowedConnections ?? []
                })
                .ToArray();
            var editedSpecifications = _specifications
                .Where(row => !string.IsNullOrWhiteSpace(row.Name))
                .Select(BuildSpecification)
                .ToArray();

            EditedComponent = _original with
            {
                Classification = new ComponentClassification { Category = NullIfBlank(_category), Subcategory = NullIfBlank(_subcategory) },
                Connector = new ComponentConnector { Family = NullIfBlank(_connectorFamily), Coding = NullIfBlank(_connectorCoding), Pins = pinCount > 0 ? pinCount : null },
                Ports = editedPorts,
                Pins = editedPins,
                Specifications = editedSpecifications,
                Assets = new ComponentAssets
                {
                    ProductPageUrl = ParseOptionalUri(_productUrl.Text, "Product URL"),
                    DatasheetUrl = ParseOptionalUri(_datasheetUrl.Text, "Datasheet URL"),
                    ImageUrl = ParseOptionalUri(_imageUrl.Text, "Image URL"),
                    CadUrl = ParseOptionalUri(_cadUrl.Text, "CAD URL")
                }
            };
            DialogResult = true;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "人工修正資料格式錯誤", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private ComponentPin BuildPin(PinEditRow row)
    {
        var original = _original.Pins.FirstOrDefault(pin =>
            string.Equals(pin.PinNumber, row.PinNumber, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(row.PortId) || string.Equals(pin.PortId, row.PortId, StringComparison.OrdinalIgnoreCase)));
        var changed = original is null ||
                      !Same(original.PortId, row.PortId) ||
                      !Same(original.Function, row.Function) ||
                      !Same(original.SignalType, row.SignalType) ||
                      !Same(original.Direction, row.Direction) ||
                      !Same(original.VoltageDomain, row.VoltageDomain) ||
                      !Same(original.Description, row.Description);
        var evidence = original?.Evidence.ToList() ?? new List<Evidence>();
        if (changed) evidence.Add(UserEvidence($"Port {row.PortId ?? "Unknown"} / Pin {row.PinNumber}: {row.Function ?? "Unknown"}"));
        return new ComponentPin
        {
            PortId = NullIfBlank(row.PortId),
            PinNumber = row.PinNumber.Trim(),
            Function = NullIfBlank(row.Function),
            SignalType = NullIfBlank(row.SignalType),
            Direction = NullIfBlank(row.Direction),
            VoltageDomain = NullIfBlank(row.VoltageDomain),
            Description = NullIfBlank(row.Description),
            Evidence = evidence
        };
    }

    private ComponentSpecification BuildSpecification(SpecificationEditRow row)
    {
        var original = _original.Specifications.FirstOrDefault(specification =>
            (!string.IsNullOrWhiteSpace(row.Key) && string.Equals(specification.Key, row.Key, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(specification.Name, row.Name, StringComparison.OrdinalIgnoreCase));
        var changed = original is null || !Same(original.Value, row.Value) || !Same(original.Name, row.Name) || !Same(original.Section, row.Section) || !Same(original.Key, row.Key);
        var evidence = original?.Evidence.ToList() ?? new List<Evidence>();
        if (changed) evidence.Add(UserEvidence($"{row.Name}: {row.Value ?? "Unknown"}"));
        return new ComponentSpecification
        {
            Key = NullIfBlank(row.Key),
            Name = row.Name.Trim(),
            Section = NullIfBlank(row.Section),
            Value = NullIfBlank(row.Value),
            Status = changed ? VerificationStatus.UserConfirmed : original?.Status ?? VerificationStatus.UserConfirmed,
            Evidence = evidence
        };
    }

    private static Evidence UserEvidence(string rawValue) => new()
    {
        SourceType = ComponentSourceType.User,
        ExtractionMethod = ExtractionMethod.UserInput,
        RetrievedAt = DateTimeOffset.UtcNow,
        VerificationStatus = VerificationStatus.UserConfirmed,
        RawValue = rawValue
    };

    private static DataGridTextColumn TextColumn(string header, string path, double width) => new()
    {
        Header = header,
        Binding = new System.Windows.Data.Binding(path) { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
        Width = new DataGridLength(width)
    };

    private static Uri? ParseOptionalUri(string? raw, string label)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return uri;
        throw new InvalidOperationException($"{label} 必須是有效的 http/https URL，或留空。" );
    }

    private static string? NullIfBlank(TextBox box) => NullIfBlank(box.Text);
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool Same(string? left, string? right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed class PortEditRow
    {
        public string PortId { get; set; } = string.Empty;
        public string? PortType { get; set; }
        public string? ConnectorFamily { get; set; }
        public string? SignalType { get; set; }
        public string? Direction { get; set; }
        public string? VoltageDomain { get; set; }
        public string? Protocol { get; set; }
        public static PortEditRow From(ComponentPort port) => new() { PortId = port.PortId, PortType = port.PortType, ConnectorFamily = port.ConnectorFamily, SignalType = port.SignalType, Direction = port.Direction, VoltageDomain = port.VoltageDomain, Protocol = port.Protocol };
    }

    private sealed class PinEditRow
    {
        public string? PortId { get; set; }
        public string PinNumber { get; set; } = string.Empty;
        public string? Function { get; set; }
        public string? SignalType { get; set; }
        public string? Direction { get; set; }
        public string? VoltageDomain { get; set; }
        public string? Description { get; set; }
        public static PinEditRow From(ComponentPin pin) => new() { PortId = pin.PortId, PinNumber = pin.PinNumber, Function = pin.Function, SignalType = pin.SignalType, Direction = pin.Direction, VoltageDomain = pin.VoltageDomain, Description = pin.Description };
    }

    private sealed class SpecificationEditRow
    {
        public string? Key { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Section { get; set; }
        public string? Value { get; set; }
        public static SpecificationEditRow From(ComponentSpecification specification) => new() { Key = specification.Key, Name = specification.Name, Section = specification.Section, Value = specification.Value };
    }
}
