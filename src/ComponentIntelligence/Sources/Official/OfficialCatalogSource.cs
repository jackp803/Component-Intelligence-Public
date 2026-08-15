using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Extraction;
using ComponentIntelligence.Network;
using ComponentIntelligence.Resolution;

namespace ComponentIntelligence.Sources.Official;

public sealed record OfficialCatalogSourceDefinition(
    string SourceName,
    string CanonicalManufacturer,
    IReadOnlyCollection<string> ManufacturerAliases,
    IReadOnlyCollection<string> AllowedHosts,
    Func<string, IReadOnlyList<Uri>> LookupUris,
    Func<string, bool>? ModelFilter = null);

public sealed class OfficialCatalogSource : IComponentSource, IComponentSourceMetadata
{
    private const int MaxDocumentFollowPages = 8;
    private const int MaxRenderedFollowPages = 3;
    private readonly OfficialCatalogSourceDefinition _definition;
    private readonly ComponentHttpClient _http;
    private readonly SpecificationParser _parser;
    private readonly DocumentPipeline _documents;
    private readonly IRenderedPageFetcher? _rendered;
    private readonly HashSet<string> _manufacturers;
    private readonly EngineeringDocumentLinkDiscovery _documentLinks = new();

    public OfficialCatalogSource(
        OfficialCatalogSourceDefinition definition,
        ComponentHttpClient http,
        SpecificationParser parser,
        DocumentPipeline documents,
        IRenderedPageFetcher? rendered = null)
    {
        _definition = definition;
        _http = http;
        _parser = parser;
        _documents = documents;
        _rendered = rendered;
        _manufacturers = definition.ManufacturerAliases
            .Append(definition.CanonicalManufacturer)
            .Select(ManufacturerNormalizer.NormalizeKey)
            .Where(value => value is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string SourceName => _definition.SourceName;
    public IReadOnlyCollection<string> SupportedManufacturers => _manufacturers;

    public bool CanHandle(string manufacturer, string model) =>
        _manufacturers.Contains(ManufacturerNormalizer.NormalizeKey(manufacturer) ?? manufacturer) &&
        (_definition.ModelFilter?.Invoke(model) ?? true);

    public async Task<IReadOnlyList<ComponentCandidate>> SearchAsync(ComponentIdentityQuery query, CancellationToken cancellationToken = default)
    {
        var manufacturer = ManufacturerNormalizer.NormalizeKey(query.NormalizedManufacturer ?? query.RawManufacturer);
        var model = ModelNormalizer.Normalize(query.NormalizedModel ?? query.RawModel)?.Canonical;
        if (manufacturer is null || model is null || !CanHandle(manufacturer, model))
            return Array.Empty<ComponentCandidate>();

        var lookupUris = _definition.LookupUris(model).DistinctBy(item => item.AbsoluteUri, StringComparer.OrdinalIgnoreCase).ToArray();
        var found = new List<ComponentCandidate>();
        var failures = new List<string>();
        var successfulPages = 0;
        foreach (var uri in lookupUris)
        {
            var result = await _http.FetchAsync(uri, cancellationToken);
            if (!result.IsSuccess)
            {
                failures.Add($"HTTP:{uri} => {(result.Error ?? result.StatusCode.ToString())}");
                continue;
            }

            successfulPages++;
            found.AddRange(ExtractCandidates(result, model));
        }

        if (found.Count == 0 && _rendered is not null)
        {
            foreach (var uri in lookupUris)
            {
                var rendered = await _rendered.FetchAsync(uri, cancellationToken);
                if (!rendered.IsSuccess)
                {
                    failures.Add($"BROWSER:{uri} => {(rendered.Error ?? rendered.StatusCode.ToString())}");
                    continue;
                }
                successfulPages++;
                found.AddRange(ExtractCandidates(rendered, model));
                if (found.Count > 0) break;
            }
        }

        if (found.Count == 0 && successfulPages == 0 && failures.Count > 0)
            throw new InvalidOperationException($"{SourceName} lookup failed: {string.Join(" | ", failures.Take(6))}");

        return found
            .GroupBy(candidate => candidate.ProductUrl?.AbsoluteUri ?? $"{candidate.Manufacturer}|{candidate.OfficialModel}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First() with { Evidence = group.SelectMany(item => item.Evidence).ToArray() })
            .ToArray();
    }

    public async Task<ProductPage?> GetProductPageAsync(ComponentIdentity identity, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(identity.OfficialManufacturer, identity.OfficialModel)) return null;
        var uri = identity.OfficialProductUrl;
        if (uri is null)
        {
            var candidates = await SearchAsync(new ComponentIdentityQuery
            {
                RawManufacturer = identity.OfficialManufacturer,
                RawModel = identity.OfficialModel,
                NormalizedManufacturer = identity.OfficialManufacturer,
                NormalizedModel = identity.OfficialModel
            }, cancellationToken);
            uri = candidates
                .OrderByDescending(candidate => LooksLikeProductPage(candidate.ProductUrl))
                .FirstOrDefault(candidate => candidate.ProductUrl is not null)?.ProductUrl;
        }

        if (uri is null) return null;
        var page = await _http.FetchAsync(uri, cancellationToken);
        if (page.IsSuccess && ContainsModel(page.Text, identity.OfficialModel))
            return new ProductPage { Url = page.FinalUri ?? uri, RawContent = page.Text };

        if (_rendered is null) return null;
        var rendered = await _rendered.FetchAsync(uri, cancellationToken);
        return rendered.IsSuccess && ContainsModel(rendered.Text, identity.OfficialModel)
            ? new ProductPage { Url = rendered.FinalUri ?? uri, RawContent = rendered.Text }
            : null;
    }

    public async Task<IReadOnlyList<ComponentDocument>> DiscoverDocumentsAsync(ComponentIdentity identity, CancellationToken cancellationToken = default)
    {
        var page = await GetProductPageAsync(identity, cancellationToken);
        if (page?.RawContent is null) return Array.Empty<ComponentDocument>();

        var documents = new List<ComponentDocument>();
        var followPages = new List<Uri>();
        CollectDocumentLinks(page.RawContent, page.Url, documents, followPages);
        await CrawlFollowPagesAsync(followPages, [page.Url], documents, cancellationToken);

        return documents
            .GroupBy(item => item.Url.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => DocumentPriority(item.Type)).First())
            .Take(30)
            .ToArray();
    }

    public async Task<RawComponentData> ExtractAsync(ComponentIdentity identity, CancellationToken cancellationToken = default)
    {
        var page = await GetProductPageAsync(identity, cancellationToken);
        if (page?.RawContent is null)
            return new RawComponentData { Issues = [$"{SourceName.ToUpperInvariant()}_PRODUCT_PAGE_UNAVAILABLE"] };

        var specs = _parser.ParseHtml(page.RawContent, page.Url).ToList();
        var documents = (await DiscoverDocumentsAsync(identity, cancellationToken)).ToList();
        var enrichedDocuments = new List<ComponentDocument>();
        var issues = new List<string>();
        foreach (var document in documents)
        {
            var extraction = await _documents.ExtractAsync(document, cancellationToken);
            enrichedDocuments.Add(extraction.Document);
            specs.AddRange(extraction.Specifications);
            if (extraction.NeedsAiReview) issues.Add($"NEEDS_AI_REVIEW:{document.Url}");
            if (extraction.Error is not null) issues.Add($"DOCUMENT_ERROR:{extraction.Error}");
            if (extraction.Diagnostics is not null)
                issues.AddRange(extraction.Diagnostics.Select(diagnostic => $"DOCUMENT_DIAGNOSTIC:{document.Url}:{diagnostic}"));
        }

        return new RawComponentData
        {
            Specifications = specs,
            Documents = enrichedDocuments,
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

            if (IsAllowedHost(link.Url.Host))
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
                     .Where(uri => IsAllowedHost(uri.Host))
                     .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                     .Take(MaxDocumentFollowPages))
        {
            if (!visited.Add(uri.AbsoluteUri)) continue;

            var before = documents.Count;
            var response = await _http.FetchAsync(uri, cancellationToken);
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

    private IReadOnlyList<ComponentCandidate> ExtractCandidates(HttpFetchResult result, string model)
    {
        var pageUri = result.FinalUri ?? result.RequestedUri;
        var parser = new HtmlParser();
        var document = parser.ParseDocument(result.Text);
        var candidates = new List<ComponentCandidate>();
        var pageTitle = document.QuerySelector("h1")?.TextContent?.Trim() ?? document.Title?.Trim();

        if (LooksLikeProductPage(pageUri) && ContainsModel($"{pageTitle} {result.Text}", model))
            candidates.Add(Candidate(model, pageUri, pageTitle, pageUri));

        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            var href = anchor.GetAttribute("href");
            var absolute = Resolve(pageUri, href);
            if (absolute is null || !IsAllowedHost(absolute.Host)) continue;
            var anchorText = $"{anchor.TextContent} {href}";
            if (!ContainsModel(anchorText, model)) continue;
            candidates.Add(Candidate(model, absolute, anchor.TextContent.Trim(), pageUri));
        }

        if (candidates.Count == 0 && ContainsModel(result.Text, model))
            candidates.Add(Candidate(model, LooksLikeProductPage(pageUri) ? pageUri : null, pageTitle, pageUri));

        return candidates;
    }

    private ComponentCandidate Candidate(string model, Uri? productUrl, string? title, Uri evidenceUrl)
    {
        var evidence = new Evidence
        {
            SourceType = ComponentSourceType.ManufacturerProductPage,
            SourceUrl = evidenceUrl,
            ExtractionMethod = ExtractionMethod.Html,
            RawValue = model,
            RetrievedAt = DateTimeOffset.UtcNow,
            VerificationStatus = VerificationStatus.SingleSource
        };
        return new ComponentCandidate
        {
            Manufacturer = _definition.CanonicalManufacturer,
            OfficialModel = model,
            Mpn = model,
            SourceType = ComponentSourceType.ManufacturerProductPage,
            ProductUrl = productUrl,
            RawSourceTitle = title,
            Evidence = [evidence]
        };
    }

    private bool IsAllowedHost(string host) => _definition.AllowedHosts.Any(allowed =>
        host.Equals(allowed, StringComparison.OrdinalIgnoreCase) || host.EndsWith($".{allowed}", StringComparison.OrdinalIgnoreCase));

    private static Uri? Resolve(Uri baseUri, string? href) =>
        string.IsNullOrWhiteSpace(href) ? null : Uri.TryCreate(baseUri, href, out var absolute) ? absolute : null;

    private static bool LooksLikeProductPage(Uri? uri)
    {
        if (uri is null) return false;
        var path = uri.AbsolutePath;
        return path.Contains("/product/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/products/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/p/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("product_detail", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/family/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsModel(string? text, string model)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Contains(model, StringComparison.OrdinalIgnoreCase)) return true;
        return Compact(text).Contains(Compact(model), StringComparison.OrdinalIgnoreCase);
    }

    private static string Compact(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static bool IsPdfResponse(HttpFetchResult response) =>
        response.ContentType?.Split(';')[0].Trim().Equals("application/pdf", StringComparison.OrdinalIgnoreCase) == true ||
        (response.FinalUri ?? response.RequestedUri).AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private static string InferDocumentTypeFromUrl(Uri uri)
    {
        var normalized = $"{uri.AbsolutePath} {uri.Query}".ToLowerInvariant();
        if (normalized.Contains("datasheet") || normalized.Contains("data-sheet") || normalized.Contains("technical") || normalized.Contains("spec")) return "datasheet";
        if (normalized.Contains("manual") || normalized.Contains("instruction") || normalized.Contains("guide")) return "manual";
        return "document";
    }

    private static int DocumentPriority(string type) => type.ToLowerInvariant() switch
    {
        "datasheet" => 3,
        "manual" => 2,
        "document" => 1,
        _ => 0
    };
}
