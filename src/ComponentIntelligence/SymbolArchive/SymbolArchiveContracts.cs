namespace ComponentIntelligence.SymbolArchive;

public enum SymbolRole
{
    Schematic,
    ConnectorDetail,
    PanelFootprint,
    TopologyVisual
}

public enum SymbolSourceType
{
    ApprovedCustom,
    Manufacturer,
    LibraryStandard,
    GeneratedGeneric
}

public enum SymbolRevisionStatus
{
    Candidate,
    Approved,
    Superseded,
    Rejected
}

public enum DeepInspectionStatus
{
    NotRequested,
    Unavailable,
    Succeeded,
    Failed
}

public sealed record SymbolPortBinding
{
    public required string EngineeringEndpointId { get; init; }
    public required string ConnectionPointId { get; init; }
}

public sealed record SymbolRevisionRecord
{
    public required string Revision { get; init; }
    public required SymbolSourceType SourceType { get; init; }
    public required string AssetPath { get; init; }
    public required string AssetHashSha256 { get; init; }
    public SymbolRevisionStatus Status { get; init; }
    public IReadOnlyList<SymbolPortBinding> PortBindings { get; init; } = [];
}

public sealed record ComponentSymbolBinding
{
    public required string ComponentId { get; init; }
    public string RepresentationId { get; init; } = "default";
    public string? RepresentationName { get; init; }
    public SymbolRole Role { get; init; }
    public IReadOnlyList<SymbolRevisionRecord> Revisions { get; init; } = [];
}

public sealed record SymbolArchiveDocument
{
    public string SchemaVersion { get; init; } = SymbolArchiveRepository.SchemaVersion;
    public IReadOnlyList<ComponentSymbolBinding> Bindings { get; init; } = [];
    public IReadOnlyList<CableArchiveEntry> CableTemplates { get; init; } = [];
    public IReadOnlyList<SchematicModuleLayoutProfile> SchematicLayouts { get; init; } = [];
}

public sealed record SchematicModulePinLayout
{
    public required string SourcePortId { get; init; }
    public required string SourcePinId { get; init; }
    public required Electrical.Schematic.SchematicPoint Position { get; init; }
    public required string Side { get; init; }
}

public sealed record SchematicModulePortLayout
{
    public required string SourcePortId { get; init; }
    public required string Side { get; init; }
    public double Coordinate { get; init; }
}

// A reusable drawing preference, not an approved CAD asset or electrical pin-mapping authority.
public sealed record SchematicModuleLayoutProfile
{
    public required string ComponentId { get; init; }
    public required string Revision { get; init; }
    public bool Active { get; init; } = true;
    public double Width { get; init; }
    public double Height { get; init; }
    public int Rotation { get; init; }
    public bool ManualSize { get; init; }
    public IReadOnlyList<SchematicModulePinLayout> Pins { get; init; } = [];
    public IReadOnlyList<SchematicModulePortLayout> Ports { get; init; } = [];
    public IReadOnlyList<string> CollapsedSourcePortIds { get; init; } = [];
}

public sealed record CableArchiveEntry
{
    public required Electrical.Domain.ArchivedCableTemplate Template { get; init; }
    public required string AssetPath { get; init; }
    public required double MillimetresPerUnit { get; init; }
    public SymbolRevisionStatus Status { get; init; } = SymbolRevisionStatus.Candidate;
    public Electrical.Domain.CableConstructionType ConstructionType { get; init; }
    public string? ConstructionEvidence { get; init; }
    public IReadOnlyList<SymbolPortBinding> ContactBindings { get; init; } = [];
    public CableCadRoleAsset? ManufacturingAsset { get; init; }
}

public sealed record CableCadRoleAsset
{
    public required string AssetPath { get; init; }
    public required string SourceSha256 { get; init; }
    public required double MillimetresPerUnit { get; init; }
    public CableCadSelection? Selection { get; init; }
}

public sealed record CableCadSelection
{
    public Electrical.Schematic.SchematicGridBounds? Bounds { get; init; }
    public string? BlockName { get; init; }
    public required string GeometrySha256 { get; init; }
}

public sealed record SymbolBoundingBox(
    double MinX,
    double MinY,
    double MinZ,
    double MaxX,
    double MaxY,
    double MaxZ);

public sealed record BlockAttributeMetadata(string Name, string Value);

public sealed record BlockDeepInspectionMetadata
{
    public IReadOnlyList<string> BlockNames { get; init; } = [];
    public IReadOnlyList<BlockAttributeMetadata> Attributes { get; init; } = [];
    public IReadOnlyList<string> TextLabels { get; init; } = [];
    public SymbolBoundingBox? BoundingBox { get; init; }
}
