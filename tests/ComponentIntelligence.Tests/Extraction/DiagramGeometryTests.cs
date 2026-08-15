using ComponentIntelligence.Extraction;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace ComponentIntelligence.Tests.Extraction;

public sealed class DiagramGeometryTests
{
    [Fact]
    public void PdfVectorExtractor_reads_native_lines_without_rasterization()
    {
        var path = Path.Combine(Path.GetTempPath(), $"component-intelligence-vector-{Guid.NewGuid():N}.pdf");
        try
        {
            var builder = new PdfDocumentBuilder();
            var page = builder.AddPage(PageSize.A4);
            page.DrawLine(new PdfPoint(50, 400), new PdfPoint(500, 400), 1);
            page.DrawLine(new PdfPoint(275, 100), new PdfPoint(275, 700), 1);
            page.DrawRectangle(new PdfPoint(100, 120), 120, 80, 1);
            File.WriteAllBytes(path, builder.Build());

            var result = new PdfVectorDiagramExtractor().Extract(path);

            var geometry = Assert.Single(result.Pages);
            Assert.True(geometry.Lines.Count >= 2);
            Assert.True(geometry.Rectangles.Count >= 1);
            Assert.All(geometry.Lines, line => Assert.Equal(DiagramGeometrySource.PdfVector, line.Source));
            Assert.Contains(result.Diagnostics, item => item.StartsWith("PDF_VECTOR_PRIMITIVES:", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void JunctionInference_requires_three_or_more_incident_segments()
    {
        var lines = new[]
        {
            Line(0.1, 0.5, 0.9, 0.5),
            Line(0.5, 0.1, 0.5, 0.9),
            Line(0.5, 0.5, 0.8, 0.8)
        };

        var junctions = DiagramGeometryMath.InferJunctions(1, lines, DiagramGeometrySource.PdfVector);

        var junction = Assert.Single(junctions);
        Assert.True(junction.Degree >= 3);
        Assert.InRange(junction.Point.X, 0.49, 0.51);
        Assert.InRange(junction.Point.Y, 0.49, 0.51);
    }

    [Fact]
    public void Reconciler_prefers_native_vector_when_optional_raster_vision_detects_same_line()
    {
        var native = Line(0.1, 0.5, 0.9, 0.5) with { Confidence = 0.94 };
        var raster = new DiagramLineSegment(
            1,
            new DiagramPoint(0.102, 0.501),
            new DiagramPoint(0.899, 0.499),
            0.003,
            DiagramGeometrySource.RasterVision,
            0.68);
        var vectorResult = new DiagramGeometryResult(
            [new DiagramGeometryPage(1, DiagramGeometrySource.PdfVector, [native], [], [], [], [])],
            []);
        var rasterResult = new DiagramGeometryResult(
            [new DiagramGeometryPage(1, DiagramGeometrySource.RasterVision, [raster], [], [], [], [])],
            []);

        var merged = new DiagramGeometryReconciler().Reconcile(vectorResult, rasterResult);

        var page = Assert.Single(merged.Pages);
        var line = Assert.Single(page.Lines);
        Assert.Equal(DiagramGeometrySource.PdfVector, line.Source);
    }

    private static DiagramLineSegment Line(double x1, double y1, double x2, double y2) =>
        new(1, new DiagramPoint(x1, y1), new DiagramPoint(x2, y2), 0.001, DiagramGeometrySource.PdfVector, 0.9);
}
