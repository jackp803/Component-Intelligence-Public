using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ComponentIntelligence.Desktop;

public static class AutocadReviewPdfPackage
{
    public static void Merge(string topologyPdfPath, IEnumerable<string> drawingPdfPaths, string outputPath)
    {
        var sources = new[] { topologyPdfPath }.Concat(drawingPdfPaths).ToArray();
        if (sources.Length < 2) throw new ArgumentException("A review package requires topology and AutoCAD drawing PDFs.", nameof(drawingPdfPaths));

        using var output = new PdfDocument();
        foreach (var sourcePath in sources)
        {
            using var source = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
            foreach (var page in source.Pages) output.AddPage(page);
        }
        output.Save(outputPath);
    }
}
