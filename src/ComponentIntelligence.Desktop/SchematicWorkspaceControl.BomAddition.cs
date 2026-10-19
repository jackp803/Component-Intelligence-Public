using System.Windows;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private async void AddBomComponent_Click(object sender, RoutedEventArgs e) => await AddProjectComponentAsync();

    private async Task AddProjectComponentAsync()
    {
        if (!AddBomComponentButton.IsEnabled || !FinishPendingDraft()) return;
        var current = _getProject(); var fingerprint = ProjectSaveSession.Fingerprint(current);
        AddBomComponentButton.IsEnabled = false;
        try
        {
            IReadOnlyList<ComponentIR> catalog;
            string? warning = null;
            try { catalog = await _catalogProvider(); }
            catch (Exception error) { catalog = []; warning = "中央庫讀取失敗：" + error.Message; }
            bool SameProject() => ReferenceEquals(current, _getProject()) && fingerprint == ProjectSaveSession.Fingerprint(_getProject()) && IsLoaded;
            if (!SameProject()) { Status.Text = "專案已變更，請重新新增元件。"; return; }
            catalog = catalog.Concat(current.BomItems.Where(b => b.DefinitionSnapshot is not null).Select(b => b.DefinitionSnapshot!))
                .DistinctBy(c => c.Identity.ComponentId, StringComparer.Ordinal).ToArray();
            var dialog = new ProjectComponentAddDialog(catalog, warning) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true || dialog.SelectedComponent is not { } selected || !SameProject()) return;
            var next = _service.AddBomComponent(_getProject(), selected, dialog.Quantity);
            CancelCommand();
            _commit(next, $"已加入專案 BOM：{selected.Identity.Model} × {dialog.Quantity}");
            _catalog = catalog;
            BomCategory.SelectedIndex = 0; Search.Text = "";
            RefreshWorkspace();
            CatalogList.SelectedItem = _bomItems.Values.FirstOrDefault(i => i.Entry.DefinitionId == selected.Identity.ComponentId);
            if (CatalogList.SelectedItem is not null) CatalogList.ScrollIntoView(CatalogList.SelectedItem);
            Status.Text = $"已加入 {dialog.Quantity} 個 {selected.Identity.Model}；可按「放置元件」。";
        }
        catch (Exception error) { Status.Text = "未新增元件：" + error.Message; }
        finally { AddBomComponentButton.IsEnabled = true; }
    }
}
