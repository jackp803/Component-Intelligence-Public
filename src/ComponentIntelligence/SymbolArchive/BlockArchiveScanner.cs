using ComponentIntelligence.Cache;

namespace ComponentIntelligence.SymbolArchive;

public sealed record BlockArchiveCandidate
{
    public required string SourcePath { get; init; }
    public required string RelativePath { get; init; }
    public required string FileName { get; init; }
    public required string Extension { get; init; }
    public required long FileSize { get; init; }
    public required DateTimeOffset ModifiedAt { get; init; }
    public required string Sha256 { get; init; }
    public DeepInspectionStatus DeepInspectionStatus { get; init; } = DeepInspectionStatus.NotRequested;
    public BlockDeepInspectionMetadata? DeepMetadata { get; init; }
    public string? ExactDuplicateRevision { get; init; }
    public bool SourceIntegrityFailed { get; init; }
}

public sealed class BlockArchiveScanner
{
    public async Task<IReadOnlyList<BlockArchiveCandidate>> ScanAsync(
        string sourceRoot,
        SymbolArchiveDocument archive,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentNullException.ThrowIfNull(archive);
        var root = Path.GetFullPath(sourceRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);

        var duplicateIndex = archive.Bindings
            .SelectMany(binding => binding.Revisions.Select(revision => new
            {
                binding.ComponentId,
                binding.Role,
                revision.Revision,
                Sha = revision.AssetHashSha256.ToLowerInvariant()
            }))
            .OrderBy(item => item.ComponentId, StringComparer.Ordinal)
            .ThenBy(item => item.Role)
            .ThenBy(item => item.Revision, StringComparer.Ordinal)
            .GroupBy(item => item.Sha, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var files = EnumerateCadFiles(root)
            .OrderBy(path => NormalizeRelative(root, path), StringComparer.Ordinal)
            .ToArray();
        var results = new List<BlockArchiveCandidate>(files.Length);
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sha = await HashService.Sha256FileAsync(path, cancellationToken);
            var info = new FileInfo(path);
            duplicateIndex.TryGetValue(sha, out var duplicate);
            results.Add(new BlockArchiveCandidate
            {
                SourcePath = Path.GetFullPath(path),
                RelativePath = NormalizeRelative(root, path),
                FileName = info.Name,
                Extension = info.Extension.ToLowerInvariant(),
                FileSize = info.Length,
                ModifiedAt = info.LastWriteTimeUtc,
                Sha256 = sha,
                ExactDuplicateRevision = duplicate is null
                    ? null
                    : $"{duplicate.ComponentId}:{duplicate.Role}:{duplicate.Revision}"
            });
        }
        return results;
    }

    private static IEnumerable<string> EnumerateCadFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            var info = new DirectoryInfo(directory);
            if (!string.Equals(info.FullName, root, StringComparison.OrdinalIgnoreCase) &&
                info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var extension = Path.GetExtension(file);
                if (string.Equals(extension, ".dwg", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".dxf", StringComparison.OrdinalIgnoreCase))
                    yield return file;
            }

            foreach (var child in Directory.EnumerateDirectories(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Reverse())
            {
                try
                {
                    if (!new DirectoryInfo(child).Attributes.HasFlag(FileAttributes.ReparsePoint)) pending.Push(child);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static string NormalizeRelative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/').Normalize(System.Text.NormalizationForm.FormKC);
}
