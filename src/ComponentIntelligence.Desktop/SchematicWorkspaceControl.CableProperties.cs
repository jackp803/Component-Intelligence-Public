using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private bool TryEditSelectedArchivedCable()
    {
        var project = _getProject();
        var symbol = project.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == _selectionId);
        if (symbol?.CableInstanceId is not { } cableId) return false;
        if (symbol.Locked) { Status.Text = "請先解鎖這個線材表示。"; return true; }
        var cable = project.Cables.Single(c => c.CableInstanceId == cableId);
        var archived = cable.ArchivedCable!;
        var dialog = new Window { Title = "實體線材設定", Width = 980, Height = 820, MinWidth = 750, MinHeight = 640,
            Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new(16) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new(14, 6, 14, 6) };
        var apply = new Button { Content = "套用草稿", Padding = new(14, 6, 14, 6), Margin = new(8, 0, 0, 0) };
        footer.Children.Add(cancel); footer.Children.Add(apply); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var error = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(error, Dock.Bottom); root.Children.Add(error);
        var properties = new StackPanel();
        properties.Children.Add(new TextBlock { Text = $"{cable.DisplayName ?? cable.CableDefinitionId}\n模板 {archived.Template.TemplateRevision} / mapping {archived.Template.MappingRevision ?? "未確認"}", TextWrapping = TextWrapping.Wrap });
        TextBox Field(string label, string value)
        {
            properties.Children.Add(new TextBlock { Text = label, Margin = new(0, 8, 0, 3) });
            var field = new TextBox { Text = value }; properties.Children.Add(field); return field;
        }
        var reference = Field("Reference", cable.ReferenceDesignator ?? "");
        var length = Field("長度 mm（可留空）", cable.ProvidedLengthMm?.ToString(CultureInfo.CurrentCulture) ?? "");
        var specification = Field("規格（可留空）", cable.Specification ?? "");
        var choices = new[] { new ConstructionChoice(CableConstructionType.Unknown, "未確認（草稿）"),
            new ConstructionChoice(CableConstructionType.Purchased, "Purchased／外購成品"), new ConstructionChoice(CableConstructionType.Custom, "Custom／自製") };
        properties.Children.Add(new TextBlock { Text = "製作方式", Margin = new(0, 8, 0, 3) });
        var construction = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Label", SelectedItem = choices.Single(c => c.Value == cable.CableConstructionType) };
        properties.Children.Add(construction);
        properties.Children.Add(new TextBlock { Text = archived.MappingConfirmed ? "內部接法：有來源確認" : "內部接法：未確認",
            Margin = new(0, 12, 0, 3) });
        properties.Children.Add(new TextBlock { Text = archived.Template.MappingEvidence ?? "尚無內部接法來源。", TextWrapping = TextWrapping.Wrap });
        properties.Children.Add(new TextBlock { Text = "實例接法變更後為待確認，不修改共用模板。", Margin = new(0, 4, 0, 10), TextWrapping = TextWrapping.Wrap });
        DockPanel.SetDock(properties, Dock.Top); root.Children.Add(properties);
        var editor = new CableManufacturingTableEditor(new CableManufacturingEditorService().Prepare(cable),
            CableConnectorCatalog.Choices(_catalog), allowInventoryChanges: false);
        root.Children.Add(editor);
        apply.Click += (_, _) =>
        {
            if (!editor.Commit()) return;
            double? lengthMm = null;
            if (!string.IsNullOrWhiteSpace(length.Text))
            {
                if (!double.TryParse(length.Text, out var parsed) || !double.IsFinite(parsed) || parsed <= 0)
                { error.Text = "長度請輸入正數，或留空待確認。"; return; }
                lengthMm = parsed;
            }
            try
            {
                var current = _getProject();
                var next = _service.SetArchivedCableDetails(current, cableId, reference.Text, lengthMm, specification.Text,
                    ((ConstructionChoice)construction.SelectedItem).Value, archived.Mapping);
                next = _service.SetArchivedCableManufacturing(next, cableId, editor.Draft);
                if (JsonSerializer.Serialize(current) == JsonSerializer.Serialize(next)) { dialog.DialogResult = true; return; }
                if (Apply(_ => next, "已更新線材實例草稿；共用模板保持不變")) dialog.DialogResult = true;
            }
            catch (Exception exception) { error.Text = exception.Message; }
        };
        dialog.Content = root; dialog.ShowDialog(); return true;
    }
}
