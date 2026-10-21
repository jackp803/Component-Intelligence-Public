using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Editing;
using System.Windows;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    private bool _workingBomSyncScheduled;
    private bool _workingBomSyncBusy;
    private long _workingBomEpoch;
    private IReadOnlyList<BomRow> _workingBomSnapshot = Array.Empty<BomRow>();
    private readonly SemaphoreSlim _workingBomSyncGate = new(1, 1);

    public void SynchronizeWorkingBomOnLoad(IReadOnlyList<BomRow> workingBom)
    {
        ArgumentNullException.ThrowIfNull(workingBom);
        _workingBomSnapshot = workingBom.ToArray();
        if (_workingBomSyncScheduled) return;

        _workingBomSyncScheduled = true;
        _workingBomEpoch = _saveSession.Epoch;
        var initialProject = _project;
        Loaded += async (_, _) =>
        {
            if (ReferenceEquals(_project, initialProject)) await SynchronizeWorkingBomAsync(_workingBomSnapshot);
        };
    }

    private async void RetryWorkingBom_Click(object sender, RoutedEventArgs e)
    {
        if (_workingBomEpoch != _saveSession.Epoch || _switchBusy || _leavingSession || _saveBusy) return;
        await SynchronizeWorkingBomAsync(_workingBomSnapshot);
    }

    private async Task SynchronizeWorkingBomAsync(IReadOnlyList<BomRow> workingBom)
    {
        if (_leavingSession || _workingBomSyncBusy) return;
        if (workingBom.Count == 0)
        {
            TopologyCanvas.SetAvailableCableMaterials(Array.Empty<BomConnectionMaterialOption>());
            WorkspaceStatusText.Text = "BOM → Topology：目前 working BOM 無資料。";
            return;
        }

        _workingBomSyncBusy = true; RetryWorkingBomButton.IsEnabled = false;
        await _workingBomSyncGate.WaitAsync();
        var targetProject = _project;
        var capture = _saveSession.Capture(targetProject);
        var generation = _backgroundGeneration;
        try
        {
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
                var local = await _resolveWorkingBomComponent(manufacturer, model);
                identityCache[key] = local;
                return local;
            }

            await CheckpointMajorImportAsync("Before working BOM → Topology synchronization");
            var result = await new BomTopologySynchronizer().SynchronizeAsync(
                capture.Snapshot,
                workingBom,
                ResolveProcessedKnowledgeAsync);

            // Loading a saved project can replace _project while an earlier initial sync is still
            // resolving Component IR. Never repaint that newer project with stale sync results;
            // A saved project restores its own BOM instead of merging the main window's working BOM.
            if (!CanApplyBackgroundResult(capture, generation)) {
                if (CanApplyBackgroundResult(capture, generation, false)) {
                    RetryWorkingBomButton.Visibility = Visibility.Visible;
                    WorkspaceStatusText.Text = "BOM 同步期間專案已修改；按「重試起始 BOM」即可重新合併。";
                }
                return;
            }
            RecordMutation("Synchronize working BOM"); _project = capture.Snapshot;
            RetryWorkingBomButton.Visibility = Visibility.Collapsed;

            TopologyCanvas.SetAvailableCableMaterials(result.ConnectionMaterials);
            RefreshAll();
            WorkspaceStatusText.Text =
                $"Processed BOM → Topology：新增 {result.AddedInstances}；" +
                $"完整 IR {result.RichInstances}；Placeholder {result.PlaceholderInstances}；" +
                $"連線材料 {result.DeferredConnectionMaterialRows}（可選型號 {result.ConnectionMaterials.Count}）；" +
                $"Qty ? {result.UnknownQuantityRows}；Spare-only 略過 {result.SkippedSpareOnlyRows}。" +
                "舊專案的位置與接線已保留；請按「儲存」寫回更新後的專案。";

            // Project instances are snapshots. Refresh their authoritative component knowledge
            // after BOM merge so central PortRole/Direction/PhysicalSide changes appear immediately
            // when the workspace is reopened, while project placement and wiring remain untouched.
            await SynchronizeCentralArchiveOnLoadAsync();
        }
        catch (Exception exception)
        {
            if (CanApplyBackgroundResult(capture, generation, false)) {
                RetryWorkingBomButton.Visibility = Visibility.Visible;
                WorkspaceStatusText.Text = $"Processed BOM → Topology 同步失敗：{exception.Message}";
            }
        }
        finally
        {
            _workingBomSyncGate.Release();
            _workingBomSyncBusy = false; RetryWorkingBomButton.IsEnabled = true;
        }
    }
}
