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
            .Where(assignment => InScope(project, connection, assignment))
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
        if (connection.CableCoreId is { } referencedCore &&
            (normalized.Length != 1 || normalized[0].CoreId?.Trim() != referencedCore))
            throw new InvalidOperationException("此連線已有明確 Core 歸屬，不可透過腳位編輯移除或改寫該歸屬。");
        var duplicateFrom = normalized.GroupBy(item => item.FromPinId, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicateFrom is not null)
            throw new InvalidOperationException($"Pin '{DescribePin(project, duplicateFrom.Key)}' is mapped more than once from side A.");
        var duplicateTo = normalized.GroupBy(item => item.ToPinId, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicateTo is not null)
            throw new InvalidOperationException($"Pin '{DescribePin(project, duplicateTo.Key)}' is mapped more than once on side B.");

        foreach (var mapping in normalized)
        {
            if ((IsPin(project, connection.FromEndpointId) && mapping.FromPinId != connection.FromEndpointId) ||
                (IsPin(project, connection.ToEndpointId) && mapping.ToPinId != connection.ToEndpointId))
                throw new InvalidOperationException("腳位證據必須符合目前明確的連線端點；不會透過 Core Mapping 改接線路。");
            if (!fromPort.Pins.Any(pin => string.Equals(pin.PinId, mapping.FromPinId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Pin '{mapping.FromPinId}' does not belong to side A port '{fromPort.Name}'.");
            if (!toPort.Pins.Any(pin => string.Equals(pin.PinId, mapping.ToPinId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Pin '{mapping.ToPinId}' does not belong to side B port '{toPort.Name}'.");
        }

        var existingCable = project.Cables.SingleOrDefault(c => c.CableInstanceId == connection.CableInstanceId);
        var previousAssignments = existingCable?.CoreAssignments.ToArray() ?? [];
        var retained = existingCable?.CoreAssignments.Where(a => !InScope(project, connection, a)).ToArray() ?? [];
        var coreIds = normalized.Select((m, i) => string.IsNullOrWhiteSpace(m.CoreId) ? $"MAP-{i + 1}" : m.CoreId.Trim()).ToArray();
        if (coreIds.Distinct(StringComparer.Ordinal).Count() != coreIds.Length ||
            retained.Any(a => coreIds.Contains(a.CoreId, StringComparer.Ordinal)))
            throw new InvalidOperationException("Core ID 已用於其他導體，請明確指定不重複的 Core ID。");
        var cable = EnsureCable(project, connection);
        cable.CoreAssignments.RemoveAll(a => InScope(project, connection, a));
        for (var index = 0; index < normalized.Length; index++)
        {
            var mapping = normalized[index];
            cable.CoreAssignments.Add(new CoreAssignment
            {
                CoreId = string.IsNullOrWhiteSpace(mapping.CoreId) ? $"MAP-{index + 1}" : mapping.CoreId.Trim(),
                NetId = previousAssignments.SingleOrDefault(a => a.CoreId == coreIds[index] &&
                    a.FromEndpointId == mapping.FromPinId && a.ToEndpointId == mapping.ToPinId)?.NetId,
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
        if (connection.CableCoreId is not null)
            throw new InvalidOperationException("此連線已有明確 Core 歸屬，不可直接清空其腳位證據。");
        if (string.IsNullOrWhiteSpace(connection.CableInstanceId)) return;
        var cable = project.Cables.FirstOrDefault(item => string.Equals(item.CableInstanceId, connection.CableInstanceId, StringComparison.OrdinalIgnoreCase));
        cable?.CoreAssignments.RemoveAll(a => InScope(project, connection, a));
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
            .SingleOrDefault(port => port.PortId == portId || port.Pins.Any(pin => pin.PinId == portId));

    private static bool IsPin(ElectricalProject project, string id) =>
        project.Components.SelectMany(c => c.Ports).SelectMany(p => p.Pins).Any(p => p.PinId == id);

    private static bool InScope(ElectricalProject project, ElectricalConnection connection, CoreAssignment assignment) =>
        (!IsPin(project, connection.FromEndpointId) || assignment.FromEndpointId == connection.FromEndpointId) &&
        (!IsPin(project, connection.ToEndpointId) || assignment.ToEndpointId == connection.ToEndpointId);

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
