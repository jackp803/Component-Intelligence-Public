namespace ComponentIntelligence.Cache;

public sealed record CacheMetadata
{
    public required Uri SourceUrl { get; init; }
    public required string LocalPath { get; init; }
    public required string Sha256 { get; init; }
    public long FileSize { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset LastAccessed { get; init; }
    public string? ContentType { get; init; }
}

public sealed record CachedDocument(CacheMetadata Metadata, byte[] Content);
