using System.Net;
using System.Net.Http;
using ComponentIntelligence.Cache;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Extraction;
using ComponentIntelligence.Network;
using ComponentIntelligence.Sources.Secondary;
using Xunit;

namespace ComponentIntelligence.Tests.Enrichment;

public sealed class WebPdfDiscoveryTests
{
    [Fact]
    public async Task DiscoverDocuments_AcceptsExactModelOfficialPdfAndClassifiesManufacturerTrust()
    {
        const string pdf = "https://media.ifm.com/dam/example/Original/TA2115-01_EN.pdf";
        var html = $"""
            <html><body>
              <div class="result">
                <a href="/url?q={Uri.EscapeDataString(pdf)}&sa=U">
                  IFM TA2115 datasheet PDF
                </a>
              </div>
            </body></html>
            """;
        var source = CreateSource(html);
        var identity = new ComponentIdentity { OfficialManufacturer = "IFM", OfficialModel = "TA2115" };

        var documents = await source.DiscoverDocumentsAsync(identity);

        var document = Assert.Single(documents);
        Assert.Equal(pdf, document.Url.AbsoluteUri);
        Assert.Equal(ComponentSourceType.ManufacturerDatasheet, document.SourceType);
        Assert.Contains("datasheet", document.Type, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiscoverDocuments_RecognizesCanonicalSchneiderOfficialDomain()
    {
        const string pdf = "https://download.schneider-electric.com/files?p_Doc_Ref=LC1D09BNE-datasheet.pdf";
        var html = $"""
            <html><body>
              <div class="result">
                <a href="/url?q={Uri.EscapeDataString(pdf)}&sa=U">
                  Schneider Electric LC1D09BNE datasheet PDF
                </a>
              </div>
            </body></html>
            """;
        var source = CreateSource(html);
        var identity = new ComponentIdentity { OfficialManufacturer = "SCHNEIDER ELECTRIC", OfficialModel = "LC1D09BNE" };

        var documents = await source.DiscoverDocumentsAsync(identity);

        var document = Assert.Single(documents);
        Assert.Equal(ComponentSourceType.ManufacturerDatasheet, document.SourceType);
    }

    [Fact]
    public async Task DiscoverDocuments_RejectsSameManufacturerPdfForDifferentModel()
    {
        const string wrongPdf = "https://media.ifm.com/dam/example/Original/PN7094-01_EN.pdf";
        var html = $"""
            <html><body>
              <div class="result">
                <a href="/url?q={Uri.EscapeDataString(wrongPdf)}&sa=U">
                  IFM PN7094 datasheet PDF
                </a>
              </div>
            </body></html>
            """;
        var source = CreateSource(html);
        var identity = new ComponentIdentity { OfficialManufacturer = "IFM", OfficialModel = "TA2115" };

        var documents = await source.DiscoverDocumentsAsync(identity);

        Assert.Empty(documents);
    }

    [Fact]
    public async Task DiscoverDocuments_StopsOnSearchEngineSecurityCheck()
    {
        var source = CreateSource("<html><body>Our systems have detected unusual traffic. CAPTCHA verify you are human.</body></html>");
        var identity = new ComponentIdentity { OfficialManufacturer = "IFM", OfficialModel = "TA2115" };

        var documents = await source.DiscoverDocumentsAsync(identity);
        var raw = await source.ExtractAsync(identity);

        Assert.Empty(documents);
        Assert.Contains(raw.Issues, issue => issue.StartsWith("WEB_PDF_SEARCH_SECURITY_CHECK", StringComparison.Ordinal));
    }

    private static WebPdfDiscoverySource CreateSource(string googleHtml)
    {
        var factory = new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(googleHtml, System.Text.Encoding.UTF8, "text/html")
        });
        var http = new ComponentHttpClient(factory, maxAttempts: 1, minimumHostDelay: TimeSpan.Zero);
        var cacheRoot = Path.Combine(Path.GetTempPath(), $"component-intelligence-webpdf-test-{Guid.NewGuid():N}");
        var parser = new SpecificationParser();
        var pipeline = new DocumentPipeline(new CacheManager(cacheRoot, http), new PdfTextExtractor(), parser);
        return new WebPdfDiscoverySource(http, pipeline, rendered: null);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(new StubHandler(_handler), disposeHandler: true);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _handler(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
