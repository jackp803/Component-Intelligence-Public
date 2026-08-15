using System.Net.Http.Headers;

namespace ComponentIntelligence.Network;

/// <summary>
/// Small IHttpClientFactory implementation for the library/desktop composition root.
/// It shares the handler (connection pool) while returning disposable HttpClient instances.
/// Official manufacturer sites frequently reject non-browser-looking clients, so the default
/// request headers intentionally resemble a normal desktop browser while keeping the crawler
/// deterministic and read-only.
/// </summary>
public sealed class DefaultHttpClientFactory : IHttpClientFactory, IDisposable
{
    private readonly SocketsHttpHandler _handler = new()
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(15),
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10
    };

    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(_handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml", 0.9));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-TW,zh;q=0.9,en-US;q=0.8,en;q=0.7");
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return client;
    }

    public void Dispose() => _handler.Dispose();
}
