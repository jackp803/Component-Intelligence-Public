using System.Text;

namespace ComponentIntelligence.Extraction;

/// <summary>
/// Machine-friendly Markdown representation of an engineering PDF.
/// The Markdown is an intermediate document, not a replacement for source evidence: page numbers,
/// structured rows and OCR text remain explicitly separated so downstream parsers can keep provenance.
/// </summary>
public sealed record EngineeringMarkdownPage(int PageNumber, string Markdown);

public sealed record EngineeringMarkdownDocument(
    string Markdown,
    IReadOnlyList<EngineeringMarkdownPage> Pages,
    IReadOnlyList<string> Diagnostics);

public sealed class EngineeringMarkdownBuilder
{
    public EngineeringMarkdownDocument Build(
        IReadOnlyList<PdfPageText> pages,
        IReadOnlyList<PdfTableRow>? tableRows = null,
        IReadOnlyDictionary<int, string>? ocrTextByPage = null)
    {
        ArgumentNullException.ThrowIfNull(pages);
        tableRows ??= Array.Empty<PdfTableRow>();
        ocrTextByPage ??= new Dictionary<int, string>();

        var pageDocuments = new List<EngineeringMarkdownPage>(pages.Count);
        foreach (var page in pages.OrderBy(page => page.PageNumber))
        {
            var markdown = BuildPageMarkdown(
                page,
                tableRows.Where(row => row.PageNumber == page.PageNumber).ToArray(),
                ocrTextByPage.TryGetValue(page.PageNumber, out var ocr) ? ocr : null);
            pageDocuments.Add(new EngineeringMarkdownPage(page.PageNumber, markdown));
        }

        var full = new StringBuilder();
        full.AppendLine("# Engineering Document");
        full.AppendLine();
        foreach (var page in pageDocuments)
        {
            full.Append(page.Markdown);
            if (!page.Markdown.EndsWith('\n')) full.AppendLine();
            full.AppendLine();
        }

        return new EngineeringMarkdownDocument(
            full.ToString().TrimEnd() + Environment.NewLine,
            pageDocuments,
            [
                $"ENGINEERING_MARKDOWN_PAGES:{pageDocuments.Count}",
                $"ENGINEERING_MARKDOWN_STRUCTURED_ROWS:{tableRows.Count}",
                $"ENGINEERING_MARKDOWN_OCR_PAGES:{ocrTextByPage.Count}"
            ]);
    }

    public string BuildPageMarkdown(
        PdfPageText page,
        IReadOnlyList<PdfTableRow>? tableRows = null,
        string? ocrText = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        tableRows ??= Array.Empty<PdfTableRow>();

        var builder = new StringBuilder();
        builder.AppendLine($"## Page {page.PageNumber}");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(page.Text))
        {
            builder.AppendLine("### Native Text");
            builder.AppendLine();
            builder.AppendLine(NormalizeText(page.Text));
            builder.AppendLine();
        }

        if (tableRows.Count > 0)
        {
            builder.AppendLine("### Structured Rows");
            builder.AppendLine();
            builder.AppendLine("| Label | Value | Cells |");
            builder.AppendLine("|---|---|---|");
            foreach (var row in tableRows)
            {
                builder.Append('|')
                    .Append(' ').Append(EscapeCell(row.Label))
                    .Append(" | ").Append(EscapeCell(row.Value))
                    .Append(" | ").Append(EscapeCell(string.Join(" / ", row.Cells)))
                    .AppendLine(" |");
            }
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(ocrText))
        {
            builder.AppendLine("### OCR Text");
            builder.AppendLine();
            builder.AppendLine(NormalizeText(ocrText));
            builder.AppendLine();
        }

        if (string.IsNullOrWhiteSpace(page.Text) && tableRows.Count == 0 && string.IsNullOrWhiteSpace(ocrText))
            builder.AppendLine("_No machine-readable text recovered on this page._");

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    public string BuildOcrPageMarkdown(int pageNumber, string ocrText) =>
        BuildPageMarkdown(new PdfPageText(pageNumber, string.Empty), Array.Empty<PdfTableRow>(), ocrText);

    private static string NormalizeText(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

    private static string EscapeCell(string? value) =>
        (value ?? string.Empty)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
}
