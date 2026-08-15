using System.Net;
using System.Text;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Repository;

public sealed class NotionComponentKnowledgeStoreTests
{
    [Fact]
    public async Task Disabled_store_never_calls_network()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Network must not be called."));
        var store = new NotionComponentKnowledgeStore(
            new NotionKnowledgeStoreOptions { Token = null },
            new HttpClient(handler));

        var result = await store.FindByIdentityAsync("IFM", "TA2115");

        Assert.False(store.IsEnabled);
        Assert.False(result.Found);
        Assert.Contains("NOTION_CENTRAL_DISABLED_NO_TOKEN", result.Diagnostics);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Canonical_key_is_trimmed_and_case_insensitive_by_contract()
    {
        Assert.Equal("IFM::TA2115", NotionComponentKnowledgeStore.CanonicalKey(" ifm ", " ta2115 "));
    }

    [Fact]
    public async Task Central_lookup_uses_current_notion_api_and_rehydrates_component_summary()
    {
        const string componentPageId = "11111111-2222-3333-4444-555555555555";
        var options = new NotionKnowledgeStoreOptions
        {
            Token = "test-token",
            ApiBaseAddress = new Uri("https://api.notion.test/v1/")
        };
        var handler = new RecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains(options.ComponentsDataSourceId, StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, $$"""
                {
                  "object": "list",
                  "results": [
                    {
                      "object": "page",
                      "id": "{{componentPageId}}",
                      "properties": {
                        "Component": { "type": "title", "title": [{ "plain_text": "IFM TA2115" }] },
                        "Manufacturer": { "type": "rich_text", "rich_text": [{ "plain_text": "IFM" }] },
                        "Model / Part Number": { "type": "rich_text", "rich_text": [{ "plain_text": "TA2115" }] },
                        "Canonical Key": { "type": "rich_text", "rich_text": [{ "plain_text": "IFM::TA2115" }] },
                        "Category": { "type": "rich_text", "rich_text": [{ "plain_text": "Temperature sensor" }] },
                        "Voltage": { "type": "rich_text", "rich_text": [{ "plain_text": "18...32 V DC" }] },
                        "Output Type": { "type": "rich_text", "rich_text": [{ "plain_text": "IO-Link" }] },
                        "Connector": { "type": "rich_text", "rich_text": [{ "plain_text": "M12" }] },
                        "Topology Readiness": { "type": "select", "select": { "name": "Review" } },
                        "Product URL": { "type": "url", "url": "https://example.test/ta2115" },
                        "Datasheet URL": { "type": "url", "url": "https://example.test/ta2115.pdf" },
                        "Image URL": { "type": "url", "url": null }
                      }
                    }
                  ],
                  "has_more": false,
                  "next_cursor": null
                }
                """);
            }

            // Ports, Pins, Specifications and Documents are empty for this fixture.
            return Json(HttpStatusCode.OK, "{\"object\":\"list\",\"results\":[],\"has_more\":false,\"next_cursor\":null}");
        });
        var store = new NotionComponentKnowledgeStore(options, new HttpClient(handler));

        var result = await store.FindByIdentityAsync("ifm", "ta2115");

        Assert.True(result.Found);
        Assert.NotNull(result.Component);
        Assert.Equal("IFM", result.Component!.Identity.Manufacturer);
        Assert.Equal("TA2115", result.Component.Identity.Model);
        Assert.Equal("M12", result.Component.Connector.Family);
        Assert.Equal(ReadinessStatus.Partial, result.Component.Readiness.Topology);
        Assert.Equal(18m, result.Component.Power.OperatingVoltage?.Min);
        Assert.Equal(32m, result.Component.Power.OperatingVoltage?.Max);
        Assert.Equal("DC", result.Component.Power.OperatingVoltage?.Type);
        Assert.Equal(new Uri("https://example.test/ta2115.pdf"), result.Component.Assets.DatasheetUrl);
        Assert.Contains("NOTION_CENTRAL_HIT", result.Diagnostics);

        Assert.Equal(5, handler.Requests.Count); // component + ports + pins + specifications + documents
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("Bearer test-token", request.Authorization);
            Assert.Equal(NotionKnowledgeStoreOptions.ApiVersion, request.NotionVersion);
        });
        Assert.Contains("IFM::TA2115", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("Canonical Key", handler.Requests[0].Body, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Notion-Version", out var versions) ? versions.SingleOrDefault() : null,
                body));
            return responseFactory(request);
        }
    }

    private sealed record CapturedRequest(Uri Uri, string? Authorization, string? NotionVersion, string Body);
}
