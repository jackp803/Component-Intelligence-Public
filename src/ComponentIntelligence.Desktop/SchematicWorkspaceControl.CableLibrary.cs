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
        CableConstructionType Construction, CableArchiveResolver Resolver);
    private PendingCable? _pendingCable;
    private bool _placingCable;

    private async void CableLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_pageId is null || !FinishPendingDraft()) return;
        if (string.IsNullOrWhiteSpace(_archiveRoot)) { Status.Text = "尚未設定中央圖塊歸檔來源。"; return; }
        try
        {
            var repository = new SymbolArchiveRepository(_archiveRoot);
            var choices = repository.Load().CableTemplates.Where(c => c.Status is SymbolRevisionStatus.Candidate or SymbolRevisionStatus.Approved)
                .Select(c => new CableChoice(c, $"{c.Template.DisplayName ?? c.Template.TemplateId} / {c.Template.TemplateRevision} / {(c.Status == SymbolRevisionStatus.Approved ? "已核准" : "候選草稿")}" )).ToArray();
            if (choices.Length == 0) { Status.Text = "中央歸檔尚無線材模板。需先歸檔接線圖塊、外部接點與內部 mapping；不會改用簡單方框。"; return; }
            var dialog = new Window { Title = "線材庫：新增一條實體線材", Width = 640, Height = 530,
                Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new DockPanel { Margin = new(16) };
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
                ok.IsEnabled = true;
            };
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
            try { geometry = await new SchematicCadFileLoader().ReadAsync(source.AbsolutePath, source.Entry.MillimetresPerUnit); }
            finally { IsEnabled = true; }
            if (!geometry.Complete && MessageBox.Show(Window.GetWindow(this), string.Join("\n", geometry.Diagnostics),
                "部分 CAD 內容不支援；僅繼續為草稿？", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            CancelCommand();
            var preview = _service.AddArchivedCable(_getProject(), source.Entry.Template, construction, geometry,
                CableContacts(source.Entry), _pageId!, new(20, 20), source.AbsolutePath, source.Entry.Status == SymbolRevisionStatus.Approved);
            _placementPreview = preview; _pendingCable = new(source, geometry, construction, resolver);
            _placeMode = true; Status.Text = "線材待放置；點畫布新增一條實體線材，Escape 取消。";
            Focus(); Render(_getProject());
        }
        catch (Exception error) { Status.Text = "線材庫：" + error.Message; }
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
                CableContacts(current.Entry), pageId, point, current.AbsolutePath, current.Entry.Status == SymbolRevisionStatus.Approved), "已新增一條實體線材"))
            { _pendingCable = null; _placeMode = false; _placementPreview = null; Render(_getProject()); }
        }
        catch (Exception error) { Status.Text = "無法放置線材：" + error.Message; }
        finally { _placingCable = false; }
    }
}
