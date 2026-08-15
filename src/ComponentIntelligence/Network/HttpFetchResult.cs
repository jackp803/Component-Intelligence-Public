namespace ComponentIntelligence.Network;

public sealed record HttpFetchResult
{
    public required Uri RequestedUri { get; init; }
    public Uri? FinalUri { get; init; }
    public int StatusCode { get; init; }
    public string? ContentType { get; init; }
    public byte[] Content { get; init; } = Array.Empty<byte>();
    public string? Error { get; init; }
    public bool IsSuccess => StatusCode is >= 200 and < 300 && Error is null;
    public string Text => System.Text.Encoding.UTF8.GetString(Content);
}
