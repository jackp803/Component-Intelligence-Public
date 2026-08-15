using ComponentIntelligence.Extraction;

namespace ComponentIntelligence.Tests.Extraction;

public sealed class OcrGeometryMatchingTests
{
    [Fact]
    public void TesseractTsvParser_normalizes_level5_word_boxes()
    {
        const string tsv =
            "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n" +
            "1\t1\t0\t0\t0\t0\t0\t0\t1000\t500\t-1\t\n" +
            "5\t1\t1\t1\t1\t1\t100\t200\t80\t40\t94.5\tM12\n" +
            "5\t1\t1\t1\t1\t2\t250\t210\t30\t30\t88.0\t1";

        var parsed = TesseractTsvParser.Parse(tsv);

        Assert.Equal(1000, parsed.ImageWidth);
        Assert.Equal(500, parsed.ImageHeight);
        Assert.Equal(2, parsed.Boxes.Count);
        var first = parsed.Boxes[0];
        Assert.Equal("M12", first.Text);
        Assert.InRange(first.Left, 0.099, 0.101);
        Assert.InRange(first.Top, 0.399, 0.401);
        Assert.InRange(first.Right, 0.179, 0.181);
        Assert.InRange(first.Bottom, 0.479, 0.481);
        Assert.Equal(94.5, first.Confidence, 1);
    }

    [Fact]
    public void Matcher_links_pin_label_to_nearby_vector_junction_without_assigning_semantics()
    {
        var geometry = new DiagramGeometryPage(
            2,
            DiagramGeometrySource.PdfVector,
            [new DiagramLineSegment(2, new DiagramPoint(0.2, 0.5), new DiagramPoint(0.8, 0.5), 0.001, DiagramGeometrySource.PdfVector, 0.94)],
            [],
            [],
            [new DiagramJunction(2, new DiagramPoint(0.5, 0.5), 3, DiagramGeometrySource.PdfVector, 0.90)],
            []);
        var boxes = new[]
        {
            new OcrTextBox("1", 0.505, 0.485, 0.525, 0.515, 95),
            new OcrTextBox("unrelated-long-paragraph-word", 0.50, 0.50, 0.60, 0.55, 99)
        };

        var matches = new DiagramTextGeometryMatcher().Match(2, boxes, geometry);

        var match = Assert.Single(matches);
        Assert.Equal("1", match.Text);
        Assert.Equal(DiagramAnchorKind.Junction, match.AnchorKind);
        Assert.Equal(DiagramGeometrySource.PdfVector, match.GeometrySource);
        Assert.True(match.Confidence > 0.6);
    }

    [Fact]
    public void Matcher_rejects_label_that_is_too_far_from_optional_raster_geometry()
    {
        var geometry = new DiagramGeometryPage(
            1,
            DiagramGeometrySource.RasterVision,
            [new DiagramLineSegment(1, new DiagramPoint(0.1, 0.1), new DiagramPoint(0.2, 0.1), 0.003, DiagramGeometrySource.RasterVision, 0.68)],
            [], [], [], []);
        var box = new OcrTextBox("24V", 0.80, 0.80, 0.85, 0.84, 99);

        var matches = new DiagramTextGeometryMatcher().Match(1, [box], geometry);

        Assert.Empty(matches);
    }

    [Fact]
    public void Low_confidence_ocr_noise_is_not_a_geometry_label()
    {
        var noise = new OcrTextBox("1", 0.1, 0.1, 0.2, 0.2, 4);
        Assert.False(DiagramTextGeometryMatcher.LooksLikeDiagramLabel(noise));
    }
}
