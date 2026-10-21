using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    public ElectricalProject ApplyModuleLayout(ElectricalProject project, string symbolId,
        SchematicModuleLayoutProfile profile) => Edit(project, (draft, doc) =>
    {
        var index = doc.Symbols.FindIndex(s => s.SymbolId == symbolId);
        if (index < 0) throw new InvalidOperationException("The module representation no longer exists.");
        var symbol = doc.Symbols[index];
        if (symbol.Geometry is not null || symbol.CableInstanceId is not null || symbol.SectionCount != 1 ||
            doc.Wires.Any(w => w.Start.SymbolId == symbolId || w.End.SymbolId == symbolId))
            throw new InvalidOperationException("The saved layout can only be applied before wiring a complete generic module.");
        var component = draft.Components.Single(c => c.ComponentInstanceId == symbol.ComponentInstanceId);
        if (component.ComponentDefinitionId != profile.ComponentId)
            throw new InvalidOperationException("ComponentId does not match the saved layout.");
        var ports = component.Ports.ToArray();
        if (ports.Any(p => string.IsNullOrWhiteSpace(p.SourcePortId)) ||
            ports.Select(p => p.SourcePortId).Distinct(StringComparer.Ordinal).Count() != ports.Length)
            throw new InvalidOperationException("SourcePortId inventory is incomplete or ambiguous.");
        var sourcePorts = ports.ToDictionary(p => p.SourcePortId!, StringComparer.Ordinal);
        var pins = ports.SelectMany(port => port.Pins.Select(pin => (Port: port, Pin: pin))).ToArray();
        if (pins.Any(item => string.IsNullOrWhiteSpace(item.Pin.SourcePinId)) ||
            pins.Select(item => item.Pin.SourcePinId).Distinct(StringComparer.Ordinal).Count() != pins.Length)
            throw new InvalidOperationException("SourcePinId inventory is incomplete or ambiguous.");
        var sourcePins = pins.ToDictionary(item => item.Pin.SourcePinId!, StringComparer.Ordinal);
        if (!sourcePins.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(profile.Pins.Select(p => p.SourcePinId)) ||
            !symbol.Anchors.Select(a => a.EndpointId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(pins.Select(item => item.Pin.PinId)) ||
            profile.Pins.Any(saved => !sourcePins.TryGetValue(saved.SourcePinId, out var item) ||
                item.Port.SourcePortId != saved.SourcePortId) ||
            profile.Ports.Any(saved => !sourcePorts.ContainsKey(saved.SourcePortId)) ||
            profile.CollapsedSourcePortIds.Any(id => !sourcePorts.ContainsKey(id)))
            throw new InvalidOperationException("Exact source Port/Pin inventory differs from this layout revision.");
        var byRuntimePin = profile.Pins.ToDictionary(saved => sourcePins[saved.SourcePinId].Pin.PinId, StringComparer.Ordinal);
        doc.Symbols[index] = symbol with
        {
            Width = profile.Width, Height = profile.Height, Rotation = profile.Rotation, ManualSize = profile.ManualSize,
            Anchors = symbol.Anchors.Select(anchor =>
            {
                var saved = byRuntimePin[anchor.EndpointId];
                return anchor with { Position = saved.Position, Direction = saved.Side };
            }).ToList(),
            PortPlacements = profile.Ports.Select(saved => new SchematicPortPlacement
            {
                PortId = sourcePorts[saved.SourcePortId].PortId,
                Side = saved.Side, Coordinate = saved.Coordinate
            }).ToList(),
            CollapsedPortIds = profile.CollapsedSourcePortIds.Select(id => sourcePorts[id].PortId).ToList()
        };
    });
}
