namespace ComponentIntelligence.Electrical.Domain;

// The archive supplies source identities. Runtime pins are distinct per physical cable.
public sealed record ArchivedCableTemplate
{
    public required string TemplateId { get; init; }
    public required string TemplateRevision { get; init; }
    public required string AssetSha256 { get; init; }
    public string? DisplayName { get; init; }
    public List<ComponentPort> Ports { get; init; } = [];
    public string? MappingRevision { get; init; }
    public string? MappingEvidence { get; init; }
    public bool MappingConfirmed { get; init; }
    public List<CablePinMapping> Mapping { get; init; } = [];
    public List<CableTextBinding> TextBindings { get; init; } = [];
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
}
