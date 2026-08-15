using System.Collections.Concurrent;
using System.Net;

namespace ComponentIntelligence.Network;

public sealed class ComponentHttpClient
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> HostGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly IHttpClientFactory _factory;
    private readonly int _maxAttempts;
    private readonly TimeSpan _minimumHostDelay;

    public ComponentHttpClient(
        IHttpClientFactory factory,
        int maxAttempts = 3,
        TimeSpan? minimumHostDelay = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _maxAttempts = Math.Max(1, maxAttempts);
        _minimumHostDelay = minimumHostDelay ?? TimeSpan.FromMilliseconds(150);
    }

    public async Task<HttpFetchResult> FetchAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Only HTTP/HTTPS URIs are supported.", nameof(uri));

        var gate = HostGates.GetOrAdd(uri.Host, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 1; attempt <= _maxAttempts; attempt++)
            {
                try
                {
                    using var client = _factory.CreateClient("component-intelligence");
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    var result = new HttpFetchResult
                    {
                        RequestedUri = uri,
                        FinalUri = response.RequestMessage?.RequestUri,
                        StatusCode = (int)response.StatusCode,
                        ContentType = response.Content.Headers.ContentType?.MediaType,
                        Content = bytes
                    };

                    if (response.IsSuccessStatusCode)
                        return result;

                    if (!IsRetryable(response.StatusCode) || attempt == _maxAttempts)
                        return result;

                    await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < _maxAttempts)
                {
                    await Task.Delay(Backoff(attempt), cancellationToken);
                }
                catch (HttpRequestException exception) when (attempt < _maxAttempts)
                {
                    await Task.Delay(Backoff(attempt), cancellationToken);
                    if (attempt == _maxAttempts)
                        return Failed(uri, exception);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    return Failed(uri, exception);
                }
            }

            return new HttpFetchResult { RequestedUri = uri, Error = "Retry budget exhausted." };
        }
        finally
        {
            await Task.Delay(_minimumHostDelay, CancellationToken.None);
            gate.Release();
        }
    }

    private static bool IsRetryable(HttpStatusCode code) =>
        code == HttpStatusCode.TooManyRequests || (int)code >= 500;

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt) =>
        response.Headers.RetryAfter?.Delta ?? Backoff(attempt);

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(300 * Math.Pow(2, Math.Max(0, attempt - 1)));

    private static HttpFetchResult Failed(Uri uri, Exception exception) => new()
    {
        RequestedUri = uri,
        Error = $"{exception.GetType().Name}: {exception.Message}"
    };
}
