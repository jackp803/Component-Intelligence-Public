using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Verification;

namespace ComponentIntelligence.Desktop;

public partial class MainWindow
{
    private async void CompareSources_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var component = await ResolveKnowledgeComponentAsync();
            if (component is null)
            {
                MessageBox.Show(
                    this,
                    T("目前沒有可比較的 Component IR。請先搜尋或處理元件。", "No Component IR is available for comparison. Search or process the component first."),
                    T("沒有來源資料", "No source data"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            DetailsText.Text = FormatSourceComparison(component, _uiLanguage);
            StatusText.Text = T("已顯示來源比較。", "Source comparison is displayed.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, App.FormatException(exception), T("來源比較失敗", "Source comparison failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string FormatSourceComparison(ComponentIR component, UiLanguage language)
    {
        var zh = language == UiLanguage.Chinese;
        var text = new StringBuilder();
        text.AppendLine(zh ? "=== 來源比較 / Source Comparison ===" : "=== Source Comparison ===")
            .AppendLine($"Manufacturer（製造商）: {component.Identity.Manufacturer}")
            .AppendLine($"Model（型號）: {component.Identity.Model}")
            .AppendLine();

        var comparable = component.Specifications
            .Where(spec => !string.IsNullOrWhiteSpace(spec.Value))
            .GroupBy(spec => string.IsNullOrWhiteSpace(spec.Key) ? $"RAW:{spec.Name}" : spec.Key!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (comparable.Length == 0)
        {
            text.AppendLine(zh ? "<目前沒有可比較的規格資料>" : "<No comparable specification data yet>");
            return text.ToString();
        }

        var differingFields = 0;
        var corroboratedFields = 0;
        foreach (var group in comparable)
        {
            var variants = group
                .GroupBy(spec => NormalizeComparisonValue(spec.Value), StringComparer.OrdinalIgnoreCase)
                .Select(valueGroup => new SourceVariant(
                    valueGroup.First().Value ?? string.Empty,
                    valueGroup
                        .SelectMany(spec => spec.Evidence)
                        .Distinct()
                        .OrderByDescending(evidence => SourceTrustPolicy.Score(evidence.SourceType))
                        .ToArray(),
                    valueGroup.Max(spec => SourceTrustPolicy.Score(spec.Evidence))))
                .OrderByDescending(variant => variant.TrustScore)
                .ThenBy(variant => variant.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var independentSources = variants
                .SelectMany(variant => variant.Evidence)
                .Select(EvidenceIdentity)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var differing = variants.Length > 1;
            var corroborated = variants.Length == 1 && independentSources >= 2;
            if (differing) differingFields++;
            if (corroborated) corroboratedFields++;

            var displayName = group.Select(spec => spec.Name).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? group.Key;
            text.AppendLine($"[{group.Key}] {displayName}");
            text.AppendLine(differing
                ? (zh ? "  ⚠ 不同來源存在不同值；請查看下列 Evidence，正式衝突狀態由 Verification Engine 判定。" : "  ⚠ Different values exist across sources; inspect the evidence below. Formal conflict status is decided by the Verification Engine.")
                : corroborated
                    ? (zh ? "  ✓ 至少兩個獨立來源提供相同值。" : "  ✓ At least two independent sources provide the same value.")
                    : (zh ? "  • 目前只有單一值／證據來源不足以交叉佐證。" : "  • A single value is available; evidence is not yet sufficient for cross-source corroboration."));

            for (var index = 0; index < variants.Length; index++)
            {
                var variant = variants[index];
                var best = index == 0 ? (zh ? " ← 正式欄位優先候選" : " <- preferred candidate for normalized field") : string.Empty;
                text.Append("  ").Append(index + 1).Append(") ").Append(variant.Value)
                    .Append("  [Trust ").Append(variant.TrustScore).Append(']').AppendLine(best);

                if (variant.Evidence.Count == 0)
                {
                    text.AppendLine(zh ? "     Evidence: <無來源資料>" : "     Evidence: <none>");
                    continue;
                }

                foreach (var evidence in variant.Evidence)
                {
                    var uri = evidence.DocumentUrl ?? evidence.SourceUrl;
                    text.Append("     - ")
                        .Append(evidence.SourceType)
                        .Append(" / ").Append(evidence.ExtractionMethod)
                        .Append(" / Trust ").Append(SourceTrustPolicy.Score(evidence.SourceType));
                    if (uri is not null) text.Append(" / ").Append(uri.Host);
                    if (evidence.PageNumber is not null) text.Append(" / p.").Append(evidence.PageNumber);
                    text.AppendLine();
                    if (uri is not null) text.Append("       ").AppendLine(uri.ToString());
                }
            }
            text.AppendLine();
        }

        text.Insert(0,
            (zh
                ? $"摘要：不同值欄位 {differingFields}；已交叉佐證欄位 {corroboratedFields}。\n"
                : $"Summary: {differingFields} field(s) with differing values; {corroboratedFields} corroborated field(s).\n"));
        text.AppendLine(zh ? "=== Trust Order（可信順序）===" : "=== Trust Order ===")
            .AppendLine("Manufacturer Datasheet > Manufacturer Manual > Manufacturer Product Page > Manufacturer Download Center > User File > Authorized Distributor > Trusted Third Party > Generic Web > AI Inference");
        return text.ToString();
    }

    private static string NormalizeComparisonValue(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant()
            .Replace('…', '.')
            .Replace('～', '-')
            .Replace("VDC", "V DC", StringComparison.OrdinalIgnoreCase)
            .Replace("VAC", "V AC", StringComparison.OrdinalIgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+", " ");
        normalized = Regex.Replace(normalized, @"\s*\.\.\.\s*", "...");
        return normalized;
    }

    private static string EvidenceIdentity(Evidence evidence)
    {
        var uri = evidence.DocumentUrl ?? evidence.SourceUrl;
        if (evidence.SourceType is ComponentSourceType.ManufacturerDatasheet or ComponentSourceType.ManufacturerManual or ComponentSourceType.ManufacturerProductPage or ComponentSourceType.ManufacturerDownloadCenter)
            return $"MANUFACTURER:{evidence.SourceType}:{uri?.Host ?? "unknown"}";
        if (evidence.SourceType == ComponentSourceType.User)
            return $"USER:{evidence.DocumentHashSha256 ?? uri?.AbsoluteUri ?? "manual"}";
        return $"{evidence.SourceType}:{uri?.Host ?? "unknown"}";
    }

    private sealed record SourceVariant(string Value, IReadOnlyList<Evidence> Evidence, int TrustScore);
}
