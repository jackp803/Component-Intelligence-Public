using System.Collections.Concurrent;
using System.Text;
using ComponentIntelligence.Extraction;
using Microsoft.Playwright;

namespace ComponentIntelligence.Network;

public interface IRenderedPageFetcher
{
    Task<HttpFetchResult> FetchAsync(Uri uri, CancellationToken cancellationToken = default);
}

public interface IRenderedDocumentDiscoveryFetcher
{
    Task<IReadOnlyList<Uri>> DiscoverDocumentUrlsAsync(Uri uri, CancellationToken cancellationToken = default);
}

/// <summary>
/// Heavy fallback for JavaScript-rendered official catalog pages. It prefers the locally installed
/// Microsoft Edge browser so Component Intelligence does not need to download its own browser binary.
/// In addition to the final DOM, it preserves bounded same-site JSON/XHR responses as embedded JSON
/// script blocks so the normal StructuredSpecificationExtractor can mine product APIs without requiring
/// a site-specific private API adapter.
///
/// On explicit Downloads/Documents pages it also performs bounded, read-only browser interactions on
/// controls whose labels clearly indicate engineering documents. Download events, PDF responses and
/// newly revealed document links are injected back into the returned DOM so existing site adapters can
/// discover them without knowing each site's JavaScript implementation.
/// </summary>
public sealed class PlaywrightRenderedPageFetcher : IRenderedPageFetcher, IRenderedDocumentDiscoveryFetcher
{
    private const int MaxCapturedJsonResponses = 16;
    private const int MaxSingleJsonCharacters = 1_500_000;
    private const int MaxTotalJsonCharacters = 5_000_000;
    private const int MaxDocumentClicks = 6;
    private const int MaxControlsToInspectPerPass = 140;
    private readonly TimeSpan _timeout;
    private readonly EngineeringDocumentLinkDiscovery _documentLinks = new();

    public PlaywrightRenderedPageFetcher(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(25);
    }

    public async Task<HttpFetchResult> FetchAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme is not ("http" or "https"))
            return Failed(uri, "Only HTTP/HTTPS URIs are supported.");

        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await LaunchBrowserAsync(playwright);
            var context = await CreateContextAsync(browser);
            var page = await context.NewPageAsync();
            var jsonResponses = new ConcurrentQueue<IResponse>();
            var documentUrls = new ConcurrentDictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);

            void CaptureDocument(string? rawUrl)
            {
                if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var documentUri)) return;
                if (documentUri.Scheme is not ("http" or "https")) return;
                if (!LooksLikeDirectDocumentUrl(documentUri)) return;
                documentUrls.TryAdd(documentUri.AbsoluteUri, documentUri);
            }

            page.Download += (_, download) => CaptureDocument(download.Url);
            page.Response += (_, response) =>
            {
                if (TryGetHeader(response.Headers, "content-type", out var responseContentType) &&
                    responseContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
                    CaptureDocument(response.Url);
                else if (Uri.TryCreate(response.Url, UriKind.Absolute, out var responseUri) && LooksLikeDirectDocumentUrl(responseUri))
                    CaptureDocument(response.Url);

                if (jsonResponses.Count >= MaxCapturedJsonResponses) return;
                if (!Uri.TryCreate(response.Url, UriKind.Absolute, out var jsonUri) || !IsSameSite(uri, jsonUri)) return;
                if (!TryGetHeader(response.Headers, "content-type", out var contentType) ||
                    !contentType.Contains("json", StringComparison.OrdinalIgnoreCase)) return;
                jsonResponses.Enqueue(response);
            };

            using var registration = cancellationToken.Register(() => _ = page.CloseAsync());
            var response = await NavigateAsync(page, uri);
            await WaitForDynamicContentAsync(page);

            if (LooksLikeDocumentDiscoveryRequest(uri))
                await InteractWithDocumentControlsAsync(page, documentUrls, cancellationToken);
            else
                await CaptureDirectLinksFromCurrentDomAsync(page, documentUrls, cancellationToken);

            var html = await page.ContentAsync();
            html = await AppendCapturedJsonAsync(html, jsonResponses, cancellationToken);
            html = AppendCapturedDocumentLinks(html, documentUrls.Values);
            var finalUrl = page.Url;
            return new HttpFetchResult
            {
                RequestedUri = uri,
                FinalUri = Uri.TryCreate(finalUrl, UriKind.Absolute, out var final) ? final : uri,
                StatusCode = response?.Status ?? 200,
                ContentType = "text/html",
                Content = Encoding.UTF8.GetBytes(html)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failed(uri, $"PLAYWRIGHT:{exception.GetType().Name}:{exception.Message}");
        }
    }

    public async Task<IReadOnlyList<Uri>> DiscoverDocumentUrlsAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme is not ("http" or "https")) return Array.Empty<Uri>();

        var discovered = new ConcurrentDictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await LaunchBrowserAsync(playwright);
            var context = await CreateContextAsync(browser);
            var page = await context.NewPageAsync();

            void Capture(string? rawUrl)
            {
                if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var documentUri)) return;
                if (documentUri.Scheme is not ("http" or "https")) return;
                if (!LooksLikeDirectDocumentUrl(documentUri)) return;
                discovered.TryAdd(documentUri.AbsoluteUri, documentUri);
            }

            page.Download += (_, download) => Capture(download.Url);
            page.Response += (_, response) =>
            {
                var contentTypeIsPdf = TryGetHeader(response.Headers, "content-type", out var contentType) &&
                                       contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase);
                if (contentTypeIsPdf || Uri.TryCreate(response.Url, UriKind.Absolute, out var responseUri) && LooksLikeDirectDocumentUrl(responseUri))
                    Capture(response.Url);
            };

            using var registration = cancellationToken.Register(() => _ = page.CloseAsync());
            await NavigateAsync(page, uri);
            await WaitForDynamicContentAsync(page);
            await InteractWithDocumentControlsAsync(page, discovered, cancellationToken);

            return discovered.Values
                .OrderBy(documentUri => documentUri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                .Take(40)
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Browser interaction is a final fallback only. Failure must never invalidate successful
            // deterministic discovery done by HTTP/DOM/JSON/document crawling.
            return discovered.Values.ToArray();
        }
    }

    private async Task InteractWithDocumentControlsAsync(
        IPage page,
        ConcurrentDictionary<string, Uri> discovered,
        CancellationToken cancellationToken)
    {
        await CaptureDirectLinksFromCurrentDomAsync(page, discovered, cancellationToken);
        var clickedControls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var click = 0; click < MaxDocumentClicks; click++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (page.IsClosed) break;

            var controls = page.Locator("a,button,[role='button'],input[type='button'],input[type='submit']");
            var count = Math.Min(await controls.CountAsync(), MaxControlsToInspectPerPass);
            ILocator? candidate = null;

            for (var index = 0; index < count; index++)
            {
                var locator = controls.Nth(index);
                var descriptor = await DescribeControlAsync(locator);
                if (string.IsNullOrWhiteSpace(descriptor) || !LooksLikeDocumentControl(descriptor) || LooksUnsafeToClick(descriptor))
                    continue;
                if (!clickedControls.Add(descriptor)) continue;

                candidate = locator;
                break;
            }

            if (candidate is null) break;

            try
            {
                await candidate.ClickAsync(new LocatorClickOptions
                {
                    Force = true,
                    Timeout = 2200
                });
                await page.WaitForTimeoutAsync(450);
            }
            catch (PlaywrightException)
            {
                // The control may detach after opening a tab/download. Captured responses/downloads
                // remain useful, so continue with the current DOM when possible.
            }

            if (page.IsClosed) break;
            if (Uri.TryCreate(page.Url, UriKind.Absolute, out var currentUri) && LooksLikeDirectDocumentUrl(currentUri))
            {
                discovered.TryAdd(currentUri.AbsoluteUri, currentUri);
                break;
            }

            await CaptureDirectLinksFromCurrentDomAsync(page, discovered, cancellationToken);
        }
    }

    private async Task CaptureDirectLinksFromCurrentDomAsync(
        IPage page,
        ConcurrentDictionary<string, Uri> discovered,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var html = await page.ContentAsync();
        if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var pageUri)) return;
        foreach (var link in _documentLinks.Discover(html, pageUri)
                     .Where(link => link.Kind == EngineeringDocumentLinkKind.DirectDocument))
            discovered.TryAdd(link.Url.AbsoluteUri, link.Url);
    }

    private async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright) =>
        await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Channel = "msedge",
            Timeout = (float)_timeout.TotalMilliseconds
        });

    private static async Task<IBrowserContext> CreateContextAsync(IBrowser browser) =>
        await browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "zh-TW",
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36"
        });

    private async Task<IResponse?> NavigateAsync(IPage page, Uri uri) =>
        await page.GotoAsync(uri.AbsoluteUri, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = (float)_timeout.TotalMilliseconds
        });

    private static async Task WaitForDynamicContentAsync(IPage page)
    {
        try
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 4000 });
        }
        catch (PlaywrightException)
        {
            // Dynamic sites often keep analytics connections open. DOM content is enough for parsing.
        }
    }

    private static async Task<string> DescribeControlAsync(ILocator locator)
    {
        try
        {
            var values = new[]
            {
                await locator.TextContentAsync(),
                await locator.GetAttributeAsync("title"),
                await locator.GetAttributeAsync("aria-label"),
                await locator.GetAttributeAsync("href"),
                await locator.GetAttributeAsync("data-url"),
                await locator.GetAttributeAsync("data-download"),
                await locator.GetAttributeAsync("data-download-url"),
                await locator.GetAttributeAsync("class"),
                await locator.GetAttributeAsync("id")
            };
            return string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        }
        catch (PlaywrightException)
        {
            return string.Empty;
        }
    }

    private static bool LooksLikeDocumentDiscoveryRequest(Uri uri)
    {
        var text = uri.PathAndQuery.ToLowerInvariant();
        return new[]
        {
            "download", "document", "manual", "datasheet", "data-sheet", "technical", "literature", "resource"
        }.Any(hint => text.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeDocumentControl(string descriptor)
    {
        var text = descriptor.ToLowerInvariant();
        return new[]
        {
            "download", "downloads", "document", "documents", "datasheet", "data sheet", "manual",
            "instruction", "technical data", "technical document", "spec sheet", "pdf", "literature",
            "下載", "下载", "文件", "規格書", "規格", "技術資料", "技术资料", "手冊", "手册", "操作說明", "操作说明"
        }.Any(hint => text.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksUnsafeToClick(string descriptor)
    {
        var text = descriptor.ToLowerInvariant();
        return new[]
        {
            "cart", "basket", "buy", "purchase", "order", "quote", "request quote", "contact",
            "login", "sign in", "share", "email", "favorite", "wishlist", "購物車", "购物车", "購買", "购买", "詢價", "询价", "登入"
        }.Any(hint => text.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeDirectDocumentUrl(Uri uri)
    {
        var value = $"{uri.AbsolutePath} {uri.Query}".ToLowerInvariant();
        return value.Contains(".pdf", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("/dam/", StringComparison.OrdinalIgnoreCase) && value.Contains("original", StringComparison.OrdinalIgnoreCase);
    }

    private static string AppendCapturedDocumentLinks(string html, IEnumerable<Uri> documentUrls)
    {
        var urls = documentUrls
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToArray();
        if (urls.Length == 0) return html;

        var block = new StringBuilder();
        foreach (var documentUrl in urls)
        {
            block.Append("\n<a data-component-intelligence-discovered-document=\"browser-interaction\" href=\"")
                .Append(System.Net.WebUtility.HtmlEncode(documentUrl.AbsoluteUri))
                .Append("\">Download PDF</a>\n");
        }

        var closingBody = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return closingBody >= 0
            ? html.Insert(closingBody, block.ToString())
            : html + block;
    }

    private static async Task<string> AppendCapturedJsonAsync(
        string html,
        ConcurrentQueue<IResponse> responses,
        CancellationToken cancellationToken)
    {
        if (responses.IsEmpty) return html;

        var blocks = new StringBuilder();
        var totalCharacters = 0;
        var index = 0;
        while (responses.TryDequeue(out var response) && index < MaxCapturedJsonResponses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var text = await response.TextAsync();
                if (string.IsNullOrWhiteSpace(text) || text.Length > MaxSingleJsonCharacters) continue;
                if (totalCharacters + text.Length > MaxTotalJsonCharacters) break;
                var trimmed = text.TrimStart();
                if (!(trimmed.StartsWith('{') || trimmed.StartsWith('['))) continue;

                var safeJson = text.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);
                blocks.Append("\n<script type=\"application/json\" data-component-intelligence-source=\"")
                    .Append(System.Net.WebUtility.HtmlEncode(response.Url))
                    .Append("\">")
                    .Append(safeJson)
                    .Append("</script>\n");
                totalCharacters += text.Length;
                index++;
            }
            catch (PlaywrightException)
            {
                // A response body can be unavailable after redirects/cancellation. Keep other evidence.
            }
        }

        if (blocks.Length == 0) return html;
        var closingBody = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return closingBody >= 0
            ? html.Insert(closingBody, blocks.ToString())
            : html + blocks;
    }

    private static bool IsSameSite(Uri requested, Uri response)
    {
        static string RegistrableApproximation(string host)
        {
            var normalized = host.Trim('.').ToLowerInvariant();
            var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length <= 2 ? normalized : string.Join('.', parts.TakeLast(2));
        }

        return string.Equals(requested.Host, response.Host, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(RegistrableApproximation(requested.Host), RegistrableApproximation(response.Host), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetHeader(IReadOnlyDictionary<string, string> headers, string name, out string value)
    {
        foreach (var pair in headers)
        {
            if (!string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = pair.Value;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static HttpFetchResult Failed(Uri uri, string error) => new()
    {
        RequestedUri = uri,
        Error = error
    };
}
