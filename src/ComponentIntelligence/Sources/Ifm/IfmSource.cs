using System.Collections.Concurrent;
using AngleSharp.Html.Parser;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Extraction;
using ComponentIntelligence.Network;
using ComponentIntelligence.Resolution;

namespace ComponentIntelligence.Sources.Ifm;

/// <summary>Live deterministic IFM adapter. No AI/model call is performed.</summary>
public sealed class IfmSource : IComponentSource, IComponentSourceMetadata
{
    private const int MaxDocumentFollowPages = 10;
    private const int MaxRenderedFollowPages = 4;
    private readonly ComponentHttpClient _http;
    private readonly SpecificationParser _parser;
    private readonly DocumentPipeline _documents;
    private readonly IRenderedPageFetcher? _rendered;
    private readonly EngineeringDocumentLinkDiscovery _documentLinks = new();
    private readonly ConcurrentDictionary<string, Task<HttpFetchResult>> _pageCache = new(StringComparer.OrdinalIgnoreCase);

    public IfmSource(ComponentHttpClient http, SpecificationParser parser, DocumentPipeline documents, IRenderedPageFetcher? rendered = null)
    {
        _http = http; _parser = parser; _documents = documents; _rendered = rendered;
    }

    public string SourceName => "IFM Official Catalog";
    public IReadOnlyCollection<string> SupportedManufacturers => ["IFM"];
    public bool CanHandle(string manufacturer, string model) =>
        string.Equals(ManufacturerNormalizer.NormalizeKey(manufacturer), "IFM", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(model);

    public async Task<IReadOnlyList<ComponentCandidate>> SearchAsync(ComponentIdentityQuery query, CancellationToken cancellationToken = default)
    {
        var manufacturer = ManufacturerNormalizer.NormalizeKey(query.NormalizedManufacturer ?? query.RawManufacturer);
        var normalizedModel = ModelNormalizer.Normalize(query.NormalizedModel ?? query.RawModel);
        if (manufacturer is null || normalizedModel is null || !CanHandle(manufacturer, normalizedModel.Canonical))
            return Array.Empty<ComponentCandidate>();

        var model = normalizedModel.Canonical;
        var productUris = ProductUris(model);
        var failures = new List<string>();
        foreach (var uri in productUris)
        {
            var page = await FetchPageAsync(uri, cancellationToken);
            if (!page.IsSuccess)
            {
                failures.Add($"HTTP:{uri} => {(page.Error ?? page.StatusCode.ToString())}");
                continue;
            }
            if (!ContainsModel(page.Text, model)) continue;
            return [Candidate(model, page.FinalUri ?? uri)];
        }

        if (_rendered is not null)
        {
            foreach (var uri in productUris)
            {
                var page = await _rendered.FetchAsync(uri, cancellationToken);
                if (!page.IsSuccess)
                {
                    failures.Add($"BROWSER:{uri} => {(page.Error ?? page.StatusCode.ToString())}");
                    continue;
                }
                if (!ContainsModel(page.Text, model)) continue;
                return [Candidate(model, page.FinalUri ?? uri)];
            }
        }

        if (failures.Count >= productUris.Count)
            throw new InvalidOperationException($"IFM lookup failed: {string.Join(" | ", failures.Take(8))}");
        return Array.Empty<ComponentCandidate>();
    }

    public async Task<ProductPage?> GetProductPageAsync(ComponentIdentity identity, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(identity.OfficialManufacturer, identity.OfficialModel)) return null;
        IReadOnlyList<Uri> uris = identity.OfficialProductUrl is not null
            ? [identity.OfficialProductUrl]
            : ProductUris(identity.OfficialModel);
        foreach (var uri in uris)
        {
            var page = await FetchPageAsync(uri, cancellationToken);
            if (page.IsSuccess && ContainsModel(page.Text, identity.OfficialModel))
                return new ProductPage { Url = page.FinalUri ?? uri, RawContent = page.Text };
        }

        if (_rendered is not null)
        {
            foreach (var uri in uris)
            {
                var page = await _rendered.FetchAsync(uri, cancellationToken);
                if (page.IsSuccess && ContainsModel(page.Text, identity.OfficialModel))
                    return new ProductPage { Url = page.FinalUri ?? uri, RawContent = page.Text };
            }
        }
        return null;
    }

    public async Task<IReadOnlyList<ComponentDocument>> DiscoverDocumentsAsync(ComponentIdentity identity, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(identity.OfficialManufacturer, identity.OfficialModel)) return Array.Empty<ComponentDocument>();
        var page = await GetProductPageAsync(identity, cancellationToken);
        if (page?.RawContent is null) return Array.Empty<ComponentDocument>();

        var results = new List<ComponentDocument>();
        var followPages = new List<Uri>();
        CollectDocumentLinks(page.RawContent, page.Url, results, followPages);

        // IFM frequently renders Downloads dynamically. Always inspect the documents tab separately.
        var documentsUri = DocumentsUri(page.Url, identity.OfficialModel);
        var downloadPage = await FetchPageAsync(documentsUri, cancellationToken);
        if (downloadPage.IsSuccess)
        {
            if (IsPdfResponse(downloadPage))
                results.Add(DocumentFromUrl(downloadPage.FinalUri ?? documentsUri, "document"));
            else
                CollectDocumentLinks(downloadPage.Text, downloadPage.FinalUri ?? documentsUri, results, followPages);
        }

        if (_rendered is not null)
        {
            var renderedDownloadPage = await _rendered.FetchAsync(documentsUri, cancellationToken);
            if (renderedDownloadPage.IsSuccess)
                CollectDocumentLinks(renderedDownloadPage.Text, renderedDownloadPage.FinalUri ?? documentsUri, results, followPages);
        }

        // One additional bounded hop models the real user action of clicking a Downloads/Documents
        // button before the actual media.ifm.com PDF becomes visible. We never recursively crawl the site.
        await CrawlFollowPagesAsync(
            followPages,
            [page.Url, documentsUri],
            results,
            cancellationToken);

        results.Add(new ComponentDocument
        {
            Type = "documents-index",
            Url = documentsUri,
            SourceType = ComponentSourceType.ManufacturerDownloadCenter
        });

        return results
            .GroupBy(item => item.Url.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => DocumentPriority(item.Type)).First())
            .Take(40)
            .ToArray();
    }

    public async Task<RawComponentData> ExtractAsync(ComponentIdentity identity, CancellationToken cancellationToken = default)
    {
        var page = await GetProductPageAsync(identity, cancellationToken);
        if (page?.RawContent is null) return new RawComponentData { Issues = ["IFM_PRODUCT_PAGE_UNAVAILABLE"] };

        var specs = _parser.ParseHtml(page.RawContent, page.Url).ToList();
        AddIdentityClassification(specs, page.RawContent, page.Url);
        var documents = (await DiscoverDocumentsAsync(identity, cancellationToken)).ToList();
        var issues = new List<string>();
        var enrichedDocuments = new List<ComponentDocument>();

        foreach (var document in documents.Where(item => !string.Equals(item.Type, "documents-index", StringComparison.OrdinalIgnoreCase)))
        {
            var extraction = await _documents.ExtractAsync(document, cancellationToken);
            enrichedDocuments.Add(extraction.Document);
            specs.AddRange(extraction.Specifications);
            if (extraction.NeedsAiReview) issues.Add($"NEEDS_AI_REVIEW:{document.Url}");
            if (extraction.Error is not null) issues.Add($"DOCUMENT_ERROR:{document.Url}:{extraction.Error}");
            if (extraction.Diagnostics is not null)
                issues.AddRange(extraction.Diagnostics.Select(diagnostic => $"DOCUMENT_DIAGNOSTIC:{document.Url}:{diagnostic}"));
        }

        return new RawComponentData
        {
            Specifications = specs
                .GroupBy(spec => $"{spec.Section}\u001f{spec.ProposedKey}\u001f{spec.RawName}\u001f{spec.RawValue}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First() with { Evidence = group.SelectMany(item => item.Evidence).Distinct().ToArray() })
                .ToArray(),
            Documents = enrichedDocuments
                .Concat(documents.Where(item => string.Equals(item.Type, "documents-index", StringComparison.OrdinalIgnoreCase)))
                .GroupBy(item => item.Url.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.Sha256 is not null).First())
                .ToArray(),
            Assets = DiscoverAssets(page.RawContent, page.Url, identity.OfficialModel),
            Issues = issues
        };
    }

    private void CollectDocumentLinks(
        string html,
        Uri pageUrl,
        List<ComponentDocument> documents,
        List<Uri> followPages)
    {
        foreach (var link in _documentLinks.Discover(html, pageUrl))
        {
            if (link.Kind == EngineeringDocumentLinkKind.DirectDocument)
            {
                documents.Add(DocumentFromUrl(link.Url, link.DocumentType));
                continue;
            }

            if (IsAllowedIfmFollowHost(link.Url.Host))
                followPages.Add(link.Url);
        }
    }

    private async Task CrawlFollowPagesAsync(
        IEnumerable<Uri> candidates,
        IEnumerable<Uri> alreadyVisited,
        List<ComponentDocument> documents,
        CancellationToken cancellationToken)
    {
        var visited = alreadyVisited
            .Select(uri => uri.AbsoluteUri)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var renderedCount = 0;

        foreach (var uri in candidates
                     .Where(uri => IsAllowedIfmFollowHost(uri.Host))
                     .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                     .Take(MaxDocumentFollowPages))
        {
            if (!visited.Add(uri.AbsoluteUri)) continue;

            var before = documents.Count;
            var response = await FetchPageAsync(uri, cancellationToken);
            if (response.IsSuccess)
            {
                var finalUri = response.FinalUri ?? uri;
                if (IsPdfResponse(response))
                {
                    documents.Add(DocumentFromUrl(finalUri, InferDocumentTypeFromUrl(finalUri)));
                    continue;
                }

                foreach (var link in _documentLinks.Discover(response.Text, finalUri)
                             .Where(link => link.Kind == EngineeringDocumentLinkKind.DirectDocument))
                    documents.Add(DocumentFromUrl(link.Url, link.DocumentType));
            }

            if (documents.Count > before || _rendered is null || renderedCount >= MaxRenderedFollowPages) continue;
            renderedCount++;
            var rendered = await _rendered.FetchAsync(uri, cancellationToken);
            if (!rendered.IsSuccess) continue;
            foreach (var link in _documentLinks.Discover(rendered.Text, rendered.FinalUri ?? uri)
                         .Where(link => link.Kind == EngineeringDocumentLinkKind.DirectDocument))
                documents.Add(DocumentFromUrl(link.Url, link.DocumentType));
        }
    }

    private static ComponentDocument DocumentFromUrl(Uri url, string type) => new()
    {
        Type = type,
        Url = url,
        SourceType = type switch
        {
            "datasheet" => ComponentSourceType.ManufacturerDatasheet,
            "manual" => ComponentSourceType.ManufacturerManual,
            _ => ComponentSourceType.ManufacturerDownloadCenter
        }
    };

    private static bool IsPdfResponse(HttpFetchResult response) =>
        response.ContentType?.Split(';')[0].Trim().Equals("application/pdf", StringComparison.OrdinalIgnoreCase) == true ||
        (response.FinalUri ?? response.RequestedUri).AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedIfmFollowHost(string host)
    {
        var normalized = host.Trim('.').ToLowerInvariant();
        return normalized == "ifm.com" || normalized.EndsWith(".ifm.com", StringComparison.Ordinal) ||
               normalized == "ifm.cn" || normalized.EndsWith(".ifm.cn", StringComparison.Ordinal);
    }

    private static string InferDocumentTypeFromUrl(Uri uri)
    {
        var normalized = $"{uri.AbsolutePath} {uri.Query}".ToLowerInvariant();
        if (normalized.Contains("datasheet") || normalized.Contains("data-sheet") || normalized.Contains("technical")) return "datasheet";
        if (normalized.Contains("manual") || normalized.Contains("instruction")) return "manual";
        return "document";
    }

    private Task<HttpFetchResult> FetchPageAsync(Uri uri, CancellationToken cancellationToken) =>
        _pageCache.GetOrAdd(uri.AbsoluteUri, _ => _http.FetchAsync(uri, cancellationToken));

    private static ComponentCandidate Candidate(string model, Uri productUri)
    {
        var evidence = new Evidence
        {
            SourceType = ComponentSourceType.ManufacturerProductPage,
            SourceUrl = productUri,
            ExtractionMethod = ExtractionMethod.Html,
            RawValue = model,
            RetrievedAt = DateTimeOffset.UtcNow,
            VerificationStatus = VerificationStatus.SingleSource
        };
        return new ComponentCandidate
        {
            Manufacturer = "IFM", OfficialModel = model, Mpn = model,
            SourceType = ComponentSourceType.ManufacturerProductPage,
            ProductUrl = productUri, RawSourceTitle = $"IFM {model}", Evidence = [evidence]
        };
    }

    private static IReadOnlyList<Uri> ProductUris(string model) =>
    [
        new Uri($"https://www.ifm.com/us/en/product/{Uri.EscapeDataString(model.Trim())}"),
        new Uri($"https://www.ifm.com/na/en/product/{Uri.EscapeDataString(model.Trim())}"),
        new Uri($"https://www.ifm.com/tw/zh/product/{Uri.EscapeDataString(model.Trim())}"),
        new Uri($"https://www.ifm.cn/cn/zh/product/{Uri.EscapeDataString(model.Trim())}"),
        new Uri($"https://www.ifm.com/qr/{Uri.EscapeDataString(model.Trim())}")
    ];

    private static Uri DocumentsUri(Uri productPage, string model)
    {
        if (!productPage.Host.Contains("ifm", StringComparison.OrdinalIgnoreCase))
            return new Uri($"https://www.ifm.com/us/en/product/{Uri.EscapeDataString(model.Trim())}?tab=documents");
        var builder = new UriBuilder(productPage) { Query = "tab=documents" };
        return builder.Uri;
    }

    private static int DocumentPriority(string type) => type.ToLowerInvariant() switch
    {
        "datasheet" => 3,
        "manual" => 2,
        "document" => 1,
        _ => 0
    };

    private static bool ContainsModel(string html, string model) => html.Contains(model, StringComparison.OrdinalIgnoreCase) && html.Contains("ifm", StringComparison.OrdinalIgnoreCase);

    private static void AddIdentityClassification(List<RawSpecification> specs, string html, Uri source)
    {
        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        var title = document.QuerySelector("h1")?.TextContent?.Trim();
        if (string.IsNullOrWhiteSpace(title)) return;
        var evidence = new Evidence
        {
            SourceType = ComponentSourceType.ManufacturerProductPage, SourceUrl = source,
            ExtractionMethod = ExtractionMethod.Html, RawValue = title,
            RetrievedAt = DateTimeOffset.UtcNow, VerificationStatus = VerificationStatus.SingleSource
        };
        specs.Add(new RawSpecification { RawName = "Product name", RawValue = title, ProposedKey = "identity.product_name", Evidence = [evidence] });
        if (title.Contains("sensor", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("transmitter", StringComparison.OrdinalIgnoreCase))
            specs.Add(new RawSpecification { RawName = "Category", RawValue = "sensor", ProposedKey = "classification.category", Evidence = [evidence with { RawValue = "sensor" }] });
    }

    private static IReadOnlyList<ComponentAsset> DiscoverAssets(string html, Uri pageUrl, string model)
    {
        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        return document.QuerySelectorAll("img[src]")
            .Where(image => (image.GetAttribute("alt") ?? string.Empty).Contains(model, StringComparison.OrdinalIgnoreCase))
            .Select(image => Uri.TryCreate(pageUrl, image.GetAttribute("src"), out var url) ? new ComponentAsset { Type = "product-image", Url = url } : null)
            .Where(asset => asset is not null).Cast<ComponentAsset>()
            .GroupBy(asset => asset.Url.AbsoluteUri, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).Take(8).ToArray();
    }
}
