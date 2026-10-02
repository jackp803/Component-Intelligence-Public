using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed class CableManufacturingDraft
{
    public List<ComponentPort> Ends { get; set; } = [];
    public string? FromSourcePortId { get; set; }
    public string? ToSourcePortId { get; set; }
    public List<CableManufacturingRow> Rows { get; set; } = [];
    public string? MappingRevision { get; set; }
    public string? MappingEvidence { get; set; }
    public bool ConfirmMapping { get; set; }
    public List<string> ExplicitlyRemovedPinIds { get; set; } = [];
}
