using ComponentIntelligence.Extraction;

namespace ComponentIntelligence.Tests.Extraction;

public sealed class EngineeringMarkdownBuilderTests
{
    [Fact]
    public void Builder_preserves_pages_structured_rows_and_ocr_sections()
    {
        var pages = new[]
        {
            new PdfPageText(1, "IFM TA2115\nOperating voltage 18...32 V DC"),
            new PdfPageText(2, "Electrical connection")
        };
        var rows = new[]
        {
            new PdfTableRow(2, "Pin 1", "L+ 24 V", ["Pin 1", "L+ 24 V"]),
            new PdfTableRow(2, "Connector", "M12", ["Connector", "M12"])
        };
        var ocr = new Dictionary<int, string>
        {
            [2] = "Pin 3 L-\nPin 4 C/Q IO-Link"
        };

        var result = new EngineeringMarkdownBuilder().Build(pages, rows, ocr);

        Assert.Equal(2, result.Pages.Count);
        Assert.Contains("## Page 1", result.Markdown);
        Assert.Contains("18...32 V DC", result.Pages[0].Markdown);
        Assert.Contains("### Structured Rows", result.Pages[1].Markdown);
        Assert.Contains("Pin 1", result.Pages[1].Markdown);
        Assert.Contains("M12", result.Pages[1].Markdown);
        Assert.Contains("### OCR Text", result.Pages[1].Markdown);
        Assert.Contains("C/Q IO-Link", result.Pages[1].Markdown);
        Assert.Contains(result.Diagnostics, item => item == "ENGINEERING_MARKDOWN_PAGES:2");
        Assert.Contains(result.Diagnostics, item => item == "ENGINEERING_MARKDOWN_STRUCTURED_ROWS:2");
        Assert.Contains(result.Diagnostics, item => item == "ENGINEERING_MARKDOWN_OCR_PAGES:1");
    }

    [Fact]
    public void Builder_marks_page_when_no_machine_readable_content_exists()
    {
        var result = new EngineeringMarkdownBuilder().Build([new PdfPageText(7, string.Empty)]);

        var page = Assert.Single(result.Pages);
        Assert.Contains("## Page 7", page.Markdown);
        Assert.Contains("No machine-readable text", page.Markdown);
    }

    [Fact]
    public void Builder_escapes_markdown_table_delimiters_without_losing_engineering_value()
    {
        var result = new EngineeringMarkdownBuilder().Build(
            [new PdfPageText(1, "Connector data")],
            [new PdfTableRow(1, "Signal | Pin", "A | B", ["Signal | Pin", "A | B"])]);

        Assert.Contains("Signal \\| Pin", result.Markdown);
        Assert.Contains("A \\| B", result.Markdown);
    }
}
