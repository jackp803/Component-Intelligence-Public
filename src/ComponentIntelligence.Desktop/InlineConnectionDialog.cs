using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public enum InlineConnectionOperation
{
    Connector,
    Terminal,
    CableSegment,
    PinMapping,
    DeleteConnection
}

public sealed class InlineConnectionDialog : Window
{
    private readonly ComboBox _operation = new();
    private readonly TextBox _reference = new();
    private readonly TextBox _family = new() { Text = "M12" };
    private readonly TextBox _coding = new();
    private readonly TextBox _pinCount = new() { Text = "4" };
    private readonly ComboBox _genderA = new();
    private readonly ComboBox _genderB = new();
    private readonly TextBox _function = new();
    private readonly ComboBox _cableDefinition = new()
    {
        IsEditable = true,
        IsTextSearchEnabled = true,
        StaysOpenOnEdit = true
    };
    private readonly TextBox _engineering = new();
    private readonly string _connectionSummary;

    public InlineConnectionDialog(
        string connectionSummary,
        IEnumerable<BomConnectionMaterialOption>? availableCableMaterials = null,
        string? selectedCableDefinitionId = null)
    {
        _connectionSummary = connectionSummary;
        Title = "編輯線路 / Edit Connection";
        Width = 680;
        Height = 760;
        MinWidth = 560;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        _operation.ItemsSource = new[]
        {
            new Choice("編輯 Pin Mapping（腳位映射）— 明確指定 A Pin → B Pin，不自動猜直通", InlineConnectionOperation.PinMapping),
            new Choice("插入 Connector（接頭）— 會把目前線路切成兩段", InlineConnectionOperation.Connector),
            new Choice("插入 Terminal（端子）— 建立一進一出 Feed-through", InlineConnectionOperation.Terminal),
            new Choice("設定 Cable Segment（線材）— 指定目前線段的線材實例", InlineConnectionOperation.CableSegment),
            new Choice("刪除目前連線 / Delete connection", InlineConnectionOperation.DeleteConnection)
        };
        _operation.DisplayMemberPath = nameof(Choice.Label);
        _operation.SelectedIndex = 0;

        _genderA.ItemsSource = Enum.GetValues<ConnectorGender>();
        _genderB.ItemsSource = Enum.GetValues<ConnectorGender>();
        _genderA.SelectedItem = ConnectorGender.Female;
        _genderB.SelectedItem = ConnectorGender.Male;

        var cableMaterials = (availableCableMaterials ?? Array.Empty<BomConnectionMaterialOption>())
            .OrderBy(item => item.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Model, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _cableDefinition.ItemsSource = cableMaterials;
        _cableDefinition.DisplayMemberPath = nameof(BomConnectionMaterialOption.DisplayLabel);
        _cableDefinition.SelectedValuePath = nameof(BomConnectionMaterialOption.CableDefinitionId);
        TextSearch.SetTextPath(_cableDefinition, nameof(BomConnectionMaterialOption.DisplayLabel));
        if (!string.IsNullOrWhiteSpace(selectedCableDefinitionId))
        {
            _cableDefinition.SelectedItem = cableMaterials.FirstOrDefault(item =>
                string.Equals(item.CableDefinitionId, selectedCableDefinitionId, StringComparison.OrdinalIgnoreCase));
            if (_cableDefinition.SelectedItem is null) _cableDefinition.Text = selectedCableDefinitionId;
        }

        _engineering.IsReadOnly = true;
        _engineering.AcceptsReturn = true;
        _engineering.TextWrapping = TextWrapping.Wrap;
        _engineering.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _engineering.MinHeight = 190;
        _engineering.MaxHeight = 280;
        _engineering.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        _engineering.Text = "線路工程分析載入中…";

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var intro = new TextBlock
        {
            Text = "操作提示：雙擊線路會開啟這個視窗。Pin Mapping 只保存明確指定的 A Pin → B Pin；沒有資料就保持 Unknown。插入 Connector / Terminal 會保留原本 A、B 端點並把原連線安全拆成兩段。\n\n" + connectionSummary,
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(intro);

        var operationPanel = Field("動作 / Action", _operation);
        Grid.SetRow(operationPanel, 1);
        root.Children.Add(operationPanel);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, 0, 12) };
        var fields = new StackPanel();
        fields.Children.Add(SectionTitle("Engineering Analysis / 線路工程分析"));
        fields.Children.Add(_engineering);
        fields.Children.Add(new TextBlock
        {
            Text = "注意：功率只有在 Voltage + Load Current 都有證據時才計算。線徑若缺少實際線長、允許壓降、安裝／溫度條件或適用標準，不會自動猜一個 mm²。Pin Mapping 也不會因兩端 Pin 號相同就假設直通。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 12)
        });
        fields.Children.Add(SectionTitle("共用 / Common"));
        fields.Children.Add(Field("Reference（可留空，自動編號）", _reference));
        fields.Children.Add(SectionTitle("Connector（接頭）設定"));
        fields.Children.Add(Field("Family，例如 M12 / RJ45 / Heavy Duty / Custom", _family));
        fields.Children.Add(Field("Coding，例如 A / D（沒有就留空）", _coding));
        fields.Children.Add(Field("Pin Count（未知可留空）", _pinCount));
        fields.Children.Add(Field("Side A Gender（A 端）", _genderA));
        fields.Children.Add(Field("Side B Gender（B 端）", _genderB));
        fields.Children.Add(SectionTitle("Terminal（端子）設定"));
        fields.Children.Add(Field("Function / 功能，例如 54V+、RS485-A", _function));
        fields.Children.Add(SectionTitle("Cable Segment（線材）設定"));
        fields.Children.Add(Field("BOM 線材 / Cable（可下拉選擇，也可手動輸入）", _cableDefinition));
        fields.Children.Add(new TextBlock
        {
            Text = cableMaterials.Length == 0
                ? "目前 BOM 沒有已辨識為 Cable / Wire / Cable Assembly 的線材；仍可手動輸入型號。"
                : $"已從目前 BOM 載入 {cableMaterials.Length} 種線材。選取後會保存對應的正式 Cable Definition ID。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, -6, 0, 10)
        });
        scroll.Content = fields;
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var apply = new Button { Content = "套用 / Apply", Padding = new Thickness(18, 7, 18, 7), IsDefault = true };
        apply.Click += Apply_Click;
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => LoadEngineeringAnalysis();
    }

    public InlineConnectionOperation Operation => (_operation.SelectedItem as Choice)?.Operation ?? InlineConnectionOperation.PinMapping;
    public string? ReferenceDesignator => BlankToNull(_reference.Text);
    public string ConnectorFamily => string.IsNullOrWhiteSpace(_family.Text) ? "Custom" : _family.Text.Trim();
    public string? ConnectorCoding => BlankToNull(_coding.Text);
    public int? ConnectorPinCount => int.TryParse(_pinCount.Text, out var count) && count > 0 ? count : null;
    public ConnectorGender SideAGender => _genderA.SelectedItem is ConnectorGender value ? value : ConnectorGender.Unknown;
    public ConnectorGender SideBGender => _genderB.SelectedItem is ConnectorGender value ? value : ConnectorGender.Unknown;
    public string? TerminalFunction => BlankToNull(_function.Text);
    public string? CableDefinitionId
    {
        get
        {
            var enteredText = BlankToNull(_cableDefinition.Text);
            return _cableDefinition.SelectedItem is BomConnectionMaterialOption selected &&
                   string.Equals(enteredText, selected.DisplayLabel, StringComparison.Ordinal)
                ? selected.CableDefinitionId
                : enteredText;
        }
    }

    public InlineConnectorOptions ConnectorOptions => new(ConnectorFamily, ConnectorCoding, ConnectorPinCount, SideAGender, SideBGender, ReferenceDesignator);
    public InlineTerminalOptions TerminalOptions => new(ReferenceDesignator, TerminalFunction);
    public CableSegmentOptions CableOptions => new(ReferenceDesignator, CableDefinitionId);

    private void Apply_Click(object? sender, RoutedEventArgs e)
    {
        if (!ValidateInput()) return;
        if (Operation == InlineConnectionOperation.PinMapping)
        {
            var connectionId = ExtractConnectionId();
            if (string.IsNullOrWhiteSpace(connectionId) || Owner is not ElectricalWorkspaceWindow workspace)
            {
                MessageBox.Show(this, "找不到目前線路的 Electrical Workspace context，無法編輯 Pin Mapping。", "Pin Mapping 無法開啟", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            workspace.EditConnectionPinMapping(connectionId);
            DialogResult = false;
            Close();
            return;
        }
        DialogResult = true;
    }

    private void LoadEngineeringAnalysis()
    {
        try
        {
            var connectionId = ExtractConnectionId();
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                _engineering.Text = "Connection ID 無法辨識，因此無法建立工程分析。";
                return;
            }
            _engineering.Text = Owner is ElectricalWorkspaceWindow workspace
                ? workspace.BuildConnectionEngineeringSummary(connectionId)
                : "找不到 Electrical Workspace context（電氣工作區內容），無法建立工程分析。";
        }
        catch (Exception exception)
        {
            _engineering.Text = $"工程分析失敗：{exception.Message}";
        }
    }

    private string? ExtractConnectionId() => _connectionSummary
        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault(line => line.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase))?
        .Split(':', 2)[1]
        .Trim();

    private bool ValidateInput()
    {
        if (Operation != InlineConnectionOperation.Connector) return true;
        if (!string.IsNullOrWhiteSpace(_family.Text)) return true;
        MessageBox.Show(this, "Connector Family 不可空白。", "資料需要補充", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private static FrameworkElement Field(string label, Control control)
    {
        control.MinHeight = 28;
        control.Margin = new Thickness(0, 3, 0, 10);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(control);
        return panel;
    }

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 10, 0, 8)
    };

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private sealed record Choice(string Label, InlineConnectionOperation Operation);
}
