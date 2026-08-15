using Microsoft.Playwright;

namespace ComponentIntelligence.Network;

public interface IProductDetailDiscoveryFetcher
{
    Task<IReadOnlyList<Uri>> DiscoverDetailUrlsAsync(
        Uri searchUri,
        string model,
        IReadOnlyCollection<string> allowedHosts,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Bounded browser helper for catalog/aggregator search pages where the exact part detail page is
/// revealed only after pressing Details / See More / Part Details. It never bypasses CAPTCHA/security
/// checks and never clicks purchase, login, quote or other unrelated controls.
/// </summary>
public sealed class PlaywrightProductDetailDiscoveryFetcher : IProductDetailDiscoveryFetcher
{
    private const int MaxControls = 160;
    private const int MaxClicks = 4;
    private readonly TimeSpan _timeout;

    public PlaywrightProductDetailDiscoveryFetcher(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(25);
    }

    public async Task<IReadOnlyList<Uri>> DiscoverDetailUrlsAsync(
        Uri searchUri,
        string model,
        IReadOnlyCollection<string> allowedHosts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searchUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (allowedHosts.Count == 0) return Array.Empty<Uri>();

        var discovered = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Channel = "msedge",
                Timeout = (float)_timeout.TotalMilliseconds
            });
            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                Locale = "zh-TW",
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36"
            });
            var page = await context.NewPageAsync();
            using var registration = cancellationToken.Register(() => _ = page.CloseAsync());

            await page.GotoAsync(searchUri.AbsoluteUri, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = (float)_timeout.TotalMilliseconds
            });
            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 4000 });
            }
            catch (PlaywrightException)
            {
                // Analytics/streaming requests may keep the page busy; DOMContentLoaded is sufficient.
            }

            var bodyText = await page.Locator("body").TextContentAsync() ?? string.Empty;
            if (LooksLikeSecurityCheck(bodyText)) return Array.Empty<Uri>();

            await CollectVisibleDetailLinksAsync(page, searchUri, model, allowedHosts, discovered);
            if (discovered.Count > 0) return discovered.Values.Take(8).ToArray();

            var clickedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var click = 0; click < MaxClicks && !page.IsClosed; click++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var controls = page.Locator("a,button,[role='button'],input[type='button'],input[type='submit']");
                var count = Math.Min(await controls.CountAsync(), MaxControls);
                ILocator? candidate = null;
                string? candidateKey = null;

                for (var index = 0; index < count; index++)
                {
                    var locator = controls.Nth(index);
                    var descriptor = await DescribeControlAsync(locator);
                    if (string.IsNullOrWhiteSpace(descriptor) || !LooksLikeDetailControl(descriptor) || LooksUnsafeToClick(descriptor))
                        continue;

                    var localContext = await ReadLocalContextAsync(locator);
                    if (!ContainsModel($"{descriptor} {localContext}", model)) continue;

                    var key = Compact($"{descriptor}|{localContext}");
                    if (!clickedKeys.Add(key)) continue;
                    candidate = locator;
                    candidateKey = key;
                    break;
                }

                if (candidate is null) break;
                var beforePages = context.Pages.Select(item => item.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var beforeUrl = page.Url;

                try
                {
                    await candidate.ClickAsync(new LocatorClickOptions
                    {
                        Force = true,
                        Timeout = 2200
                    });
                    await page.WaitForTimeoutAsync(500);
                }
                catch (PlaywrightException)
                {
                    // Navigation/popup may detach the source control. Inspect resulting pages anyway.
                }

                foreach (var candidatePage in context.Pages)
                {
                    var url = candidatePage.Url;
                    if (string.IsNullOrWhiteSpace(url) || beforePages.Contains(url) && string.Equals(url, beforeUrl, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
                        IsAllowedHost(parsed.Host, allowedHosts) &&
                        LooksLikeDetailPage(parsed))
                        discovered.TryAdd(parsed.AbsoluteUri, parsed);
                }

                if (!page.IsClosed && Uri.TryCreate(page.Url, UriKind.Absolute, out var current) &&
                    !string.Equals(page.Url, beforeUrl, StringComparison.OrdinalIgnoreCase) &&
                    IsAllowedHost(current.Host, allowedHosts) &&
                    LooksLikeDetailPage(current))
                    discovered.TryAdd(current.AbsoluteUri, current);

                if (discovered.Count == 0 && !page.IsClosed)
                    await CollectVisibleDetailLinksAsync(page, searchUri, model, allowedHosts, discovered);

                if (discovered.Count > 0) break;
                if (candidateKey is null) break;
            }

            return discovered.Values.Take(8).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // This is a final interaction fallback; failure never invalidates static/rendered search.
            return discovered.Values.Take(8).ToArray();
        }
    }

    private static async Task CollectVisibleDetailLinksAsync(
        IPage page,
        Uri baseUri,
        string model,
        IReadOnlyCollection<string> allowedHosts,
        IDictionary<string, Uri> discovered)
    {
        var anchors = page.Locator("a[href]");
        var count = Math.Min(await anchors.CountAsync(), MaxControls);
        for (var index = 0; index < count; index++)
        {
            var anchor = anchors.Nth(index);
            var href = await anchor.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(href) || !Uri.TryCreate(baseUri, href, out var uri)) continue;
            if (!IsAllowedHost(uri.Host, allowedHosts) || !LooksLikeDetailPage(uri)) continue;

            var descriptor = await DescribeControlAsync(anchor);
            var localContext = await ReadLocalContextAsync(anchor);
            if (!ContainsModel($"{descriptor} {localContext} {href}", model)) continue;
            discovered[uri.AbsoluteUri] = uri;
        }
    }

    private static async Task<string> ReadLocalContextAsync(ILocator locator)
    {
        try
        {
            return await locator.EvaluateAsync<string>("""
                el => {
                  const scope = el.closest('tr, li, article, [role="row"], .product, .result, .search-result, .part-row, .card')
                             || el.parentElement;
                  return (scope?.innerText || el.innerText || '').trim();
                }
                """) ?? string.Empty;
        }
        catch (PlaywrightException)
        {
            return string.Empty;
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

    private static bool LooksLikeDetailControl(string descriptor)
    {
        var text = descriptor.ToLowerInvariant();
        return new[]
        {
            "details", "detail", "part details", "product details", "see more", "view details",
            "view product", "more info", "more information", "詳細", "詳情", "查看更多", "更多資訊", "更多信息"
        }.Any(hint => text.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksUnsafeToClick(string descriptor)
    {
        var text = descriptor.ToLowerInvariant();
        return new[]
        {
            "cart", "basket", "buy", "purchase", "order", "quote", "request quote", "contact",
            "login", "sign in", "register", "share", "email", "favorite", "wishlist",
            "購物車", "购物车", "購買", "购买", "詢價", "询价", "登入", "註冊", "注册"
        }.Any(hint => text.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeDetailPage(Uri uri)
    {
        var path = uri.AbsolutePath.ToLowerInvariant();
        return path.Contains("/detail/") || path.Contains("/details/") || path.Contains("/part/") ||
               path.Contains("/product/") || path.Contains("/products/") || path.Contains("/web/p/") ||
               path.Contains("/p/");
    }

    private static bool IsAllowedHost(string host, IReadOnlyCollection<string> allowedHosts) =>
        allowedHosts.Any(allowed =>
            host.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith($".{allowed}", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsModel(string text, string model)
    {
        if (text.Contains(model, StringComparison.OrdinalIgnoreCase)) return true;
        return Compact(text).Contains(Compact(model), StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSecurityCheck(string text) =>
        text.Contains("Security Check!", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("verify you are human", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("unusual traffic", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("captcha", StringComparison.OrdinalIgnoreCase) &&
        (text.Contains("verify", StringComparison.OrdinalIgnoreCase) || text.Contains("human", StringComparison.OrdinalIgnoreCase));

    private static string Compact(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
