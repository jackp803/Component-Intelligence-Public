using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    private bool _workingBomSyncScheduled;
    private IReadOnlyList<BomRow> _workingBomSnapshot = Array.Empty<BomRow>();
    private readonly SemaphoreSlim _workingBomSyncGate = new(1, 1);

    public void SynchronizeWorkingBomOnLoad(IReadOnlyList<BomRow> workingBom)
    {
        ArgumentNullException.ThrowIfNull(workingBom);
        _workingBomSnapshot = workingBom.ToArray();
        if (_workingBomSyncScheduled) return;

        _workingBomSyncScheduled = true;
        Loaded += async (_, _) => await SynchronizeWorkingBomAsync(_workingBomSnapshot);
    }

    private async Task SynchronizeWorkingBomAsync(IReadOnlyList<BomRow> workingBom)
    {
        if (workingBom.Count == 0)
        {
            TopologyCanvas.SetAvailableCableMaterials(Array.Empty<BomConnectionMaterialOption>());
            WorkspaceStatusText.Text = "BOM → Topology：目前 working BOM 無資料。";
            return;
        }

        await _workingBomSyncGate.WaitAsync();
        var targetProject = _project;
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
                targetProject,
                workingBom,
                ResolveProcessedKnowledgeAsync);

            // Loading a saved project can replace _project while an earlier initial sync is still
            // resolving Component IR.  Never repaint that newer project with stale sync results;
            // LoadProject_Click immediately performs a fresh sync against the loaded snapshot.
            if (!ReferenceEquals(targetProject, _project)) return;

            TopologyCanvas.SetAvailableCableMaterials(result.ConnectionMaterials);
            RefreshAll();
            WorkspaceStatusText.Text =
                $"Processed BOM → Topology：新增 {result.AddedInstances}；" +
                $"完整 IR {result.RichInstances}；Placeholder {result.PlaceholderInstances}；" +
                $"連線材料 {result.DeferredConnectionMaterialRows}（可選型號 {result.ConnectionMaterials.Count}）；" +
                $"Qty ? {result.UnknownQuantityRows}；Spare-only 略過 {result.SkippedSpareOnlyRows}。" +
                "舊專案的位置與接線已保留；請按「儲存」寫回更新後的專案。";
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(targetProject, _project))
                WorkspaceStatusText.Text = $"Processed BOM → Topology 同步失敗：{exception.Message}";
        }
        finally
        {
            _workingBomSyncGate.Release();
        }
    }
}
