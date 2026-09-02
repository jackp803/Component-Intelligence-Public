using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

/// <summary>
/// Lets the user choose one manually drawn connector-to-connector (or connector-to-loose-wire)
/// conductor group and assign cable metadata without changing any endpoints.
/// </summary>
public sealed class ConnectorCableEditorDialog : Window
{
    private readonly ComboBox _candidate = new() { MinWidth = 520 };
    private readonly ComboBox _material = new() { IsEditable = true, StaysOpenOnEdit = true, MinWidth = 520 };
    private readonly TextBox _reference = new();
    private readonly TextBox _length = new();
    private readonly IReadOnlyList<CableInstance> _cables;

    public ConnectorCableEditorDialog(
        IReadOnlyList<ConnectorCableCandidate> candidates,
        IEnumerable<CableInstance> cables,
        IEnumerable<BomConnectionMaterialOption> materials)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(cables);
        ArgumentNullException.ThrowIfNull(materials);
        if (candidates.Count == 0) throw new ArgumentException("At least one cable candidate is required.", nameof(candidates));
        _cables = cables.ToArray();

        Title = "接頭 Cable / Connector Cable";
        Width = 700;
        Height = 500;
        MinWidth = 620;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        _candidate.ItemsSource = candidates;
        _candidate.DisplayMemberPath = nameof(ConnectorCableCandidate.Display);
        _candidate.SelectedIndex = 0;
        _candidate.SelectionChanged += (_, _) => LoadSelectedCandidate();

        var cableMaterials = materials.OrderBy(item => item.Manufacturer).ThenBy(item => item.Model).ToArray();
        _material.ItemsSource = cableMaterials;
        _material.DisplayMemberPath = nameof(BomConnectionMaterialOption.DisplayLabel);
        _material.SelectedValuePath = nameof(BomConnectionMaterialOption.CableDefinitionId);
        TextSearch.SetTextPath(_material, nameof(BomConnectionMaterialOption.DisplayLabel));

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var intro = new TextBlock
        {
            Text = "程式只整理你已經畫好的 Pin 連線，不會自動接線、改 Pin 或合併其他分支。若同一接頭連到兩個 M12，請在下方一次選一個分支設定。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(intro, 0);
        root.Children.Add(intro);

        var fields = new StackPanel();
        fields.Children.Add(Field("Cable 範圍（依你畫好的 Pin 連線偵測）", _candidate));
        fields.Children.Add(Field("BOM 線材／自製線材型號（可下拉，也可手動輸入）", _material));
        fields.Children.Add(Field("Cable 編號／Reference（可留白自動編號）", _reference));
        fields.Children.Add(Field("實際線長 mm（未知可留白）", _length));
        fields.Children.Add(new TextBlock
        {
            Text = "套用後，同一範圍內的每條 Pin 連線會成為同一條 Cable 的 Core 1、Core 2……；公母直接對插不會被算成 Cable。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 0)
        });
        var scroll = new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = "取消", MinWidth = 88, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var apply = new Button { Content = "套用 / Apply", MinWidth = 120, Padding = new Thickness(12, 6, 12, 6), IsDefault = true };
        apply.Click += Apply_Click;
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        LoadSelectedCandidate();
    }

    public ConnectorCableCandidate SelectedCandidate =>
        (ConnectorCableCandidate)_candidate.SelectedItem;

    public CableSegmentOptions CableOptions => new(
        BlankToNull(_reference.Text),
        CableDefinitionId,
        CableDisplayName);

    public double? ProvidedLengthMm => double.TryParse(_length.Text, out var value) && value > 0d ? value : null;

    private string? CableDisplayName => _material.SelectedItem is BomConnectionMaterialOption selected
        ? $"{selected.Manufacturer} {selected.Model}".Trim()
        : BlankToNull(_material.Text);

    private string? CableDefinitionId
    {
        get
        {
            var enteredText = BlankToNull(_material.Text);
            return _material.SelectedItem is BomConnectionMaterialOption selected &&
                   string.Equals(enteredText, selected.DisplayLabel, StringComparison.Ordinal)
                ? selected.CableDefinitionId
                : enteredText;
        }
    }

    private void LoadSelectedCandidate()
    {
        if (_candidate.SelectedItem is not ConnectorCableCandidate candidate) return;
        var cable = candidate.ExistingCableIds.Count == 1
            ? _cables.FirstOrDefault(item => string.Equals(
                item.CableInstanceId,
                candidate.ExistingCableIds[0],
                StringComparison.OrdinalIgnoreCase))
            : null;
        _reference.Text = cable?.ReferenceDesignator ?? string.Empty;
        _length.Text = cable?.ProvidedLengthMm?.ToString("0.###") ?? string.Empty;
        _material.SelectedItem = cable is null
            ? null
            : _material.Items.Cast<BomConnectionMaterialOption>().FirstOrDefault(item => string.Equals(
                item.CableDefinitionId,
                cable.CableDefinitionId,
                StringComparison.OrdinalIgnoreCase));
        if (_material.SelectedItem is null)
            _material.Text = cable?.CableDefinitionId == "UNRESOLVED-CABLE"
                ? cable.DisplayName ?? string.Empty
                : cable?.CableDefinitionId ?? string.Empty;
    }

    private void Apply_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_length.Text) &&
            (!double.TryParse(_length.Text, out var value) || value <= 0d))
        {
            MessageBox.Show(this, "線長請輸入大於 0 的 mm 數值；未知可留白。", "線長格式不正確", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }

    private static FrameworkElement Field(string label, Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        control.MinHeight = 32;
        panel.Children.Add(control);
        return panel;
    }

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
