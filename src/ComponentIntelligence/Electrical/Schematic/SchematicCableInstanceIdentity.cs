using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

internal static class SchematicCableInstanceIdentity
{
    internal static Dictionary<string, string> EndpointMap(CableInstance original, CableInstance copy)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var port in original.ArchivedCable!.Ports)
        {
            var target = copy.ArchivedCable!.Ports.Single(p => p.SourcePortId == port.SourcePortId);
            result.Add(port.PortId, target.PortId);
            foreach (var pin in port.Pins) result.Add(pin.PinId, target.Pins.Single(p => p.SourcePinId == pin.SourcePinId).PinId);
        }
        return result;
    }

    internal static SchematicSymbol Rebind(SchematicSymbol source, string cableId, IReadOnlyDictionary<string, string> ids) => source with {
        CableInstanceId = cableId,
        Anchors = source.Anchors.Select(a => a with { EndpointId = ids[a.EndpointId] }).ToList(),
        CadPortBindings = source.CadPortBindings.Select(b => b with { PortId = ids[b.PortId] }).ToList(),
        CollapsedPortIds = source.CollapsedPortIds.Select(id => ids[id]).ToList(),
        PortPlacements = source.PortPlacements.Select(p => p with { PortId = ids[p.PortId] }).ToList(),
        EndpointDisplayPlacements = source.EndpointDisplayPlacements.Select(p => p with {
            EndpointId = ids[p.EndpointId], PortId = ids[p.PortId] }).ToList() };
}
