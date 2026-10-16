using System.Text;
using System.Windows;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Pipeline;
using ComponentIntelligence.Runtime;
using ComponentIntelligence.Verification;

namespace ComponentIntelligence.Desktop;

public partial class MainWindow
{
    private void MainWindow_NotionOnlyLoaded(object sender, RoutedEventArgs e) => ApplyNotionOnlyLanguageText();

    private void ToggleNotionLanguage_Click(object sender, RoutedEventArgs e)
    {
        _uiLanguage = _uiLanguage == UiLanguage.Chinese ? UiLanguage.English : UiLanguage.Chinese;
        for (var index = 0; index < _rows.Count; index++)
            _rows[index] = _rows[index].WithLanguage(_uiLanguage);
        if (_pendingSearchView is not null)
            _pendingSearchView = _pendingSearchView.WithLanguage(_uiLanguage);

        // Keep all legacy labels localized first, then override the retired online-search wording.
        ApplyLanguageText();
        ApplyNotionOnlyLanguageText();
        RefreshDetailsForLanguage();
        StatusText.Text = T("已切換為中文介面。", "Switched to English interface.");
    }

    private void ApplyNotionOnlyLanguageText()
    {
        var zh = _uiLanguage == UiLanguage.Chinese;
        SubtitleText.Text = zh
            ? "BOM → Notion 中央庫 → 缺欄位清單 → 人工找原廠 PDF → GPT 更新 Notion → 電氣設計"
            : "BOM → Notion Central Knowledge → Missing-Field Checklist → Human Official PDF → GPT Updates Notion → Electrical Design";

        ProcessButton.Content = zh ? "從 Notion 取得" : "Load from Notion";
        SearchHeaderText.Text = zh ? "Notion 元件查詢" : "Notion Component Lookup";
        SearchButton.Content = zh ? "查 Notion" : "Lookup Notion";
        SearchHintText.Text = zh
            ? "只讀取 Notion；不再自動上網搜尋、抓 PDF 或補資料。缺欄位會列出給人工 + GPT 處理。"
            : "Notion only. No automatic web search, PDF download, or enrichment. Missing fields are handed to a human + GPT.";
        AddSearchResultButton.ToolTip = zh
            ? "Notion 查詢只預覽；按這裡才加入目前 BOM"
            : "Notion lookup is preview-only; click here to add the result to the current BOM";
        DetailsHeaderText.Text = zh ? "Notion 元件資料與缺欄位" : "Notion Component Data & Missing Fields";
        DetailsText.ToolTip = zh
            ? "可複製缺欄位清單，連同人工找到的原廠 PDF 一起交給 GPT 更新 Notion"
            : "Copy the missing-field checklist and give it with the official PDF to GPT for a Notion update";
        DatabaseLabelText.Text = zh ? "SQLite 執行快取：" : "SQLite runtime cache: ";

        // Retired desktop workflows remain compiled for compatibility but are not exposed to users.
        DeepSearchButton.Visibility = Visibility.Collapsed;
        AddKnowledgeFileButton.Visibility = Visibility.Collapsed;
    }

    private async void ProcessNotion_Click(object sender, RoutedEventArgs e)
    {
        if (_importedRows.Count == 0)
        {
            MessageBox.Show(this,
                T("請先匯入 BOM，或查詢 Notion 元件後按「加入 BOM」。", "Import a BOM first, or look up a Notion component and click Add to BOM."),
                T("尚未有 BOM", "No BOM loaded"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ProcessButton.IsEnabled = false;
        try
        {
            var lookup = ComponentRuntimeFactory.CreateNotionOnlyLookupService(_databasePath);
            for (var index = 0; index < _importedRows.Count; index++)
            {
                var row = _importedRows[index];
                StatusText.Text = T(
                    $"Notion 讀取中 {index + 1}/{_importedRows.Count}：{row.Manufacturer} {row.ModelOrPartNumber}",
                    $"Loading Notion {index + 1}/{_importedRows.Count}: {row.Manufacturer} {row.ModelOrPartNumber}");

                var result = await lookup.LookupAsync(row);
                var view = BomViewRow.FromResult(row, result, _uiLanguage);
                _rows[index] = AttachKnowledgeGapDetails(view, result);
            }

            var requiredGapCount = _importedRows
                .Select((_, index) => _rows[index])
                .Count(row => row.StatusCode is "Resolved" or "NotFound");
            StatusText.Text = T(
                $"Notion 讀取完成：{_importedRows.Count} 筆。請查看右側缺欄位清單。",
                $"Notion load complete: {_importedRows.Count} rows. Review the missing-field checklist on the right.");
        }
        catch (Exception exception)
        {
            StatusText.Text = T("Notion 讀取失敗", "Notion load failed");
            MessageBox.Show(this, App.FormatException(exception), T("Notion 讀取失敗", "Notion load failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ProcessButton.IsEnabled = true;
        }
    }

    private async void SearchNotionComponent_Click(object sender, RoutedEventArgs e)
    {
        var manufacturer = SearchManufacturerText.Text?.Trim();
        var model = SearchModelText.Text?.Trim();
        if (string.IsNullOrWhiteSpace(manufacturer) || string.IsNullOrWhiteSpace(model))
        {
            MessageBox.Show(this,
                T("請輸入製造商與型號 / 料號。", "Enter Manufacturer and Model / Part Number."),
                T("Notion 查詢條件不足", "Missing Notion lookup criteria"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SearchButton.IsEnabled = false;
        AddSearchResultButton.IsEnabled = false;
        _pendingSearchResult = null;
        _pendingSearchView = null;

        try
        {
            StatusText.Text = T($"正在讀取 Notion：{manufacturer} {model}", $"Loading from Notion: {manufacturer} {model}");
            var lookup = ComponentRuntimeFactory.CreateNotionOnlyLookupService(_databasePath);
            var response = await lookup.SearchAsync(manufacturer, model);
            _pendingSearchResult = response;

            var view = BomViewRow.FromResult(response.Query, response.Result, _uiLanguage);
            _pendingSearchView = AttachKnowledgeGapDetails(view, response.Result);
            _showingSearchPreview = true;
            BomGrid.SelectedItem = null;
            DetailsText.Text = _pendingSearchView.Details;
            AddSearchResultButton.IsEnabled = response.Result.ResolutionStatus == ResolutionStatus.Resolved;

            var gaps = GetKnowledgeGaps(response.Result);
            var required = gaps.Count(gap => gap.Priority == KnowledgeGapPriority.Required);
            var recommended = gaps.Count - required;
            StatusText.Text = response.Result.ResolutionStatus switch
            {
                ResolutionStatus.Resolved => T(
                    $"Notion 讀取完成：必要缺欄位 {required}，建議補充 {recommended}。",
                    $"Notion load complete: {required} required gap(s), {recommended} recommended gap(s)."),
                ResolutionStatus.NotFound => T(
                    "Notion 查無此料號。請人工找原廠 PDF，再交給 GPT 建檔。",
                    "Component not found in Notion. Find the official PDF manually and give it to GPT to create the record."),
                _ => T("Notion 讀取失敗，請查看診斷資訊。", "Notion lookup failed. Review diagnostics.")
            };
        }
        catch (Exception exception)
        {
            _showingSearchPreview = false;
            StatusText.Text = T("Notion 查詢失敗", "Notion lookup failed");
            MessageBox.Show(this, App.FormatException(exception), T("Notion 查詢失敗", "Notion lookup failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private BomViewRow AttachKnowledgeGapDetails(BomViewRow view, PipelineResult result)
    {
        var chinese = view.DetailsChinese + BuildKnowledgeGapSection(result, UiLanguage.Chinese);
        var english = view.DetailsEnglish + BuildKnowledgeGapSection(result, UiLanguage.English);
        return view with
        {
            DetailsChinese = chinese,
            DetailsEnglish = english,
            Details = _uiLanguage == UiLanguage.Chinese ? chinese : english
        };
    }

    private static IReadOnlyList<KnowledgeGap> GetKnowledgeGaps(PipelineResult result)
    {
        if (result.Component is not null)
            return KnowledgeCompletenessPolicy.Assess(result.Component);
        return result.ResolutionStatus == ResolutionStatus.NotFound
            ? KnowledgeCompletenessPolicy.ForMissingNotionRecord()
            : Array.Empty<KnowledgeGap>();
    }

    private static string BuildKnowledgeGapSection(PipelineResult result, UiLanguage language)
    {
        var zh = language == UiLanguage.Chinese;
        var gaps = GetKnowledgeGaps(result);
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine(zh ? "=== Notion 缺欄位檢查 ===" : "=== Notion Missing-Field Check ===");

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
                builder.AppendLine($"  {(zh ? gap.ChineseReason : gap.EnglishReason)}");
                var hint = zh ? gap.PdfHintChinese : gap.PdfHintEnglish;
                if (!string.IsNullOrWhiteSpace(hint))
                    builder.AppendLine($"  PDF: {hint}");
            }
        }

        builder.AppendLine();
        builder.AppendLine(zh
            ? "人工流程：找原廠 PDF → 將 PDF/URL + 上述缺欄位清單交給 GPT → GPT 依證據更新 Notion → 軟體重新讀取 Notion。"
            : "Human workflow: find official PDF → give PDF/URL + checklist to GPT → GPT updates Notion from evidence → reload Notion in the application.");
        return builder.ToString();
    }
}
