using System.Text;
using ComponentIntelligence.Extraction;
using Xunit;

namespace ComponentIntelligence.Tests.Extraction;

public sealed class PdfFullPageRasterizerTests
{
    [Fact]
    public void Extract_RendersCompleteVectorAndTextPageToPng_OnSupportedPlatform()
    {
        if (!PdfFullPageRasterizer.IsSupportedPlatform)
            return;

        var root = Path.Combine(Path.GetTempPath(), $"component-intelligence-raster-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var pdfPath = Path.Combine(root, "vector-pinout.pdf");

        try
        {
            File.WriteAllBytes(pdfPath, BuildSinglePagePdf(
                "0 0 1 RG 2 w 20 30 m 180 170 l S\n" +
                "BT /F1 14 Tf 20 100 Td (M12 Pin 1 L+) Tj ET"));

            var pages = new PdfFullPageRasterizer(dpi: 150, maxPages: 5).Extract(pdfPath);

            var page = Assert.Single(pages);
            Assert.Equal(1, page.PageNumber);
            Assert.Equal(".png", page.Extension);
            Assert.True(page.WidthPixels > 100);
            Assert.True(page.HeightPixels > 100);
            Assert.True(page.Bytes.Length > 100);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, page.Bytes.Take(8).ToArray());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SelectVisualScanPageNumbers_UsesSparseTextTopologyHintsAndEmbeddedImagesWithoutScanningEveryDensePage()
    {
        var denseUnrelated = string.Join(' ', Enumerable.Repeat("mechanical housing dimensions material tolerance enclosure", 12));
        var denseTopology = string.Join(' ', Enumerable.Repeat("general product information", 25)) + " M12 connector pinout wiring";
        var pages = new[]
        {
            new PdfPageText(1, denseUnrelated),
            new PdfPageText(2, denseTopology),
            new PdfPageText(3, denseUnrelated),
            new PdfPageText(4, "short page")
        };

        var adaptive = DocumentPipeline.SelectVisualScanPageNumbers(pages, new HashSet<int> { 3 }, sparseDigitalPdf: false);

        Assert.DoesNotContain(1, adaptive);
        Assert.Contains(2, adaptive); // topology visual hint
        Assert.Contains(3, adaptive); // embedded image evidence
        Assert.Contains(4, adaptive); // sparse page

        var globallySparse = DocumentPipeline.SelectVisualScanPageNumbers(pages, new HashSet<int>(), sparseDigitalPdf: true);
        Assert.Equal(new[] { 1, 2, 3, 4 }, globallySparse.OrderBy(page => page).ToArray());
    }

    private static byte[] BuildSinglePagePdf(string pageContent)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(pageContent)} >>\nstream\n{pageContent}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(index + 1).Append(" 0 obj\n")
                .Append(objects[index]).Append("\nendobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 ").Append(objects.Length + 1).Append("\n")
            .Append("0000000000 65535 f \n");
        for (var index = 1; index < offsets.Count; index++)
            builder.Append(offsets[index].ToString("D10")).Append(" 00000 n \n");

        builder.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\n")
            .Append("startxref\n").Append(xrefOffset).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}