using ComponentIntelligence.SymbolArchive;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Drawing;

public interface IDrawingAssetResolver
{
    DrawingAssetResolution? Resolve(string ownerId, DrawingRepresentationRole role);
}

public sealed record DrawingAssetResolution
{
    public required string SourceType { get; init; }
    public required string Revision { get; init; }
    public required string AssetPath { get; init; }
    public required string AssetHashSha256 { get; init; }
    public IReadOnlyList<DrawingPortBinding> PortBindings { get; init; } = [];
}

public sealed class Cp3aDrawingAssetResolver(SymbolResolver resolver, SymbolArchiveRepository repository) : IDrawingAssetResolver
{
    private readonly SymbolResolver _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

    public DrawingAssetResolution? Resolve(string ownerId, DrawingRepresentationRole role)
    {
        if (!TryMapRole(role, out var assetRole)) return null;
        var document = repository.Load();
        if (!document.Bindings.Any(b => string.Equals(b.ComponentId, ownerId, StringComparison.Ordinal) && b.Role == assetRole
            && b.Revisions.Any(r => r.Status == SymbolRevisionStatus.Approved))) return null;
        var resolved = _resolver.ResolveAsync(ownerId, assetRole, allowGeneratedGeneric: false)
            .ConfigureAwait(false).GetAwaiter().GetResult();
        var path = repository.ResolveArchivePath(resolved.AssetPath);
        // Reject linked descendants so lexical containment cannot redirect execution outside the root.
        for (var current = new FileInfo(path) as FileSystemInfo; current is not null && !string.Equals(current.FullName, repository.ArchiveRoot, StringComparison.OrdinalIgnoreCase);
             current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Approved symbol path contains a linked descendant.");
        return new DrawingAssetResolution
        {
            SourceType = resolved.SourceType.ToString(),
            Revision = resolved.Revision,
            AssetPath = path,
            AssetHashSha256 = resolved.Sha256.ToUpperInvariant(),
            PortBindings = resolved.PortBindings.Select(x => new DrawingPortBinding
            {
                EngineeringEndpointId = x.EngineeringEndpointId,
                ConnectionPointId = x.ConnectionPointId
            }).OrderBy(x => x.EngineeringEndpointId, StringComparer.Ordinal).ToArray()
        };
    }

    private static bool TryMapRole(DrawingRepresentationRole role, out SymbolRole mapped)
    {
        switch (role)
        {
            case DrawingRepresentationRole.Schematic: mapped = SymbolRole.Schematic; return true;
            case DrawingRepresentationRole.ConnectorDetail: mapped = SymbolRole.ConnectorDetail; return true;
            case DrawingRepresentationRole.PanelFootprint: mapped = SymbolRole.PanelFootprint; return true;
            case DrawingRepresentationRole.TopologyVisual: mapped = SymbolRole.TopologyVisual; return true;
            default: mapped = default; return false;
        }
    }
}

public sealed record RepresentationRequest
{
    public required string RepresentationId { get; init; }
    public required DrawingRepresentationOwnerKind OwnerKind { get; init; }
    public required string OwnerId { get; init; }
    public required string AssetComponentId { get; init; }
    public required DrawingRepresentationRole Role { get; init; }
    public required DrawingRepresentationFamily PreferredFamily { get; init; }
    public DrawingRepresentationControlState ControlState { get; init; }
    public IReadOnlyList<int> AllowedRotations { get; init; } = [0];
    public IReadOnlyList<DrawingPortBinding> PortBindings { get; init; } = [];
    public ComponentInstance? SourceInstance { get; init; }
    public bool RequiresExplicitEndpointEvidence { get; init; }
    public string? FieldDeviceClass { get; init; }
    public string? ControllerId { get; init; }
    public string? PhysicalModuleId { get; init; }
    public string? FunctionKind { get; init; }
    public string? MachineZoneId { get; init; }
    public string? NetworkId { get; init; }
    public string? NetworkKind { get; init; }
    public string? SeriesChainId { get; init; }
    public string? HeavyDutyConnectorId { get; init; }
    public bool PhysicalInterfaceMeaning { get; init; }
}

public sealed record RepresentationDecisionResult(DrawingRepresentationDecision Decision, IReadOnlyList<DrawingPlanningIssue> Issues);

public sealed class RepresentationPolicy(IDrawingAssetResolver assetResolver)
{
    private readonly IDrawingAssetResolver _assetResolver = assetResolver ?? throw new ArgumentNullException(nameof(assetResolver));

    public RepresentationDecisionResult Decide(RepresentationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AllowedRotations.Count == 0 || request.AllowedRotations.Any(x => x is not (0 or 90 or 180 or 270)))
            throw new InvalidOperationException("Representation requires explicit legal rotations.");

        var issues = new List<DrawingPlanningIssue>();
        var assetEligibleFamily = request.PreferredFamily is DrawingRepresentationFamily.ArchivedExact
            or DrawingRepresentationFamily.StandardSymbol
            or DrawingRepresentationFamily.ConnectorDetail
            || request.OwnerKind == DrawingRepresentationOwnerKind.Component && request.Role == DrawingRepresentationRole.Schematic
                && request.ControlState == DrawingRepresentationControlState.Auto && request.PreferredFamily == DrawingRepresentationFamily.FunctionalGeneric;
        var asset = assetEligibleFamily ? _assetResolver.Resolve(request.AssetComponentId, request.Role) : null;
        IReadOnlyList<DrawingPortBinding>? exactBindings = null;
        if (asset is not null && request.OwnerKind == DrawingRepresentationOwnerKind.Component)
        {
            var instance = request.SourceInstance;
            var bridge = instance?.Ports.Select(p => (Source: p.SourcePortId, Instance: p.PortId))
                .Concat(instance.Ports.SelectMany(p => p.Pins.Select(pin => (Source: pin.SourcePinId, Instance: pin.PinId))))
                .Where(p => !string.IsNullOrWhiteSpace(p.Source)).ToArray() ?? [];
            if (instance is null || bridge.GroupBy(p => p.Source, StringComparer.Ordinal).Any(g => g.Count() > 1)
                || asset.PortBindings.Any(b => !bridge.Any(p => string.Equals(p.Source, b.EngineeringEndpointId, StringComparison.Ordinal))))
            {
                issues.Add(new DrawingPlanningIssue
                {
                    IssueId = $"ISSUE:{request.RepresentationId}:source-bridge",
                    Severity = DrawingPlanningIssueSeverity.Blocker,
                    Code = "DRAWING_APPROVED_SOURCE_BRIDGE_INVALID",
                    Message = "Approved asset requires unique typed SourcePortId/SourcePinId for every binding.",
                    TargetKind = "Representation",
                    TargetId = request.RepresentationId
                });
                asset = null;
            }
            else exactBindings = asset.PortBindings.Select(b => b with { EngineeringEndpointId = bridge.Single(p => string.Equals(p.Source, b.EngineeringEndpointId, StringComparison.Ordinal)).Instance }).ToArray();
        }

        if (request.RequiresExplicitEndpointEvidence && request.PortBindings.Count == 0)
        {
            issues.Add(new DrawingPlanningIssue
            {
                IssueId = $"ISSUE:{request.RepresentationId}:required-evidence",
                Severity = DrawingPlanningIssueSeverity.Blocker,
                Code = "DRAWING_REQUIRED_ENGINEERING_EVIDENCE_MISSING",
                Message = "Required explicit endpoint evidence is missing for the selected drawing role.",
                TargetKind = "Representation",
                TargetId = request.RepresentationId
            });
        }

        if (asset is null && assetEligibleFamily)
        {
            issues.Add(new DrawingPlanningIssue
            {
                IssueId = $"ISSUE:{request.RepresentationId}:visual-fallback",
                Severity = DrawingPlanningIssueSeverity.Warning,
                Code = "DRAWING_EXACT_VISUAL_ASSET_UNAVAILABLE",
                Message = "Exact visual asset is unavailable; confirmed engineering truth remains the authority.",
                TargetKind = "Representation",
                TargetId = request.RepresentationId
            });
        }

        var family = asset is not null && request.OwnerKind == DrawingRepresentationOwnerKind.Component && request.Role == DrawingRepresentationRole.Schematic
            ? DrawingRepresentationFamily.ArchivedExact
            : asset is null && request.PreferredFamily == DrawingRepresentationFamily.ArchivedExact
            ? DrawingRepresentationFamily.FunctionalGeneric
            : request.PreferredFamily;
        var bindings = exactBindings ?? (request.PortBindings.Count > 0 ? request.PortBindings : asset?.PortBindings ?? []);
        var decision = new DrawingRepresentationDecision
        {
            RepresentationId = request.RepresentationId,
            OwnerKind = request.OwnerKind,
            OwnerId = request.OwnerId,
            Role = request.Role,
            Family = family,
            ControlState = request.ControlState,
            AllowedRotations = request.AllowedRotations.Distinct().OrderBy(x => x).ToArray(),
            SourceType = asset?.SourceType,
            AssetRevision = asset?.Revision,
            AssetPath = asset?.AssetPath,
            AssetHashSha256 = asset?.AssetHashSha256,
            PortBindings = bindings.OrderBy(x => x.EngineeringEndpointId, StringComparer.Ordinal).ToArray(),
            FieldDeviceClass = request.FieldDeviceClass,
            ControllerId = request.ControllerId,
            PhysicalModuleId = request.PhysicalModuleId,
            FunctionKind = request.FunctionKind,
            MachineZoneId = request.MachineZoneId,
            NetworkId = request.NetworkId,
            NetworkKind = request.NetworkKind,
            SeriesChainId = request.SeriesChainId,
            HeavyDutyConnectorId = request.HeavyDutyConnectorId,
            PhysicalInterfaceMeaning = request.PhysicalInterfaceMeaning
        };
        return new RepresentationDecisionResult(decision, issues);
    }
}
