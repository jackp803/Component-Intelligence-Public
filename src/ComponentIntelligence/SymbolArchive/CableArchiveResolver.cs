using ComponentIntelligence.Cache;

namespace ComponentIntelligence.SymbolArchive;

public sealed record ResolvedCableArchive(CableArchiveEntry Entry, string AbsolutePath)
{
    public string? ManufacturingPath { get; init; }
}

public sealed class CableArchiveResolver(SymbolArchiveRepository repository)
{
    public async Task<ResolvedCableArchive> ResolveAsync(string templateId, string revision, CancellationToken cancellationToken = default)
    {
        var entry = repository.Load().CableTemplates.SingleOrDefault(e => e.Template.TemplateId == templateId && e.Template.TemplateRevision == revision)
            ?? throw new InvalidDataException("Cable template revision was not found.");
        if (entry.Status is not (SymbolRevisionStatus.Candidate or SymbolRevisionStatus.Approved))
            throw new InvalidDataException("Rejected or superseded cable templates cannot create new instances.");
        var path = await VerifyAssetAsync(entry.AssetPath, entry.Template.AssetSha256, cancellationToken);
        var manufacturingPath = entry.ManufacturingAsset is { } detail
            ? await VerifyAssetAsync(detail.AssetPath, detail.SourceSha256, cancellationToken) : null;
        return new(entry, path) { ManufacturingPath = manufacturingPath };
    }

    private async Task<string> VerifyAssetAsync(string relativePath, string expectedHash, CancellationToken cancellationToken)
    {
        var path = repository.ResolveArchivePath(relativePath);
        if (!File.Exists(path)) throw new FileNotFoundException("Archived cable geometry is missing.", path);
        for (FileSystemInfo? current = new FileInfo(path); current is not null &&
            !string.Equals(current.FullName, repository.ArchiveRoot, StringComparison.OrdinalIgnoreCase);
            current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Cable archive asset path contains a linked descendant.");
        var hash = await HashService.Sha256FileAsync(path, cancellationToken);
        if (!string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cable archive asset SHA-256 mismatch.");
        return path;
    }
}
