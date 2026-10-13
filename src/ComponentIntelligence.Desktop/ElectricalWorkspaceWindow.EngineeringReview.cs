using System.Windows;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Repository;
using ComponentIntelligence.SymbolArchive;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    private async void EngineeringReview_Click(object sender, RoutedEventArgs e)
    {
        if (_saveBusy || _switchBusy || _schematicWorkspace?.FinishPendingDraft() == false) return;
        var reviewEpoch = _saveSession.Epoch;
        try
        {
            if (string.IsNullOrWhiteSpace(_centralWorkbookPath))
                throw new InvalidOperationException("請先設定中央型錄。");
            string CatalogHash() { using var stream = File.OpenRead(_centralWorkbookPath); return Convert.ToHexString(SHA256.HashData(stream)); }
            var catalogHash = CatalogHash();
            var catalog = await new WorkbookComponentKnowledgeStore(_centralWorkbookPath).ListAsync();
            if (_saveSession.Epoch != reviewEpoch) return;
            TopologyCanvas.PersistCurrentRouteGeometry();
            var persisted = new List<ComponentIntelligence.Contracts.ComponentIR>();
            // Context lookup must not initialize schema or change journal mode on Cancel/Skip.
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = _databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='component_ir_snapshots'";
                if (Convert.ToInt64(await command.ExecuteScalarAsync()) > 0)
                {
                    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
                    foreach (var id in _project.Components.Select(c => c.ComponentDefinitionId).Distinct(StringComparer.Ordinal))
                    {
                        command.Parameters.Clear();
                        command.CommandText = "SELECT json FROM component_ir_snapshots WHERE id=$id";
                        command.Parameters.AddWithValue("$id", id);
                        var raw = await command.ExecuteScalarAsync() as string;
                        var value = raw is null ? null : JsonSerializer.Deserialize<ComponentIntelligence.Contracts.ComponentIR>(raw, options);
                        if (value?.Identity.ComponentId == id) persisted.Add(value);
                    }
                }
            }
            if (catalogHash != CatalogHash()) throw new InvalidOperationException("讀取期間型錄已變更，請重新開啟。");
            var service = new EngineeringReviewService(catalog, new SymbolArchiveRepository(_centralWorkbookPath),
                "已設定的中央工作簿 / exact ComponentID / SHA256=" + catalogHash, persisted);
            var dialog = new EngineeringReviewDialog(service, () => _project, async (draft, evidence) =>
            {
                if (_saveBusy || _saveSession.Epoch != reviewEpoch) throw new InvalidOperationException("專案正在儲存或已切換，未套用工程確認。");
                if (catalogHash != CatalogHash()) throw new InvalidOperationException("型錄已變更，未儲存；請關閉並重新開啟工程確認。");
                draft = CableConstructionAuthority.ApplyDefinitions(draft, catalog);
                var revisions = EnsureDrawingRevisionService();
                var capture = _saveSession.Capture(_project); var saved = ProjectSaveSession.Clone(draft);
                _saveBusy = true; UpdateSaveState();
                try {
                    await _queuedCheckpoints;
                    await Task.Run(() => revisions.CreateCheckpointAsync(capture.Snapshot, ProjectRevisionTrigger.TopologyChange, "Before " + evidence));
                    saved = await _persistProjectSnapshot(saved);
                    _lastSavedSnapshot = ProjectSaveSession.Clone(saved); _savedOnce = true;
                    if (_saveSession.Complete(capture, saved, _project)) { RecordMutation(evidence); _project = saved; RefreshAll(); }
                    try { await Task.Run(() => revisions.CreateCheckpointAsync(saved, ProjectRevisionTrigger.TopologyChange, evidence)); }
                    catch (Exception ex) { throw new InvalidOperationException("專案已儲存，但後置版本紀錄失敗；請檢查 History：" + ex.Message, ex); }
                }
                finally { _saveBusy = false; ScheduleStateCheck(); UpdateSaveState(); }
            }) { Owner = this };
            dialog.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show(this, App.FormatException(ex), "工程確認", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
