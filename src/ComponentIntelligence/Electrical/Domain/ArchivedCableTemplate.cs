namespace ComponentIntelligence.Electrical.Domain;

// The archive supplies source identities. Runtime pins are distinct per physical cable.
public sealed record ArchivedCableTemplate
{
    public required string TemplateId { get; init; }
    public required string TemplateRevision { get; init; }
    public required string AssetSha256 { get; init; }
    public string? WiringSelectionSha256 { get; init; }
    public string? DisplayName { get; init; }
    public List<ComponentPort> Ports { get; init; } = [];
    public string? MappingRevision { get; init; }
    public string? MappingEvidence { get; init; }
    public bool MappingConfirmed { get; init; }
    public List<CablePinMapping> Mapping { get; init; } = [];
    public List<CableTextBinding> TextBindings { get; init; } = [];
    public CableManufacturingDefinition? Manufacturing { get; init; }
}

public enum CableTextField { Reference, LengthMm, Specification }
public sealed record CableTextBinding(string AttributeTag, CableTextField Field);

public sealed record CablePinMapping(string FromSourcePinId, string ToSourcePinId);

public sealed record ArchivedCableBinding
{
    public required ArchivedCableTemplate Template { get; init; }
    public List<ComponentPort> Ports { get; init; } = [];
    public List<CablePinMapping> Mapping { get; init; } = [];
    public bool MappingConfirmed { get; init; }
    public bool HasMappingOverride { get; init; }
    public CableManufacturingDefinition? Manufacturing { get; init; }
    public Schematic.SchematicCadAsset? ManufacturingGeometry { get; init; }
}

public enum CablePinUsage { Pending, Unused, Nc }
public sealed record CablePinUsageDeclaration(string SourcePinId, CablePinUsage Usage);

public sealed class CableManufacturingRow
{
    public string? FromSourcePinId { get; set; }
    public string? ToSourcePinId { get; set; }
    public string? FromFunction { get; set; }
    public string? ToFunction { get; set; }
    public CablePinUsage FromUsage { get; set; }
    public CablePinUsage ToUsage { get; set; }
    public Dictionary<string, CableManufacturingCell> AdditionalEnds { get; set; } = [];
}

public sealed class CableManufacturingCell
{
    public string? SourcePinId { get; set; }
    public string? Function { get; set; }
    public CablePinUsage Usage { get; set; }
}

public sealed record CableManufacturingDefinition
{
    public string? FromSourcePortId { get; init; }
    public string? ToSourcePortId { get; init; }
    public List<CableManufacturingRow> IncompleteRows { get; init; } = [];
    public List<CablePinUsageDeclaration> PinUsage { get; init; } = [];
    public List<CableManufacturingRow>? Rows { get; init; }
}
