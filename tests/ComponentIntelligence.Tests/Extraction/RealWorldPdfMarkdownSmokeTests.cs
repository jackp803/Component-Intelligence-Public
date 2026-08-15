using System.Text.Json;
using ComponentIntelligence.Extraction;

namespace ComponentIntelligence.Tests.Extraction;

public sealed class RealWorldPdfMarkdownSmokeTests
{
    [Fact]
    public void Public_engineering_pdfs_produce_useful_markdown_vector_and_table_evidence()
    {
        var root = Environment.GetEnvironmentVariable("COMPONENT_INTELLIGENCE_REAL_PDF_DIR");
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

        var fixtures = new[]
        {
            new Fixture(
                "espressif-esp32-s3.pdf",
                ["ESP32-S3", "GPIO"],
                MinimumNativeCharacters: 1000),
            new Fixture(
                "arduino-uno-r4-minima.pdf",
                ["UNO R4", "RA4M1"],
                MinimumNativeCharacters: 600),
            new Fixture(
                "ti-tps62160.pdf",
                ["TPS62160", "Pin"],
                MinimumNativeCharacters: 1000)
        };

        var textExtractor = new PdfTextExtractor();
        var tableExtractor = new PdfTableExtractor();
        var vectorExtractor = new PdfVectorDiagramExtractor(maxPages: 160);
        var markdownBuilder = new EngineeringMarkdownBuilder();
        var results = new List<object>();

        foreach (var fixture in fixtures)
        {
            var path = Path.Combine(root, fixture.FileName);
            Assert.True(File.Exists(path), $"Real-world fixture missing: {path}");

            var pages = textExtractor.Extract(path);
            var rows = tableExtractor.Extract(path);
            var geometry = vectorExtractor.Extract(path);
            var markdown = markdownBuilder.Build(pages, rows);
            var nativeCharacters = pages.Sum(page => page.Text.Count(character => !char.IsWhiteSpace(character)));
            var vectorPrimitives = geometry.Pages.Sum(page => page.PrimitiveCount);

            Assert.NotEmpty(pages);
            Assert.True(nativeCharacters >= fixture.MinimumNativeCharacters,
                $"{fixture.FileName} recovered only {nativeCharacters} native characters.");
            Assert.True(markdown.Markdown.Length >= fixture.MinimumNativeCharacters,
                $"{fixture.FileName} produced unexpectedly small Markdown.");
            foreach (var token in fixture.ExpectedTokens)
                Assert.Contains(token, markdown.Markdown, StringComparison.OrdinalIgnoreCase);

            results.Add(new
            {
                fixture = fixture.FileName,
                pages = pages.Count,
                native_characters = nativeCharacters,
                structured_rows = rows.Count,
                vector_pages = geometry.Pages.Count,
                vector_primitives = vectorPrimitives,
                markdown_characters = markdown.Markdown.Length,
                expected_tokens = fixture.ExpectedTokens,
                expected_tokens_found = fixture.ExpectedTokens.Count(token => markdown.Markdown.Contains(token, StringComparison.OrdinalIgnoreCase))
            });
        }

        var reportPath = Environment.GetEnvironmentVariable("COMPONENT_INTELLIGENCE_REAL_PDF_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            var directory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new
            {
                generated_utc = DateTimeOffset.UtcNow,
                architecture = "pdf-to-engineering-markdown-native-vector-no-opencv",
                vendors = new[] { "Espressif", "Arduino", "Texas Instruments" },
                results
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private sealed record Fixture(
        string FileName,
        IReadOnlyList<string> ExpectedTokens,
        int MinimumNativeCharacters);
}
