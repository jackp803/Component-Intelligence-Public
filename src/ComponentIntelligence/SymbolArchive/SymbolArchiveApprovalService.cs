using ComponentIntelligence.Cache;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.SymbolArchive;

public sealed record ApproveSymbolRequest
{
    public required string SourcePath { get; init; }
    public required string ComponentId { get; init; }
    public required SymbolRole Role { get; init; }
    public required SymbolSourceType SourceType { get; init; }
    public IReadOnlyList<SymbolPortBinding> PortBindings { get; init; } = [];
    public bool UserConfirmed { get; init; }
}

public enum SymbolApprovalDisposition
{
    CreatedRevision,
    ExactDuplicate,
    ReapprovedExisting
}

public sealed record SymbolApprovalResult(
    SymbolApprovalDisposition Disposition,
    string ComponentId,
    SymbolRole Role,
    string Revision,
    string AssetPath,
    string Sha256);

public sealed class SymbolArchiveApprovalService
{
    private readonly SymbolArchiveRepository _repository;
    private readonly IReadOnlyDictionary<string, ComponentIR> _components;

    public SymbolArchiveApprovalService(SymbolArchiveRepository repository, IReadOnlyList<ComponentIR> components)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _components = (components ?? throw new ArgumentNullException(nameof(components)))
            .ToDictionary(component => component.Identity.ComponentId, StringComparer.Ordinal);
    }

    public async Task<SymbolApprovalResult> ApproveAsync(
        ApproveSymbolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request, out var component);
        var sourcePath = Path.GetFullPath(request.SourcePath);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Symbol source asset does not exist.", sourcePath);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".dwg" or ".dxf"))
            throw new InvalidDataException("CP3-A symbol approval accepts only DWG or DXF source assets.");

        var sourceShaBefore = await HashService.Sha256FileAsync(sourcePath, cancellationToken);
        var document = _repository.Load();
        var binding = document.Bindings.SingleOrDefault(item =>
            string.Equals(item.ComponentId, request.ComponentId, StringComparison.Ordinal) && item.Role == request.Role);
        var existingSameHash = binding?.Revisions.FirstOrDefault(revision =>
            string.Equals(revision.AssetHashSha256, sourceShaBefore, StringComparison.OrdinalIgnoreCase));

        if (existingSameHash is not null)
        {
            var sourceShaAfter = await HashService.Sha256FileAsync(sourcePath, cancellationToken);
            if (!string.Equals(sourceShaBefore, sourceShaAfter, StringComparison.Ordinal))
                throw new IOException("Source asset changed during approval; no authority was written.");
            var existingPath = _repository.ResolveArchivePath(existingSameHash.AssetPath);
            if (!File.Exists(existingPath) || !string.Equals(await HashService.Sha256FileAsync(existingPath, cancellationToken), sourceShaBefore, StringComparison.Ordinal))
                throw new InvalidDataException("Existing duplicate revision is missing or hash-mismatched; approval fails closed.");

            if (existingSameHash.Status == SymbolRevisionStatus.Approved)
                return new SymbolApprovalResult(SymbolApprovalDisposition.ExactDuplicate, request.ComponentId, request.Role,
                    existingSameHash.Revision, existingSameHash.AssetPath, sourceShaBefore);

            var rebound = SetApproved(document, request.ComponentId, request.Role, existingSameHash.Revision);
            _repository.Save(rebound);
            return new SymbolApprovalResult(SymbolApprovalDisposition.ReapprovedExisting, request.ComponentId, request.Role,
                existingSameHash.Revision, existingSameHash.AssetPath, sourceShaBefore);
        }

        var revision = NextRevision(binding);
        var relativePath = BuildRelativeAssetPath(component, request.Role, revision, extension);
        var destination = _repository.ResolveArchivePath(relativePath);
        var revisionDirectory = Path.GetDirectoryName(destination)!;
        if (Directory.Exists(revisionDirectory))
            throw new InvalidDataException($"Immutable revision directory already exists: {revisionDirectory}");
        Directory.CreateDirectory(revisionDirectory);
        var temp = Path.Combine(revisionDirectory, $".symbol.{Guid.NewGuid():N}.tmp");
        var committedAsset = false;
        try
        {
            await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            var destinationSha = await HashService.Sha256FileAsync(temp, cancellationToken);
            var sourceShaAfter = await HashService.Sha256FileAsync(sourcePath, cancellationToken);
            if (!string.Equals(sourceShaBefore, sourceShaAfter, StringComparison.Ordinal) ||
                !string.Equals(sourceShaBefore, destinationSha, StringComparison.Ordinal))
                throw new IOException("Source/copy SHA-256 integrity check failed; no authority was written.");

            File.Move(temp, destination);
            committedAsset = true;
            var revisionRecord = new SymbolRevisionRecord
            {
                Revision = revision,
                SourceType = request.SourceType,
                AssetPath = relativePath,
                AssetHashSha256 = sourceShaBefore,
                Status = SymbolRevisionStatus.Approved,
                PortBindings = request.PortBindings
                    .OrderBy(item => item.EngineeringEndpointId, StringComparer.Ordinal)
                    .ToArray()
            };
            var updated = AddApprovedRevision(document, request.ComponentId, request.Role, revisionRecord);
            try
            {
                _repository.Save(updated);
            }
            catch
            {
                if (File.Exists(destination)) File.Delete(destination);
                committedAsset = false;
                throw;
            }

            return new SymbolApprovalResult(SymbolApprovalDisposition.CreatedRevision, request.ComponentId, request.Role,
                revision, relativePath, sourceShaBefore);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            if (!committedAsset && Directory.Exists(revisionDirectory) && !Directory.EnumerateFileSystemEntries(revisionDirectory).Any())
                Directory.Delete(revisionDirectory);
        }
    }

    private void ValidateRequest(ApproveSymbolRequest request, out ComponentIR component)
    {
        if (!request.UserConfirmed) throw new InvalidOperationException("Explicit user confirmation is required before archive writes.");
        if (request.SourceType == SymbolSourceType.GeneratedGeneric)
            throw new InvalidOperationException("GeneratedGeneric is virtual resolver output and cannot be imported from a file.");
        if (string.IsNullOrWhiteSpace(request.ComponentId) || !_components.TryGetValue(request.ComponentId.Trim(), out var found))
            throw new InvalidOperationException("ComponentId must exist in the current central component catalog.");
        component = found;
        if (string.IsNullOrWhiteSpace(request.SourcePath)) throw new ArgumentException("SourcePath is required.", nameof(request));

        var explicitEndpointIds = component.Ports.Select(port => port.PortId)
            .Concat(component.Pins.Where(pin => !string.IsNullOrWhiteSpace(pin.PinId)).Select(pin => pin.PinId!))
            .ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in request.PortBindings ?? [])
        {
            if (string.IsNullOrWhiteSpace(binding.EngineeringEndpointId) || !explicitEndpointIds.Contains(binding.EngineeringEndpointId.Trim()))
                throw new InvalidOperationException($"Endpoint '{binding.EngineeringEndpointId}' is not an explicit stable PortId/PinId for ComponentId '{request.ComponentId}'.");
            if (!seen.Add(binding.EngineeringEndpointId.Trim()))
                throw new InvalidOperationException($"Endpoint '{binding.EngineeringEndpointId}' is mapped more than once.");
            if (string.IsNullOrWhiteSpace(binding.ConnectionPointId))
                throw new InvalidOperationException("ConnectionPointId must be explicit and nonblank.");
        }
    }

    private static string NextRevision(ComponentSymbolBinding? binding)
    {
        var maximum = (binding?.Revisions ?? [])
            .Select(revision => revision.Revision)
            .Where(value => value.StartsWith("rev-", StringComparison.Ordinal) && int.TryParse(value[4..], out _))
            .Select(value => int.Parse(value[4..], System.Globalization.CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max();
        return $"rev-{maximum + 1:000}";
    }

    private static string BuildRelativeAssetPath(ComponentIR component, SymbolRole role, string revision, string extension)
    {
        var manufacturer = SafePathSegment(component.Identity.Manufacturer);
        var model = SafePathSegment(component.Identity.Model);
        return $"Documents/{manufacturer}/{model}/autocad/{RoleFolder(role)}/{revision}/symbol{extension}";
    }

    private static string SafePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Trim().Select(character => invalid.Contains(character) || character is '/' or '\\' ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "_" : sanitized;
    }

    private static string RoleFolder(SymbolRole role) => role switch
    {
        SymbolRole.Schematic => "schematic",
        SymbolRole.ConnectorDetail => "connector-detail",
        SymbolRole.PanelFootprint => "panel-footprint",
        SymbolRole.TopologyVisual => "topology-visual",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    private static SymbolArchiveDocument AddApprovedRevision(
        SymbolArchiveDocument document,
        string componentId,
        SymbolRole role,
        SymbolRevisionRecord revision)
    {
        var found = false;
        var bindings = document.Bindings.Select(binding =>
        {
            if (!string.Equals(binding.ComponentId, componentId, StringComparison.Ordinal) || binding.Role != role) return binding;
            found = true;
            var revisions = binding.Revisions
                .Select(existing => existing.Status == SymbolRevisionStatus.Approved
                    ? existing with { Status = SymbolRevisionStatus.Superseded }
                    : existing)
                .Append(revision)
                .ToArray();
            return binding with { Revisions = revisions };
        }).ToList();
        if (!found)
        {
            bindings.Add(new ComponentSymbolBinding
            {
                ComponentId = componentId,
                Role = role,
                Revisions = [revision]
            });
        }
        return document with { Bindings = bindings };
    }

    private static SymbolArchiveDocument SetApproved(SymbolArchiveDocument document, string componentId, SymbolRole role, string revision)
    {
        return document with
        {
            Bindings = document.Bindings.Select(binding =>
                string.Equals(binding.ComponentId, componentId, StringComparison.Ordinal) && binding.Role == role
                    ? binding with
                    {
                        Revisions = binding.Revisions.Select(item => item with
                        {
                            Status = string.Equals(item.Revision, revision, StringComparison.Ordinal)
                                ? SymbolRevisionStatus.Approved
                                : item.Status == SymbolRevisionStatus.Approved
                                    ? SymbolRevisionStatus.Superseded
                                    : item.Status
                        }).ToArray()
                    }
                    : binding).ToArray()
        };
    }
}
