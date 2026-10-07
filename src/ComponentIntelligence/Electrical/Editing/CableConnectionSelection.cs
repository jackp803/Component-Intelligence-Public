using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Editing;

public sealed record CableConnectionSelectionRow(string ConnectionId, string Label, bool IncludesPort);

public static class CableConnectionSelection
{
    public static IReadOnlyList<CableConnectionSelectionRow> Build(ElectricalProject project, string? cableId = null,
        bool includePorts = false)
    {
        var endpoints = new List<(string Id, string Label, bool IsPort)>();
        foreach (var component in project.Components)
        foreach (var port in component.Ports)
        {
            var label = $"{component.ReferenceDesignator ?? component.DisplayName ?? "未命名元件"} / {port.Name}";
            if (includePorts) endpoints.Add((port.PortId, label + " (Port)", true));
            endpoints.AddRange(port.Pins.Select(pin => (pin.PinId, $"{label} / Pin {pin.PinNumber}", false)));
        }
        var unique = endpoints.GroupBy(e => e.Id, StringComparer.Ordinal).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        var rows = new List<CableConnectionSelectionRow>();
        foreach (var connection in project.Connections.Where(c => c.Kind is ConnectionKind.Wire or ConnectionKind.Cable &&
            (cableId is null || string.IsNullOrWhiteSpace(c.CableInstanceId) || c.CableInstanceId == cableId))
            .OrderBy(c => c.ConnectionId, StringComparer.Ordinal))
        {
            if (!unique.TryGetValue(connection.FromEndpointId, out var from) ||
                !unique.TryGetValue(connection.ToEndpointId, out var to)) continue;
            var cable = project.Cables.SingleOrDefault(c => c.CableInstanceId == connection.CableInstanceId);
            rows.Add(new(connection.ConnectionId,
                $"{from.Label} -> {to.Label} | Cable: {cable?.ReferenceDesignator ?? connection.CableInstanceId ?? "未指定"}",
                from.IsPort || to.IsPort));
        }
        return rows;
    }
}
