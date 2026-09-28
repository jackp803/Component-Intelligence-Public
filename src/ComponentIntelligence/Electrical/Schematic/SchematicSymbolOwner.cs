using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicSymbolOwner(ComponentInstance? Component, CableInstance? Cable)
{
    public string DefinitionId => Component?.ComponentDefinitionId ?? Cable!.CableDefinitionId;
    public string? Reference => Component?.ReferenceDesignator ?? Cable?.ReferenceDesignator;
    public string DisplayName => Component?.DisplayName ?? Cable?.DisplayName ?? DefinitionId;
    public IReadOnlyList<ComponentPort> Ports => Component?.Ports ?? Cable!.ArchivedCable!.Ports;

    public static SchematicSymbolOwner Resolve(ElectricalProject project, SchematicSymbol symbol)
    {
        if (symbol.CableInstanceId is { } cableId)
        {
            if (!string.IsNullOrEmpty(symbol.ComponentInstanceId))
                throw new InvalidOperationException("A representation must have exactly one physical owner.");
            var cable = project.Cables.SingleOrDefault(c => c.CableInstanceId == cableId);
            if (cable?.ArchivedCable is null) throw new InvalidOperationException("Unknown archived cable representation.");
            return new(null, cable);
        }
        var component = project.Components.SingleOrDefault(c => c.ComponentInstanceId == symbol.ComponentInstanceId)
            ?? throw new InvalidOperationException("Unknown component representation.");
        return new(component, null);
    }

    public void SetReference(string? reference)
    {
        var value = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        if (Component is not null)
        {
            Component.ReferenceDesignator = value;
            Component.ReferenceSource = ReferenceSource.Manual;
            Component.ReferenceLocked = value is not null;
        }
        else Cable!.ReferenceDesignator = value;
    }
}
