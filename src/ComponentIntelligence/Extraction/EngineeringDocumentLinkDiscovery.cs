using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace ComponentIntelligence.Extraction;

public enum EngineeringDocumentLinkKind
{
    DirectDocument,
    FollowPage
}

public sealed record EngineeringDocumentLink(
    Uri Url,
    EngineeringDocumentLinkKind Kind,
    string DocumentType,
    string DiscoveryMethod,
    string? Hint = null);

/// <summary>
/// Finds engineering documents deeper than literal &lt;a href="...pdf"&gt; links.
/// It inspects normal links, button/data attributes, JavaScript-ish attributes and raw embedded
/// HTML/JSON text. It also returns a small set of likely Downloads/Documents pages for a bounded
/// one-hop crawl by the site adapter.
/// </summary>
public sealed class EngineeringDocumentLinkDiscovery
{
    private const int MaxRawUrlMatches = 96;
    private static readonly string[] UriAttributes =
    [
        "href", "src", "data-href", "data-url", "data-download", "data-download-url",
        "data-file", "data-file-url", "data-pdf", "data-document", "data-document-url",
        "content", "onclick"
    ];

    private static readonly string[] DirectHints =
    [
        "pdf", "datasheet", "data sheet", "technical data", "technical datasheet", "manual",
        "instruction", "operating instruction", "user guide", "download file", "download pdf",
        "specification", "spec sheet", "規格書", "規格", "技術資料", "技术资料", "手冊", "手册",
        "操作說明", "操作说明", "下載 pdf", "下载 pdf"
    ];

    private static readonly string[] FollowHints =
    [
        "download", "downloads", "document", "documents", "literature", "technical documents",
        "resources", "media", "manuals", "下載", "下载", "文件", "技術文件", "技术文件"
    ];

    private static readonly Regex AbsoluteUrl = new(
        "https?://[A-Za-z0-9\\-._~:/?#\\[\\]@!$&'()*+,;=%]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RelativePdf = new(
        "(?<url>/[A-Za-z0-9\\-._~:/?#\\[\\]@!$&'()*+,;=%]*?\\.pdf(?:\\?[A-Za-z0-9\\-._~:/?#\\[\\]@!$&'()*+,;=%]*)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UrlInsideAttribute = new(
        "(?<url>https?://[^\\s'\\\"<>]+|/[^\\s'\\\"<>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IReadOnlyList<EngineeringDocumentLink> Discover(string html, Uri pageUrl)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(pageUrl);

        var results = new List<EngineeringDocumentLink>();
        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);

        foreach (var element in document.All)
        {
            var hint = BuildHint(element);
            foreach (var attributeName in UriAttributes)
            {
                var raw = element.GetAttribute(attributeName);
                if (string.IsNullOrWhiteSpace(raw)) continue;

                foreach (var candidate in ExtractAttributeUrls(raw, pageUrl))
                    TryAdd(results, candidate, hint, $"dom:{element.TagName.ToLowerInvariant()}@{attributeName}");
            }
        }

        // Modern product pages frequently embed asset URLs in JSON/script state rather than DOM hrefs.
        // Normalize common JSON escaping before scanning so strings like https:\/\/media.ifm.com\/dam\/...pdf
        // become ordinary URLs.
        var normalizedRaw = NormalizeEscapedText(html);
        foreach (Match match in AbsoluteUrl.Matches(normalizedRaw).Cast<Match>().Take(MaxRawUrlMatches))
        {
            if (Uri.TryCreate(CleanUrl(match.Value), UriKind.Absolute, out var uri))
                TryAdd(results, uri, "embedded html/json", "raw:absolute");
        }
        foreach (Match match in RelativePdf.Matches(normalizedRaw).Cast<Match>().Take(MaxRawUrlMatches))
        {
            var raw = CleanUrl(match.Groups["url"].Value);
            if (Uri.TryCreate(pageUrl, raw, out var uri))
                TryAdd(results, uri, "embedded pdf url", "raw:relative-pdf");
        }

        return results
            .Where(item => item.Url.Scheme is "http" or "https")
            .GroupBy(item => item.Url.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.Kind == EngineeringDocumentLinkKind.DirectDocument)
                .ThenByDescending(item => DocumentPriority(item.DocumentType))
                .First())
            .ToArray();
    }

    private static IEnumerable<Uri> ExtractAttributeUrls(string raw, Uri pageUrl)
    {
        var normalized = NormalizeEscapedText(raw);
        if (TryCreateHttpUri(pageUrl, CleanUrl(normalized), out var direct))
            yield return direct;

        foreach (Match match in UrlInsideAttribute.Matches(normalized))
        {
            if (TryCreateHttpUri(pageUrl, CleanUrl(match.Groups["url"].Value), out var nested))
                yield return nested;
        }
    }

    private static void TryAdd(List<EngineeringDocumentLink> results, Uri uri, string hint, string method)
    {
        if (uri.Scheme is not ("http" or "https")) return;
        var combined = $"{hint} {uri.AbsolutePath} {uri.Query}";
        var normalized = combined.ToLowerInvariant();

        var explicitPdf = uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                          normalized.Contains(".pdf", StringComparison.OrdinalIgnoreCase);
        var mediaAsset = normalized.Contains("/dam/", StringComparison.OrdinalIgnoreCase) ||
                         normalized.Contains("/asset/", StringComparison.OrdinalIgnoreCase) ||
                         normalized.Contains("/media/", StringComparison.OrdinalIgnoreCase);
        var directHint = DirectHints.Any(value => normalized.Contains(value, StringComparison.OrdinalIgnoreCase));
        var followHint = FollowHints.Any(value => normalized.Contains(value, StringComparison.OrdinalIgnoreCase));

        if (explicitPdf || directHint && (LooksDownloadEndpoint(uri) || mediaAsset))
        {
            results.Add(new EngineeringDocumentLink(uri, EngineeringDocumentLinkKind.DirectDocument, InferDocumentType(combined), method, hint));
            return;
        }

        if (followHint && !LooksLikeUnrelatedPage(normalized))
            results.Add(new EngineeringDocumentLink(uri, EngineeringDocumentLinkKind.FollowPage, "document-index", method, hint));
    }

    private static bool LooksDownloadEndpoint(Uri uri)
    {
        var text = $"{uri.AbsolutePath} {uri.Query}".ToLowerInvariant();
        return text.Contains("download") || text.Contains("document") || text.Contains("file") ||
               text.Contains("media") || text.Contains("asset") || text.Contains("dam") || text.Contains("pdf");
    }

    private static bool LooksLikeUnrelatedPage(string combined)
    {
        if (combined.Contains("accessor", StringComparison.OrdinalIgnoreCase)) return true;
        if (combined.Contains("cart", StringComparison.OrdinalIgnoreCase)) return true;
        if (combined.Contains("contact", StringComparison.OrdinalIgnoreCase)) return true;
        if (combined.Contains("login", StringComparison.OrdinalIgnoreCase)) return true;
        if (combined.Contains("privacy", StringComparison.OrdinalIgnoreCase)) return true;
        if (combined.Contains("terms", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string BuildHint(IElement element)
    {
        var parts = new[]
        {
            element.TextContent,
            element.GetAttribute("title"),
            element.GetAttribute("aria-label"),
            element.GetAttribute("download"),
            element.GetAttribute("class"),
            element.GetAttribute("id")
        };
        return CleanWhitespace(string.Join(' ', parts.Where(value => !string.IsNullOrWhiteSpace(value))));
    }

    private static string InferDocumentType(string text)
    {
        var normalized = text.ToLowerInvariant();
        if (normalized.Contains("datasheet") || normalized.Contains("data sheet") ||
            normalized.Contains("technical data") || normalized.Contains("spec sheet") ||
            normalized.Contains("規格書") || normalized.Contains("技術資料") || normalized.Contains("技术资料"))
            return "datasheet";
        if (normalized.Contains("manual") || normalized.Contains("instruction") ||
            normalized.Contains("user guide") || normalized.Contains("手冊") || normalized.Contains("手册") ||
            normalized.Contains("操作說明") || normalized.Contains("操作说明"))
            return "manual";
        return "document";
    }

    private static int DocumentPriority(string type) => type.ToLowerInvariant() switch
    {
        "datasheet" => 3,
        "manual" => 2,
        "document" => 1,
        _ => 0
    };

    private static bool TryCreateHttpUri(Uri pageUrl, string raw, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith('#') || raw.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!Uri.TryCreate(pageUrl, raw, out var created) || created.Scheme is not ("http" or "https")) return false;
        uri = created;
        return true;
    }

    private static string NormalizeEscapedText(string value) => WebUtility.HtmlDecode(value)
        .Replace("\\/", "/", StringComparison.Ordinal)
        .Replace("\\u002F", "/", StringComparison.OrdinalIgnoreCase)
        .Replace("\\u003A", ":", StringComparison.OrdinalIgnoreCase)
        .Replace("\\u0026", "&", StringComparison.OrdinalIgnoreCase)
        .Replace("\\u003F", "?", StringComparison.OrdinalIgnoreCase)
        .Replace("\\u003D", "=", StringComparison.OrdinalIgnoreCase);

    private static string CleanUrl(string value) => value
        .Trim()
        .Trim('\'', '"', ')', ']', '}', ',', ';')
        .Replace("\\/", "/", StringComparison.Ordinal);

    private static string CleanWhitespace(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}
