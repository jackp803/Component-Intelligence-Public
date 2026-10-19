using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Electrical.Domain;

public sealed record ProjectBomItem
{
    public required BomRow Row { get; init; }
    public string? ComponentDefinitionId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ComponentIR? DefinitionSnapshot { get; init; }
    public string? Category { get; init; }
    public string? Subcategory { get; init; }
    public bool ConnectionMaterial { get; init; }
    public CableProductAuthority? CableProduct { get; init; }
    public IReadOnlyList<string> ComponentInstanceIds { get; init; } = [];
}
