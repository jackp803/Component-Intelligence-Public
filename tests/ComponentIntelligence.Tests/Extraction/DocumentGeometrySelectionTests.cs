using ComponentIntelligence.Extraction;

namespace ComponentIntelligence.Tests.Extraction;

public sealed class DocumentGeometrySelectionTests
{
    [Fact]
    public void VisualScan_selects_embedded_image_topology_hint_and_short_pages()
    {
        var pages = new[]
        {
            new PdfPageText(1, new string('A', 600)),
            new PdfPageText(2, new string('B', 600)),
            new PdfPageText(3, new string('C', 500) + " wiring connector pinout"),
            new PdfPageText(4, "short page")
        };

        var selected = DocumentPipeline.SelectVisualScanPageNumbers(
            pages,
            new HashSet<int> { 2 },
            sparseDigitalPdf: false);

        Assert.DoesNotContain(1, selected);
        Assert.Contains(2, selected);
        Assert.Contains(3, selected);
        Assert.Contains(4, selected);
    }

    [Fact]
    public void Sparse_digital_pdf_selects_all_pages_for_local_ocr()
    {
        var pages = new[]
        {
            new PdfPageText(1, new string('A', 600)),
            new PdfPageText(2, new string('B', 600)),
            new PdfPageText(3, new string('C', 600))
        };

        var selected = DocumentPipeline.SelectVisualScanPageNumbers(
            pages,
            new HashSet<int>(),
            sparseDigitalPdf: true);

        Assert.Equal(new[] { 1, 2, 3 }, selected.OrderBy(value => value));
    }
}
