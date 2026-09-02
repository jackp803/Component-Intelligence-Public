using System.Windows;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    private void ExportAutocadV2_Click(object sender, RoutedEventArgs e)
    {
        ExportAutocadV2Button.IsEnabled = false;
        try
        {
            var result = new AutocadStagingGraphV2ExportCoordinator().Export(_project);
            if (!result.Succeeded)
            {
                var blockingEvidence = string.Join(
                    Environment.NewLine,
                    result.Issues
                        .Where(issue => string.Equals(issue.Severity, "Error", StringComparison.Ordinal))
                        .Select(issue => $"{issue.Code}: {issue.Message}"));
                WorkspaceStatusText.Text = "AutoCAD v2 繪圖資料尚未產生：請處理阻擋項目。";
                MessageBox.Show(
                    this,
                    string.IsNullOrWhiteSpace(blockingEvidence)
                        ? "AutoCAD v2 繪圖資料未產生。"
                        : blockingEvidence,
                    "AutoCAD v2 繪圖資料阻擋",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            WorkspaceStatusText.Text = $"AutoCAD v2 繪圖資料已準備：{result.GraphPath}";
            MessageBox.Show(
                this,
                $"Schema: {result.SchemaVersion}{Environment.NewLine}" +
                $"Project: {result.ProjectId}{Environment.NewLine}" +
                $"Output: {result.GraphPath}",
                "AutoCAD v2 繪圖資料已準備",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            WorkspaceStatusText.Text = "AutoCAD v2 繪圖資料未產生。";
            MessageBox.Show(
                this,
                App.FormatException(exception),
                "AutoCAD v2 繪圖資料失敗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ExportAutocadV2Button.IsEnabled = true;
        }
    }
}
