using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Knowledge;
using ComponentIntelligence.Repository;
using ComponentIntelligence.Runtime;
using Microsoft.Win32;

namespace ComponentIntelligence.Desktop;

public sealed class ComponentDataCompletionDialog : Window
{
    private readonly string _databasePath;
    private readonly ComponentInstance _instance;
    private ComponentIR? _component;
    private readonly TextBlock _identity = new();
    private readonly TextBlock _readiness = new();
    private readonly TextBox _missing = new();
    private readonly TextBox _url = new();
    private readonly TextBlock _status = new();
    private readonly Button _deepSearch = new();
    private readonly Button _upload = new();
    private readonly Button _addUrl = new();
    private readonly Button _copyGptPrompt = new();
    private readonly Button _manualEdit = new();
    private readonly Button _syncNotion = new();
    private readonly Button _reloadNotion = new();

    public ComponentDataCompletionDialog(string databasePath, ComponentInstance instance, ComponentIR? component)
    {
        _databasePath = databasePath;
        _instance = instance;
        _component = component;

        Title = $"補元件資料 / Complete Component Data - {instance.ReferenceDesignator ?? instance.DisplayName ?? instance.ComponentInstanceId}";
        Width = 900;
        Height = 760;
        MinWidth = 720;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = "缺什麼就從這裡補什麼", FontSize = 22, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = "PDF、圖片、手冊、截圖或其他工程文件都可以直接加入；也可人工修正並同步到 Notion 中央電料庫。元件知識更新後會重新整理 Component IR（元件中介資料）與拓樸 Readiness（就緒度），專案位置、Reference 與既有接線不會被重設。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 5, 0, 0)
        });
        root.Children.Add(header);

        var summary = new Border
        {
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var summaryPanel = new StackPanel();
        _identity.FontWeight = FontWeights.SemiBold;
        _readiness.Margin = new Thickness(0, 4, 0, 0);
        summaryPanel.Children.Add(_identity);
        summaryPanel.Children.Add(_readiness);
        summary.Child = summaryPanel;
        Grid.SetRow(summary, 1);
        root.Children.Add(summary);

        var center = new Grid();
        center.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        center.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        center.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        center.Children.Add(new TextBlock { Text = "目前缺口 / Missing data", FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 0, 6) });

        _missing.IsReadOnly = true;
        _missing.AcceptsReturn = true;
        _missing.TextWrapping = TextWrapping.Wrap;
        _missing.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _missing.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        _missing.Padding = new Thickness(8);
        Grid.SetRow(_missing, 1);
        center.Children.Add(_missing);

        var buttonPanel = new WrapPanel { Margin = new Thickness(0, 12, 0, 8) };
        ConfigureButton(_upload, "上傳 PDF / 圖片 / 文件", Upload_Click);
        ConfigureButton(_deepSearch, "重新深度搜尋 / Deep Search", DeepSearch_Click);
        ConfigureButton(_manualEdit, "人工修正 / Edit", ManualEdit_Click);
        ConfigureButton(_syncNotion, "同步到 Notion / Sync", SyncNotion_Click);
        ConfigureButton(_reloadNotion, "從 Notion 重新載入 / Reload", ReloadNotion_Click);
        ConfigureButton(_copyGptPrompt, "複製 GPT 特製料歸檔提示詞", CopyGptPrompt_Click);
        _copyGptPrompt.ToolTip = "貼到任何新的 GPT 聊天室，再附上廠商 PDF、圖面、照片或規格。GPT 會依 Component Intelligence 固定格式整理並在可用時寫入 Notion。";
        buttonPanel.Children.Add(_upload);
        buttonPanel.Children.Add(_deepSearch);
        buttonPanel.Children.Add(_manualEdit);
        buttonPanel.Children.Add(_syncNotion);
        buttonPanel.Children.Add(_reloadNotion);
        buttonPanel.Children.Add(_copyGptPrompt);
        Grid.SetRow(buttonPanel, 2);
        center.Children.Add(buttonPanel);

        var urlPanel = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        urlPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        urlPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _url.MinHeight = 30;
        _url.VerticalContentAlignment = VerticalAlignment.Center;
        _url.ToolTip = "可貼 PDF、圖片或公開文件網址；一般產品頁建議使用 Deep Search。";
        _addUrl.Content = "加入網址文件";
        _addUrl.Padding = new Thickness(12, 6, 12, 6);
        _addUrl.Margin = new Thickness(8, 0, 0, 0);
        _addUrl.Click += AddUrl_Click;
        Grid.SetColumn(_addUrl, 1);
        urlPanel.Children.Add(_url);
        urlPanel.Children.Add(_addUrl);
        Grid.SetRow(urlPanel, 3);
        center.Children.Add(urlPanel);

        Grid.SetRow(center, 2);
        root.Children.Add(center);

        _status.Text = "可先看缺口，再決定要上傳、人工修正或同步什麼。Notion 無連線時，本機修正仍會保存並留下待同步狀態。";
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = System.Windows.Media.Brushes.DimGray;
        _status.Margin = new Thickness(0, 10, 0, 10);
        Grid.SetRow(_status, 3);
        root.Children.Add(_status);

        var close = new Button
        {
            Content = "完成 / Close",
            Padding = new Thickness(18, 7, 18, 7),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
            IsCancel = true
        };
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 4);
        root.Children.Add(close);

        Content = root;
        RefreshSummary();
    }

    public bool KnowledgeChanged { get; private set; }
    public ComponentIR? LatestComponent => _component;

    private static void ConfigureButton(Button button, string content, RoutedEventHandler handler)
    {
        button.Content = content;
        button.Padding = new Thickness(12, 7, 12, 7);
        button.Margin = new Thickness(0, 0, 8, 6);
        button.Click += handler;
    }

    private void CopyGptPrompt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var identity = ResolveIdentity();
            var prompt = VendorPartIntakePrompt.Build(identity.Manufacturer, identity.Model, _instance.ComponentDefinitionId);
            Clipboard.SetText(prompt);
            _status.Text = "已複製 Vendor Part Intake（廠商／特製料歸檔）GPT 提示詞。到新的 GPT 聊天室貼上後，再附上廠商 PDF、圖面、照片、規格或 BOM 資料即可。";
        }
        catch (Exception exception)
        {
            _status.Text = "複製提示詞失敗。";
            MessageBox.Show(this, App.FormatException(exception), "無法複製提示詞", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ManualEdit_Click(object sender, RoutedEventArgs e)
    {
        await WithBusyAsync(async () =>
        {
            var component = _component ?? CreateMinimalComponentFromIdentity();
            if (component is null)
                throw new InvalidOperationException("人工建檔前至少需要 Manufacturer + Model / Part Number。請先從 BOM 補上身分，或使用 GPT 特製料歸檔提示詞整理身分資料。");

            var dialog = new ComponentManualEditorDialog(component) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.EditedComponent is null) return;

            var sync = ComponentRuntimeFactory.CreateKnowledgeSyncService(_databasePath);
            var result = await sync.SaveLocalAsync(dialog.EditedComponent);
            _component = dialog.EditedComponent;
            KnowledgeChanged = true;
            _status.Text = result.LocalSaved
                ? "人工修正已保存到本機。若要更新中央電料庫，按「同步到 Notion」。"
                : "人工修正未保存。";
        });
    }

    private async void SyncNotion_Click(object sender, RoutedEventArgs e)
    {
        await WithBusyAsync(async () =>
        {
            var component = _component ?? CreateMinimalComponentFromIdentity();
            if (component is null)
                throw new InvalidOperationException("目前沒有可同步的元件身分或 Component IR。至少需要 Manufacturer + Model / Part Number。");

            var sync = ComponentRuntimeFactory.CreateKnowledgeSyncService(_databasePath);
            var result = await sync.SaveAndSyncAsync(component);
            _component = component;
            KnowledgeChanged = true;
            _status.Text = result.Status switch
            {
                ComponentSyncStatus.Synced => "✓ 本機與 Notion 中央電料庫已同步。",
                ComponentSyncStatus.Conflict => "⚠ 本機修正已保存，但中央 Verified 資料有衝突，未自動覆蓋。\n" + string.Join(Environment.NewLine, result.Conflicts),
                ComponentSyncStatus.Pending => "本機已保存；Notion 尚未完成同步，狀態為 Pending Sync（待同步）。有網路／Token 後可再次按同步。",
                _ => "本機已保存；中央同步狀態：" + result.Status
            };
        });
    }

    private async void ReloadNotion_Click(object sender, RoutedEventArgs e)
    {
        await WithBusyAsync(async () =>
        {
            var identity = ResolveIdentity();
            if (string.IsNullOrWhiteSpace(identity.Manufacturer) || string.IsNullOrWhiteSpace(identity.Model))
                throw new InvalidOperationException("從 Notion 載入前需要 Manufacturer + Model / Part Number。");

            var sync = ComponentRuntimeFactory.CreateKnowledgeSyncService(_databasePath);
            var lookup = await sync.ReloadCentralAsync(identity.Manufacturer, identity.Model, saveToLocal: true);
            if (lookup.Component is null)
            {
                _status.Text = lookup.Diagnostics.Contains("NOTION_CENTRAL_DISABLED_NO_TOKEN")
                    ? "Notion 尚未連線；本機資料不受影響。"
                    : "Notion 中央電料庫沒有找到這個 Manufacturer + Model。";
                return;
            }
            _component = lookup.Component;
            KnowledgeChanged = true;
            _status.Text = "✓ 已從 Notion 重新載入中央元件知識並更新本機快取。";
        });
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "選擇要補給這顆元件的資料",
            Filter = "Engineering data|*.pdf;*.txt;*.md;*.csv;*.json;*.xml;*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff|PDF (*.pdf)|*.pdf|Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff|All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        await WithBusyAsync(async () =>
        {
            var identity = ResolveIdentity();
            var importer = new ManualKnowledgeImportService(_databasePath);
            var ocr = new OcrReviewQueueService(_databasePath);
            var importedFields = 0;
            var ocrCandidates = 0;
            foreach (var file in dialog.FileNames)
            {
                _status.Text = $"讀取：{System.IO.Path.GetFileName(file)}";
                var result = await importer.ImportAsync(_instance.ComponentDefinitionId, identity.Manufacturer, identity.Model, file);
                importedFields += result.ExtractedSpecificationCount;
                var review = await ocr.AnalyzeAsync(_instance.ComponentDefinitionId, identity.Manufacturer, identity.Model, result.StoredPath);
                ocrCandidates += review.CandidateCount;
                if (result.Component is not null) _component = result.Component;
            }
            KnowledgeChanged = true;
            await RefreshFromSearchAsync(forceRefresh: false);
            _status.Text = $"已加入 {dialog.FileNames.Length} 個檔案；直接解析 {importedFields} 個欄位；OCR 待審核候選 {ocrCandidates}。";
        });
    }

    private async void DeepSearch_Click(object sender, RoutedEventArgs e)
    {
        await WithBusyAsync(async () =>
        {
            var identity = ResolveIdentity();
            if (string.IsNullOrWhiteSpace(identity.Manufacturer) || string.IsNullOrWhiteSpace(identity.Model))
                throw new InvalidOperationException("這顆元件目前缺少 Manufacturer / Model，請先從主 BOM 或人工資料補上身份。");
            _status.Text = $"深度搜尋：{identity.Manufacturer} {identity.Model}";
            await RefreshFromSearchAsync(forceRefresh: true);
            KnowledgeChanged = true;
            _status.Text = "深度搜尋完成；已重新整理元件資料與 Readiness。";
        });
    }

    private async void AddUrl_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(_url.Text?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            MessageBox.Show(this, "請貼入有效的 http/https PDF、圖片或文件網址。", "網址格式不正確", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await WithBusyAsync(async () =>
        {
            _status.Text = $"正在下載：{uri.Host}";
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ComponentIntelligence", "1.0"));
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var extension = ResolveExtension(uri, response.Content.Headers.ContentType?.MediaType);
            if (extension == ".html")
                throw new InvalidOperationException("這是一般網頁，不是可直接加入的文件。請按「重新深度搜尋」，系統會從產品頁繼續找 Downloads / Datasheet。");

            var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"component-intelligence-{Guid.NewGuid():N}{extension}");
            try
            {
                await using (var output = System.IO.File.Create(temp)) await response.Content.CopyToAsync(output);
                var identity = ResolveIdentity();
                var importer = new ManualKnowledgeImportService(_databasePath);
                var result = await importer.ImportAsync(_instance.ComponentDefinitionId, identity.Manufacturer, identity.Model, temp);
                if (result.Component is not null) _component = result.Component;
                var ocr = await new OcrReviewQueueService(_databasePath).AnalyzeAsync(_instance.ComponentDefinitionId, identity.Manufacturer, identity.Model, result.StoredPath);
                KnowledgeChanged = true;
                await RefreshFromSearchAsync(forceRefresh: false);
                _status.Text = $"網址文件已加入；解析 {result.ExtractedSpecificationCount} 欄；OCR 候選 {ocr.CandidateCount}。";
                _url.Clear();
            }
            finally
            {
                try { if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp); } catch { }
            }
        });
    }

    private async Task RefreshFromSearchAsync(bool forceRefresh)
    {
        var identity = ResolveIdentity();
        if (string.IsNullOrWhiteSpace(identity.Manufacturer) || string.IsNullOrWhiteSpace(identity.Model))
        {
            RefreshSummary();
            return;
        }
        var search = ComponentRuntimeFactory.CreateOnlineSearchService(_databasePath);
        var response = await search.SearchAsync(identity.Manufacturer, identity.Model, forceRefresh: forceRefresh);
        if (response.Result.Component is not null) _component = response.Result.Component;
        RefreshSummary();
    }

    private async Task WithBusyAsync(Func<Task> action)
    {
        foreach (var button in BusyButtons()) button.IsEnabled = false;
        try { await action(); }
        catch (Exception exception)
        {
            _status.Text = "操作失敗。";
            MessageBox.Show(this, App.FormatException(exception), "補資料失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            foreach (var button in BusyButtons()) button.IsEnabled = true;
            RefreshSummary();
        }
    }

    private IEnumerable<Button> BusyButtons()
    {
        yield return _upload;
        yield return _deepSearch;
        yield return _addUrl;
        yield return _manualEdit;
        yield return _syncNotion;
        yield return _reloadNotion;
    }

    private void RefreshSummary()
    {
        var identity = ResolveIdentity();
        _identity.Text = $"{_instance.ReferenceDesignator ?? "(no ref)"}  |  {identity.Manufacturer ?? "Manufacturer ?"}  {identity.Model ?? "Model ?"}";
        _readiness.Text = _component is null
            ? "Component IR：尚未找到；仍可用人工修正建立特製料資料。"
            : $"Readiness｜Topology: {_component.Readiness.Topology}  Wiring: {_component.Readiness.Wiring}  Validation: {_component.Readiness.Validation}  Drawing: {_component.Readiness.Drawing}";
        _missing.Text = BuildMissingSummary();
    }

    private string BuildMissingSummary()
    {
        var missing = new List<string>();
        if (_component is null)
        {
            missing.Add("- Component IR（元件資料）尚未建立 / 找不到本機快照");
            if (_instance.Ports.Count == 0) missing.Add("- Port（接口）未知");
            missing.Add("- 可按「人工修正 / Edit」從 Manufacturer + Model 建立最小特製料資料");
            return string.Join(Environment.NewLine, missing);
        }
        if (_component.Assets.ProductPageUrl is null) missing.Add("- 原廠產品頁 URL");
        if (_component.Assets.DatasheetUrl is null && !_component.Documents.Any()) missing.Add("- Datasheet / Manual（規格書／手冊）");
        if (_component.Assets.ImageUrl is null) missing.Add("- Product Image（元件圖片）");
        if (_component.Ports.Count == 0) missing.Add("- Port（接口）結構");
        foreach (var port in _component.Ports)
        {
            if (string.IsNullOrWhiteSpace(port.Protocol) && string.IsNullOrWhiteSpace(port.SignalType)) missing.Add($"- Port {port.PortId}: Protocol / Signal Type 未知");
            if (string.IsNullOrWhiteSpace(port.ConnectorFamily)) missing.Add($"- Port {port.PortId}: Connector 未知");
        }
        foreach (var pin in _component.Pins.Where(pin => string.IsNullOrWhiteSpace(pin.Function)).Take(12)) missing.Add($"- Pin {pin.PinNumber}: Function 未確認");
        if (_component.Readiness.Topology != ReadinessStatus.Ready) missing.Add("- Topology Readiness 尚未 Ready：系統仍應繼續搜尋或人工補資料");
        if (_component.Readiness.Wiring != ReadinessStatus.Ready) missing.Add("- Wiring Readiness 尚未 Ready：接線資料仍不完整");
        return missing.Count == 0
            ? "✓ 目前沒有明顯資料缺口。仍可加入圖片、PDF 或其他文件作為額外 Evidence（證據）。"
            : string.Join(Environment.NewLine, missing.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private ComponentIR? CreateMinimalComponentFromIdentity()
    {
        var identity = ResolveIdentity();
        if (string.IsNullOrWhiteSpace(identity.Manufacturer) || string.IsNullOrWhiteSpace(identity.Model)) return null;
        return new ComponentIR
        {
            Identity = new ComponentIrIdentity
            {
                ComponentId = _instance.ComponentDefinitionId,
                Manufacturer = identity.Manufacturer,
                Model = identity.Model,
                Mpn = identity.Model
            },
            Readiness = new ComponentReadiness
            {
                Topology = ReadinessStatus.NotReady,
                Wiring = ReadinessStatus.NotReady,
                Validation = ReadinessStatus.NotReady,
                Drawing = ReadinessStatus.NotReady
            }
        };
    }

    private (string? Manufacturer, string? Model) ResolveIdentity()
    {
        if (_component is not null) return (_component.Identity.Manufacturer, _component.Identity.Model);
        var tokens = (_instance.DisplayName ?? string.Empty).Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length == 2 ? (tokens[0], tokens[1]) : (null, null);
    }

    private static string ResolveExtension(Uri uri, string? mediaType)
    {
        var extension = System.IO.Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        if (extension is ".pdf" or ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".tif" or ".tiff" or ".txt" or ".csv" or ".json" or ".xml" or ".md") return extension;
        return mediaType?.ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/tiff" => ".tiff",
            "text/plain" => ".txt",
            "text/csv" => ".csv",
            "application/json" => ".json",
            "application/xml" or "text/xml" => ".xml",
            "text/html" => ".html",
            _ => ".bin"
        };
    }
}
