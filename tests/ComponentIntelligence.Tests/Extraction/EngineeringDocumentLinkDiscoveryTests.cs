using ComponentIntelligence.Extraction;
using Xunit;

namespace ComponentIntelligence.Tests.Extraction;

public sealed class EngineeringDocumentLinkDiscoveryTests
{
    [Fact]
    public void Discover_FindsIfmDamPdfEmbeddedInEscapedJson()
    {
        const string expected = "https://media.ifm.com/dam/031bf053-7945-42c8-a72e-e5a15896ea0a/Original/EVC014-01_ZH-CN.pdf";
        var html = """
            <html><body>
            <script>
              window.__documentData = {"downloadUrl":"https:\/\/media.ifm.com\/dam\/031bf053-7945-42c8-a72e-e5a15896ea0a\/Original\/EVC014-01_ZH-CN.pdf"};
            </script>
            </body></html>
            """;

        var links = new EngineeringDocumentLinkDiscovery().Discover(
            html,
            new Uri("https://www.ifm.com/tw/zh/product/EVC014"));

        var link = Assert.Single(links.Where(item => item.Url.AbsoluteUri == expected));
        Assert.Equal(EngineeringDocumentLinkKind.DirectDocument, link.Kind);
    }

    [Fact]
    public void Discover_FindsDownloadButtonWithoutPdfExtension()
    {
        var html = """
            <button aria-label="Download PDF datasheet"
                    data-download-url="/download/document?id=TA2115&amp;language=en">
              Download
            </button>
            """;

        var links = new EngineeringDocumentLinkDiscovery().Discover(
            html,
            new Uri("https://www.ifm.com/us/en/product/TA2115"));

        Assert.Contains(links, link =>
            link.Kind == EngineeringDocumentLinkKind.DirectDocument &&
            link.Url.AbsoluteUri.Contains("/download/document", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Discover_ReturnsDocumentsTabAsBoundedFollowPage()
    {
        var html = """
            <a href="?tab=documents">Downloads / Documents</a>
            """;

        var links = new EngineeringDocumentLinkDiscovery().Discover(
            html,
            new Uri("https://www.ifm.com/us/en/product/TA2115"));

        Assert.Contains(links, link =>
            link.Kind == EngineeringDocumentLinkKind.FollowPage &&
            link.Url.Query.Contains("tab=documents", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Discover_FindsPdfHiddenInOnClickAttribute()
    {
        const string expected = "https://media.ifm.com/dam/example/Original/TA2115-01-EN.pdf";
        var html = $"<button title='Technical data' onclick=\"window.open('{expected}')\">Open</button>";

        var links = new EngineeringDocumentLinkDiscovery().Discover(
            html,
            new Uri("https://www.ifm.com/us/en/product/TA2115"));

        Assert.Contains(links, link =>
            link.Kind == EngineeringDocumentLinkKind.DirectDocument &&
            link.Url.AbsoluteUri == expected);
    }
}
