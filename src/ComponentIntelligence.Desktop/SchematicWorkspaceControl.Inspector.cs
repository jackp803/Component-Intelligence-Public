using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private readonly Dictionary<string, Control> _inspectorFields = new(StringComparer.Ordinal);
    private string? _inspectorTarget, _inspectorPage, _inspectorProjectId;
    private string _inspectorBaseline = "", _inspectorSource = "";
    private bool _populatingInspector, _applyingInspector;
    private TextBlock _pinInventory = null!, _wireEndpoints = null!;
    private TextBlock _wireSizingSummary = null!;
    private Button _adoptWireSize = null!;
    public event EventHandler? PendingEditsChanged;
    public bool HasPendingEdits => InspectorDirty || _wireStart is not null || _gestureStart is not null;
    public bool IsInteractionActive => HasPendingEdits || _exporting || _outputBusy;
    public bool HasActiveGesture => _gestureStart is not null;
    private bool InspectorDirty => _inspectorTarget is not null && InspectorFormKey() != _inspectorBaseline;

    private void InitializeInspector()
    {
        Field(ModuleInspectorFields, "x", "X (mm)"); Field(ModuleInspectorFields, "y", "Y (mm)");
        Field(ModuleInspectorFields, "width", "寬 (mm)"); Field(ModuleInspectorFields, "height", "高 (mm)");
        Choice(ModuleInspectorFields, "rotation", "旋轉", new[] { 0, 90, 180, 270 });
        Flag(ModuleInspectorFields, "locked", "鎖定模塊");
        _pinInventory = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) }; ModuleInspectorFields.Children.Add(_pinInventory);
        Field(WireInspectorFields, "designation", "線號");
        Choice(WireInspectorFields, "sizeMode", "線徑設定", new[] { "保留目前規格", "重新自動建議（待核對）", "手動 AWG", "手動截面積 mm²" });
        Choice(WireInspectorFields, "awg", "AWG", Enumerable.Range(0, 41).ToArray());
        Field(WireInspectorFields, "area", "導體截面積 (mm²)");
        _wireSizingSummary = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) }; WireInspectorFields.Children.Add(_wireSizingSummary);
        _adoptWireSize = AddAction(WireInspectorFields, "採用建議", () => {
            if (ResolveInspectorDraft() && _selectionId is { } id) Apply(p => _service.AcceptWireSizeSuggestion(p, id), "已採用線徑建議；依據保留，非安全核准"); });
        AddAction(WireInspectorFields, "查看依據", () => ShowWireSizing(true));
        AddAction(WireInspectorFields, "手動修改", () => ShowWireSizing(false, true));
        _wireEndpoints = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) }; WireInspectorFields.Children.Add(_wireEndpoints);
        AddAction(WireInspectorFields, "編輯起點接點", () => EditWireEndpoint(true));
        AddAction(WireInspectorFields, "編輯終點接點", () => EditWireEndpoint(false));
        AddAction(WireInspectorFields, "線材／內部接法", () => { if (ResolveInspectorDraft()) CableSettings_Click(this, new()); });
        AddAction(WireInspectorFields, "完整導線明細", () => WireDetails_Click(this, new()));
        foreach (var (id, label) in new[] { ("tx", "表格 X (mm)"), ("ty", "表格 Y (mm)"), ("tw", "表格寬 (mm)"), ("ts", "表格文字倍率"),
            ("sx", "示意圖 X (mm)"), ("sy", "示意圖 Y (mm)"), ("sw", "示意圖最大寬 (mm)"), ("sh", "示意圖最大高 (mm)") }) Field(DetailInspectorFields, id, label);
        Flag(DetailInspectorFields, "table", "接法表"); Flag(DetailInspectorFields, "sketch", "製作示意圖");
        AddAction(DetailInspectorFields, "編輯線材與接法", () => { if (ResolveInspectorDraft() && SelectedCableDetail() is { } d) TryEditSelectedArchivedCable(d.CableInstanceId); });
        ReferenceText.TextChanged += InspectorFieldChanged; CadScaleText.TextChanged += InspectorFieldChanged;
        PreviewKeyDown += InspectorPreviewKeyDown;
    }

    private void Field(StackPanel panel, string id, string label)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new(0, 7, 0, 2) });
        var field = new TextBox { Name = "Inspector_" + id, MinWidth = 0, MaxLength = 120 };
        field.TextChanged += InspectorFieldChanged; panel.Children.Add(field); _inspectorFields.Add(id, field);
    }
    private void Choice<T>(StackPanel panel, string id, string label, IReadOnlyList<T> options)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new(0, 7, 0, 2) });
        var field = new ComboBox { ItemsSource = options, MinHeight = 26 };
        field.SelectionChanged += (_, _) => InspectorFieldChanged(this, new()); panel.Children.Add(field); _inspectorFields.Add(id, field);
    }
    private void Flag(StackPanel panel, string id, string label)
    {
        var field = new CheckBox { Content = label, Margin = new(0, 8, 0, 0) };
        field.Checked += InspectorFieldChanged; field.Unchecked += InspectorFieldChanged;
        panel.Children.Add(field); _inspectorFields.Add(id, field);
    }
    private static Button AddAction(StackPanel panel, string label, Action action)
    {
        var button = new Button { Content = label, Margin = new(0, 6, 0, 0), Padding = new(6, 4, 6, 4) };
        button.Click += (_, _) => action(); panel.Children.Add(button); return button;
    }
    private string InspectorFormKey() => JsonSerializer.Serialize(new[] { ReferenceText.Text, CadScaleText.Text }
        .Concat(_inspectorFields.OrderBy(p => p.Key).Select(p => p.Value switch { TextBox t => t.Text, ComboBox c => c.SelectedIndex.ToString(), CheckBox b => b.IsChecked.ToString(), _ => "" })));
    private void InspectorFieldChanged(object sender, RoutedEventArgs e)
    {
        if (_populatingInspector) return;
        InspectorError.Text = "";
        _inspectorFields["awg"].IsEnabled = ((ComboBox)_inspectorFields["sizeMode"]).SelectedIndex == 2;
        _inspectorFields["area"].IsEnabled = ((ComboBox)_inspectorFields["sizeMode"]).SelectedIndex == 3;
        PendingEditsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void SetField(string id, object? value)
    {
        switch (_inspectorFields[id]) {
            case TextBox t: t.Text = value is double d ? d.ToString("R", CultureInfo.CurrentCulture) : value?.ToString() ?? ""; break;
            case ComboBox c: c.SelectedItem = value; break;
            case CheckBox b: b.IsChecked = value is true; break;
        }
    }
    private double Number(string id)
    {
        var text = id == "cad" ? CadScaleText.Text : ((TextBox)_inspectorFields[id]).Text;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) || !double.IsFinite(value))
            throw new InvalidOperationException("請輸入有效數值，不能使用空白、NaN 或無限大。");
        return value;
    }
    private bool FlagValue(string id) => ((CheckBox)_inspectorFields[id]).IsChecked == true;
    private string TargetSource(ElectricalProject project, string? target)
    {
        var s = project.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == target);
        if (s is not null) return JsonSerializer.Serialize(new { Symbol = s, Reference = SchematicSymbolOwner.Resolve(project, s).Reference });
        var w = project.Schematic?.Wires.SingleOrDefault(w => w.WireId == target);
        if (w is not null) return JsonSerializer.Serialize(new { Wire = w, Endpoints = SchematicWireContacts.Ends(project, w) });
        return JsonSerializer.Serialize(SelectedCableDetail());
    }
    private void UpdateInspector(ElectricalProject project, bool force = false)
    {
        if (_selectedWires.Count > 1)
        {
            _inspectorTarget = null;
            ModuleInspectorPanel.Visibility = WireInspectorFields.Visibility = DetailInspectorFields.Visibility = InspectorActions.Visibility = Visibility.Collapsed;
            InspectorDelete.Visibility = Visibility.Visible;
            return;
        }
        if (!force && _gestureStart is not null && _inspectorTarget == _selectionId) return;
        if (!force && _inspectorTarget == _selectionId && _inspectorProjectId == project.ProjectId &&
            (InspectorDirty || _inspectorSource == TargetSource(project, _selectionId))) return;
        if (InspectorDirty && !force && !_applyingInspector && _inspectorProjectId == project.ProjectId) {
            _selectionId = _inspectorTarget; _pageId = _inspectorPage;
            InspectorError.Text = "設定尚未套用；請先套用或取消。"; return;
        }
        _populatingInspector = true;
        try {
            _inspectorTarget = _selectionId; _inspectorPage = _pageId; _inspectorProjectId = project.ProjectId;
            var symbol = project.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == _selectionId);
            var wire = project.Schematic?.Wires.SingleOrDefault(w => w.WireId == _selectionId);
            var detail = SelectedCableDetail();
            ModuleInspectorPanel.Visibility = symbol is null ? Visibility.Collapsed : Visibility.Visible;
            WireInspectorFields.Visibility = wire is null ? Visibility.Collapsed : Visibility.Visible;
            DetailInspectorFields.Visibility = detail is null ? Visibility.Collapsed : Visibility.Visible;
            InspectorActions.Visibility = InspectorDelete.Visibility = symbol is not null || wire is not null || detail is not null ? Visibility.Visible : Visibility.Collapsed;
            InspectorError.Text = "";
            if (wire is not null && project.Connections.Any(c => c.ConnectionId == wire.ConnectionId && c.Kind == ConnectionKind.DirectMating))
            { WireInspectorFields.Visibility = InspectorActions.Visibility = Visibility.Collapsed; SelectionLabel.Text = "接頭對接 / 逐 Pin 映射"; }
            ReferenceText.Text = symbol is null ? "" : SchematicSymbolOwner.Resolve(project, symbol).Reference ?? "";
            CadScaleText.Text = ""; UpdateCadScaleSelection(symbol);
            if (symbol is not null) {
                var owner = SchematicSymbolOwner.Resolve(project, symbol);
                var bounds = SchematicPortPresentation.GenericBodyBounds(project.Schematic!, symbol, owner);
                SetField("x", symbol.Position.X); SetField("y", symbol.Position.Y); SetField("width", bounds.Width); SetField("height", bounds.Height);
                SetField("rotation", symbol.Rotation); SetField("locked", symbol.Locked);
                _inspectorFields["width"].IsEnabled = _inspectorFields["height"].IsEnabled = symbol.Geometry is null;
                _pinInventory.Text = string.Join("\n", owner.Ports.Where(p => SchematicPortPresentation.IsRepresented(symbol, p)).Select(p => p.Name + " / " + p.Pins.Count + " Pin"));
            }
            if (wire is not null) {
                var spec = SchematicWirePresentation.Resolve(project, wire);
                _wireSizingSummary.Text = SchematicWireSizingPresentation.Summary(project, wire);
                _adoptWireSize.IsEnabled = SchematicWireSizingPresentation.CanAccept(project, wire);
                SetField("designation", wire.Designation); ((ComboBox)_inspectorFields["sizeMode"]).SelectedIndex = 0;
                SetField("awg", spec.Awg); SetField("area", spec.AreaMm2);
                _inspectorFields["awg"].IsEnabled = _inspectorFields["area"].IsEnabled = false;
                _wireEndpoints.Text = string.Join("\n\n", new[] { ("起點", wire.Start), ("終點", wire.End) }.Select(end => {
                    var contact = SchematicWireContacts.Resolve(project, end.Item2);
                    return end.Item1 + "：" + (contact?.Caption ?? "未連接接點") + "\n功能來源：" + (contact?.Function ?? "待確認") +
                        "\n線色來源：" + (contact?.Pin is { } pin ? SchematicWireContacts.Color(pin)?.Name ?? "未指定" : "未指定"); }));
            }
            if (detail is not null) {
                SetField("tx", detail.TablePosition.X); SetField("ty", detail.TablePosition.Y); SetField("tw", detail.TableWidth); SetField("ts", detail.TableScale);
                SetField("sx", detail.SketchPosition.X); SetField("sy", detail.SketchPosition.Y); SetField("sw", detail.SketchWidth); SetField("sh", detail.SketchHeight);
                SetField("table", detail.TableVisible); SetField("sketch", detail.SketchVisible);
                SelectionLabel.Text = SelectedDetailPart() == SchematicCableDetailPart.Table ? "線材接法表" : "線材製作示意圖";
            }
            if (symbol is null && wire is null && detail is null) { _inspectorTarget = null; SelectionLabel.Text = project.Schematic?.Pages.SingleOrDefault(p => p.PageId == _pageId)?.Title ?? "尚無頁面"; }
            _inspectorSource = TargetSource(project, _selectionId); _inspectorBaseline = InspectorFormKey();
        }
        finally { _populatingInspector = false; }
        PendingEditsChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool ApplyInspector()
    {
        if (!InspectorDirty) return true;
        try {
            var current = _getProject(); var target = _inspectorTarget;
            if (target is null || current.ProjectId != _inspectorProjectId || target != _selectionId || TargetSource(current, target) != _inspectorSource)
                throw new InvalidOperationException("此物件已變更或不存在；請取消設定後重新選取。");
            ElectricalProject next;
            if (current.Schematic!.Symbols.Any(s => s.SymbolId == target)) {
                var symbol = current.Schematic.Symbols.Single(s => s.SymbolId == target);
                next = SchematicInspectorEdit.ApplyModule(current, target, new(ReferenceText.Text, Number("x"), Number("y"), Number("width"), Number("height"),
                    (int)((ComboBox)_inspectorFields["rotation"]).SelectedItem, FlagValue("locked"), symbol.Geometry is null ? null : Number("cad") / 100));
            }
            else if (current.Schematic.Wires.Any(w => w.WireId == target))
                next = SchematicInspectorEdit.ApplyWire(current, target, new(((TextBox)_inspectorFields["designation"]).Text,
                    ((ComboBox)_inspectorFields["sizeMode"]).SelectedIndex, ((ComboBox)_inspectorFields["sizeMode"]).SelectedIndex == 2 ?
                        ((ComboBox)_inspectorFields["awg"]).SelectedItem is int gauge ? gauge : throw new InvalidOperationException("請先選擇 AWG，未自動套用預設值。") : 0,
                    ((ComboBox)_inspectorFields["sizeMode"]).SelectedIndex == 3 ? Number("area") : .5));
            else {
                var basis = SelectedCableDetail() ?? throw new InvalidOperationException("線材明細已不存在。");
                var layout = basis with { TablePosition = new(Number("tx"), Number("ty")), TableWidth = Number("tw"), TableScale = Number("ts"),
                    SketchPosition = new(Number("sx"), Number("sy")), SketchWidth = Number("sw"), SketchHeight = Number("sh"), TableVisible = FlagValue("table"), SketchVisible = FlagValue("sketch") };
                if (!layout.TableVisible && !layout.SketchVisible) throw new InvalidOperationException("至少保留一個表示；要刪除請使用垃圾桶。");
                next = new SchematicCableDetailService().SetLayout(current, _inspectorPage!, basis.DetailId, layout);
            }
            _applyingInspector = true;
            next = CompleteExpandedFanoutEdit(current, next);
            if (!ReferenceEquals(next, current)) { _commit(next, "已套用右側設定"); }
            UpdateInspector(_getProject(), true); RefreshWorkspace(); Status.Text = "已套用右側設定" + (_autoFanoutNotice.Length > 0 ? "\n" + _autoFanoutNotice : ""); return true;
        }
        catch (Exception error) { InspectorError.Text = error.Message; return false; }
        finally { _applyingInspector = false; }
    }
    private bool ResolveInspectorDraft()
    {
        if (!InspectorDirty) return true;
        var answer = MessageBox.Show(Window.GetWindow(this), "右側設定尚未套用。\n是：套用設定\n否：放棄設定\n取消：留在目前設定", "尚未套用的設定", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes) return ApplyInspector();
        if (answer != MessageBoxResult.No) return false;
        DiscardInspector(); return true;
    }
    public void DiscardPendingEdits() { CancelCommand(); DiscardInspector(); }
    private void DiscardInspector() { UpdateInspector(_getProject(), true); }
    private void InspectorApply_Click(object sender, RoutedEventArgs e) => ApplyInspector();
    private void InspectorCancel_Click(object sender, RoutedEventArgs e) => DiscardInspector();
    private void InspectorDelete_Click(object sender, RoutedEventArgs e) { if (ResolveInspectorDraft()) { DeleteSelectedObject(); RefreshWorkspace(); } }
    private void InspectorBindings_Click(object sender, RoutedEventArgs e) { if (ResolveInspectorDraft()) Bindings_Click(sender, e); }
    private void InspectorEditModule_Click(object sender, RoutedEventArgs e) { if (ResolveInspectorDraft()) { EditModuleTool.IsChecked = true; EditModule_Click(sender, e); } }
    private void InspectorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || !IsInInspector(e.OriginalSource as DependencyObject)) return;
        if (e.Key == Key.Escape) { DiscardInspector(); e.Handled = true; }
        else if (e.Key == Key.Enter && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Control) {
            var node = e.OriginalSource as DependencyObject;
            while (node is not null && node != InspectorContent) { if (node is ButtonBase || node is ComboBox { IsDropDownOpen: true }) return; node = System.Windows.Media.VisualTreeHelper.GetParent(node); }
            ApplyInspector(); e.Handled = true;
        }
    }
    private bool IsInInspector(DependencyObject? node)
    {
        while (node is not null) { if (node == InspectorContent) return true; node = System.Windows.Media.VisualTreeHelper.GetParent(node); }
        return false;
    }
    private void EditWireEndpoint(bool start)
    {
        if (!ResolveInspectorDraft()) return;
        var wire = _getProject().Schematic?.Wires.SingleOrDefault(w => w.WireId == _selectionId);
        var endpoint = start ? wire?.Start : wire?.End;
        if (endpoint?.SymbolId is not { } id) { InspectorError.Text = "此端尚未連到模塊接點。"; return; }
        var project = _getProject(); var symbol = project.Schematic!.Symbols.Single(s => s.SymbolId == id);
        var owner = SchematicSymbolOwner.Resolve(project, symbol);
        if (owner.Cable is not null) { TryEditSelectedArchivedCable(owner.Cable.CableInstanceId); return; }
        var pin = owner.Ports.SelectMany(p => p.Pins).SingleOrDefault(p => p.PinId == endpoint.EndpointId);
        if (pin is null) { _selectionId = id; RefreshWorkspace(); Bindings_Click(this, new()); return; }
        var sourceKey = JsonSerializer.Serialize(pin); var projectId = project.ProjectId;
        var dialog = new Window { Title = "專案腳位設定", Width = 440, Height = 310, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new(14) };
        panel.Children.Add(new TextBlock { Text = (start ? "起點：" : "終點：") + SchematicWireContacts.Resolve(project, endpoint)?.Caption, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "功能", Margin = new(0, 8, 0, 3) });
        var function = new TextBox { Text = SchematicWireContacts.Resolve(project, endpoint)?.Function ?? "", MaxLength = 120 }; panel.Children.Add(function);
        panel.Children.Add(new TextBlock { Text = "線色（此 Pin 的專案資料）", Margin = new(0, 8, 0, 3) });
        var color = new ComboBox { ItemsSource = SchematicInspectorEdit.PinColors, SelectedItem = SchematicWireContacts.Color(pin)?.Name ?? "" }; panel.Children.Add(color);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new(0, 6, 0, 6) }; panel.Children.Add(error);
        var apply = new Button { Content = "套用此 Pin", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(apply);
        apply.Click += (_, _) => {
            var current = _getProject(); var currentSymbol = current.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == id);
            var currentPin = currentSymbol is null ? null : SchematicSymbolOwner.Resolve(current, currentSymbol).Ports.SelectMany(p => p.Pins).SingleOrDefault(p => p.PinId == endpoint.EndpointId);
            if (current.ProjectId != projectId || currentPin is null || sourceKey != JsonSerializer.Serialize(currentPin)) { error.Text = "腳位已變更，請重新開啟設定。"; return; }
            if (Apply(p => SchematicInspectorEdit.ApplyPinMetadata(p, id, pin.PinId, function.Text, (string)color.SelectedItem), "已更新指定 Pin 的功能／線色；對端與接法不變")) dialog.DialogResult = true;
            else error.Text = Status.Text;
        };
        dialog.Content = panel; dialog.ShowDialog();
    }
}
