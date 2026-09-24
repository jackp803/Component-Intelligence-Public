using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Desktop;

public enum CableSettingsChoice { OrdinaryWire, Purchased, Custom }

public sealed class CableSettingsDialog : Window
{
    private sealed record Choice(string Label, CableSettingsChoice Value);
    private readonly ComboBox _choice = new() { DisplayMemberPath = "Label" };
    private readonly TextBox _reference = new();
    private readonly ComboBox _material = new() { DisplayMemberPath = "DisplayLabel" };
    private readonly TextBox _length = new();
    private readonly CheckBox _consolidate = new() { Content = "確認將所選導體目前的線材歸屬合併為同一條實體線" };
    public CableSettingsChoice ChoiceValue { get; private set; }
    public string Reference => _reference.Text;
    public string? DefinitionId => _material.SelectedItem is BomConnectionMaterialOption option ? option.CableDefinitionId : null;
    public double? LengthMm { get; private set; }
    public bool ConfirmConsolidation => _consolidate.IsChecked == true;

    public CableSettingsDialog(string context, IEnumerable<BomConnectionMaterialOption> materials, CableInstance? existing, bool consolidation, bool multiEnd = false)
    {
        Title = "線材設定";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _choice.ItemsSource = new[] { new Choice("普通配線 / Ordinary Wire", CableSettingsChoice.OrdinaryWire),
            new Choice("外購成品線 / Purchased", CableSettingsChoice.Purchased), new Choice("自製加工線 / Custom", CableSettingsChoice.Custom) };
        var state = CableConstructionAuthority.Describe(existing);
        _choice.SelectedIndex = consolidation || state.RequiresChoice ? -1 : state.Construction switch
        {
            CableConstructionType.Purchased => 1,
            CableConstructionType.Custom => 2,
            _ => 0
        };
        _reference.Text = existing?.ReferenceDesignator ?? "";
        _material.ItemsSource = materials.ToArray();
        _material.SelectedItem = materials.FirstOrDefault(m => m.CableDefinitionId == existing?.CableDefinitionId);
        _material.IsEditable = false;
        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(new TextBlock { Text = context, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        var status = new TextBlock { Text = consolidation ? "合併多條實體線材：請確認製作方式" : $"{state.State} — {state.Evidence}", TextWrapping = TextWrapping.Wrap };
        body.Children.Add(status);
        var materialResolved = false;
        _choice.SelectionChanged += (_, _) =>
        {
            materialResolved = false;
            status.Text = _choice.SelectedItem is Choice choice ? choice.Label : "Unknown Cable";
        };
        void ApplyMaterialAuthority()
        {
            if (materialResolved) _choice.SelectedIndex = -1;
            if (_material.SelectedItem is BomConnectionMaterialOption material &&
                CableConstructionAuthority.IsPurchased(material.CableProduct) &&
                existing?.CableConstructionType is not CableConstructionType.Custom)
            {
                _choice.SelectedIndex = 1;
                status.Text = "Purchased — 型錄成品線證據：" + material.CableProduct!.Evidence;
                materialResolved = true;
            }
        }
        _material.SelectionChanged += (_, _) => ApplyMaterialAuthority();
        if (!multiEnd) ApplyMaterialAuthority();
        void Field(string label, UIElement control) { body.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 4) }); body.Children.Add(control); }
        Field("線材類型", _choice);
        Field("線材代號 / Reference", _reference);
        if (!multiEnd)
        {
            Field("BOM / 中央資料庫線材型號（可留空）", _material);
            Field(existing?.ProvidedLengthMm is { } length ? $"新線長 mm（留白保留 {length} mm / {existing.LengthSource}）" : "線長 mm（未知留白）", _length);
        }
        if (consolidation && !multiEnd) { _consolidate.Margin = new Thickness(0, 16, 0, 0); body.Children.Add(_consolidate); }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(new Button { Content = "取消", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) });
        var apply = new Button { Content = "套用", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        apply.Click += (_, _) => {
            if (_choice.SelectedItem is not Choice selected) { MessageBox.Show(this, "Unknown Cable：請為這條實體線材確認 Purchased 或 Custom。", Title); return; }
            if (!string.IsNullOrWhiteSpace(_length.Text))
            {
                if (!double.TryParse(_length.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) || !double.IsFinite(value) || value <= 0)
                { MessageBox.Show(this, "線長必須為正數。", Title); return; }
                LengthMm = value;
            }
            ChoiceValue = selected.Value;
            DialogResult = true;
        };
        buttons.Children.Add(apply);
        body.Children.Add(buttons);
        Content = body;
    }
}
