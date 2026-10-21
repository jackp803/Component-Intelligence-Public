using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Electrical.Connectivity;

public static class ProjectConductiveEdges
{
    public static IEnumerable<(string From, string To)> External(ElectricalProject project)
    {
        foreach (var connection in project.Connections)
        {
            if (connection.MatingMap is not null)
            {
                ConnectorMatingService.Validate(project, connection);
                foreach (var pair in connection.MatingMap.Pairs) yield return (pair.FromPinId, pair.ToPinId);
                continue;
            }
            yield return (connection.FromEndpointId, connection.ToEndpointId);
            if (connection.Kind == ConnectionKind.Cable && connection.CableInstanceId is { } cableId &&
                project.Cables.SingleOrDefault(c => c.CableInstanceId == cableId) is { ArchivedCable: null } cable)
                foreach (var assignment in cable.CoreAssignments.Where(a => a.FromEndpointId is not null && a.ToEndpointId is not null &&
                    (connection.CableCoreId is null || connection.CableCoreId == a.CoreId)))
                    yield return (assignment.FromEndpointId!, assignment.ToEndpointId!);
        }
    }
    public static IEnumerable<(string From, string To)> CableInteriors(ElectricalProject project)
    {
        foreach (var cable in project.Cables.Where(c => c.ArchivedCable is { MappingConfirmed: true }))
        {
            var binding = cable.ArchivedCable!;
            var runtime = binding.Ports.SelectMany(p => p.Pins).ToDictionary(p => p.SourcePinId!, p => p.PinId, StringComparer.Ordinal);
            foreach (var mapping in binding.Mapping)
                if (runtime.TryGetValue(mapping.FromSourcePinId, out var from) && runtime.TryGetValue(mapping.ToSourcePinId, out var to))
                    yield return (from, to);
        }
    }
    public static IReadOnlySet<string> ConnectedEndpointIds(ElectricalProject project) =>
        External(project).SelectMany(e => new[] { e.From, e.To }).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<(string From, string To)> KnownTerminalInteriors(ElectricalProject project)
    {
        foreach (var component in project.Components.Where(c =>
            TopologyPaletteMaterialPolicy.Classify(c.TypeKey) == TopologyPaletteMaterialKind.TerminalBlock &&
            string.Equals(c.ComponentDefinitionId, "PHOENIX_CONTACT_3209578", StringComparison.OrdinalIgnoreCase)))
        {
            if (component.Ports.Sum(p => p.Pins.Count) < 2) continue;
            var ids = component.Ports.SelectMany(p => new[] { p.PortId }.Concat(p.Pins.Select(pin => pin.PinId)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var id in ids.Skip(1)) yield return (ids[0], id);
        }
    }
}
