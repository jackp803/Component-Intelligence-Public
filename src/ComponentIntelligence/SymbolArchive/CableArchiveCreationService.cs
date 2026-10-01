using System.Security.Cryptography;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.SymbolArchive;

public sealed record CableArchiveDraft
{
    public required CableArchiveEntry Entry { get; init; }
    public string? WiringSourcePath { get; init; }
    public SchematicCadAsset? WiringGeometry { get; init; }
    public string? ManufacturingSourcePath { get; init; }
    public SchematicCadAsset? ManufacturingGeometry { get; init; }
    public string? ExpectedArchiveSha256 { get; init; }
}

public sealed class CableArchiveCreationService(SymbolArchiveRepository repository)
{
    public async Task<CableArchiveEntry> CreateAsync(CableArchiveDraft draft, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (draft.Entry.Status != SymbolRevisionStatus.Candidate) throw new InvalidDataException("New cable revisions must be saved as candidates.");
        if (draft.Entry.WiringAssetPending
            ? draft.WiringSourcePath is not null || draft.WiringGeometry is not null
            : draft.WiringSourcePath is null || draft.WiringGeometry is null)
            throw new InvalidDataException("Wiring role requires its source and geometry, or an explicit pending state.");
        var original = repository.Load();
        if (original.CableTemplates.Any(e => e.Template.TemplateId == draft.Entry.Template.TemplateId && e.Template.TemplateRevision == draft.Entry.Template.TemplateRevision))
            throw new InvalidDataException("Cable revisions are immutable; choose a new revision.");
        if ((draft.Entry.ManufacturingAsset is null) != (draft.ManufacturingGeometry is null) ||
            (draft.Entry.ManufacturingAsset is null) != (draft.ManufacturingSourcePath is null))
            throw new InvalidDataException("Manufacturing role requires its source file and geometry.");
        var entry = draft.Entry with { WiringGeometry = draft.WiringGeometry,
            Template = draft.Entry.Template with { WiringSelectionSha256 = draft.Entry.WiringSelection?.GeometrySha256 },
            ManufacturingAsset = draft.Entry.ManufacturingAsset is { } detail ? detail with { Geometry = draft.ManufacturingGeometry } : null };
        repository.ValidateAndNormalize(original with { CableTemplates = [..original.CableTemplates, entry] });
        var relative = "Documents/Cables/" + Guid.NewGuid().ToString("N");
        var directory = repository.ResolveArchivePath(relative);
        EnsureNoLinks(directory);
        Directory.CreateDirectory(directory);
        var saved = false;
        try
        {
            var wiringPath = "";
            if (!entry.WiringAssetPending)
            {
                wiringPath = relative + "/wiring" + Extension(draft.WiringSourcePath!);
                await CopyVerifiedAsync(draft.WiringSourcePath!, repository.ResolveArchivePath(wiringPath), entry.Template.AssetSha256, cancellationToken);
            }
            CableCadRoleAsset? manufacturing = null;
            if (entry.ManufacturingAsset is { } role)
            {
                var detailPath = relative + "/manufacturing" + Extension(draft.ManufacturingSourcePath!);
                await CopyVerifiedAsync(draft.ManufacturingSourcePath!, repository.ResolveArchivePath(detailPath), role.SourceSha256, cancellationToken);
                manufacturing = role with { AssetPath = detailPath };
            }
            entry = entry with { AssetPath = wiringPath, ManufacturingAsset = manufacturing };
            cancellationToken.ThrowIfCancellationRequested();
            repository.SaveIfUnchanged(original with { CableTemplates = [..original.CableTemplates, entry] }, draft.ExpectedArchiveSha256);
            saved = true;
            return repository.Load().CableTemplates.Single(e => e.Template.TemplateId == entry.Template.TemplateId && e.Template.TemplateRevision == entry.Template.TemplateRevision);
        }
        finally
        {
            if (!saved && Directory.Exists(directory))
            {
                EnsureNoLinks(directory);
                if (Directory.EnumerateFileSystemEntries(directory).Any(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0))
                    throw new IOException("Failed import staging contains a linked path; cleanup was not performed.");
                Directory.Delete(directory, true);
            }
        }
    }

    private void EnsureNoLinks(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null &&
            !string.Equals(current.FullName, repository.ArchiveRoot, StringComparison.OrdinalIgnoreCase); current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Cable archive destination contains a linked descendant.");
    }

    private static string Extension(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".dxf" or ".dwg" or ".dwt" ? extension : throw new InvalidDataException("Expected DWG, DWT or DXF.");
    }

    private static async Task CopyVerifiedAsync(string source, string target, string expectedHash, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(source, cancellationToken);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source CAD changed after preview; reload it before saving.");
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await output.WriteAsync(bytes, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }
}
