using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

/// <summary>
/// Discovers cable candidates from the pin-level connections the user actually drew. It never
/// rewires, merges, or converts a project merely because the project was loaded.
/// </summary>
public sealed class ConnectorCableTopologyService
{
    public ConnectorCableTopology AnalyzeConnector(ElectricalProject project, string connectorPortId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorPortId);

        var selected = FindPort(project, connectorPortId);
        if (selected is null || selected.Port.Connector is null)
            throw new InvalidOperationException($"Endpoint '{connectorPortId}' is not a connector port.");

        var selectedEndpointIds = selected.Port.Pins.Select(pin => pin.PinId)
            .Append(selected.Port.PortId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Older saved field-adapter objects used a separate loose-wire port beside the mating port.
        // Reading those endpoints keeps old projects editable without changing any saved connection.
        if (HasDirectMating(project, selectedEndpointIds))
        {
            foreach (var loosePort in selected.Component.Ports.Where(port => port.Connector is null))
            {
                selectedEndpointIds.Add(loosePort.PortId);
                foreach (var pin in loosePort.Pins) selectedEndpointIds.Add(pin.PinId);
            }
        }

        var groups = project.Connections
            .Where(connection => connection.Kind != ConnectionKind.DirectMating &&
                                 (selectedEndpointIds.Contains(connection.FromEndpointId) ||
                                  selectedEndpointIds.Contains(connection.ToEndpointId)))
            .Select(connection =>
            {
                var remoteEndpointId = selectedEndpointIds.Contains(connection.FromEndpointId)
                    ? connection.ToEndpointId
                    : connection.FromEndpointId;
                var remote = FindConnectorPortForEndpoint(project, remoteEndpointId);
                var key = remote?.Port.Connector?.ConnectorId ?? "LOOSE-WIRE";
                var label = remote is null
                    ? "散線端 / Loose wire"
                    : $"{remote.Component.ReferenceDesignator ?? remote.Component.DisplayName ?? remote.Component.ComponentInstanceId} · " +
                      $"{remote.Port.Connector!.Family} {remote.Port.Connector.Gender}";
                return new { Connection = connection, Remote = remote, Key = key, Label = label };
            })
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var connections = group.Select(item => item.Connection).ToArray();
                var existingCableIds = connections
                    .Select(connection => connection.CableInstanceId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return new ConnectorCableCandidate(
                    selected.Component.ComponentInstanceId,
                    selected.Port.PortId,
                    selected.Port.Connector!.ConnectorId,
                    selected.Component.ReferenceDesignator ?? selected.Component.DisplayName ?? selected.Component.ComponentInstanceId,
                    selected.Port.Connector.Family,
                    selected.Port.Connector.Gender,
                    first.Remote?.Component.ComponentInstanceId,
                    first.Remote?.Port.PortId,
                    first.Remote?.Port.Connector?.ConnectorId,
                    first.Label,
                    connections,
                    existingCableIds);
            })
            .OrderBy(candidate => candidate.RemoteLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ConnectorCableTopology(
            selected.Component.ComponentInstanceId,
            selected.Port.PortId,
            selected.Port.Connector.ConnectorId,
            selected.Component.ReferenceDesignator ?? selected.Component.DisplayName ?? selected.Component.ComponentInstanceId,
            groups);
    }

    public ConnectorCableAssignmentResult AssignCandidateAsCable(
        ElectricalProject project,
        ConnectorCableCandidate candidate,
        CableSegmentOptions options,
        double? providedLengthMm = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(options);
        if (providedLengthMm is <= 0)
            throw new InvalidOperationException("Cable length must be greater than zero when supplied.");

        var connectionIds = candidate.Connections.Select(item => item.ConnectionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var connections = project.Connections
            .Where(connection => connectionIds.Contains(connection.ConnectionId))
            .ToArray();
        if (connections.Length != connectionIds.Count || connections.Length == 0)
            throw new InvalidOperationException("The selected cable conductors changed. Please select the connector again.");
        if (connections.Any(connection => connection.Kind == ConnectionKind.DirectMating))
            throw new InvalidOperationException("Direct connector mating cannot be assigned as cable material.");

        var previousCableIds = connections
            .Select(connection => connection.CableInstanceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sharedExistingCableId = previousCableIds.Count == 1 ? previousCableIds.Single() : null;
        var cable = sharedExistingCableId is null
            ? null
            : project.Cables.FirstOrDefault(item => string.Equals(
                item.CableInstanceId,
                sharedExistingCableId,
                StringComparison.OrdinalIgnoreCase));
        var definitionId = string.IsNullOrWhiteSpace(options.CableDefinitionId)
            ? "UNRESOLVED-CABLE"
            : options.CableDefinitionId.Trim();

        if (cable is null)
        {
            cable = new CableInstance
            {
                CableInstanceId = $"cbl-{Guid.NewGuid():N}",
                CableDefinitionId = definitionId,
                ReferenceDesignator = string.IsNullOrWhiteSpace(options.ReferenceDesignator)
                    ? NextCableReference(project)
                    : options.ReferenceDesignator.Trim()
            };
            project.Cables.Add(cable);
        }
        else
        {
            cable.CableDefinitionId = definitionId;
            if (!string.IsNullOrWhiteSpace(options.ReferenceDesignator))
                cable.ReferenceDesignator = options.ReferenceDesignator.Trim();
            else if (string.IsNullOrWhiteSpace(cable.ReferenceDesignator))
                cable.ReferenceDesignator = NextCableReference(project);
        }

        cable.DisplayName = string.IsNullOrWhiteSpace(options.DisplayName) ? cable.DisplayName : options.DisplayName.Trim();
        cable.ProvidedLengthMm = providedLengthMm ?? cable.ProvidedLengthMm;
        cable.LengthSource = providedLengthMm is null ? cable.LengthSource : CableLengthSource.User;
        cable.CoreAssignments.Clear();

        for (var index = 0; index < connections.Length; index++)
        {
            var connection = connections[index];
            var coreId = (index + 1).ToString();
            var net = string.IsNullOrWhiteSpace(connection.NetId)
                ? null
                : project.Nets.FirstOrDefault(item => string.Equals(item.NetId, connection.NetId, StringComparison.OrdinalIgnoreCase));
            connection.Kind = ConnectionKind.Cable;
            connection.CableInstanceId = cable.CableInstanceId;
            connection.CableCoreId = coreId;
            cable.CoreAssignments.Add(new CoreAssignment
            {
                CoreId = coreId,
                NetId = connection.NetId,
                Signal = net?.Label,
                Layer = net?.Layer ?? ElectricalLayer.Unknown,
                Status = "ASSIGNED",
                FromEndpointId = connection.FromEndpointId,
                ToEndpointId = connection.ToEndpointId
            });
        }

        var assembly = project.CableAssemblies.FirstOrDefault(item => item.Members.Any(member =>
            string.Equals(member.CableInstanceId, cable.CableInstanceId, StringComparison.OrdinalIgnoreCase)));
        if (assembly is null)
        {
            assembly = new CableAssembly
            {
                CableAssemblyId = $"ca-{Guid.NewGuid():N}",
                ReferenceDesignator = cable.ReferenceDesignator,
                IsCustom = true,
                EndAConnectorId = candidate.SelectedConnectorId,
                EndBConnectorId = candidate.RemoteConnectorId
            };
            assembly.Members.Add(new CableAssemblyMember
            {
                CableInstanceId = cable.CableInstanceId,
                Purpose = candidate.RemoteConnectorId is null ? "CONNECTOR-TO-LOOSE-WIRE" : "CONNECTOR-TO-CONNECTOR"
            });
            project.CableAssemblies.Add(assembly);
        }
        else
        {
            assembly.ReferenceDesignator = cable.ReferenceDesignator;
            assembly.IsCustom = true;
            assembly.EndAConnectorId = candidate.SelectedConnectorId;
            assembly.EndBConnectorId = candidate.RemoteConnectorId;
        }

        CleanupOrphanedPreviousCables(project, previousCableIds, cable.CableInstanceId);
        return new ConnectorCableAssignmentResult(candidate, cable, assembly);
    }

    private static bool HasDirectMating(ElectricalProject project, IReadOnlySet<string> endpointIds) =>
        project.Connections.Any(connection => connection.Kind == ConnectionKind.DirectMating &&
                                              (endpointIds.Contains(connection.FromEndpointId) ||
                                               endpointIds.Contains(connection.ToEndpointId)));

    private static PortOwner? FindPort(ElectricalProject project, string portId)
    {
        foreach (var component in project.Components)
        foreach (var port in component.Ports)
            if (string.Equals(port.PortId, portId, StringComparison.OrdinalIgnoreCase))
                return new PortOwner(component, port);
        return null;
    }

    private static PortOwner? FindConnectorPortForEndpoint(
        ElectricalProject project,
        string endpointId)
    {
        foreach (var component in project.Components)
        foreach (var port in component.Ports.Where(port => port.Connector is not null))
            if (string.Equals(port.PortId, endpointId, StringComparison.OrdinalIgnoreCase) ||
                port.Pins.Any(pin => string.Equals(pin.PinId, endpointId, StringComparison.OrdinalIgnoreCase)))
                return new PortOwner(component, port);
        return null;
    }

    private static void CleanupOrphanedPreviousCables(
        ElectricalProject project,
        IEnumerable<string> previousCableIds,
        string retainedCableId)
    {
        var orphaned = previousCableIds
            .Where(id => !string.Equals(id, retainedCableId, StringComparison.OrdinalIgnoreCase))
            .Where(id => project.Connections.All(connection => !string.Equals(
                connection.CableInstanceId,
                id,
                StringComparison.OrdinalIgnoreCase)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (orphaned.Count == 0) return;
        project.CableAssemblies.RemoveAll(assembly => assembly.Members.Any(member => orphaned.Contains(member.CableInstanceId)));
        project.Cables.RemoveAll(cable => orphaned.Contains(cable.CableInstanceId));
    }

    private static string NextCableReference(ElectricalProject project)
    {
        var used = project.Cables.Select(cable => cable.ReferenceDesignator)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < 10000; index++)
        {
            var candidate = $"CBL-{index:000}";
            if (!used.Contains(candidate)) return candidate;
        }
        return $"CBL-{Guid.NewGuid():N}";
    }

    private sealed record PortOwner(ComponentInstance Component, ComponentPort Port);
}

public sealed record ConnectorCableTopology(
    string ComponentInstanceId,
    string ConnectorPortId,
    string ConnectorId,
    string ConnectorLabel,
    IReadOnlyList<ConnectorCableCandidate> Candidates);

public sealed record ConnectorCableCandidate(
    string SelectedComponentId,
    string SelectedConnectorPortId,
    string SelectedConnectorId,
    string SelectedConnectorLabel,
    string SelectedConnectorFamily,
    ConnectorGender SelectedConnectorGender,
    string? RemoteComponentId,
    string? RemoteConnectorPortId,
    string? RemoteConnectorId,
    string RemoteLabel,
    IReadOnlyList<ElectricalConnection> Connections,
    IReadOnlyList<string> ExistingCableIds)
{
    public string Display =>
        $"{SelectedConnectorLabel} → {RemoteLabel} · {Connections.Count} Core(s)";
}

public sealed record ConnectorCableAssignmentResult(
    ConnectorCableCandidate Candidate,
    CableInstance Cable,
    CableAssembly Assembly);
