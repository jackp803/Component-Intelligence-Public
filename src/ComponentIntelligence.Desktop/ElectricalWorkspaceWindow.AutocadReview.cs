using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using ComponentIntelligence.Electrical.Export;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    private async void AutoCadReview_Click(object sender, RoutedEventArgs e)
    {
        AutoCadReviewButton.IsEnabled = false;
        string? runRoot = null;
        try
        {
            var preflight = new AutocadReviewPreflightCoordinator().Prepare(_project);
            var preparation = preflight.Preparation;
            var dialog = new AutocadPreflightDialog(
                preflight.Issues,
                preparation?.Graph is not null,
                preflight.BindingSidecarPath,
                preflight.DrawingEvidenceSidecarPath,
                preflight.SymbolAcceptanceRegistryPath)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true || !preflight.CanLaunch || preparation?.Graph is null) return;

            runRoot = AutocadStagingReviewRunner.CreateRunRoot();
            var packageName = SanitizePackageFileName(_project.Name ?? _project.ProjectId);
            var projectName = $"{packageName}-ACADE-REVIEW";
            var jsonPath = Path.Combine(runRoot, "lrdu-staging-route.v1.json");
            var topologyPdfPath = Path.Combine(runRoot, "topology-review.pdf");
            var combinedPdfPath = Path.Combine(runRoot, "lrdu-autocad-topology-review.pdf");
            var json = JsonSerializer.Serialize(preparation.Graph, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(jsonPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // Use the same exact-canvas visual path as the topology tab's PDF button.
            TopologyCanvas.ExportCurrentVisualPdf(topologyPdfPath);

            WorkspaceStatusText.Text = $"AutoCAD Electrical staging 工程圖產生中：{runRoot}";
            var result = await new AutocadStagingReviewRunner().RunAsync(new AutocadStagingReviewRequest
            {
                GraphPath = jsonPath,
                OutputRoot = runRoot,
                ProjectName = projectName,
                SymbolAcceptanceRegistryPath = preflight.SymbolAcceptanceRegistryPath
            });
            AutocadReviewPdfPackage.Merge(topologyPdfPath, result.PdfPaths, combinedPdfPath);

            WorkspaceStatusText.Text = $"AutoCAD Electrical staging 工程圖完成。WDP: {result.ProjectPath}; PDF: {combinedPdfPath}";
            MessageBox.Show(
                this,
                $"AutoCAD Electrical staging 工程圖已完成。{Environment.NewLine}{Environment.NewLine}" +
                $"Project: {result.ProjectPath}{Environment.NewLine}" +
                $"Combined review PDF: {combinedPdfPath}{Environment.NewLine}" +
                $"Formal DWG modified: {result.FormalDwgModified}",
                "AutoCAD Electrical staging 完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            var evidence = runRoot is null ? string.Empty : $"{Environment.NewLine}{Environment.NewLine}Staging evidence: {runRoot}";
            MessageBox.Show(
                this,
                App.FormatException(exception) + evidence,
                "AutoCAD Electrical staging 失敗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            AutoCadReviewButton.IsEnabled = true;
        }
    }

    private static string SanitizePackageFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Electrical-Topology" : sanitized;
    }

}
