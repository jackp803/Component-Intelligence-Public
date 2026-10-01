using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private sealed record CableChoice(CableArchiveEntry Entry, string Label);
    private sealed record ConstructionChoice(CableConstructionType Value, string Label);
    private sealed record PendingCable(ResolvedCableArchive Source, SchematicCadAsset Geometry,
        CableConstructionType Construction, CableArchiveResolver Resolver, SchematicCadAsset? ManufacturingGeometry);
    private PendingCable? _pendingCable;
    private bool _placingCable;
    private string? _pendingCableRepresentation;

    private void BeginAnotherCableRepresentation(SchematicSymbol source)
    {
        if (_pageId is null || !FinishPendingDraft()) return;
        CancelCommand();
        _placementPreview = _service.PlaceExistingRepresentation(_getProject(), source.SymbolId, _pageId, new(20, 20));
        _pendingCableRepresentation = source.SymbolId; _placeMode = true;
        Status.Text = "同一條線材的另一個表示待放置；不增加實體數量。";
        Focus(); Render(_getProject());
    }

    private void PlaceAnotherCableRepresentation(SchematicPoint point)
    {
        if (_pendingCableRepresentation is not { } source || _pageId is null) return;
        if (Apply(p => _service.PlaceExistingRepresentation(p, source, _pageId, point), "已放置同一條線材的另一個表示"))
        { _pendingCableRepresentation = null; _placeMode = false; _placementPreview = null; Render(_getProject()); }
    }

    private async void CableLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_pageId is null || !FinishPendingDraft()) return;
        if (string.IsNullOrWhiteSpace(_archiveRoot)) { Status.Text = "尚未設定中央圖塊歸檔來源。"; return; }
        try
        {
            var repository = new SymbolArchiveRepository(_archiveRoot);
            CableChoice[] Choices() => repository.Load().CableTemplates.Where(c => c.Status is SymbolRevisionStatus.Candidate or SymbolRevisionStatus.Approved)
                .Select(c => new CableChoice(c, $"{c.Template.DisplayName ?? c.Template.TemplateId} / {c.Template.TemplateRevision} / {(c.Status == SymbolRevisionStatus.Approved ? "已核准" : "候選草稿")}" )).ToArray();
            var choices = Choices();
            var dialog = new Window { Title = "線材庫：新增一條實體線材", Width = 640, Height = 530,
                Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new DockPanel { Margin = new(16) };
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 8) };
            var create = new Button { Content = "\uE710", FontFamily = new("Segoe MDL2 Assets"), ToolTip = "新增線材模板 (Ctrl+Alt+C)", Padding = new(9, 5, 9, 5) };
            var revise = new Button { Content = "\uE70F", FontFamily = new("Segoe MDL2 Assets"), ToolTip = "以所選模板建立新版本", Padding = new(9, 5, 9, 5), Margin = new(6, 0, 0, 0), IsEnabled = false };
            toolbar.Children.Add(create); toolbar.Children.Add(revise); DockPanel.SetDock(toolbar, Dock.Top); panel.Children.Add(toolbar);
            var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "取消", IsCancel = true, Padding = new(14, 6, 14, 6) };
            var ok = new Button { Content = "選取並放置", IsEnabled = false, Margin = new(8, 0, 0, 0), Padding = new(14, 6, 14, 6) };
            bottom.Children.Add(cancel); bottom.Children.Add(ok); DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);
            var details = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 10) };
            var constructionChoices = new[] { new ConstructionChoice(CableConstructionType.Unknown, "未確認（草稿）"),
                new ConstructionChoice(CableConstructionType.Purchased, "Purchased／外購成品"), new ConstructionChoice(CableConstructionType.Custom, "Custom／自製") };
            var classification = new ComboBox { ItemsSource = constructionChoices, DisplayMemberPath = "Label",
                Margin = new(0, 4, 0, 10), IsEnabled = false };
            var lower = new StackPanel(); lower.Children.Add(details);
            lower.Children.Add(new TextBlock { Text = "製作方式（未知可保留草稿）" }); lower.Children.Add(classification);
            DockPanel.SetDock(lower, Dock.Bottom); panel.Children.Add(lower);
            var list = new ListBox { ItemsSource = choices, DisplayMemberPath = "Label" }; panel.Children.Add(list);
            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedItem is not CableChoice choice) return;
                var entry = choice.Entry;
                classification.SelectedItem = constructionChoices.Single(c => c.Value == entry.ConstructionType);
                classification.IsEnabled = entry.ConstructionType == CableConstructionType.Unknown;
                details.Text = $"{entry.Template.TemplateId}\n{entry.AssetPath}\n接點綁定：{entry.ContactBindings.Count} / {entry.Template.Ports.Sum(p => p.Pins.Count)}\n" +
                    $"內部接法：{(entry.Template.MappingConfirmed ? entry.Template.MappingRevision : "未確認")}\n" +
                    (entry.ConstructionEvidence is null ? "製作方式尚無來源證據。" : "製作方式來源：" + entry.ConstructionEvidence);
                ok.IsEnabled = !entry.WiringAssetPending; revise.IsEnabled = true;
                if (entry.WiringAssetPending) details.Text += "\n尚無接線 CAD：可編輯新版本，不能放置。";
            };
            void EditTemplate(CableArchiveEntry? entry)
            {
                var editor = new CableArchiveEditorDialog(repository, _catalog, entry) { Owner = dialog };
                if (editor.ShowDialog() != true) return;
                choices = Choices(); list.ItemsSource = choices;
                list.SelectedItem = choices.Single(c => c.Entry.Template.TemplateId == editor.SavedEntry!.Template.TemplateId &&
                    c.Entry.Template.TemplateRevision == editor.SavedEntry.Template.TemplateRevision);
            }
            create.Click += (_, _) => EditTemplate(null);
            revise.Click += (_, _) => { if (list.SelectedItem is CableChoice current) EditTemplate(current.Entry); };
            ok.Click += (_, _) => { if (list.SelectedItem is CableChoice) dialog.DialogResult = true; };
            dialog.Content = panel;
            if (dialog.ShowDialog() != true || list.SelectedItem is not CableChoice selected) return;
            var construction = classification.SelectedItem is ConstructionChoice choiceValue ? choiceValue.Value : CableConstructionType.Unknown;
            var resolver = new CableArchiveResolver(repository);
            var source = await resolver.ResolveAsync(selected.Entry.Template.TemplateId, selected.Entry.Template.TemplateRevision);
            if (JsonSerializer.Serialize(source.Entry) != JsonSerializer.Serialize(selected.Entry))
                throw new InvalidOperationException("歸檔模板已變更，請重新選取。");
            IsEnabled = false;
            SchematicCadAsset geometry;
            SchematicCadAsset? manufacturing = null;
            try
            {
                geometry = await new SchematicCadFileLoader().ReadArchivedAsync(source.AbsolutePath, source.Entry.MillimetresPerUnit,
                    source.Entry.WiringSelection, source.Entry.WiringGeometry);
                if (source.Entry.ManufacturingAsset is { } role)
                    manufacturing = await new SchematicCadFileLoader().ReadArchivedAsync(source.ManufacturingPath!, role.MillimetresPerUnit,
                        role.Selection, role.Geometry);
            }
            finally { IsEnabled = true; }
            if (!geometry.Complete && MessageBox.Show(Window.GetWindow(this), string.Join("\n", geometry.Diagnostics),
                "部分 CAD 內容不支援；僅繼續為草稿？", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            CancelCommand();
            var preview = _service.AddArchivedCable(_getProject(), source.Entry.Template, construction, geometry,
                CableContacts(source.Entry), _pageId!, new(20, 20), source.AbsolutePath, source.Entry.Status == SymbolRevisionStatus.Approved, manufacturing);
            _placementPreview = preview; _pendingCable = new(source, geometry, construction, resolver, manufacturing);
            _placeMode = true; Status.Text = "線材待放置；點畫布新增一條實體線材，Escape 取消。";
            Focus(); Render(_getProject());
        }
        catch (Exception error) { Status.Text = "線材庫：" + error.Message; }
    }

    private void CableArchive_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_archiveRoot)) { Status.Text = "尚未設定中央圖塊歸檔來源。"; return; }
        try
        {
            var dialog = new CableArchiveEditorDialog(new(_archiveRoot), _catalog) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true) Status.Text = "已儲存線材候選草稿；可從線材庫選取。";
        }
        catch (Exception error) { Status.Text = "線材歸檔：" + error.Message; }
    }

    private static Dictionary<string, string> CableContacts(CableArchiveEntry entry) =>
        entry.ContactBindings.ToDictionary(b => b.EngineeringEndpointId, b => b.ConnectionPointId, StringComparer.Ordinal);

    private async Task PlacePendingCableAsync(SchematicPoint point)
    {
        if (_placingCable || _pendingCable is not { } pending || _pageId is null) return;
        var pageId = _pageId;
        _placingCable = true;
        try
        {
            var current = await pending.Resolver.ResolveAsync(pending.Source.Entry.Template.TemplateId, pending.Source.Entry.Template.TemplateRevision);
            if (!ReferenceEquals(pending, _pendingCable) || _pageId != pageId) return;
            if (JsonSerializer.Serialize(current.Entry) != JsonSerializer.Serialize(pending.Source.Entry))
                throw new InvalidOperationException("歸檔模板已變更，請重新選取。" );
            if (Apply(p => _service.AddArchivedCable(p, current.Entry.Template, pending.Construction, pending.Geometry,
                CableContacts(current.Entry), pageId, point, current.AbsolutePath, current.Entry.Status == SymbolRevisionStatus.Approved, pending.ManufacturingGeometry), "已新增一條實體線材"))
            { _pendingCable = null; _placeMode = false; _placementPreview = null; Render(_getProject()); }
        }
        catch (Exception error) { Status.Text = "無法放置線材：" + error.Message; }
        finally { _placingCable = false; }
    }
}
