using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.SymbolArchive;

public sealed record SchematicModuleLayoutApplyResult(ElectricalProject Project, bool Applied, string? Warning);

public sealed class SchematicModuleLayoutStore(SymbolArchiveRepository repository)
{
    public SchematicModuleLayoutProfile Save(ElectricalProject project, string symbolId)
    {
        var symbol = project.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == symbolId)
            ?? throw new InvalidOperationException("Select a module representation.");
        if (symbol.Geometry is not null || symbol.CableInstanceId is not null || symbol.SectionCount != 1)
            throw new InvalidOperationException("Only a complete generic component module can be saved as a reusable layout.");
        var component = project.Components.SingleOrDefault(c => c.ComponentInstanceId == symbol.ComponentInstanceId)
            ?? throw new InvalidOperationException("The module has no physical component identity.");
        if (string.IsNullOrWhiteSpace(component.ComponentDefinitionId))
            throw new InvalidOperationException("The module has no catalog ComponentId.");
        var ports = component.Ports.ToDictionary(p => p.PortId, StringComparer.Ordinal);
        if (ports.Values.Any(p => string.IsNullOrWhiteSpace(p.SourcePortId)) ||
            ports.Values.Select(p => p.SourcePortId).Distinct(StringComparer.Ordinal).Count() != ports.Count)
            throw new InvalidOperationException("Reusable layouts require unique exact SourcePortId values.");
        var pins = ports.Values.SelectMany(p => p.Pins.Select(pin => (Port: p, Pin: pin))).ToArray();
        if (pins.Any(item => string.IsNullOrWhiteSpace(item.Pin.SourcePinId)) ||
            pins.Select(item => item.Pin.SourcePinId).Distinct(StringComparer.Ordinal).Count() != pins.Length ||
            pins.Select(item => item.Pin.PinId).Order(StringComparer.Ordinal)
                .SequenceEqual(symbol.Anchors.Select(a => a.EndpointId).Order(StringComparer.Ordinal)) is false)
            throw new InvalidOperationException("Reusable layouts require every exact source Pin on one complete representation.");
        var byRuntimePin = pins.ToDictionary(item => item.Pin.PinId, StringComparer.Ordinal);
        var profile = new SchematicModuleLayoutProfile
        {
            ComponentId = component.ComponentDefinitionId,
            Revision = $"layout-{Guid.NewGuid():N}",
            Width = symbol.Width, Height = symbol.Height, Rotation = symbol.Rotation, ManualSize = symbol.ManualSize,
            Pins = symbol.Anchors.Select(anchor => new SchematicModulePinLayout
            {
                SourcePortId = byRuntimePin[anchor.EndpointId].Port.SourcePortId!,
                SourcePinId = byRuntimePin[anchor.EndpointId].Pin.SourcePinId!,
                Position = anchor.Position, Side = anchor.Direction
            }).ToArray(),
            Ports = symbol.PortPlacements.Select(placement => new SchematicModulePortLayout
            {
                SourcePortId = ports[placement.PortId].SourcePortId!,
                Side = placement.Side, Coordinate = placement.Coordinate
            }).ToArray(),
            CollapsedSourcePortIds = symbol.CollapsedPortIds.Select(id => ports[id].SourcePortId!).ToArray()
        };
        var document = repository.Load();
        var updated = document.SchematicLayouts.Select(existing => existing.ComponentId == profile.ComponentId && existing.Active
            ? existing with { Active = false } : existing).Append(profile).ToArray();
        repository.Save(document with { SchematicLayouts = updated });
        return profile;
    }

    public SchematicModuleLayoutApplyResult ApplyActive(ElectricalProject project, string symbolId)
    {
        var symbol = project.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == symbolId);
        var component = project.Components.SingleOrDefault(c => c.ComponentInstanceId == symbol?.ComponentInstanceId);
        if (symbol is null || component is null || symbol.Geometry is not null) return new(project, false, null);
        var profile = repository.Load().SchematicLayouts.SingleOrDefault(p => p.Active &&
            p.ComponentId == component.ComponentDefinitionId);
        if (profile is null) return new(project, false, null);
        try
        {
            return new(new SchematicAuthoringService().ApplyModuleLayout(project, symbolId, profile), true, null);
        }
        catch (InvalidOperationException error)
        {
            return new(project, false, "Saved module layout not applied: " + error.Message);
        }
    }
}
