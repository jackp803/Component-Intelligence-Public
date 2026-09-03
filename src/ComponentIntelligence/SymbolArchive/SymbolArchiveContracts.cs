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
    public SymbolRole Role { get; init; }
    public IReadOnlyList<SymbolRevisionRecord> Revisions { get; init; } = [];
}

public sealed record SymbolArchiveDocument
{
    public string SchemaVersion { get; init; } = SymbolArchiveRepository.SchemaVersion;
    public IReadOnlyList<ComponentSymbolBinding> Bindings { get; init; } = [];
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
