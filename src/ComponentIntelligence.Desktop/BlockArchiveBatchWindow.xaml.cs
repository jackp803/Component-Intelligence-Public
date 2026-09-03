using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Repository;
using ComponentIntelligence.SymbolArchive;
using Microsoft.Win32;

namespace ComponentIntelligence.Desktop;

public partial class BlockArchiveBatchWindow : Window
{
    private readonly string _workbookPath;
    private readonly ObservableCollection<BlockArchiveReviewRow> _rows = [];
    private BlockArchiveBatchCoordinator? _coordinator;

    public BlockArchiveBatchWindow(string workbookPath)
    {
        _workbookPath = Path.GetFullPath(workbookPath ?? throw new ArgumentNullException(nameof(workbookPath)));
        InitializeComponent();
        RowsGrid.ItemsSource = _rows;
        Loaded += BlockArchiveBatchWindow_Loaded;
    }

    private async void BlockArchiveBatchWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= BlockArchiveBatchWindow_Loaded;
        try
        {
            var store = new WorkbookComponentKnowledgeStore(_workbookPath);
            if (!store.IsEnabled) throw new InvalidOperationException("Configured central workbook is unavailable or unreadable.");
            var components = await store.ListAsync();
            _coordinator = new BlockArchiveBatchCoordinator(
                _workbookPath,
                components,
                new AutocadBlockDeepInspector());
            ProtectionText.Text =
                $"中央工作簿保持唯讀：{_workbookPath}\n" +
                $"Symbol Archive root：{_coordinator.ArchiveRoot}\n" +
                "只有明確確認歸檔才寫 SymbolArchive.json / Documents/.../autocad/...；來源 DWG/DXF 僅讀取。";
            StatusText.Text = $"已載入 {components.Count} 個中央元件；尚未掃描 / {components.Count} central component(s) loaded; not scanned";
        }
        catch (Exception exception)
        {
            StatusText.Text = "初始化失敗 / Initialization failed";
            MessageBox.Show(this, App.FormatException(exception), "Block Archive", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_coordinator is null) return;
        var dialog = new OpenFolderDialog
        {
            Title = "選擇唯讀 DWG/DXF 來源資料夾 / Select read-only DWG/DXF source folder",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        ScanButton.IsEnabled = false;
        DeepInspectButton.IsEnabled = false;
        ArchiveButton.IsEnabled = false;
        try
        {
            var rows = await _coordinator.ScanAsync(dialog.FolderName);
            SourcePathText.Text = dialog.FolderName;
            _rows.Clear();
            foreach (var row in rows) _rows.Add(row);
            StatusText.Text = $"基本掃描完成：{rows.Count} 筆；尚未建立任何 Symbol Binding / Basic scan complete: {rows.Count} row(s); no authority written";
            DetailsText.Text = rows.Count == 0
                ? "未找到 DWG/DXF / No DWG/DXF files found."
                : "候選排序只供人工 review，不會自動填入 Component / Role / approval。";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, App.FormatException(exception), "Block Archive Scan", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ScanButton.IsEnabled = true;
            DeepInspectButton.IsEnabled = true;
            ArchiveButton.IsEnabled = true;
        }
    }

    private async void DeepInspectSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_coordinator is null) return;
        var selected = RowsGrid.SelectedItems.Cast<BlockArchiveReviewRow>().ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "請先選取至少一列 / Select at least one row.", "Block Archive", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DeepInspectButton.IsEnabled = false;
        try
        {
            foreach (var row in selected.OrderBy(item => item.Candidate.RelativePath, StringComparer.Ordinal))
            {
                StatusText.Text = $"深度掃描：{row.Candidate.RelativePath}";
                await _coordinator.DeepInspectAsync(row);
            }
            RowsGrid.Items.Refresh();
            RefreshDetails(RowsGrid.SelectedItem as BlockArchiveReviewRow);
            StatusText.Text = "深度掃描完成。Failed/Unavailable 列仍保留基本候選；完整性失敗列禁止歸檔。";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, App.FormatException(exception), "AutoCAD Deep Inspection", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DeepInspectButton.IsEnabled = true;
        }
    }

    private void ApplyMappings_Click(object sender, RoutedEventArgs e)
    {
        if (RowsGrid.SelectedItem is not BlockArchiveReviewRow row) return;
        try
        {
            var mappings = new List<SymbolPortBinding>();
            foreach (var raw in PortBindingsText.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = raw.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                    throw new InvalidDataException($"Mapping 必須是 stable-id=connection-point：'{raw}'");
                mappings.Add(new SymbolPortBinding
                {
                    EngineeringEndpointId = parts[0],
                    ConnectionPointId = parts[1]
                });
            }
            row.PortBindings = mappings
                .OrderBy(mapping => mapping.EngineeringEndpointId, StringComparer.Ordinal)
                .ToArray();
            RowsGrid.Items.Refresh();
            RefreshDetails(row);
            StatusText.Text = $"已套用 {mappings.Count} 筆 Port/Pin mapping；尚未寫入 archive。";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Port/Pin Mapping", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ArchiveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_coordinator is null) return;
        var selected = RowsGrid.SelectedItems.Cast<BlockArchiveReviewRow>().ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "請先選取要歸檔的列 / Select rows to archive.", "Block Archive", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            // Validate everything except the confirmation bit first. No write occurs here.
            foreach (var row in selected)
            {
                row.UserConfirmed = true;
                try { _coordinator.ValidateForApproval(row); }
                finally { row.UserConfirmed = false; }
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Review Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var answer = MessageBox.Show(this,
            $"即將歸檔 {selected.Length} 筆選取項目。\n\n" +
            "中央工作簿仍保持唯讀；本動作只會寫 SymbolArchive.json 並複製資產到 Documents/.../autocad/...。\n" +
            "來源 DWG/DXF 不會移動或覆寫。\n\n確認執行？",
            "確認歸檔 / Confirm Archive",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        ArchiveButton.IsEnabled = false;
        try
        {
            foreach (var row in selected) row.UserConfirmed = true;
            var results = await _coordinator.ApproveSelectedAsync(selected);
            RowsGrid.Items.Refresh();
            RefreshDetails(RowsGrid.SelectedItem as BlockArchiveReviewRow);
            StatusText.Text = $"歸檔完成：{results.Count} 筆。Revision / relative path / SHA 已回寫到 review row。";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, App.FormatException(exception), "Archive Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ArchiveButton.IsEnabled = true;
        }
    }

    private void RowsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RowsGrid.SelectedItem is not BlockArchiveReviewRow row)
        {
            DetailsText.Text = string.Empty;
            PortBindingsText.Text = string.Empty;
            return;
        }
        PortBindingsText.Text = string.Join(Environment.NewLine,
            row.PortBindings.Select(binding => $"{binding.EngineeringEndpointId}={binding.ConnectionPointId}"));
        RefreshDetails(row);
    }

    private void RefreshDetails(BlockArchiveReviewRow? row)
    {
        if (row is null) return;
        var candidate = row.Candidate;
        var builder = new StringBuilder()
            .AppendLine($"File: {candidate.RelativePath}")
            .AppendLine($"Size: {candidate.FileSize}")
            .AppendLine($"SHA-256: {candidate.Sha256}")
            .AppendLine($"Deep: {candidate.DeepInspectionStatus}")
            .AppendLine($"Exact duplicate: {candidate.ExactDuplicateRevision ?? "<none>"}")
            .AppendLine($"Source integrity failed: {candidate.SourceIntegrityFailed}")
            .AppendLine($"Suggestions: {row.SuggestedComponentDisplay}")
            .AppendLine($"Selected Component: {row.SelectedComponentId ?? "<review required>"}")
            .AppendLine($"Selected Role: {row.SelectedRole?.ToString() ?? "<review required>"}")
            .AppendLine($"Selected SourceType: {row.SelectedSourceType?.ToString() ?? "<review required>"}")
            .AppendLine($"Review status: {row.ReviewStatus}");

        if (candidate.DeepMetadata is not null)
        {
            builder.AppendLine().AppendLine("Deep metadata:");
            builder.AppendLine("Blocks: " + string.Join(", ", candidate.DeepMetadata.BlockNames));
            builder.AppendLine("Attributes: " + string.Join(", ", candidate.DeepMetadata.Attributes.Select(attribute => $"{attribute.Name}={attribute.Value}")));
            builder.AppendLine("Text: " + string.Join(" | ", candidate.DeepMetadata.TextLabels));
            if (candidate.DeepMetadata.BoundingBox is { } box)
                builder.AppendLine($"BBOX: ({box.MinX},{box.MinY},{box.MinZ}) -> ({box.MaxX},{box.MaxY},{box.MaxZ})");
        }
        if (row.DeepInspectionDiagnostics.Count > 0)
            builder.AppendLine("Diagnostics: " + string.Join(" | ", row.DeepInspectionDiagnostics));
        if (row.ApprovedRevision is not null)
        {
            builder.AppendLine().AppendLine("Approved archive evidence:");
            builder.AppendLine($"Revision: {row.ApprovedRevision}");
            builder.AppendLine($"AssetPath: {row.ApprovedAssetPath}");
            builder.AppendLine($"SHA-256: {row.ApprovedSha256}");
        }
        DetailsText.Text = builder.ToString();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
