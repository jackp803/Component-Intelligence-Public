using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ComponentIntelligence.Desktop;

/// <summary>
/// Lightweight visual cache for topology product images. This is display-only and never feeds
/// engineering inference. It uses ordinary HTTP/file IO; no OpenCV or vision runtime is involved.
/// </summary>
public sealed class TopologyImageCache
{
    private const long MaximumBytes = 5 * 1024 * 1024;
    private const int MaximumProductPageBytes = 2 * 1024 * 1024;
    private readonly string _root;
    private readonly HttpClient _http;

    public TopologyImageCache(string? root = null, HttpClient? httpClient = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ComponentIntelligence",
            "topology-images");
        Directory.CreateDirectory(_root);
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public Task<string?> GetLocalPathAsync(Uri? source, CancellationToken cancellationToken = default) =>
        GetLocalPathAsync(source, null, cancellationToken);

    /// <summary>
    /// Tries the explicit image URL first. If a vendor CDN rejects direct hot-link access, the public
    /// product page is inspected for its og:image/twitter:image and that image is cached instead.
    /// </summary>
    public async Task<string?> GetLocalPathAsync(
        Uri? source,
        Uri? productPage,
        CancellationToken cancellationToken = default)
    {
        var direct = await TryGetLocalPathAsync(source, cancellationToken);
        if (!string.IsNullOrWhiteSpace(direct)) return direct;

        var discovered = await DiscoverProductImageAsync(productPage, cancellationToken);
        if (discovered is null || Uri.Compare(discovered, source, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0)
            return null;

        return await TryGetLocalPathAsync(discovered, cancellationToken);
    }

    private async Task<string?> TryGetLocalPathAsync(Uri? source, CancellationToken cancellationToken)
    {
        if (source is null) return null;
        if (source.IsFile)
            return File.Exists(source.LocalPath) ? source.LocalPath : null;
        if (source.Scheme is not ("http" or "https")) return null;

        var extension = SafeExtension(Path.GetExtension(source.AbsolutePath));
        var path = Path.Combine(_root, Hash(source.AbsoluteUri) + extension);
        if (File.Exists(path) && new FileInfo(path).Length is > 0 and <= MaximumBytes) return path;

        try
        {
            using var request = BuildImageRequest(source);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumBytes) return null;
            var media = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (media is not null && !media.StartsWith("image/", StringComparison.Ordinal)) return null;

            var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                var buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (read == 0) break;
                    total += read;
                    if (total > MaximumBytes) return null;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                await output.FlushAsync(cancellationToken);
                if (total == 0) return null;
                File.Move(temp, path, overwrite: true);
                return path;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            return File.Exists(path) ? path : null;
        }
    }

    private async Task<Uri?> DiscoverProductImageAsync(Uri? productPage, CancellationToken cancellationToken)
    {
        if (productPage is null || productPage.Scheme is not ("http" or "https")) return null;
        try
        {
            using var request = BuildPageRequest(productPage);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumProductPageBytes) return null;

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (html.Length > MaximumProductPageBytes) html = html[..MaximumProductPageBytes];
            var image = ExtractMetaImage(html);
            if (string.IsNullOrWhiteSpace(image)) return null;
            image = WebUtility.HtmlDecode(image.Trim());
            return Uri.TryCreate(image, UriKind.Absolute, out var absolute)
                ? absolute
                : Uri.TryCreate(productPage, image, out var relative) ? relative : null;
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    internal static string? ExtractMetaImage(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        foreach (var property in new[] { "og:image", "twitter:image", "twitter:image:src" })
        {
            var escaped = Regex.Escape(property);
            var first = Regex.Match(
                html,
                $"<meta[^>]+(?:property|name)\\s*=\\s*[\"']{escaped}[\"'][^>]+content\\s*=\\s*[\"'](?<url>[^\"']+)[\"'][^>]*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (first.Success) return first.Groups["url"].Value;

            var reversed = Regex.Match(
                html,
                $"<meta[^>]+content\\s*=\\s*[\"'](?<url>[^\"']+)[\"'][^>]+(?:property|name)\\s*=\\s*[\"']{escaped}[\"'][^>]*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (reversed.Success) return reversed.Groups["url"].Value;
        }
        return null;
    }

    private static HttpRequestMessage BuildImageRequest(Uri source)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, source);
        ApplyBrowserHeaders(request);

        // Moxa publishes product images on its public Azure Front Door CDN. The browser obtains those
        // images from moxa.com, so preserve that public-page context instead of looking like a hotlink bot.
        if (source.Host.EndsWith("azurefd.net", StringComparison.OrdinalIgnoreCase) &&
            source.AbsolutePath.Contains("moxa", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Referrer = new Uri("https://www.moxa.com/");
        }

        return request;
    }

    private static HttpRequestMessage BuildPageRequest(Uri source)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, source);
        ApplyBrowserHeaders(request);
        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        return request;
    }

    private static void ApplyBrowserHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/126.0 Safari/537.36 ComponentIntelligence/1.0");
        request.Headers.Accept.ParseAdd("image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string SafeExtension(string extension)
    {
        extension = extension.ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif" ? extension : ".img";
    }
}
