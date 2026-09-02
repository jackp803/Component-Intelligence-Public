using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Bom;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Pipeline;
using ComponentIntelligence.Resolution;
using ComponentIntelligence.Runtime;
using ComponentIntelligence.Search;
using Microsoft.Win32;

namespace ComponentIntelligence.Desktop;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<BomViewRow> _rows = [];
    private readonly string _databasePath;
    private IReadOnlyList<BomRow> _importedRows = Array.Empty<BomRow>();
    private ComponentSearchResult? _pendingSearchResult;
    private BomViewRow? _pendingSearchView;
    private bool _showingSearchPreview;
    private UiLanguage _uiLanguage = UiLanguage.Chinese;

    public MainWindow()
    {
        InitializeComponent();
        BomGrid.ItemsSource = _rows;
        _databasePath = DesktopDatabasePathResolver.Resolve();
        DatabasePathText.Text = _databasePath;
        ApplyLanguageText();
    }

    private string T(string chinese, string english) => _uiLanguage == UiLanguage.Chinese ? chinese : english;

    private void ToggleLanguage_Click(object sender, RoutedEventArgs e)
    {
        _uiLanguage = _uiLanguage == UiLanguage.Chinese ? UiLanguage.English : UiLanguage.Chinese;
        for (var index = 0; index < _rows.Count; index++)
            _rows[index] = _rows[index].WithLanguage(_uiLanguage);
        if (_pendingSearchView is not null)
            _pendingSearchView = _pendingSearchView.WithLanguage(_uiLanguage);

        ApplyLanguageText();
        RefreshDetailsForLanguage();
        StatusText.Text = T("已切換為中文介面。", "Switched to English interface.");
    }

    private void ApplyLanguageText()
    {
        var zh = _uiLanguage == UiLanguage.Chinese;
        Title = zh ? "Component Intelligence｜元件智慧" : "Component Intelligence";
        LanguageButton.Content = zh ? "English" : "中文";
        LanguageButton.ToolTip = zh ? "切換到 English" : "Switch to Chinese";
        SubtitleText.Text = zh
            ? "BOM → 本機資料庫 → 原廠搜尋 → Component IR（元件中介資料）→ 電氣設計"
            : "BOM → Local DB → Manufacturer Search → Component IR → Electrical Design";

        ImportBomButton.Content = zh ? "匯入 BOM" : "Import BOM";
        TemplateButton.Content = zh ? "產生 BOM 範本" : "Generate BOM Template";
        ProcessButton.Content = zh ? "開始處理" : "Process BOM";
        ElectricalButton.Content = zh ? "電氣設計" : "Electrical Design";
        TopologyButton.Content = zh ? "電路拓樸" : "Electrical Topology";
        TopologyButton.ToolTip = zh ? "直接開啟電路拓樸頁面" : "Open the Electrical Topology page directly";

        SearchHeaderText.Text = zh ? "元件搜尋" : "Component Search";
        ModelLabelText.Text = zh ? "型號 / 料號" : "Model / Part Number";
        SearchButton.Content = zh ? "搜尋" : "Search";
        AddSearchResultButton.Content = zh ? "加入 BOM" : "Add to BOM";
        AddSearchResultButton.ToolTip = zh ? "搜尋只預覽；按這裡才加入目前 BOM" : "Search only previews; click here to add the result to the current BOM";
        SearchHintText.Text = zh ? "搜尋只預覽，不會自動加入 BOM" : "Search is preview-only and does not automatically add to the BOM";
        SearchManufacturerText.ToolTip = zh ? "製造商，例如 IFM、WAGO、OMRON" : "Manufacturer, e.g. IFM, WAGO, OMRON";
        SearchModelText.ToolTip = zh ? "型號 / 料號" : "Model / Part Number";

        ManufacturerColumn.Header = zh ? "製造商" : "Manufacturer";
        ModelColumn.Header = zh ? "型號 / 料號" : "Model / Part Number";
        UsedColumn.Header = zh ? "使用" : "Used";
        TotalColumn.Header = zh ? "總數" : "Total";
        SpareColumn.Header = zh ? "備品" : "Spare";
        StatusColumn.Header = zh ? "狀態" : "Status";
        DetailsHeaderText.Text = zh ? "元件詳細資料（可選取 / 複製文字）" : "Component Details (selectable / copyable text)";
        DetailsText.ToolTip = zh ? "文字可選取與複製，方便查詢或核對資料" : "Select and copy text for searching or verification";
        DatabaseLabelText.Text = zh ? "SQLite 資料庫：" : "SQLite Database: ";
    }

    private void RefreshDetailsForLanguage()
    {
        if (_showingSearchPreview && _pendingSearchView is not null)
        {
            DetailsText.Text = _pendingSearchView.Details;
            return;
        }
        if (BomGrid.SelectedItem is BomViewRow selected)
        {
            DetailsText.Text = selected.Details;
            return;
        }
        DetailsText.Text = T("選取一筆 BOM 資料，或使用上方元件搜尋。", "Select a BOM row or use Component Search above.");
    }

    private void GenerateTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = T("儲存 BOM 範本", "Save BOM Template"),
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = "BOM.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx"
        };
        if (dialog.ShowDialog(this) != true) return;
        new BomTemplateGenerator().Generate(dialog.FileName);
        StatusText.Text = T($"BOM 範本已建立：{Path.GetFileName(dialog.FileName)}", $"BOM template created: {Path.GetFileName(dialog.FileName)}");
    }

    private async void ImportBom_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = T("選擇 BOM Excel", "Select BOM Excel"),
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            StatusText.Text = T("正在匯入 BOM...", "Importing BOM...");
            var import = await new BomImporter().ImportAsync(dialog.FileName);
            _importedRows = import.Rows;
            _rows.Clear();
            foreach (var row in import.Rows) _rows.Add(BomViewRow.FromImport(row, _uiLanguage));
            _pendingSearchResult = null;
            _pendingSearchView = null;
            _showingSearchPreview = false;
            AddSearchResultButton.IsEnabled = false;
            StatusText.Text = import.Errors.Count == 0
                ? T($"已匯入 {import.Rows.Count} 筆 BOM", $"Imported {import.Rows.Count} BOM rows")
                : T($"已匯入 {import.Rows.Count} 筆，{import.Errors.Count} 筆有警告", $"Imported {import.Rows.Count} rows with {import.Errors.Count} warning(s)");
            DetailsText.Text = import.Errors.Count == 0
                ? T("BOM 匯入完成。按「開始處理」執行元件解析。", "BOM import complete. Click Process BOM to resolve components.")
                : T("匯入警告：\n", "Import warnings:\n") + string.Join("\n", import.Errors);
        }
        catch (Exception exception)
        {
            StatusText.Text = T("BOM 匯入失敗", "BOM import failed");
            MessageBox.Show(this, App.FormatException(exception), T("匯入失敗", "Import failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Process_Click(object sender, RoutedEventArgs e)
    {
        if (_importedRows.Count == 0)
        {
            MessageBox.Show(this, T("請先匯入 BOM，或搜尋元件後按「加入 BOM」。", "Import a BOM first, or search for a component and click Add to BOM."), T("尚未有 BOM", "No BOM loaded"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ProcessButton.IsEnabled = false;
        try
        {
            var pipeline = ComponentRuntimeFactory.CreateOnline(_databasePath);
            for (var index = 0; index < _importedRows.Count; index++)
            {
                var row = _importedRows[index];
                StatusText.Text = T($"處理中 {index + 1}/{_importedRows.Count}：{row.Manufacturer} {row.ModelOrPartNumber}", $"Processing {index + 1}/{_importedRows.Count}: {row.Manufacturer} {row.ModelOrPartNumber}");
                var result = await pipeline.ProcessAsync(row);
                _rows[index] = BomViewRow.FromResult(row, result, _uiLanguage);
            }
            StatusText.Text = T($"處理完成：{_importedRows.Count} 筆", $"Processing complete: {_importedRows.Count} rows");
        }
        catch (Exception exception)
        {
            StatusText.Text = T("處理失敗", "Processing failed");
            MessageBox.Show(this, App.FormatException(exception), T("處理失敗", "Processing failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ProcessButton.IsEnabled = true;
        }
    }

    private async void SearchComponent_Click(object sender, RoutedEventArgs e)
    {
        var manufacturer = SearchManufacturerText.Text?.Trim();
        var model = SearchModelText.Text?.Trim();
        if (string.IsNullOrWhiteSpace(manufacturer) || string.IsNullOrWhiteSpace(model))
        {
            MessageBox.Show(this, T("請輸入製造商與型號 / 料號。", "Enter Manufacturer and Model / Part Number."), T("搜尋條件不足", "Missing search criteria"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SearchButton.IsEnabled = false;
        AddSearchResultButton.IsEnabled = false;
        _pendingSearchResult = null;
        _pendingSearchView = null;
        try
        {
            StatusText.Text = T($"搜尋中：{manufacturer} {model}", $"Searching: {manufacturer} {model}");
            var search = ComponentRuntimeFactory.CreateOnlineSearchService(_databasePath);
            var response = await search.SearchAsync(manufacturer, model);
            _pendingSearchResult = response;
            _pendingSearchView = BomViewRow.FromResult(response.Query, response.Result, _uiLanguage);
            _showingSearchPreview = true;
            BomGrid.SelectedItem = null;
            DetailsText.Text = _pendingSearchView.Details;
            AddSearchResultButton.IsEnabled = true;
            StatusText.Text = T($"搜尋完成：{LocalizeStatus(_pendingSearchView.StatusCode, UiLanguage.Chinese)}。尚未加入 BOM。", $"Search complete: {LocalizeStatus(_pendingSearchView.StatusCode, UiLanguage.English)}. Not added to BOM yet.");
        }
        catch (Exception exception)
        {
            _showingSearchPreview = false;
            StatusText.Text = T("搜尋失敗", "Search failed");
            MessageBox.Show(this, App.FormatException(exception), T("搜尋失敗", "Search failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private void AddSearchResultToBom_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingSearchResult is null) return;

        var query = _pendingSearchResult.Query;
        var addedRow = query with
        {
            RowId = $"MANUAL-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
            Notes = "Added from manual component search"
        };

        _importedRows = _importedRows.Append(addedRow).ToArray();
        var addedView = BomViewRow.FromResult(addedRow, _pendingSearchResult.Result, _uiLanguage);
        _rows.Add(addedView);
        BomGrid.SelectedItem = addedView;
        BomGrid.ScrollIntoView(addedView);

        _pendingSearchResult = null;
        _pendingSearchView = null;
        _showingSearchPreview = false;
        AddSearchResultButton.IsEnabled = false;
        DetailsText.Text = addedView.Details;
        StatusText.Text = T($"已加入 BOM：{addedRow.Manufacturer} {addedRow.ModelOrPartNumber}（使用 1 / 總數 1）", $"Added to BOM: {addedRow.Manufacturer} {addedRow.ModelOrPartNumber} (Used 1 / Total 1)");
    }

    private void BomGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BomGrid.SelectedItem is not BomViewRow row) return;
        _showingSearchPreview = false;
        DetailsText.Text = row.Details;
    }

    private sealed record BomViewRow
    {
        public required string RowId { get; init; }
        public string? Manufacturer { get; init; }
        public string? Model { get; init; }
        public int? UsedQuantity { get; init; }
        public int? TotalQuantity { get; init; }
        public int? SpareQuantity { get; init; }
        public required string StatusCode { get; init; }
        public required string Status { get; init; }
        public required string DetailsChinese { get; init; }
        public required string DetailsEnglish { get; init; }
        public required string Details { get; init; }

        public BomViewRow WithLanguage(UiLanguage language) => this with
        {
            Status = LocalizeStatus(StatusCode, language),
            Details = language == UiLanguage.Chinese ? DetailsChinese : DetailsEnglish
        };

        public static BomViewRow FromImport(BomRow row, UiLanguage language)
        {
            var statusCode = row.ImportStatus.ToString();
            var chinese = BuildImportDetails(row, UiLanguage.Chinese);
            var english = BuildImportDetails(row, UiLanguage.English);
            return new BomViewRow
            {
                RowId = row.RowId,
                Manufacturer = row.Manufacturer,
                Model = row.ModelOrPartNumber,
                UsedQuantity = row.UsedQuantity,
                TotalQuantity = row.TotalQuantity,
                SpareQuantity = row.SpareQuantity,
                StatusCode = statusCode,
                Status = LocalizeStatus(statusCode, language),
                DetailsChinese = chinese,
                DetailsEnglish = english,
                Details = language == UiLanguage.Chinese ? chinese : english
            };
        }

        public static BomViewRow FromResult(BomRow row, PipelineResult result, UiLanguage language)
        {
            var statusCode = DisplayStatusCode(result);
            var chinese = BuildResultDetails(row, result, UiLanguage.Chinese);
            var english = BuildResultDetails(row, result, UiLanguage.English);
            return new BomViewRow
            {
                RowId = row.RowId,
                Manufacturer = row.Manufacturer,
                Model = row.ModelOrPartNumber,
                UsedQuantity = row.UsedQuantity,
                TotalQuantity = row.TotalQuantity,
                SpareQuantity = row.SpareQuantity,
                StatusCode = statusCode,
                Status = LocalizeStatus(statusCode, language),
                DetailsChinese = chinese,
                DetailsEnglish = english,
                Details = language == UiLanguage.Chinese ? chinese : english
            };
        }

        private static string BuildResultDetails(BomRow row, PipelineResult result, UiLanguage language)
        {
            var zh = language == UiLanguage.Chinese;
            var component = result.Component;
            var voltage = component?.Power.OperatingVoltage;
            var details = new StringBuilder()
                .AppendLine($"{(zh ? "列 ID" : "Row ID")}: {row.RowId}")
                .AppendLine($"{(zh ? "製造商" : "Manufacturer")}: {row.Manufacturer ?? (zh ? "<缺少>" : "<missing>")}")
                .AppendLine($"{(zh ? "型號 / 料號" : "Model / Part Number")}: {row.ModelOrPartNumber ?? (zh ? "<缺少>" : "<missing>")}")
                .AppendLine($"{(zh ? "解析狀態" : "Resolution")}: {LocalizeStatus(result.ResolutionStatus.ToString(), language)}")
                .AppendLine($"{(zh ? "顯示狀態" : "Display status")}: {LocalizeStatus(DisplayStatusCode(result), language)}")
                .AppendLine($"{(zh ? "本機資料庫命中" : "Local repository hit")}: {(result.LocalRepositoryHit ? (zh ? "是" : "Yes") : (zh ? "否" : "No"))}")
                .AppendLine();

            if (component is not null)
            {
                details.AppendLine($"Component ID（元件 ID）: {component.Identity.ComponentId}")
                    .AppendLine($"{(zh ? "工作電壓" : "Voltage")}: {(voltage is null ? (zh ? "<未知>" : "<unknown>") : $"{voltage.Min}...{voltage.Max} {voltage.Unit} {voltage.Type}")}")
                    .AppendLine($"{(zh ? "輸出型態" : "Output")}: {component.Io.OutputType ?? (zh ? "<未知>" : "<unknown>")}")
                    .AppendLine($"{(zh ? "接頭" : "Connector")}: {component.Connector.Family ?? (zh ? "<未知>" : "<unknown>")} {component.Connector.Coding ?? ""} / {component.Connector.Pins?.ToString() ?? "?"} {(zh ? "腳" : "pins")}")
                    .AppendLine($"{(zh ? "腳位數" : "Pins")}: {component.Pins.Count}")
                    .AppendLine($"Wiring Readiness（接線可用性）: {LocalizeStatus(component.Readiness.Wiring.ToString(), language)}")
                    .AppendLine($"Topology Readiness（拓樸可用性）: {LocalizeStatus(component.Readiness.Topology.ToString(), language)}")
                    .AppendLine($"{(zh ? "產品頁網址" : "Product URL")}: {component.Assets.ProductPageUrl?.ToString() ?? (zh ? "<無>" : "<none>")}")
                    .AppendLine($"{(zh ? "規格書網址" : "Datasheet URL")}: {component.Assets.DatasheetUrl?.ToString() ?? (zh ? "<無>" : "<none>")}");
            }
            if (result.Verification is not null)
                details.AppendLine()
                    .AppendLine($"Verification（驗證）: {LocalizeStatus(result.Verification.Status.ToString(), language)}")
                    .AppendLine($"{(zh ? "完整度" : "Completeness")}: {result.Verification.Completeness:P0}")
                    .AppendLine($"{(zh ? "可信度" : "Confidence")}: {LocalizeStatus(result.Verification.Confidence.ToString(), language)}");
            if (result.Raw is not null && result.Raw.MissingData.Count > 0)
                details.AppendLine().AppendLine(zh ? "缺少 / 需要確認：" : "Missing / review:").AppendLine(string.Join("\n", result.Raw.MissingData.Select(issue => $"- {issue}")));
            if (result.Issues.Count > 0)
                details.AppendLine().AppendLine(zh ? "搜尋 / 解析診斷資訊：" : "Search / resolution diagnostics:").AppendLine(string.Join("\n", result.Issues.Select(issue => $"- {issue}")));
            return details.ToString();
        }

        private static string DisplayStatusCode(PipelineResult result)
        {
            if (result.LocalRepositoryHit) return "LocalReuse";
            if (result.ResolutionStatus == ResolutionStatus.Resolved) return "Resolved";
            if (result.Issues.Any(issue => issue.StartsWith(ResolutionDiagnostics.UnsupportedManufacturer, StringComparison.Ordinal))) return "UnsupportedManufacturer";
            if (result.Issues.Any(issue => issue == ResolutionDiagnostics.SearchFailed || issue.StartsWith("SOURCE_ERROR:", StringComparison.Ordinal))) return "SearchFailed";
            if (result.Issues.Any(issue => issue == ResolutionDiagnostics.CustomComponent)) return "ManualInputRequired";
            if (result.Issues.Any(issue => issue is ResolutionDiagnostics.MissingIdentity or ResolutionDiagnostics.PlaceholderIdentity)) return "WaitingForInput";
            if (result.Issues.Any(issue => issue == ResolutionDiagnostics.ProductNotFound)) return "NotFound";
            return result.ResolutionStatus.ToString();
        }

        private static string BuildImportDetails(BomRow row, UiLanguage language)
        {
            var zh = language == UiLanguage.Chinese;
            var details = new StringBuilder()
                .AppendLine($"{(zh ? "列 ID" : "Row ID")}: {row.RowId}")
                .AppendLine($"{(zh ? "製造商" : "Manufacturer")}: {row.Manufacturer ?? (zh ? "<缺少>" : "<missing>")}")
                .AppendLine($"{(zh ? "型號 / 料號" : "Model / Part Number")}: {row.ModelOrPartNumber ?? (zh ? "<缺少>" : "<missing>")}")
                .AppendLine($"{(zh ? "使用 / 總數 / 備品" : "Used / Total / Spare")}: {row.UsedQuantity?.ToString() ?? "?"} / {row.TotalQuantity?.ToString() ?? "?"} / {row.SpareQuantity?.ToString() ?? "?"}")
                .AppendLine($"{(zh ? "匯入狀態" : "Import status")}: {LocalizeStatus(row.ImportStatus.ToString(), language)}");
            if (row.ValidationFlags.Count > 0)
                details.AppendLine(zh ? "警告：" : "Warnings:").AppendLine(string.Join("\n", row.ValidationFlags.Select(flag => $"- {flag}")));
            return details.ToString();
        }
    }

    internal static string LocalizeStatus(string code, UiLanguage language)
    {
        if (language == UiLanguage.English) return code;
        return code switch
        {
            "Imported" => "已匯入",
            "ImportedWithWarnings" => "已匯入（有警告）",
            "Invalid" => "資料無效",
            "WaitingForInput" => "等待輸入",
            "Resolving" => "解析中",
            "Resolved" => "已解析",
            "Ambiguous" => "候選不明確",
            "NotFound" => "找不到",
            "Conflict" => "資料衝突",
            "Failed" => "失敗",
            "LocalReuse" => "使用本機資料",
            "UnsupportedManufacturer" => "尚未支援此製造商",
            "SearchFailed" => "搜尋失敗",
            "ManualInputRequired" => "需要人工輸入",
            "Verified" => "已驗證",
            "SingleSource" => "單一來源",
            "NotAvailable" => "資料不可取得",
            "Inferred" => "推論值",
            "UserConfirmed" => "使用者已確認",
            "Ready" => "可使用",
            "Partial" => "部分可使用",
            "NotReady" => "尚未可使用",
            "High" => "高",
            "Medium" => "中",
            "Low" => "低",
            "Unknown" => "未知",
            _ => code
        };
    }
}

internal enum UiLanguage
{
    Chinese,
    English
}
