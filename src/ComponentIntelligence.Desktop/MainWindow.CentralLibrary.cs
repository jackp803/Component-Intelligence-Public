using System.IO;
using System.Text;
using System.Windows;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Pipeline;
using ComponentIntelligence.Repository;
using ComponentIntelligence.Runtime;
using ComponentIntelligence.Verification;
using Microsoft.Win32;

namespace ComponentIntelligence.Desktop;

public partial class MainWindow
{
    private static string CentralWorkbookSettingsPath
    {
        get
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ComponentIntelligence");
            Directory.CreateDirectory(root);
            return Path.Combine(root, "central-workbook.txt");
        }
    }

    private void MainWindow_CentralLibraryLoaded(object sender, RoutedEventArgs e)
    {
        ApplyCentralLibraryLanguageText();
        UpdateCentralLibraryPathUi();
    }

    private void ToggleCentralLanguage_Click(object sender, RoutedEventArgs e)
    {
        _uiLanguage = _uiLanguage == UiLanguage.Chinese ? UiLanguage.English : UiLanguage.Chinese;
        for (var index = 0; index < _rows.Count; index++)
            _rows[index] = _rows[index].WithLanguage(_uiLanguage);
        if (_pendingSearchView is not null)
            _pendingSearchView = _pendingSearchView.WithLanguage(_uiLanguage);

        ApplyLanguageText();
        ApplyCentralLibraryLanguageText();
        RefreshDetailsForLanguage();
        StatusText.Text = T("已切換為中文介面。", "Switched to English interface.");
    }

    private void ApplyCentralLibraryLanguageText()
    {
        var zh = _uiLanguage == UiLanguage.Chinese;
        SubtitleText.Text = zh
            ? "BOM → Google Drive 中央工作簿 → SQLite 執行快取 → Topology / Layout"
            : "BOM → Google Drive Central Workbook → SQLite Runtime Cache → Topology / Layout";

        ProcessButton.Content = zh ? "從中央庫取得" : "Load Central Library";
        SearchHeaderText.Text = zh ? "中央元件查詢" : "Central Component Lookup";
        SearchButton.Content = zh ? "查中央庫" : "Lookup Central Library";
        SearchHintText.Text = zh
            ? "只讀 Components / Ports / Pins；不自動上網、不抓 PDF、不改中央庫。缺資料交由人工 + GPT 歸檔。"
            : "Reads Components / Ports / Pins only. No automatic web search, PDF download, or central-library write. Missing data goes to the human + GPT archive workflow.";
        AddSearchResultButton.ToolTip = zh
            ? "中央庫查詢只預覽；按這裡才加入目前 BOM"
            : "Central lookup is preview-only; click here to add the result to the current BOM";
        DetailsHeaderText.Text = zh ? "中央元件資料與缺欄位" : "Central Component Data & Missing Fields";
        DetailsText.ToolTip = zh
            ? "可複製缺欄位清單，連同原廠 PDF 交給 GPT 更新 Google Drive 中央歸檔"
            : "Copy the missing-field checklist and give it with the official PDF to GPT to update the Google Drive central archive";
        CentralLibraryButton.Content = zh ? "中央資料庫" : "Central Library";
        DatabaseLabelText.Text = zh ? "SQLite 執行快取：" : "SQLite runtime cache: ";

        DeepSearchButton.Visibility = Visibility.Collapsed;
        AddKnowledgeFileButton.Visibility = Visibility.Collapsed;
        CompareSourcesButton.Visibility = Visibility.Collapsed;
    }

    private void ConfigureCentralLibrary_Click(object sender, RoutedEventArgs e)
    {
        var current = LoadCentralWorkbookPath();
        var dialog = new OpenFileDialog
        {
            Title = T(
                "選擇 Google Drive 同步的 Component_Intelligence_Database.xlsx",
                "Select the Google Drive-synced Component_Intelligence_Database.xlsx"),
            Filter = "Component Intelligence Workbook (*.xlsx)|*.xlsx|Excel Workbook (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false
        };
        if (!string.IsNullOrWhiteSpace(current) && File.Exists(current))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(current);
            dialog.FileName = Path.GetFileName(current);
        }

        if (dialog.ShowDialog(this) != true) return;
        SaveCentralWorkbookPath(dialog.FileName);
        UpdateCentralLibraryPathUi();
        StatusText.Text = T(
            $"中央資料庫已設定：{dialog.FileName}",
            $"Central library configured: {dialog.FileName}");
    }

    private async void ProcessCentralLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_importedRows.Count == 0)
        {
            MessageBox.Show(this,
                T("請先匯入 BOM，或查詢中央元件後按「加入 BOM」。", "Import a BOM first, or look up a central component and click Add to BOM."),
                T("尚未有 BOM", "No BOM loaded"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var workbookPath = RequireCentralWorkbookPath();
        if (workbookPath is null) return;

        ProcessButton.IsEnabled = false;
        try
        {
            var lookup = ComponentRuntimeFactory.CreateCentralWorkbookLookupService(_databasePath, workbookPath);
            for (var index = 0; index < _importedRows.Count; index++)
            {
                var row = _importedRows[index];
                StatusText.Text = T(
                    $"中央庫讀取中 {index + 1}/{_importedRows.Count}：{row.Manufacturer} {row.ModelOrPartNumber}",
                    $"Loading central library {index + 1}/{_importedRows.Count}: {row.Manufacturer} {row.ModelOrPartNumber}");

                var result = await lookup.LookupAsync(row);
                var view = BomViewRow.FromResult(row, result, _uiLanguage);
                _rows[index] = AttachCentralKnowledgeGapDetails(view, result);
            }

            StatusText.Text = T(
                $"中央庫讀取完成：{_importedRows.Count} 筆。請查看右側缺欄位清單。",
                $"Central library load complete: {_importedRows.Count} row(s). Review the missing-field checklist on the right.");
        }
        catch (Exception exception)
        {
            StatusText.Text = T("中央庫讀取失敗", "Central library load failed");
            MessageBox.Show(this, App.FormatException(exception), T("中央庫讀取失敗", "Central library load failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ProcessButton.IsEnabled = true;
        }
    }

    private async void SearchCentralComponent_Click(object sender, RoutedEventArgs e)
    {
        var manufacturer = SearchManufacturerText.Text?.Trim();
        var model = SearchModelText.Text?.Trim();
        if (string.IsNullOrWhiteSpace(manufacturer) || string.IsNullOrWhiteSpace(model))
        {
            MessageBox.Show(this,
                T("請輸入製造商與型號 / 料號。", "Enter Manufacturer and Model / Part Number."),
                T("中央庫查詢條件不足", "Missing central-library lookup criteria"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var workbookPath = RequireCentralWorkbookPath();
        if (workbookPath is null) return;

        SearchButton.IsEnabled = false;
        AddSearchResultButton.IsEnabled = false;
        _pendingSearchResult = null;
        _pendingSearchView = null;

        try
        {
            StatusText.Text = T($"正在讀取中央庫：{manufacturer} {model}", $"Loading central library: {manufacturer} {model}");
            var lookup = ComponentRuntimeFactory.CreateCentralWorkbookLookupService(_databasePath, workbookPath);
            var response = await lookup.SearchAsync(manufacturer, model);
            _pendingSearchResult = response;

            var view = BomViewRow.FromResult(response.Query, response.Result, _uiLanguage);
            _pendingSearchView = AttachCentralKnowledgeGapDetails(view, response.Result);
            _showingSearchPreview = true;
            BomGrid.SelectedItem = null;
            DetailsText.Text = _pendingSearchView.Details;
            AddSearchResultButton.IsEnabled = response.Result.ResolutionStatus == ResolutionStatus.Resolved;

            var gaps = GetCentralKnowledgeGaps(response.Result);
            var required = gaps.Count(gap => gap.Priority == KnowledgeGapPriority.Required);
            var recommended = gaps.Count - required;
            StatusText.Text = response.Result.ResolutionStatus switch
            {
                ResolutionStatus.Resolved => T(
                    $"中央庫讀取完成：必要缺欄位 {required}，建議補充 {recommended}。",
                    $"Central library load complete: {required} required gap(s), {recommended} recommended gap(s)."),
                ResolutionStatus.NotFound => T(
                    "中央庫查無此料號。請人工找原廠 PDF，再交給 GPT 歸檔。",
                    "Component not found in the central library. Find the official PDF manually and give it to GPT for archiving."),
                _ => T("中央庫讀取失敗，請查看診斷資訊。", "Central-library lookup failed. Review diagnostics.")
            };
        }
        catch (Exception exception)
        {
            _showingSearchPreview = false;
            StatusText.Text = T("中央庫查詢失敗", "Central-library lookup failed");
            MessageBox.Show(this, App.FormatException(exception), T("中央庫查詢失敗", "Central-library lookup failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private BomViewRow AttachCentralKnowledgeGapDetails(BomViewRow view, PipelineResult result)
    {
        var chinese = view.DetailsChinese + BuildCentralKnowledgeGapSection(result, UiLanguage.Chinese);
        var english = view.DetailsEnglish + BuildCentralKnowledgeGapSection(result, UiLanguage.English);
        return view with
        {
            DetailsChinese = chinese,
            DetailsEnglish = english,
            Details = _uiLanguage == UiLanguage.Chinese ? chinese : english
        };
    }

    private static IReadOnlyList<KnowledgeGap> GetCentralKnowledgeGaps(PipelineResult result)
    {
        if (result.Component is not null)
            return KnowledgeCompletenessPolicy.Assess(result.Component);
        return result.ResolutionStatus == ResolutionStatus.NotFound
            ?
            [
                new KnowledgeGap
                {
                    Key = "central.component",
                    ChineseName = "中央元件資料",
                    EnglishName = "Central component record",
                    ChineseReason = "中央庫沒有這個料號；桌面軟體不會自行上網搜尋。",
                    EnglishReason = "The component is absent from the central library; the desktop application will not search the web.",
                    PdfHintChinese = "人工找原廠 PDF，交給 GPT 依 Components / Ports / Pins 規格歸檔。",
                    PdfHintEnglish = "Find the official PDF manually and give it to GPT to archive using the Components / Ports / Pins schema.",
                    Priority = KnowledgeGapPriority.Required
                }
            ]
            : Array.Empty<KnowledgeGap>();
    }

    private static string BuildCentralKnowledgeGapSection(PipelineResult result, UiLanguage language)
    {
        var zh = language == UiLanguage.Chinese;
        var gaps = GetCentralKnowledgeGaps(result);
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine(zh ? "=== 中央庫缺欄位檢查 ===" : "=== Central Library Missing-Field Check ===");

        if (gaps.Count == 0)
        {
            builder.AppendLine(zh
                ? "必要工程欄位未發現明確缺漏。軟體不會自行執行網路 / PDF enrichment（補資料）。"
                : "No explicit required engineering-field gap was found. The application will not perform web/PDF enrichment.");
            return builder.ToString();
        }

        foreach (var priority in new[] { KnowledgeGapPriority.Required, KnowledgeGapPriority.Recommended })
        {
            var selected = gaps.Where(gap => gap.Priority == priority).ToArray();
            if (selected.Length == 0) continue;

            builder.AppendLine(priority == KnowledgeGapPriority.Required
                ? (zh ? $"必要補齊（{selected.Length}）：" : $"Required ({selected.Length}):")
                : (zh ? $"建議補充（{selected.Length}）：" : $"Recommended ({selected.Length}):"));

            foreach (var gap in selected)
            {
                builder.AppendLine($"- [{gap.Key}] {(zh ? gap.ChineseName : gap.EnglishName)}");
                var reason = zh ? gap.ChineseReason : gap.EnglishReason;
                builder.AppendLine($"  {CentralizeGapText(reason, zh)}");
                var hint = zh ? gap.PdfHintChinese : gap.PdfHintEnglish;
                if (!string.IsNullOrWhiteSpace(hint))
                    builder.AppendLine($"  PDF: {CentralizeGapText(hint, zh)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine(zh
            ? "人工流程：找原廠 PDF → 將 PDF/URL + 缺欄位清單交給 GPT → GPT 更新 Google Drive 中央歸檔 → 軟體重新讀取中央工作簿。"
            : "Human workflow: find official PDF → give PDF/URL + checklist to GPT → GPT updates the Google Drive central archive → reload the central workbook in the application.");
        return builder.ToString();
    }

    private static string CentralizeGapText(string text, bool chinese) => chinese
        ? text.Replace("Notion", "中央庫", StringComparison.OrdinalIgnoreCase)
              .Replace("Specification 與 Document", "Components / Ports / Pins", StringComparison.OrdinalIgnoreCase)
        : text.Replace("Notion", "central library", StringComparison.OrdinalIgnoreCase)
              .Replace("Notion update workflow", "central archive workflow", StringComparison.OrdinalIgnoreCase);

    private string? RequireCentralWorkbookPath()
    {
        var path = LoadCentralWorkbookPath();
        if (!string.IsNullOrWhiteSpace(path) && new WorkbookComponentKnowledgeStore(path).IsEnabled)
            return path;

        MessageBox.Show(this,
            T(
                "尚未設定可讀取的 Component_Intelligence_Database.xlsx。\n\n請按「中央資料庫」選擇 Google Drive for Desktop 同步下來的 .xlsx 工作簿。",
                "No readable Component_Intelligence_Database.xlsx is configured.\n\nClick Central Library and select the .xlsx workbook synchronized by Google Drive for Desktop."),
            T("請設定中央資料庫", "Configure Central Library"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return null;
    }

    private static string? LoadCentralWorkbookPath()
    {
        var environment = Environment.GetEnvironmentVariable("COMPONENT_INTELLIGENCE_WORKBOOK")?.Trim();
        if (!string.IsNullOrWhiteSpace(environment)) return environment;
        try
        {
            return File.Exists(CentralWorkbookSettingsPath)
                ? File.ReadAllText(CentralWorkbookSettingsPath).Trim()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveCentralWorkbookPath(string path)
    {
        File.WriteAllText(CentralWorkbookSettingsPath, Path.GetFullPath(path.Trim()));
    }

    private void UpdateCentralLibraryPathUi()
    {
        var path = LoadCentralWorkbookPath();
        CentralLibraryButton.ToolTip = string.IsNullOrWhiteSpace(path)
            ? T("尚未設定中央 .xlsx 工作簿", "Central .xlsx workbook is not configured")
            : T($"中央工作簿：{path}", $"Central workbook: {path}");
    }
}
