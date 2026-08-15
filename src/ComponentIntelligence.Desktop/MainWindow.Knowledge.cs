using System.IO;
using System.Text;
using System.Windows;
using ComponentIntelligence.Knowledge;
using ComponentIntelligence.Runtime;
using Microsoft.Win32;

namespace ComponentIntelligence.Desktop;

public partial class MainWindow
{
    private async void AddKnowledgeFile_Click(object sender, RoutedEventArgs e)
    {
        var target = ResolveKnowledgeTarget();
        if (target is null)
        {
            MessageBox.Show(
                this,
                T("請先選取一筆 BOM 元件，或先執行一次元件搜尋。", "Select a BOM component or run a component search first."),
                T("尚未選取元件", "No component selected"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = T("選擇要補充的規格資料", "Select knowledge files"),
            Filter = "Engineering documents|*.pdf;*.txt;*.md;*.csv;*.json;*.xml;*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff|PDF (*.pdf)|*.pdf|Text / Data|*.txt;*.md;*.csv;*.json;*.xml|Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff|All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        AddKnowledgeFileButton.IsEnabled = false;
        try
        {
            var service = new ManualKnowledgeImportService(_databasePath);
            var ocrReview = new OcrReviewQueueService(_databasePath);
            var totalSpecs = 0;
            var reviewNeeded = 0;
            var imported = 0;
            var evidenceOnly = 0;
            var ocrRecognizedPages = 0;
            var ocrCandidates = 0;
            var ocrAttemptedFiles = 0;
            var ocrUnavailableFiles = 0;
            var issues = new List<string>();

            foreach (var file in dialog.FileNames)
            {
                var fileName = Path.GetFileName(file);
                StatusText.Text = T($"正在讀取：{fileName}", $"Reading: {fileName}");
                var result = await service.ImportAsync(
                    target.Value.RowId,
                    target.Value.Manufacturer,
                    target.Value.Model,
                    file);

                totalSpecs += result.ExtractedSpecificationCount;
                if (result.NeedsAiReview) reviewNeeded++;
                if (result.Status == ManualKnowledgeImportStatus.ImportedToComponentIr) imported++;
                else evidenceOnly++;
                issues.AddRange(result.Issues.Select(issue => $"{fileName}: {issue}"));

                // OCR is run against the durable knowledge-store copy, not the original picker path.
                // That makes the review queue reproducible even if the user's source file is later moved.
                StatusText.Text = T($"OCR 檢查：{fileName}", $"OCR check: {fileName}");
                var ocr = await ocrReview.AnalyzeAsync(
                    target.Value.RowId,
                    target.Value.Manufacturer,
                    target.Value.Model,
                    result.StoredPath);
                if (ocr.Attempted)
                {
                    ocrAttemptedFiles++;
                    if (!ocr.EngineAvailable) ocrUnavailableFiles++;
                    ocrRecognizedPages += ocr.RecognizedPages;
                    ocrCandidates += ocr.CandidateCount;
                }
                issues.AddRange(ocr.Diagnostics.Select(issue => $"{fileName}: {issue}"));
            }

            await RefreshTargetFromLocalRepositoryAsync(target.Value);

            var summary = new StringBuilder()
                .AppendLine(T("補充資料完成。", "Knowledge import complete."))
                .AppendLine(T($"檔案：{dialog.FileNames.Length}", $"Files: {dialog.FileNames.Length}"))
                .AppendLine(T($"直接解析出的工程欄位：{totalSpecs}", $"Directly extracted engineering fields: {totalSpecs}"))
                .AppendLine(T($"已更新 Component IR：{imported}", $"Component IR updated: {imported}"))
                .AppendLine(T($"僅保存為證據：{evidenceOnly}", $"Stored as evidence only: {evidenceOnly}"))
                .AppendLine(T($"需要人工 / AI Review：{reviewNeeded}", $"Needs human / AI review: {reviewNeeded}"))
                .AppendLine()
                .AppendLine(T("=== OCR Review（OCR 審核）===", "=== OCR Review ==="))
                .AppendLine(T($"需要 OCR 的檔案：{ocrAttemptedFiles}", $"Files requiring OCR: {ocrAttemptedFiles}"))
                .AppendLine(T($"OCR 成功讀取頁面：{ocrRecognizedPages}", $"OCR-recognized pages: {ocrRecognizedPages}"))
                .AppendLine(T($"待審核工程候選：{ocrCandidates}", $"Engineering candidates awaiting review: {ocrCandidates}"));

            if (ocrUnavailableFiles > 0)
                summary.AppendLine(T(
                    $"本機 OCR 引擎未安裝 / 未偵測：{ocrUnavailableFiles} 個檔案；原始檔已安全保存，可之後重新處理。",
                    $"Local OCR engine was unavailable for {ocrUnavailableFiles} file(s); originals were preserved for later reprocessing."));

            if (issues.Count > 0)
            {
                summary.AppendLine().AppendLine(T("診斷：", "Diagnostics:"));
                foreach (var issue in issues.Distinct().Take(30)) summary.AppendLine($"- {issue}");
            }

            DetailsText.Text = summary.ToString();
            StatusText.Text = T(
                $"已補充 {dialog.FileNames.Length} 個檔案，直接欄位 {totalSpecs}，OCR 候選 {ocrCandidates}",
                $"Imported {dialog.FileNames.Length} file(s): {totalSpecs} direct field(s), {ocrCandidates} OCR candidate(s)");
        }
        catch (Exception exception)
        {
            StatusText.Text = T("補充資料失敗", "Knowledge import failed");
            MessageBox.Show(
                this,
                App.FormatException(exception),
                T("補充資料失敗", "Knowledge import failed"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            AddKnowledgeFileButton.IsEnabled = true;
        }
    }

    private (string RowId, string? Manufacturer, string? Model)? ResolveKnowledgeTarget()
    {
        if (_showingSearchPreview && _pendingSearchResult is not null)
        {
            var query = _pendingSearchResult.Query;
            return (query.RowId, query.Manufacturer, query.ModelOrPartNumber);
        }

        if (BomGrid.SelectedItem is not BomViewRow selected) return null;
        var source = _importedRows.FirstOrDefault(row => string.Equals(row.RowId, selected.RowId, StringComparison.Ordinal));
        return source is null
            ? (selected.RowId, selected.Manufacturer, selected.Model)
            : (source.RowId, source.Manufacturer, source.ModelOrPartNumber);
    }

    private async Task RefreshTargetFromLocalRepositoryAsync((string RowId, string? Manufacturer, string? Model) target)
    {
        if (string.IsNullOrWhiteSpace(target.Manufacturer) || string.IsNullOrWhiteSpace(target.Model)) return;

        try
        {
            var search = ComponentRuntimeFactory.CreateOnlineSearchService(_databasePath);
            var refreshed = await search.SearchAsync(target.Manufacturer, target.Model);
            var index = _importedRows.Select((row, index) => (row, index))
                .FirstOrDefault(item => string.Equals(item.row.RowId, target.RowId, StringComparison.Ordinal));

            if (index.row is not null && index.index >= 0 && index.index < _rows.Count)
            {
                var view = BomViewRow.FromResult(index.row, refreshed.Result, _uiLanguage);
                _rows[index.index] = view;
                BomGrid.SelectedItem = view;
            }
            else if (_showingSearchPreview)
            {
                _pendingSearchResult = refreshed;
                _pendingSearchView = BomViewRow.FromResult(refreshed.Query, refreshed.Result, _uiLanguage);
            }
        }
        catch
        {
            // Knowledge persistence already succeeded. Refresh is best-effort and must not turn
            // a successful local import into a user-visible failure merely because the online
            // resolver is currently unavailable.
        }
    }
}
