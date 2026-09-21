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
        try
        {
            if (string.IsNullOrWhiteSpace(_centralWorkbookPath))
                throw new InvalidOperationException("請先設定中央型錄。");
            string CatalogHash() { using var stream = File.OpenRead(_centralWorkbookPath); return Convert.ToHexString(SHA256.HashData(stream)); }
            var catalogHash = CatalogHash();
            var catalog = await new WorkbookComponentKnowledgeStore(_centralWorkbookPath).ListAsync();
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
                if (catalogHash != CatalogHash()) throw new InvalidOperationException("型錄已變更，未儲存；請關閉並重新開啟工程確認。");
                var revisions = EnsureDrawingRevisionService();
                await revisions.CreateCheckpointAsync(_project, ProjectRevisionTrigger.TopologyChange, "Before " + evidence);
                // The modal dialog edits a clone. The active project changes only after normal Save succeeds.
                await _repository.SaveAsync(draft);
                RecordMutation(evidence);
                _project = draft;
                RefreshAll();
                try { await revisions.CreateCheckpointAsync(_project, ProjectRevisionTrigger.TopologyChange, evidence); }
                catch (Exception ex) { throw new InvalidOperationException("專案已儲存，但後置版本紀錄失敗；請重新檢查專案及 History：" + ex.Message, ex); }
            }) { Owner = this };
            dialog.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show(this, App.FormatException(ex), "工程確認", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
