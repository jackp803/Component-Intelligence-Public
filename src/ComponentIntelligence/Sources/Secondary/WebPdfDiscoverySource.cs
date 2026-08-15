using System.Collections.Concurrent;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Extraction;
using ComponentIntelligence.Network;

namespace ComponentIntelligence.Sources.Secondary;

/// <summary>
/// Final enrichment-only fallback that uses public search-engine result pages to discover openly
/// reachable engineering PDFs. It never resolves identity and never bypasses CAPTCHA/security checks.
/// Search results are candidates only; their source domain is classified conservatively and every
/// document is still verified through the ordinary evidence pipeline.
/// </summary>
public sealed class WebPdfDiscoverySource : IComponentSource, IComponentSourceMetadata, ISecondaryEnrichmentSource
{
    private const int MaxSearchQueries = 2;
    private const int MaxCandidateDocuments = 8;
    private readonly ComponentHttpClient _http;
    private readonly DocumentPipeline _documents;
    private readonly IRenderedPageFetcher? _rendered;
    private readonly EngineeringDocumentLinkDiscovery _documentLinks = new();
    private readonly ConcurrentDictionary<string, Task<WebPdfDiscoveryResult>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public WebPdfDiscoverySource(
        ComponentHttpClient http,
        DocumentPipeline documents,
        IRenderedPageFetcher? rendered = null)
    {
        _http = http;
        _documents = documents;
        _rendered = rendered;
    }

    public string SourceName => "Public Web PDF Discovery";
    public IReadOnlyCollection<string> SupportedManufacturers => Array.Empty<string>();
    public bool CanHandle(string manufacturer, string model) =>
        !string.IsNullOrWhiteSpace(manufacturer) && !string.IsNullOrWhiteSpace(model);

    // Search engines are discovery aids only. Official identity remains owned by manufacturer sources.
    public Task<IReadOnlyList<ComponentCandidate>> SearchAsync(
        ComponentIdentityQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ComponentCandidate>>(Array.Empty<ComponentCandidate>());

    public Task<ProductPage?> GetProductPageAsync(
        ComponentIdentity identity,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ProductPage?>(null);

    public async Task<IReadOnlyList<ComponentDocument>> DiscoverDocumentsAsync(
        ComponentIdentity identity,
        CancellationToken cancellationToken = default) =>
        (await DiscoverAsync(identity, cancellationToken)).Documents;

    public async Task<RawComponentData> ExtractAsync(
        ComponentIdentity identity,
        CancellationToken cancellationToken = default)
    {
        var discovery = await DiscoverAsync(identity, cancellationToken);
        var specs = new List<RawSpecification>();
        var enrichedDocuments = new List<ComponentDocument>();
        var issues = discovery.Issues.ToList();

        foreach (var document in discovery.Documents)
        {
            var extraction = await _documents.ExtractAsync(document, cancellationToken);
            enrichedDocuments.Add(extraction.Document);
            specs.AddRange(extraction.Specifications);
            if (extraction.NeedsAiReview)
                issues.Add($"WEB_PDF_NEEDS_REVIEW:{document.Url}");
            if (extraction.Error is not null)
                issues.Add($"WEB_PDF_DOCUMENT_ERROR:{document.Url}:{extraction.Error}");
            if (extraction.Diagnostics is not null)
                issues.AddRange(extraction.Diagnostics.Select(value => $"WEB_PDF_DIAGNOSTIC:{document.Url}:{value}"));
        }

        return new RawComponentData
        {
            Specifications = specs,
            Documents = enrichedDocuments,
            Issues = issues
        };
    }

    private Task<WebPdfDiscoveryResult> DiscoverAsync(
        ComponentIdentity identity,
        CancellationToken cancellationToken)
    {
        var key = $"{identity.OfficialManufacturer}\u001f{identity.OfficialModel}";
        return _cache.GetOrAdd(key, _ => DiscoverCoreAsync(identity, cancellationToken));
    }

    private async Task<WebPdfDiscoveryResult> DiscoverCoreAsync(
        ComponentIdentity identity,
        CancellationToken cancellationToken)
    {
        var documents = new List<ComponentDocument>();
        var issues = new List<string>();
        var queries = BuildQueries(identity).Take(MaxSearchQueries).ToArray();

        foreach (var queryUri in queries)
        {
            var response = await _http.FetchAsync(queryUri, cancellationToken);
            if (response.IsSuccess && !LooksLikeSecurityCheck(response.Text))
            {
                CollectSearchResultDocuments(identity, response.Text, response.FinalUri ?? queryUri, documents);
            }
            else if (response.IsSuccess && LooksLikeSecurityCheck(response.Text))
            {
                issues.Add("WEB_PDF_SEARCH_SECURITY_CHECK:google");
                continue;
            }

            if (documents.Count >= MaxCandidateDocuments || _rendered is null) continue;
            var rendered = await _rendered.FetchAsync(queryUri, cancellationToken);
            if (rendered.IsSuccess && !LooksLikeSecurityCheck(rendered.Text))
                CollectSearchResultDocuments(identity, rendered.Text, rendered.FinalUri ?? queryUri, documents);
            else if (rendered.IsSuccess && LooksLikeSecurityCheck(rendered.Text))
                issues.Add("WEB_PDF_SEARCH_SECURITY_CHECK:google-browser");
        }

        return new WebPdfDiscoveryResult(
            documents
                .GroupBy(item => item.Url.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => TrustRank(item.SourceType)).First())
                .Take(MaxCandidateDocuments)
                .ToArray(),
            issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private void CollectSearchResultDocuments(
        ComponentIdentity identity,
        string html,
        Uri resultPage,
        List<ComponentDocument> documents)
    {
        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            var href = anchor.GetAttribute("href");
            var target = ResolveSearchResultUrl(resultPage, href);
            if (target is null || target.Host.Contains("google.", StringComparison.OrdinalIgnoreCase)) continue;

            var context = BuildResultContext(anchor, href);
            if (!ContainsIdentitySignal(context, target, identity)) continue;
            if (!LooksLikePdfCandidate(target, context)) continue;

            var type = InferDocumentType(context, target);
            documents.Add(new ComponentDocument
            {
                Type = type,
                Url = target,
                SourceType = SourceDomainTrustClassifier.Classify(identity.OfficialManufacturer, target.Host, type)
            });
        }

        // Also catch PDF URLs embedded directly in result-page JSON/state. Raw embedded URLs have very
        // little surrounding context, so they are accepted only when the model itself is visible in the
        // URL/hint. This prevents a manufacturer's unrelated PDF from entering the component profile.
        foreach (var link in _documentLinks.Discover(html, resultPage)
                     .Where(item => item.Kind == EngineeringDocumentLinkKind.DirectDocument))
        {
            if (link.Url.Host.Contains("google.", StringComparison.OrdinalIgnoreCase)) continue;
            if (!ContainsIdentitySignal(link.Hint ?? string.Empty, link.Url, identity)) continue;
            documents.Add(new ComponentDocument
            {
                Type = link.DocumentType,
                Url = link.Url,
                SourceType = SourceDomainTrustClassifier.Classify(identity.OfficialManufacturer, link.Url.Host, link.DocumentType)
            });
        }
    }

    private static IReadOnlyList<Uri> BuildQueries(ComponentIdentity identity)
    {
        var manufacturer = identity.OfficialManufacturer.Trim();
        var model = identity.OfficialModel.Trim();
        var terms = new[]
        {
            $"\"{manufacturer}\" \"{model}\" datasheet filetype:pdf",
            $"\"{model}\" (datasheet OR manual OR \"technical data\") filetype:pdf"
        };
        return terms
            .Select(term => new Uri($"https://www.google.com/search?q={Uri.EscapeDataString(term)}&num=10&filter=0"))
            .ToArray();
    }

    private static Uri? ResolveSearchResultUrl(Uri resultPage, string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        if (!Uri.TryCreate(resultPage, href, out var absolute)) return null;

        if (absolute.Host.Contains("google.", StringComparison.OrdinalIgnoreCase) &&
            absolute.AbsolutePath.Equals("/url", StringComparison.OrdinalIgnoreCase))
        {
            var raw = GetQueryValue(absolute.Query, "q") ?? GetQueryValue(absolute.Query, "url");
            return Uri.TryCreate(raw, UriKind.Absolute, out var unwrapped) ? unwrapped : null;
        }

        return absolute;
    }

    private static string? GetQueryValue(string query, string key)
    {
        var trimmed = query.TrimStart('?');
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var name = separator >= 0 ? pair[..separator] : pair;
            if (!string.Equals(Uri.UnescapeDataString(name.Replace('+', ' ')), key, StringComparison.OrdinalIgnoreCase)) continue;
            var value = separator >= 0 ? pair[(separator + 1)..] : string.Empty;
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        return null;
    }

    private static string BuildResultContext(IElement anchor, string? href)
    {
        var parentText = anchor.ParentElement?.TextContent ?? string.Empty;
        return Clean($"{anchor.TextContent} {parentText} {href}");
    }

    private static bool ContainsIdentitySignal(string context, Uri target, ComponentIdentity identity)
    {
        var model = Compact(identity.OfficialModel);
        if (model.Length == 0) return false;
        var combined = Compact($"{context} {target.AbsoluteUri}");
        return combined.Contains(model, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikePdfCandidate(Uri target, string context)
    {
        var combined = $"{target.AbsolutePath} {target.Query} {context}";
        return target.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("datasheet", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("manual", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("technical data", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static string InferDocumentType(string context, Uri target)
    {
        var combined = $"{context} {target.AbsoluteUri}";
        if (combined.Contains("datasheet", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("data sheet", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("technical data", StringComparison.OrdinalIgnoreCase)) return "datasheet-web";
        if (combined.Contains("manual", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("instruction", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("user guide", StringComparison.OrdinalIgnoreCase)) return "manual-web";
        return "document-web";
    }

    private static int TrustRank(ComponentSourceType type) => type switch
    {
        ComponentSourceType.ManufacturerDatasheet => 6,
        ComponentSourceType.ManufacturerManual => 5,
        ComponentSourceType.ManufacturerDownloadCenter => 4,
        ComponentSourceType.AuthorizedDistributor => 3,
        ComponentSourceType.TrustedThirdParty => 2,
        ComponentSourceType.GenericWeb => 1,
        _ => 0
    };

    private static bool LooksLikeSecurityCheck(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("unusual traffic", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Security Check!", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("verify you are human", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("captcha", StringComparison.OrdinalIgnoreCase) &&
               text.Contains("verify", StringComparison.OrdinalIgnoreCase);
    }

    private static string Compact(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static string Clean(string? value) => string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed record WebPdfDiscoveryResult(
        IReadOnlyList<ComponentDocument> Documents,
        IReadOnlyList<string> Issues);
}
