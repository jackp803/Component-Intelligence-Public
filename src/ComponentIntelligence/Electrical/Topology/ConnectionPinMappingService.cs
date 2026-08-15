using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

public sealed record PinMappingEntry(
    string FromPinId,
    string ToPinId,
    string? CoreId = null,
    string? Signal = null,
    ElectricalLayer Layer = ElectricalLayer.Unknown);

/// <summary>
/// Stores explicit Pin → Pin mapping in the existing CableInstance.CoreAssignments model.
/// No straight-through mapping is inferred. If a user/vendor document has not supplied a mapping,
/// the collection remains empty and the engineering state is Unknown.
/// </summary>
public sealed class ConnectionPinMappingService
{
    public IReadOnlyList<PinMappingEntry> GetMappings(ElectricalProject project, string connectionId)
    {
        ArgumentNullException.ThrowIfNull(project);
        var connection = FindConnection(project, connectionId);
        if (string.IsNullOrWhiteSpace(connection.CableInstanceId)) return [];
        var cable = project.Cables.FirstOrDefault(item =>
            string.Equals(item.CableInstanceId, connection.CableInstanceId, StringComparison.OrdinalIgnoreCase));
        if (cable is null) return [];

        return cable.CoreAssignments
            .Where(assignment => !string.IsNullOrWhiteSpace(assignment.FromEndpointId) && !string.IsNullOrWhiteSpace(assignment.ToEndpointId))
            .Select(assignment => new PinMappingEntry(
                assignment.FromEndpointId!,
                assignment.ToEndpointId!,
                assignment.CoreId,
                assignment.Signal,
                assignment.Layer))
            .ToArray();
    }

    public CableInstance SetMappings(
        ElectricalProject project,
        string connectionId,
        IEnumerable<PinMappingEntry> mappings)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(mappings);
        var connection = FindConnection(project, connectionId);
        var fromPort = FindPort(project, connection.FromEndpointId)
            ?? throw new InvalidOperationException("Pin mapping requires the connection A endpoint to be a component Port.");
        var toPort = FindPort(project, connection.ToEndpointId)
            ?? throw new InvalidOperationException("Pin mapping requires the connection B endpoint to be a component Port.");

        var normalized = mappings.ToArray();
        var duplicateFrom = normalized.GroupBy(item => item.FromPinId, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicateFrom is not null)
            throw new InvalidOperationException($"Pin '{DescribePin(project, duplicateFrom.Key)}' is mapped more than once from side A.");
        var duplicateTo = normalized.GroupBy(item => item.ToPinId, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicateTo is not null)
            throw new InvalidOperationException($"Pin '{DescribePin(project, duplicateTo.Key)}' is mapped more than once on side B.");

        foreach (var mapping in normalized)
        {
            if (!fromPort.Pins.Any(pin => string.Equals(pin.PinId, mapping.FromPinId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Pin '{mapping.FromPinId}' does not belong to side A port '{fromPort.Name}'.");
            if (!toPort.Pins.Any(pin => string.Equals(pin.PinId, mapping.ToPinId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Pin '{mapping.ToPinId}' does not belong to side B port '{toPort.Name}'.");
        }

        var cable = EnsureCable(project, connection);
        cable.CoreAssignments.Clear();
        for (var index = 0; index < normalized.Length; index++)
        {
            var mapping = normalized[index];
            cable.CoreAssignments.Add(new CoreAssignment
            {
                CoreId = string.IsNullOrWhiteSpace(mapping.CoreId) ? $"MAP-{index + 1}" : mapping.CoreId.Trim(),
                Status = "ASSIGNED",
                FromEndpointId = mapping.FromPinId,
                ToEndpointId = mapping.ToPinId,
                Signal = string.IsNullOrWhiteSpace(mapping.Signal) ? null : mapping.Signal.Trim(),
                Layer = mapping.Layer
            });
        }
        return cable;
    }

    public void ClearMappings(ElectricalProject project, string connectionId)
    {
        var connection = FindConnection(project, connectionId);
        if (string.IsNullOrWhiteSpace(connection.CableInstanceId)) return;
        var cable = project.Cables.FirstOrDefault(item => string.Equals(item.CableInstanceId, connection.CableInstanceId, StringComparison.OrdinalIgnoreCase));
        cable?.CoreAssignments.Clear();
    }

    public (ComponentPort From, ComponentPort To) GetPortPair(ElectricalProject project, string connectionId)
    {
        var connection = FindConnection(project, connectionId);
        return (
            FindPort(project, connection.FromEndpointId) ?? throw new InvalidOperationException("Connection A endpoint is not a component Port."),
            FindPort(project, connection.ToEndpointId) ?? throw new InvalidOperationException("Connection B endpoint is not a component Port."));
    }

    private static CableInstance EnsureCable(ElectricalProject project, ElectricalConnection connection)
    {
        if (!string.IsNullOrWhiteSpace(connection.CableInstanceId))
        {
            var existing = project.Cables.FirstOrDefault(item => string.Equals(item.CableInstanceId, connection.CableInstanceId, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing;
        }

        var cable = new CableInstance
        {
            CableInstanceId = $"cbl-map-{Guid.NewGuid():N}",
            CableDefinitionId = "UNRESOLVED-CABLE",
            ReferenceDesignator = null
        };
        project.Cables.Add(cable);
        connection.CableInstanceId = cable.CableInstanceId;
        connection.Kind = ConnectionKind.Cable;
        return cable;
    }

    private static ElectricalConnection FindConnection(ElectricalProject project, string connectionId) =>
        project.Connections.FirstOrDefault(item => string.Equals(item.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Connection '{connectionId}' does not exist.");

    private static ComponentPort? FindPort(ElectricalProject project, string portId) =>
        project.Components.SelectMany(component => component.Ports)
            .FirstOrDefault(port => string.Equals(port.PortId, portId, StringComparison.OrdinalIgnoreCase));

    private static string DescribePin(ElectricalProject project, string pinId)
    {
        foreach (var component in project.Components)
        foreach (var port in component.Ports)
        {
            var pin = port.Pins.FirstOrDefault(item => string.Equals(item.PinId, pinId, StringComparison.OrdinalIgnoreCase));
            if (pin is not null) return $"{component.ReferenceDesignator ?? component.ComponentInstanceId}.{port.Name}.Pin{pin.PinNumber}";
        }
        return pinId;
    }
}
