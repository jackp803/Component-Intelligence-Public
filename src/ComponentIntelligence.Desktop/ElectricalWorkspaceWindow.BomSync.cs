using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    private bool _workingBomSyncScheduled;

    public void SynchronizeWorkingBomOnLoad(IReadOnlyList<BomRow> workingBom)
    {
        ArgumentNullException.ThrowIfNull(workingBom);
        if (_workingBomSyncScheduled) return;

        _workingBomSyncScheduled = true;
        var snapshot = workingBom.ToArray();
        Loaded += async (_, _) => await SynchronizeWorkingBomAsync(snapshot);
    }

    private async Task SynchronizeWorkingBomAsync(IReadOnlyList<BomRow> workingBom)
    {
        if (workingBom.Count == 0)
        {
            WorkspaceStatusText.Text = "BOM → Topology：目前 working BOM 無資料。";
            return;
        }

        try
        {
            var catalog = new ComponentIrCatalogReader(_databasePath);
            var identityCache = new Dictionary<string, ComponentIR?>(StringComparer.OrdinalIgnoreCase);

            async Task<ComponentIR?> ResolveProcessedKnowledgeAsync(
                string manufacturer,
                string model,
                CancellationToken cancellationToken)
            {
                var key = $"{manufacturer.Trim()}::{model.Trim()}";
                if (identityCache.TryGetValue(key, out var cached)) return cached;

                // Topology is a result/projection view. It deliberately does not perform Notion or web
                // discovery here. Main-window Process BOM owns knowledge resolution and writes the
                // resulting Component IR into local SQLite before this view is opened.
                var local = await catalog.FindByIdentityAsync(manufacturer, model);
                identityCache[key] = local;
                return local;
            }

            var result = await new BomTopologySynchronizer().SynchronizeAsync(
                _project,
                workingBom,
                ResolveProcessedKnowledgeAsync);

            RefreshAll();
            WorkspaceStatusText.Text =
                $"Processed BOM → Topology：新增 {result.AddedInstances}；" +
                $"完整 IR {result.RichInstances}；Placeholder {result.PlaceholderInstances}；" +
                $"連線材料 {result.DeferredConnectionMaterialRows}；" +
                $"Qty ? {result.UnknownQuantityRows}；Spare-only 略過 {result.SkippedSpareOnlyRows}。";
        }
        catch (Exception exception)
        {
            WorkspaceStatusText.Text = $"Processed BOM → Topology 同步失敗：{exception.Message}";
        }
    }
}
