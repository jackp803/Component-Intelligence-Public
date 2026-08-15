using UglyToad.PdfPig;

namespace ComponentIntelligence.Extraction;

public sealed record PdfPageText(int PageNumber, string Text);

public sealed class PdfTextExtractor
{
    public IReadOnlyList<PdfPageText> Extract(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var pages = new List<PdfPageText>();
        using var document = PdfDocument.Open(filePath);
        foreach (var page in document.GetPages())
            pages.Add(new PdfPageText(page.Number, page.Text ?? string.Empty));
        return pages;
    }
}
