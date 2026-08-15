using System.IO;
using System.Text;
using System.Windows;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Knowledge;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Desktop;

public partial class MainWindow
{
    private async void ShowKnowledgeDetails_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var component = await ResolveKnowledgeComponentAsync();
            if (component is null)
            {
                MessageBox.Show(
                    this,
                    T("目前沒有可顯示的 Component IR。請先搜尋或處理這個元件。", "No Component IR is available yet. Search or process this component first."),
                    T("沒有知識資料", "No knowledge data"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var pendingOcr = await new OcrReviewQueueService(_databasePath)
                .GetPendingAsync(component.Identity.Manufacturer, component.Identity.Model);
            DetailsText.Text = FormatKnowledge(component, _uiLanguage, pendingOcr);
            StatusText.Text = T(
                $"知識明細：{component.Specifications.Count} 筆規格、{component.Documents.Count} 份文件、{pendingOcr.Count} 筆 OCR 待審核",
                $"Knowledge: {component.Specifications.Count} specification(s), {component.Documents.Count} document(s), {pendingOcr.Count} OCR candidate(s) pending review");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, App.FormatException(exception), T("讀取知識失敗", "Knowledge read failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<ComponentIR?> ResolveKnowledgeComponentAsync()
    {
        if (_showingSearchPreview && _pendingSearchResult?.Result.Component is not null)
            return _pendingSearchResult.Result.Component;

        if (BomGrid.SelectedItem is not BomViewRow selected ||
            string.IsNullOrWhiteSpace(selected.Manufacturer) ||
            string.IsNullOrWhiteSpace(selected.Model))
            return null;

        return await new SqliteComponentIrRepository(_databasePath)
            .FindByIdentityAsync(selected.Manufacturer, selected.Model);
    }

    private static string FormatKnowledge(
        ComponentIR component,
        UiLanguage language,
        IReadOnlyList<OcrReviewCandidate>? pendingOcr = null)
    {
        var zh = language == UiLanguage.Chinese;
        pendingOcr ??= Array.Empty<OcrReviewCandidate>();
        var evidence = component.Specifications.SelectMany(spec => spec.Evidence).Distinct().ToArray();
        var mapped = component.Specifications.Count(spec => !string.IsNullOrWhiteSpace(spec.Key));
        var rawOnly = component.Specifications.Count - mapped;
        var sourceCount = evidence
            .Select(item => $"{item.SourceType}|{(item.DocumentUrl ?? item.SourceUrl)?.Host ?? "local"}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var pinsWithFunction = component.Pins.Count(pin => !string.IsNullOrWhiteSpace(pin.Function));
        var pinCoverage = component.Pins.Count == 0 ? 0m : pinsWithFunction / (decimal)component.Pins.Count;
        var structuredEvidence = evidence.Count(item => item.ExtractionMethod is ExtractionMethod.StructuredJson or ExtractionMethod.JsonLd);
        var pdfTableEvidence = evidence.Count(item => item.ExtractionMethod == ExtractionMethod.TableParser && item.DocumentUrl is not null);
        var ocrEvidence = evidence.Count(item => item.ExtractionMethod == ExtractionMethod.OcrText);

        var text = new StringBuilder();
        text.AppendLine(zh ? "=== 元件知識明細 / Knowledge Inspector ===" : "=== Component Knowledge Inspector ===")
            .AppendLine($"Manufacturer（製造商）: {component.Identity.Manufacturer}")
            .AppendLine($"Model（型號）: {component.Identity.Model}")
            .AppendLine($"Component ID（元件 ID）: {component.Identity.ComponentId}")
            .AppendLine($"Product URL（產品頁）: {component.Assets.ProductPageUrl?.ToString() ?? "<none>"}")
            .AppendLine($"Datasheet URL（規格書）: {component.Assets.DatasheetUrl?.ToString() ?? "<none>"}")
            .AppendLine();

        text.AppendLine(zh ? "=== 知識品質摘要 / Knowledge Quality ===" : "=== Knowledge Quality Summary ===")
            .AppendLine($"Specifications（總規格）: {component.Specifications.Count}")
            .AppendLine($"Normalized（已正規化）: {mapped}")
            .AppendLine($"Raw only（尚未正規化）: {rawOnly}")
            .AppendLine($"Documents（文件）: {component.Documents.Count}")
            .AppendLine($"Independent sources（獨立來源）: {sourceCount}")
            .AppendLine($"Pins with function（已知腳位功能）: {pinsWithFunction}/{component.Pins.Count} ({pinCoverage:P0})")
            .AppendLine($"Structured JSON / JSON-LD（結構化資料證據）: {structuredEvidence}")
            .AppendLine($"PDF Table（PDF 表格證據）: {pdfTableEvidence}")
            .AppendLine($"OCR Text（OCR 文字證據）: {ocrEvidence}")
            .AppendLine($"OCR Review Queue（OCR 待審核候選）: {pendingOcr.Count}")
            .AppendLine();

        if (component.Specifications.Count == 0)
        {
            text.AppendLine(zh
                ? "尚未保存技術規格。這通常代表舊版稀疏快取；請使用「深度搜尋」重新抓取。"
                : "No technical specifications are stored. This usually indicates a legacy sparse cache; use Deep Search to refresh it.");
        }
        else
        {
            text.AppendLine(zh ? "=== 技術規格 / Technical Specifications ===" : "=== Technical Specifications ===");
            foreach (var group in component.Specifications
                         .OrderBy(spec => spec.Section ?? string.Empty)
                         .ThenBy(spec => spec.Name)
                         .GroupBy(spec => string.IsNullOrWhiteSpace(spec.Section) ? (zh ? "未分類" : "Uncategorized") : spec.Section!))
            {
                text.AppendLine().AppendLine($"[{group.Key}]");
                foreach (var spec in group)
                {
                    text.Append("- ").Append(spec.Name).Append(": ").AppendLine(spec.Value ?? "<unknown>");
                    text.Append("  Key: ").AppendLine(spec.Key ?? (zh ? "<原始資料，尚未正規化>" : "<raw / not normalized yet>"));
                    text.Append("  Status（狀態）: ").AppendLine(LocalizeStatus(spec.Status.ToString(), language));
                    var sources = spec.Evidence
                        .Select(FormatEvidenceSource)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    text.Append("  Evidence（證據）: ")
                        .AppendLine(sources.Length == 0 ? "<none>" : string.Join(" | ", sources));
                }
            }
        }

        text.AppendLine().AppendLine(zh ? "=== OCR 待審核候選 / OCR Review Queue ===" : "=== OCR Review Queue ===");
        if (pendingOcr.Count == 0)
        {
            text.AppendLine(zh ? "<目前沒有 OCR 待審核候選>" : "<No OCR candidates are awaiting review>");
        }
        else
        {
            text.AppendLine(zh
                ? "注意：以下資料由圖片文字辨識產生，尚未自動升級為正式 Pin Function（腳位功能）或 Verified（已驗證）工程事實。"
                : "Note: the following values were recognized from images and have not been promoted automatically to formal pin functions or Verified engineering facts.");
            foreach (var candidate in pendingOcr.Take(80))
            {
                text.Append("- p.").Append(candidate.PageNumber)
                    .Append(" | ").Append(candidate.ProposedKey ?? "<raw>")
                    .Append(" | ").Append(candidate.RawName)
                    .Append(" = ").Append(candidate.RawValue ?? "<unknown>")
                    .Append(" | ").Append(candidate.Engine)
                    .Append(" | ").AppendLine(Path.GetFileName(candidate.SourcePath));
            }
            if (pendingOcr.Count > 80)
                text.AppendLine(zh ? $"...另外還有 {pendingOcr.Count - 80} 筆候選。" : $"...and {pendingOcr.Count - 80} more candidate(s).");
        }

        text.AppendLine().AppendLine(zh ? "=== 腳位 / Pins ===" : "=== Pins ===");
        if (component.Pins.Count == 0)
        {
            text.AppendLine(zh ? "<尚未取得腳位資料>" : "<No pin data captured yet>");
        }
        else
        {
            foreach (var pin in component.Pins)
                text.Append("- Pin ").Append(pin.PinNumber)
                    .Append(" = ").Append(pin.Function ?? (zh ? "<功能未知>" : "<function unknown>"))
                    .Append(" | ").Append(pin.SignalType ?? "?")
                    .Append(" | ").AppendLine(pin.Direction ?? "?");
        }

        text.AppendLine().AppendLine(zh ? "=== 文件 / Documents ===" : "=== Documents ===");
        if (component.Documents.Count == 0)
        {
            text.AppendLine(zh ? "<尚未找到或保存文件>" : "<No document discovered or stored yet>");
        }
        else
        {
            foreach (var document in component.Documents.OrderBy(document => document.Type))
            {
                text.Append("- ").Append(document.Type)
                    .Append(" | ").Append(document.SourceType)
                    .Append(" | ").AppendLine(document.Url.ToString());
                if (!string.IsNullOrWhiteSpace(document.Sha256))
                    text.Append("  SHA256: ").AppendLine(document.Sha256);
            }
        }

        text.AppendLine().AppendLine(zh ? "=== 可信來源層級 / Trust Order ===" : "=== Source Trust Order ===")
            .AppendLine("Manufacturer Datasheet > Manufacturer Manual > Manufacturer Product Page > Manufacturer Download Center > User File > Authorized Distributor > Trusted Third Party > Generic Web > AI Inference")
            .AppendLine(zh
                ? "同一來源內：Native Structured/PDF Text（原生結構化／PDF 文字）優先於 OCR Text（OCR 文字）。"
                : "Within the same source: native structured/PDF text is preferred over OCR text.");

        return text.ToString();
    }

    private static string FormatEvidenceSource(Evidence evidence)
    {
        var uri = evidence.DocumentUrl ?? evidence.SourceUrl;
        var location = uri is null ? string.Empty : $"@{uri.Host}";
        var page = evidence.PageNumber is null ? string.Empty : $" p.{evidence.PageNumber}";
        return $"{evidence.SourceType}/{evidence.ExtractionMethod}{location}{page}";
    }
}
