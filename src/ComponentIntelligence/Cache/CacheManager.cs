using System.Text.Json;
using ComponentIntelligence.Network;

namespace ComponentIntelligence.Cache;

public sealed class CacheManager
{
    private readonly string _root;
    private readonly ComponentHttpClient _http;
    private readonly long _maximumBytes;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public CacheManager(string root, ComponentHttpClient http, long maximumBytes = 10L * 1024 * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _maximumBytes = Math.Max(64L * 1024 * 1024, maximumBytes);
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, ".metadata"));
    }

    public async Task<CachedDocument?> GetOrDownloadAsync(
        Uri sourceUrl,
        string category = "documents",
        CancellationToken cancellationToken = default)
    {
        var urlKey = HashService.Sha256(sourceUrl.AbsoluteUri);
        var metadataPath = Path.Combine(_root, ".metadata", $"{urlKey}.json");
        if (File.Exists(metadataPath))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<CacheMetadata>(await File.ReadAllTextAsync(metadataPath, cancellationToken), _json);
                if (existing is not null && File.Exists(existing.LocalPath))
                {
                    var touched = existing with { LastAccessed = DateTimeOffset.UtcNow };
                    await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(touched, _json), cancellationToken);
                    return new CachedDocument(touched, await File.ReadAllBytesAsync(existing.LocalPath, cancellationToken));
                }
            }
            catch (JsonException)
            {
                // Corrupt cache metadata is not engineering truth; discard and re-fetch.
                File.Delete(metadataPath);
            }
        }

        var fetched = await _http.FetchAsync(sourceUrl, cancellationToken);
        if (!fetched.IsSuccess || fetched.Content.Length == 0)
            return null;

        var hash = HashService.Sha256(fetched.Content);
        var extension = ExtensionFor(sourceUrl, fetched.ContentType);
        var directory = Path.Combine(_root, category);
        Directory.CreateDirectory(directory);
        var localPath = Path.Combine(directory, $"{hash}{extension}");
        if (!File.Exists(localPath))
            await File.WriteAllBytesAsync(localPath, fetched.Content, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var metadata = new CacheMetadata
        {
            SourceUrl = sourceUrl,
            LocalPath = localPath,
            Sha256 = hash,
            FileSize = fetched.Content.LongLength,
            CreatedAt = now,
            LastAccessed = now,
            ContentType = fetched.ContentType
        };
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, _json), cancellationToken);
        await EnforceLimitAsync(cancellationToken);
        return new CachedDocument(metadata, fetched.Content);
    }

    public async Task EnforceLimitAsync(CancellationToken cancellationToken = default)
    {
        var metadataFiles = Directory.EnumerateFiles(Path.Combine(_root, ".metadata"), "*.json").ToArray();
        var entries = new List<(string MetadataPath, CacheMetadata Metadata)>();
        foreach (var file in metadataFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var metadata = JsonSerializer.Deserialize<CacheMetadata>(await File.ReadAllTextAsync(file, cancellationToken), _json);
                if (metadata is not null && File.Exists(metadata.LocalPath)) entries.Add((file, metadata));
            }
            catch (JsonException) { }
        }

        var total = entries.GroupBy(entry => entry.Metadata.LocalPath, StringComparer.OrdinalIgnoreCase)
            .Sum(group => group.First().Metadata.FileSize);
        if (total <= _maximumBytes) return;

        foreach (var entry in entries.OrderBy(entry => entry.Metadata.LastAccessed))
        {
            if (total <= _maximumBytes) break;
            try
            {
                if (File.Exists(entry.Metadata.LocalPath)) File.Delete(entry.Metadata.LocalPath);
                if (File.Exists(entry.MetadataPath)) File.Delete(entry.MetadataPath);
                total -= entry.Metadata.FileSize;
            }
            catch (IOException) { }
        }
    }

    private static string ExtensionFor(Uri uri, string? contentType)
    {
        var extension = Path.GetExtension(uri.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(extension) && extension.Length <= 8) return extension.ToLowerInvariant();
        return contentType?.ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            _ => ".bin"
        };
    }
}
