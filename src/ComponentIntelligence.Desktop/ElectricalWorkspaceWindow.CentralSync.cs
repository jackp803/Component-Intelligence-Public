using System.IO;
using System.Windows;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    private async void SyncCentralArchive_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_centralWorkbookPath) || !File.Exists(_centralWorkbookPath))
        {
            MessageBox.Show(this, "請先在主畫面設定有效的中央工作簿。", "同步中央庫", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            WorkspaceStatusText.Text = "正在讀取中央資料庫並比對專案元件…";
            var workbookPath = _centralWorkbookPath;
            var archive = await new WorkbookComponentKnowledgeStore(workbookPath).ListAsync();
            var preview = new CentralArchiveProjectSynchronizer().Synchronize(CloneForPreview(_project), archive);
            if (preview.UpdatedInstances == 0)
            {
                WorkspaceStatusText.Text = $"中央庫同步：沒有可更新元件；未找到 {preview.MissingInstanceIds.Count} 個。";
                return;
            }

            RecordMutation("Synchronize project components from central archive");
            var result = new CentralArchiveProjectSynchronizer().Synchronize(_project, archive);

            var sqlite = new SqliteComponentIrRepository(_databasePath);
            foreach (var component in archive)
                await sqlite.SaveAsync(component);

            RefreshAll();
            WorkspaceStatusText.Text =
                $"中央庫同步完成：更新 {result.UpdatedInstances} 個元件（{result.UpdatedDefinitions} 個型號）；" +
                $"中央庫未找到 {result.MissingInstanceIds.Count} 個。數量、位置、旋轉與接線均保留；請按儲存。";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, App.FormatException(exception), "中央庫同步失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static Electrical.Domain.ElectricalProject CloneForPreview(Electrical.Domain.ElectricalProject project)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(project, ProjectJsonOptions);
        return System.Text.Json.JsonSerializer.Deserialize<Electrical.Domain.ElectricalProject>(json, ProjectJsonOptions)
               ?? throw new InvalidOperationException("Unable to inspect project synchronization.");
    }

    private static readonly System.Text.Json.JsonSerializerOptions ProjectJsonOptions = CreateProjectJsonOptions();

    private static System.Text.Json.JsonSerializerOptions CreateProjectJsonOptions()
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }
}
