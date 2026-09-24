using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Desktop;

public sealed class MultiEndCableEditorDialog : Window
{
    private readonly ElectricalProject _project;
    private readonly MultiEndCableEditorService _service;
    private readonly ComboBox _common = new() { DisplayMemberPath = nameof(MultiEndPortContext.Label), MinHeight = 30 };
    private readonly ComboBox _construction = new() { MinHeight = 30 };
    private readonly TextBox _reference = new();
    private readonly TextBox _trunk = new();
    private readonly CheckBox _consolidate = new() { Content = "我確認將上列配線的既有 Cable 歸屬整併為一條實體線材", IsChecked = false };
    private readonly StackPanel _branches = new();
    private readonly ListBox _conductors = new() { Height = 185, DisplayMemberPath = nameof(MultiEndConductorContext.Label) };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick };
    private readonly List<(MultiEndCableBranch Branch, TextBox Length)> _lengths = new();
    public MultiEndCableDraft Draft { get; }

    public MultiEndCableEditorDialog(ElectricalProject project, MultiEndCableDraft draft, MultiEndCableEditorService service)
    {
        _project = project; Draft = draft; _service = service;
        _consolidate.Unchecked += (_, _) => _service.ClearConsolidationConfirmation(Draft);
        Title = "多端線材 / Multi-End Cable";
        Width = 1050; Height = 830; MinWidth = 720; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(16) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Button("取消", () => DialogResult = false);
        var save = Button("套用線材", Save);
        buttons.Children.Add(cancel); buttons.Children.Add(save);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var fields = new StackPanel();
        _reference.Text = draft.Reference ?? "";
        _trunk.Text = Format(draft.TrunkLengthMm);
        _construction.Items.Add(new ComboBoxItem { Content = "外購 / Purchased", Tag = CableConstructionType.Purchased });
        _construction.Items.Add(new ComboBoxItem { Content = "自製 / Custom", Tag = CableConstructionType.Custom });
        _construction.SelectedIndex = draft.ConstructionType == CableConstructionType.Purchased ? 0 : draft.ConstructionType == CableConstructionType.Custom ? 1 : -1;
        fields.Children.Add(Field("線材編號 / Reference", _reference));
        fields.Children.Add(Field("製造類型（必選）", _construction));
        fields.Children.Add(new TextBlock { Text = "Custom 可能需要 Cable Detail；保留既有 Pin 對 Pin 對應，不推導芯線、接腳或屏蔽接法。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) });
        fields.Children.Add(Field("選取的原始 Pin 配線", _conductors));
        fields.Children.Add(_consolidate);
        fields.Children.Add(Button("增減配線…", () =>
        {
            var picker = new MultiEndConductorPickerDialog(project, Draft.ConnectionIds, Draft.IsNew ? null : Draft.CableId) { Owner = this };
            if (picker.ShowDialog() != true) return;
            Draft.ConnectionIds.Clear(); Draft.ConnectionIds.AddRange(picker.SelectedIds);
            _consolidate.IsChecked = false;
            RefreshConductors();
            _error.Text = "配線範圍已修改，請重新確認共同端及分支。";
        }));
        fields.Children.Add(Field("共同端 / Common End（必選）", _common));
        fields.Children.Add(Button("確認共同端與分支", () =>
        {
            try
            {
                if (_common.SelectedItem is not MultiEndPortContext selected) throw new InvalidOperationException("請選擇共同端。");
                _service.ConfirmEnds(project, Draft, selected.PortId);
                RefreshBranches(); _error.Text = "";
            }
            catch (InvalidOperationException ex) { _error.Text = ex.Message; }
        }));
        fields.Children.Add(Field("共用外皮主幹長度 mm（未知留白）", _trunk));
        fields.Children.Add(_branches); fields.Children.Add(_error);
        root.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        RefreshConductors(); RefreshBranches();
    }

    private void RefreshConductors()
    {
        var selected = Draft.CommonPortId;
        _conductors.ItemsSource = _service.GetConductors(_project, Draft);
        _common.ItemsSource = _service.GetEnds(_project, Draft);
        _common.SelectedItem = _common.Items.Cast<MultiEndPortContext>().SingleOrDefault(e => e.PortId == selected);
    }

    private void RefreshBranches()
    {
        _branches.Children.Clear(); _lengths.Clear();
        var ends = _service.GetEnds(_project, Draft).ToDictionary(e => e.PortId);
        foreach (var branch in Draft.Branches.OrderBy(b => b.Index))
        {
            var length = new TextBox { Text = Format(branch.LengthMm) };
            _branches.Children.Add(Field($"分支 {branch.Index}：{ends.GetValueOrDefault(branch.PortId)?.Label ?? "介面已變更"} · 長度 mm", length));
            _lengths.Add((branch, length));
        }
    }

    private void Save()
    {
        try
        {
            if (_common.SelectedItem is not MultiEndPortContext end || end.PortId != Draft.CommonPortId)
                throw new InvalidOperationException("請確認共同端與分支後再套用。");
            Draft.Reference = _reference.Text;
            Draft.ConstructionType = _construction.SelectedItem is ComboBoxItem { Tag: CableConstructionType type } ? type : CableConstructionType.Unknown;
            Draft.TrunkLengthMm = ParseLength(_trunk.Text);
            foreach (var pair in _lengths) pair.Branch.LengthMm = ParseLength(pair.Length.Text);
            if (_consolidate.IsChecked == true) _service.ConfirmConsolidation(_project, Draft);
            _service.Validate(_project, Draft);
            DialogResult = true;
        }
        catch (InvalidOperationException ex) { _error.Text = ex.Message; }
    }

    private static double? ParseLength(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && double.IsFinite(value) && value > 0) return value;
        throw new InvalidOperationException("長度必須是大於零的 mm 數值；未知請留白。");
    }
    private static string Format(double? value) => value?.ToString("0.###", CultureInfo.CurrentCulture) ?? "";
    internal static Button Button(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 5, 8, 5) };
        button.Click += (_, _) => action(); return button;
    }
    private static FrameworkElement Field(string label, Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        control.MinHeight = 28; panel.Children.Add(control); return panel;
    }
}

public sealed class MultiEndConductorPickerDialog : Window
{
    private sealed record Row(string Id, string Label, CheckBox Check);
    private readonly List<Row> _rows = new();
    public IReadOnlyList<string> SelectedIds => _rows.Where(r => r.Check.IsChecked == true).Select(r => r.Id).ToArray();

    public MultiEndConductorPickerDialog(ElectricalProject project, IEnumerable<string> selected, string? cableId = null, int minimumConnections = 2)
    {
        Title = "選取多端線材配線"; Width = 1120; Height = 700; MinWidth = 700; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var selectedIds = selected.ToHashSet(StringComparer.Ordinal);
        var pins = project.Components.SelectMany(c => c.Ports.SelectMany(p => p.Pins.Select(pin =>
            (pin.PinId, Label: $"{c.ReferenceDesignator ?? c.DisplayName ?? "未命名元件"} / {p.Name} / Pin {pin.PinNumber}"))))
            .GroupBy(x => x.PinId).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().Label);
        var root = new DockPanel { Margin = new Thickness(16) };
        var filter = new TextBox { MinHeight = 30, Margin = new Thickness(0, 0, 0, 8), ToolTip = "篩選元件／介面／Pin" };
        DockPanel.SetDock(filter, Dock.Top); root.Children.Add(filter);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        bottom.Children.Add(count);
        bottom.Children.Add(MultiEndCableEditorDialog.Button("取消", () => DialogResult = false));
        bottom.Children.Add(MultiEndCableEditorDialog.Button("確認配線", () =>
        {
            if (SelectedIds.Count < minimumConnections) { count.Text = $"請至少選取 {minimumConnections} 條配線"; return; }
            DialogResult = true;
        }));
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var list = new StackPanel();
        foreach (var c in project.Connections.Where(c => c.Kind is ConnectionKind.Wire or ConnectionKind.Cable &&
                     (cableId is null || string.IsNullOrWhiteSpace(c.CableInstanceId) || c.CableInstanceId == cableId)).OrderBy(c => c.ConnectionId, StringComparer.Ordinal))
        {
            if (!pins.TryGetValue(c.FromEndpointId, out var from) || !pins.TryGetValue(c.ToEndpointId, out var to)) continue;
            var ownership = project.Cables.SingleOrDefault(x => x.CableInstanceId == c.CableInstanceId);
            var label = $"{from} -> {to} | Cable: {ownership?.ReferenceDesignator ?? c.CableInstanceId ?? "未指定"}";
            var check = new CheckBox { Content = label, IsChecked = selectedIds.Contains(c.ConnectionId), Margin = new Thickness(2, 6, 2, 6), ToolTip = c.ConnectionId };
            check.Checked += (_, _) => count.Text = $"已選 {SelectedIds.Count} 條";
            check.Unchecked += (_, _) => count.Text = $"已選 {SelectedIds.Count} 條";
            _rows.Add(new Row(c.ConnectionId, label, check)); list.Children.Add(check);
        }
        filter.TextChanged += (_, _) =>
        {
            foreach (var row in _rows) row.Check.Visibility = row.Label.Contains(filter.Text, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        };
        count.Text = $"已選 {SelectedIds.Count} 條";
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }
}
