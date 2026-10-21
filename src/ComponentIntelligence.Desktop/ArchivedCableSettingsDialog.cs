using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public sealed class ArchivedCableSettingsDialog : Window
{
    private readonly Func<Func<ElectricalProject, ElectricalProject>, bool> _apply;
    private readonly string _cableId, _baseline;
    private readonly TextBox _reference, _length, _specification, _notes;
    private readonly ComboBox _construction;
    private readonly ComboBox _kind;
    private sealed record ItemKindChoice(CableLibraryItemKind Value, string Label);
    private readonly CableManufacturingTableEditor _editor;
    private readonly TextBlock _error;
    private sealed record ConstructionChoice(CableConstructionType Value, string Label);

    public ArchivedCableSettingsDialog(ElectricalProject project, string cableId, IReadOnlyList<CableConnectorChoice> choices,
        Func<Func<ElectricalProject, ElectricalProject>, bool> apply)
    {
        _apply = apply; _cableId = cableId;
        var cable = project.Cables.Single(c => c.CableInstanceId == cableId);
        var archived = cable.ArchivedCable ?? throw new InvalidOperationException("請選取有接線模板的實體線材。");
        _baseline = JsonSerializer.Serialize(cable);
        Title = "實體線材設定"; Width = 980; Height = 820; MinWidth = 750; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new(16) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        footer.Children.Add(new Button { Content = "取消", IsCancel = true, Padding = new(14, 6, 14, 6) });
        var save = new Button { Content = "套用", Padding = new(14, 6, 14, 6), Margin = new(8, 0, 0, 0), IsDefault = true };
        save.Click += (_, _) => { if (TryApply()) DialogResult = true; }; footer.Children.Add(save);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
        DockPanel.SetDock(_error, Dock.Bottom); root.Children.Add(_error);
        var properties = new StackPanel { Margin = new(8) };
        properties.Children.Add(new TextBlock { Text = cable.DisplayName ?? cable.CableDefinitionId, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        TextBox Field(string label, string value)
        {
            properties.Children.Add(new TextBlock { Text = label, Margin = new(0, 10, 0, 3) });
            var field = new TextBox { Text = value }; properties.Children.Add(field); return field;
        }
        _reference = Field("Reference", cable.ReferenceDesignator ?? "");
        properties.Children.Add(new TextBlock { Text = "項目類型", Margin = new(0, 10, 0, 3) });
        _kind = new() { ItemsSource = new[] { new ItemKindChoice(CableLibraryItemKind.Cable, "完整線材"),
            new ItemKindChoice(CableLibraryItemKind.Connector, "單獨接頭") }, DisplayMemberPath = "Label", SelectedValuePath = "Value",
            SelectedValue = CableWiringEquivalence.Kind(cable) };
        properties.Children.Add(_kind);
        _length = Field("實際線長 (mm)", cable.ProvidedLengthMm?.ToString(CultureInfo.CurrentCulture) ?? "");
        _specification = Field("規格", cable.Specification ?? "");
        _notes = Field("此條線材備註", cable.Notes ?? "");
        _notes.AcceptsReturn = true; _notes.TextWrapping = TextWrapping.Wrap; _notes.MinHeight = 90; _notes.MaxHeight = 180;
        _notes.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; _notes.MaxLength = 4000;
        var options = new[] { new ConstructionChoice(CableConstructionType.Unknown, "未確認"),
            new ConstructionChoice(CableConstructionType.Purchased, "Purchased／外購成品"), new ConstructionChoice(CableConstructionType.Custom, "Custom／自製") };
        properties.Children.Add(new TextBlock { Text = "製作方式", Margin = new(0, 10, 0, 3) });
        _construction = new() { ItemsSource = options, DisplayMemberPath = "Label", SelectedItem = options.Single(c => c.Value == cable.CableConstructionType) };
        properties.Children.Add(_construction);
        properties.Children.Add(new TextBlock { Text = "模板：" + archived.Template.TemplateRevision + " / 接法：" + (archived.Template.MappingRevision ?? "未確認"), Margin = new(0, 14, 0, 4) });
        var diagnostics = new SchematicCableDetailService().BuildArchived(project, project.Schematic?.Pages.FirstOrDefault() ?? new() { PageId = "preview", Title = "" },
            new(cableId, null) { TableVisible = false, SketchVisible = false }).Diagnostics;
        properties.Children.Add(new Expander { Header = "檢查項目 (" + diagnostics.Count + ")", IsExpanded = false,
            Content = new TextBlock { Text = string.Join("\n", diagnostics), TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) } });
        _editor = new(new CableManufacturingEditorService().Prepare(cable), choices, allowInventoryChanges: false);
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "線材資料", Content = new ScrollViewer { Content = properties, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.Items.Add(new TabItem { Header = "接頭／接線表", Content = _editor });
        root.Children.Add(tabs); Content = root;
    }

    public bool TryApply()
    {
        if (!_editor.Commit()) return false;
        try
        {
            double? length = string.IsNullOrWhiteSpace(_length.Text) ? null : double.Parse(_length.Text, CultureInfo.CurrentCulture);
            string? rejection = null;
            var applied = _apply(current => {
                try
                {
                    var cable = current.Cables.Single(c => c.CableInstanceId == _cableId);
                    if (JsonSerializer.Serialize(cable) != _baseline) throw new InvalidOperationException("線材資料已變更；請重新開啟設定後再套用。");
                    var author = new SchematicAuthoringService();
                    var next = author.SetArchivedCableDetails(current, _cableId, _reference.Text, length, _specification.Text,
                        ((ConstructionChoice)_construction.SelectedItem).Value, cable.ArchivedCable!.Mapping, _notes.Text);
                    next = author.SetArchivedCableManufacturing(next, _cableId, _editor.Draft);
                    return author.SetArchivedCableItemKind(next, _cableId, (CableLibraryItemKind)_kind.SelectedValue);
                }
                catch (Exception error) { rejection = error.Message; throw; }
            });
            if (!applied) _error.Text = rejection ?? "未套用線材設定；請核對檢查訊息或重新開啟設定。";
            return applied;
        }
        catch (Exception error) { _error.Text = error is FormatException ? "線長請輸入正數，未知可留空。" : error.Message; return false; }
    }
}
