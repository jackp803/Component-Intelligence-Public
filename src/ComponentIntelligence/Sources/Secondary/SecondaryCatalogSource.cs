using System.Collections.Concurrent;
using AngleSharp.Html.Parser;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Extraction;
using ComponentIntelligence.Network;
using ComponentIntelligence.Resolution;

namespace ComponentIntelligence.Sources.Secondary;

public sealed record SecondaryCatalogSourceDefinition(
    string SourceName,
    ComponentSourceType SourceType,
    IReadOnlyCollection<string> AllowedHosts,
    Func<string, IReadOnlyList<Uri>> SearchUris);

/// <summary>
/// Enrichment-only secondary catalog. It never resolves identity. It searches a trusted distributor
/// or aggregator after manufacturer/model identity has already been resolved, then preserves its tables
/// and documents as lower-trust evidence that can corroborate or conflict with manufacturer data.
///
/// Search/result browsing is bounded. The source may follow a small number of product/detail links,
/// press an exact-model Details / See More control when the site requires it, and then use the browser
/// fallback to reveal explicit engineering-document controls. CAPTCHA/security checks are never bypassed.
/// </summary>
public sealed class SecondaryCatalogSource : IComponentSource, IComponentSourceMetadata, ISecondaryEnrichmentSource
{
    private const int MaxDocumentFollowPages = 6;
    private readonly SecondaryCatalogSourceDefinition _definition;
    private readonly ComponentHttpClient _http;
    private readonly SpecificationParser _parser;
    private readonly DocumentPipeline _documents;
    private readonly IRenderedPageFetcher? _rendered;
    private readonly IProductDetailDiscoveryFetcher? _detailDiscovery;
    private readonly EngineeringDocumentLinkDiscovery _documentLinks = new();
    private readonly ConcurrentDictionary<string, Task<ProductPage?>> _pageCache = new(StringComparer.OrdinalIgnoreCase);

    public SecondaryCatalogSource(
        SecondaryCatalogSourceDefinition definition,
        ComponentHttpClient http,
        SpecificationParser parser,
        DocumentPipeline documents,
        IRenderedPageFetcher? rendered = null,
        IProductDetailDiscoveryFetcher? detailDiscovery = null)
    {
        _definition = definition;
        _http = http;
        _parser = parser;
        _documents = documents;
        _rendered = rendered;
        _detailDiscovery = detailDiscovery ?? (rendered is null ? null : new PlaywrightProductDetailDiscoveryFetcher());
    }

    public string SourceName => _definition.SourceName;
    public IReadOnlyCollection<string> SupportedManufacturers => Array.Empty<string>();

    public bool CanHandle(string manufacturer, string model) =>
        !string.IsNullOrWhiteSpace(manufacturer) && !string.IsNullOrWhiteSpace(model);

    // Identity belongs to manufacturer/official sources only. Secondary sources deliberately return no candidates.
    public Task<IReadOnlyList<ComponentCandidate>> SearchAsync(ComponentIdentityQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ComponentCandidate>>(Array.Empty<ComponentCandidate>());

    public Task<ProductPage?> GetProductPageAsync(ComponentIdentity identity, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(identity.OfficialManufacturer, identity.OfficialModel)) return Task.FromResult<ProductPage?>(null);
        var key = $"{identity.OfficialManufacturer}\u001f{identity.OfficialModel}";
        return _pageCache.GetOrAdd(key, _ => FindProductPageAsync(identity, cancellationToken));
    }

    public async Task<IReadOnlyList<ComponentDocument>> DiscoverDocumentsAsync(ComponentIdentity identity, CancellationToken cancellationToken = default)
    {
        var page = await GetProductPageAsync(identity, cancellationToken);
        if (page?.RawContent is null || LooksLikeSecurityCheck(page.RawContent)) return Array.Empty<ComponentDocument>();

        var results = new List<ComponentDocument>();
        var followPages = new List<Uri>();
        CollectDocumentLinks(page.RawContent, page.Url, results, followPages);

        // Some aggregators show a Datasheet/Document button that only exposes the real PDF after a
        // browser click. This is a final bounded fallback; it does not click buy/login/captcha controls.
        if (_rendered is IRenderedDocumentDiscoveryFetcher interactive)
        {
            foreach (var documentUrl in await interactive.DiscoverDocumentUrlsAsync(page.Url, cancellationToken))
                results.Add(DocumentFromUrl(documentUrl, InferDocumentTypeFromUrl(documentUrl)));
        }

        // If the product page first points to a Documents/Resources child page, follow one bounded hop.
        foreach (var followUri in followPages
                     .Where(uri => IsAllowedHost(uri.Host))
                     .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                     .Take(MaxDocumentFollowPages))
        {
            var fetched = await _http.FetchAsync(followUri, cancellationToken);
            if (fetched.IsSuccess && !LooksLikeSecurityCheck(fetched.Text))
            {
                var finalUri = fetched.FinalUri ?? followUri;
                if (IsPdfResponse(fetched))
                {
                    results.Add(DocumentFromUrl(finalUri, InferDocumentTypeFromUrl(finalUri)));
                }
                else
                {
                    foreach (var link in _documentLinks.Discover(fetched.Text, finalUri)
                                 .Where(link => link.Kind == EngineeringDocumentLinkKind.DirectDocument))
                        results.Add(DocumentFromUrl(link.Url, link.DocumentType));
                }
            }

            if (_rendered is not IRenderedDocumentDiscoveryFetcher followInteractive) continue;
            foreach (var documentUrl in await followInteractive.DiscoverDocumentUrlsAsync(followUri, cancellationToken))
                results.Add(DocumentFromUrl(documentUrl, InferDocumentTypeFromUrl(documentUrl)));
        }

        return results
            .GroupBy(item => item.Url.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(24)
            .ToArray();
    }

    public async Task<RawComponentData> ExtractAsync(ComponentIdentity identity, CancellationToken cancellationToken = default)
    {
        var page = await GetProductPageAsync(identity, cancellationToken);
        if (page?.RawContent is null) return new RawComponentData();
        if (LooksLikeSecurityCheck(page.RawContent))
            return new RawComponentData { Issues = [$"SECONDARY_SECURITY_CHECK:{SourceName}"] };

        var specs = _parser.ParseHtml(page.RawContent, page.Url, _definition.SourceType).ToList();
        var documents = (await DiscoverDocumentsAsync(identity, cancellationToken)).ToList();
        var enrichedDocuments = new List<ComponentDocument>();
        var issues = new List<string>();

        foreach (var document in documents)
        {
            var extraction = await _documents.ExtractAsync(document, cancellationToken);
            enrichedDocuments.Add(extraction.Document);
            specs.AddRange(extraction.Specifications);
            if (extraction.NeedsAiReview) issues.Add($"NEEDS_OCR_REVIEW:{SourceName}:{document.Url}");
            if (extraction.Error is not null) issues.Add($"SECONDARY_DOCUMENT_ERROR:{SourceName}:{extraction.Error}");
            if (extraction.Diagnostics is not null)
                issues.AddRange(extraction.Diagnostics.Select(diagnostic => $"SECONDARY_DOCUMENT_DIAGNOSTIC:{SourceName}:{diagnostic}"));
        }

        return new RawComponentData
        {
            Specifications = specs,
            Documents = enrichedDocuments,
            Issues = issues
        };
    }

    private async Task<ProductPage?> FindProductPageAsync(ComponentIdentity identity, CancellationToken cancellationToken)
    {
        foreach (var searchUri in _definition.SearchUris(identity.OfficialModel).DistinctBy(item => item.AbsoluteUri, StringComparer.OrdinalIgnoreCase))
        {
            var lightweight = await _http.FetchAsync(searchUri, cancellationToken);
            var page = await TryResolveFromSearchResultAsync(identity, lightweight, cancellationToken);
            if (page is not null) return page;

            if (lightweight.IsSuccess && LooksLikeSecurityCheck(lightweight.Text)) continue;

            if (_rendered is not null)
            {
                var rendered = await _rendered.FetchAsync(searchUri, cancellationToken);
                page = await TryResolveFromSearchResultAsync(identity, rendered, cancellationToken);
                if (page is not null) return page;
                if (rendered.IsSuccess && LooksLikeSecurityCheck(rendered.Text)) continue;
            }

            // Some catalog/aggregator result pages expose the exact product only after pressing a
            // Details / See More control. The browser helper clicks only a model-matching detail control.
            if (_detailDiscovery is null) continue;
            var detailUris = await _detailDiscovery.DiscoverDetailUrlsAsync(
                searchUri,
                identity.OfficialModel,
                _definition.AllowedHosts,
                cancellationToken);
            foreach (var detailUri in detailUris.Take(6))
            {
                page = await TryLoadProductPageAsync(identity, detailUri, cancellationToken);
                if (page is not null) return page;
            }
        }

        return null;
    }

    private async Task<ProductPage?> TryLoadProductPageAsync(
        ComponentIdentity identity,
        Uri candidate,
        CancellationToken cancellationToken)
    {
        if (!IsAllowedHost(candidate.Host)) return null;
        var fetched = await _http.FetchAsync(candidate, cancellationToken);
        if (fetched.IsSuccess && !LooksLikeSecurityCheck(fetched.Text) && ContainsModel(fetched.Text, identity.OfficialModel))
            return new ProductPage { Url = fetched.FinalUri ?? candidate, RawContent = fetched.Text };

        if (_rendered is null || fetched.IsSuccess && LooksLikeSecurityCheck(fetched.Text)) return null;
        var rendered = await _rendered.FetchAsync(candidate, cancellationToken);
        return rendered.IsSuccess && !LooksLikeSecurityCheck(rendered.Text) && ContainsModel(rendered.Text, identity.OfficialModel)
            ? new ProductPage { Url = rendered.FinalUri ?? candidate, RawContent = rendered.Text }
            : null;
    }

    private async Task<ProductPage?> TryResolveFromSearchResultAsync(
        ComponentIdentity identity,
        HttpFetchResult result,
        CancellationToken cancellationToken)
    {
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Text) || LooksLikeSecurityCheck(result.Text)) return null;
        var pageUri = result.FinalUri ?? result.RequestedUri;

        if (IsAllowedHost(pageUri.Host) && LooksLikeProductPage(pageUri) && ContainsModel(result.Text, identity.OfficialModel))
            return new ProductPage { Url = pageUri, RawContent = result.Text };

        var parser = new HtmlParser();
        var document = parser.ParseDocument(result.Text);
        var candidates = document.QuerySelectorAll("a[href]")
            .Select(anchor => new
            {
                Url = Resolve(pageUri, anchor.GetAttribute("href")),
                Text = Clean(anchor.TextContent),
                Href = anchor.GetAttribute("href") ?? string.Empty
            })
            .Where(item => item.Url is not null && IsAllowedHost(item.Url.Host))
            .Where(item => ContainsModel($"{item.Text} {item.Href}", identity.OfficialModel))
            .OrderByDescending(item => LinkScore(item.Url!, item.Text, identity.OfficialModel))
            .Select(item => item.Url!)
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        foreach (var candidate in candidates)
        {
            var page = await TryLoadProductPageAsync(identity, candidate, cancellationToken);
            if (page is not null) return page;
        }

        return null;
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

            if (IsAllowedHost(link.Url.Host)) followPages.Add(link.Url);
        }
    }

    private ComponentDocument DocumentFromUrl(Uri url, string type) => new()
    {
        Type = type switch
        {
            "datasheet" => "datasheet-mirror",
            "manual" => "manual-mirror",
            _ => "document"
        },
        Url = url,
        SourceType = _definition.SourceType
    };

    private bool IsAllowedHost(string host) => _definition.AllowedHosts.Any(allowed =>
        host.Equals(allowed, StringComparison.OrdinalIgnoreCase) || host.EndsWith($".{allowed}", StringComparison.OrdinalIgnoreCase));

    private static int LinkScore(Uri uri, string text, string model)
    {
        var score = 0;
        if (LooksLikeProductPage(uri)) score += 100;
        if (ContainsModel(uri.AbsolutePath, model)) score += 50;
        if (ContainsModel(text, model)) score += 40;
        if (text.Contains("details", StringComparison.OrdinalIgnoreCase)) score += 25;
        if (uri.AbsolutePath.Contains("search", StringComparison.OrdinalIgnoreCase)) score -= 30;
        return score;
    }

    private static bool LooksLikeProductPage(Uri uri)
    {
        var path = uri.AbsolutePath;
        return path.Contains("/products/detail/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/detail/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/details/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/part/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/product/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/web/p/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/p/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPdfResponse(HttpFetchResult response) =>
        response.ContentType?.Split(';')[0].Trim().Equals("application/pdf", StringComparison.OrdinalIgnoreCase) == true ||
        (response.FinalUri ?? response.RequestedUri).AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private static string InferDocumentTypeFromUrl(Uri uri)
    {
        var combined = $"{uri.AbsolutePath} {uri.Query}";
        if (combined.Contains("datasheet", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("data-sheet", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("technical", StringComparison.OrdinalIgnoreCase)) return "datasheet";
        if (combined.Contains("manual", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("instruction", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("guide", StringComparison.OrdinalIgnoreCase)) return "manual";
        return "document";
    }

    private static bool LooksLikeSecurityCheck(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("Security Check!", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("verify yourself", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("verify you are human", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("unusual traffic", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("captcha", StringComparison.OrdinalIgnoreCase) &&
               (text.Contains("verify", StringComparison.OrdinalIgnoreCase) || text.Contains("human", StringComparison.OrdinalIgnoreCase));
    }

    private static Uri? Resolve(Uri baseUri, string? href) =>
        string.IsNullOrWhiteSpace(href) ? null : Uri.TryCreate(baseUri, href, out var absolute) ? absolute : null;

    private static bool ContainsModel(string? text, string model)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Contains(model, StringComparison.OrdinalIgnoreCase)) return true;
        return Compact(text).Contains(Compact(model), StringComparison.OrdinalIgnoreCase);
    }

    private static string Compact(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static string Clean(string? value) => string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
