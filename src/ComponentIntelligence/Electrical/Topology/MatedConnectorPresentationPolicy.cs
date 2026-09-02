using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

public sealed record MatedConnectorPresentationPair(
    string EndAComponentId,
    string EndAPortId,
    ConnectorGender EndAGender,
    string EndBComponentId,
    string EndBPortId,
    ConnectorGender EndBGender,
    IReadOnlyList<string> ConnectionIds);

public sealed record MatedConnectorSnapTarget(
    string PartnerComponentId,
    double X,
    double Y,
    IReadOnlyList<string> ConnectionIds);

public sealed record AvailableMatingSnapTarget(
    string PartnerComponentId,
    string MovedPortId,
    string PartnerPortId,
    double X,
    double Y);

/// <summary>
/// Derives a visual plug/socket relationship from the existing electrical model. It never creates
/// or removes engineering connections: snapping only aligns the two documented mating faces.
/// </summary>
public static class MatedConnectorPresentationPolicy
{
    public static IReadOnlyList<MatedConnectorPresentationPair> BuildPairs(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var pairs = new List<MatedConnectorPresentationPair>();

        foreach (var assembly in project.CableAssemblies)
        {
            if (!TryFindConnector(project, assembly.EndAConnectorId, out var endA) ||
                !TryFindConnector(project, assembly.EndBConnectorId, out var endB) ||
                !AreComplementary(endA.Port.Connector!.Gender, endB.Port.Connector!.Gender))
                continue;

            var cableIds = assembly.Members.Select(member => member.CableInstanceId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var connectionIds = project.Connections
                .Where(connection => !string.IsNullOrWhiteSpace(connection.CableInstanceId) &&
                                     cableIds.Contains(connection.CableInstanceId!) &&
                                     ConnectsComponents(project, connection, endA.Component.ComponentInstanceId, endB.Component.ComponentInstanceId))
                .Select(connection => connection.ConnectionId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (connectionIds.Length == 0) continue;

            pairs.Add(new MatedConnectorPresentationPair(
                endA.Component.ComponentInstanceId,
                endA.Port.PortId,
                endA.Port.Connector.Gender,
                endB.Component.ComponentInstanceId,
                endB.Port.PortId,
                endB.Port.Connector.Gender,
                connectionIds));
        }

        foreach (var connection in project.Connections.Where(item => item.Kind == ConnectionKind.DirectMating))
        {
            if (!TryFindEndpointPort(project, connection.FromEndpointId, out var endA) ||
                !TryFindEndpointPort(project, connection.ToEndpointId, out var endB) ||
                endA.Port.Connector is null || endB.Port.Connector is null ||
                !AreComplementary(endA.Port.Connector.Gender, endB.Port.Connector.Gender))
                continue;
            if (pairs.Any(pair => pair.ConnectionIds.Contains(connection.ConnectionId, StringComparer.OrdinalIgnoreCase)))
                continue;

            pairs.Add(new MatedConnectorPresentationPair(
                endA.Component.ComponentInstanceId,
                endA.Port.PortId,
                endA.Port.Connector.Gender,
                endB.Component.ComponentInstanceId,
                endB.Port.PortId,
                endB.Port.Connector.Gender,
                [connection.ConnectionId]));
        }

        return pairs;
    }

    public static bool TryGetSnapTarget(
        ElectricalProject project,
        string movedComponentId,
        double snapDistance,
        out MatedConnectorSnapTarget target)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(movedComponentId);
        if (snapDistance < 0d) throw new ArgumentOutOfRangeException(nameof(snapDistance));

        foreach (var pair in BuildPairs(project))
        {
            var movedIsA = string.Equals(pair.EndAComponentId, movedComponentId, StringComparison.OrdinalIgnoreCase);
            var movedIsB = string.Equals(pair.EndBComponentId, movedComponentId, StringComparison.OrdinalIgnoreCase);
            if (!movedIsA && !movedIsB) continue;

            var movedId = movedIsA ? pair.EndAComponentId : pair.EndBComponentId;
            var movedPortId = movedIsA ? pair.EndAPortId : pair.EndBPortId;
            var partnerId = movedIsA ? pair.EndBComponentId : pair.EndAComponentId;
            var partnerPortId = movedIsA ? pair.EndBPortId : pair.EndAPortId;
            if (!TryGetMatingAnchor(project, movedId, movedPortId, out var movedAnchor) ||
                !TryGetMatingAnchor(project, partnerId, partnerPortId, out var partnerAnchor))
                continue;

            var dx = partnerAnchor.X - movedAnchor.X;
            var dy = partnerAnchor.Y - movedAnchor.Y;
            if (Math.Sqrt(dx * dx + dy * dy) > snapDistance) continue;

            var placement = project.TopologyPlacements.First(item =>
                string.Equals(item.ObjectId, movedId, StringComparison.OrdinalIgnoreCase));
            target = new MatedConnectorSnapTarget(
                partnerId,
                placement.X + dx,
                placement.Y + dy,
                pair.ConnectionIds);
            return true;
        }

        target = null!;
        return false;
    }

    /// <summary>
    /// Finds an unused compatible plug/socket close enough to the moved inline connector. Unlike
    /// <see cref="TryGetSnapTarget"/>, this is used before a DirectMating record exists so the UI can
    /// turn an intentional physical plug-in gesture into the same formal engineering connection
    /// that would be created by selecting the two mating ports in Wire mode.
    /// </summary>
    public static bool TryGetAvailableSnapTarget(
        ElectricalProject project,
        string movedComponentId,
        double snapDistance,
        out AvailableMatingSnapTarget target)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(movedComponentId);
        if (snapDistance < 0d) throw new ArgumentOutOfRangeException(nameof(snapDistance));

        var movedComponent = project.Components.FirstOrDefault(component =>
            string.Equals(component.ComponentInstanceId, movedComponentId, StringComparison.OrdinalIgnoreCase));
        if (movedComponent is null ||
            !string.Equals(movedComponent.TypeKey, "INLINE_CONNECTOR", StringComparison.OrdinalIgnoreCase))
        {
            target = null!;
            return false;
        }

        AvailableMatingSnapTarget? best = null;
        var bestDistance = double.MaxValue;
        foreach (var movedPort in movedComponent.Ports.Where(IsOpenOrReplaceableMatingPort))
        {
            foreach (var partner in project.Components.Where(component =>
                         !string.Equals(component.ComponentInstanceId, movedComponentId, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(component.TypeKey, "INLINE_CONNECTOR", StringComparison.OrdinalIgnoreCase)))
            foreach (var partnerPort in partner.Ports.Where(IsOpenOrReplaceableMatingPort))
            {
                if (!AreCompatible(movedPort.Connector!, partnerPort.Connector!)) continue;
                if (!TryGetMatingAnchor(project, movedComponentId, movedPort.PortId, out var movedAnchor) ||
                    !TryGetMatingAnchor(project, partner.ComponentInstanceId, partnerPort.PortId, out var partnerAnchor))
                    continue;

                var dx = partnerAnchor.X - movedAnchor.X;
                var dy = partnerAnchor.Y - movedAnchor.Y;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > snapDistance || distance >= bestDistance) continue;

                var placement = project.TopologyPlacements.First(item =>
                    string.Equals(item.ObjectId, movedComponentId, StringComparison.OrdinalIgnoreCase));
                bestDistance = distance;
                best = new AvailableMatingSnapTarget(
                    partner.ComponentInstanceId,
                    movedPort.PortId,
                    partnerPort.PortId,
                    placement.X + dx,
                    placement.Y + dy);
            }
        }

        target = best!;
        return best is not null;

        bool IsOpenOrReplaceableMatingPort(ComponentPort port)
        {
            if (port.Connector?.Gender is not (ConnectorGender.Female or ConnectorGender.Male))
                return false;
            var connections = project.Connections.Where(connection =>
                string.Equals(connection.FromEndpointId, port.PortId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(connection.ToEndpointId, port.PortId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (connections.Length == 0) return true;
            if (connections.Length != 1 || connections[0].Kind != ConnectionKind.DirectMating) return false;

            var remotePortId = string.Equals(connections[0].FromEndpointId, port.PortId, StringComparison.OrdinalIgnoreCase)
                ? connections[0].ToEndpointId
                : connections[0].FromEndpointId;
            var remote = project.Components.FirstOrDefault(component => component.Ports.Any(candidate =>
                string.Equals(candidate.PortId, remotePortId, StringComparison.OrdinalIgnoreCase)));
            return remote is not null &&
                   remote.ComponentDefinitionId.StartsWith("inline-mated-adapter:", StringComparison.OrdinalIgnoreCase) &&
                   !project.TopologyPlacements.Any(placement =>
                       string.Equals(placement.ObjectId, remote.ComponentInstanceId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static bool IsVisuallyMated(
        ElectricalProject project,
        MatedConnectorPresentationPair pair,
        double tolerance = 1.5d) =>
        TryGetMatingAnchor(project, pair.EndAComponentId, pair.EndAPortId, out var first) &&
        TryGetMatingAnchor(project, pair.EndBComponentId, pair.EndBPortId, out var second) &&
        Math.Sqrt(Math.Pow(first.X - second.X, 2d) + Math.Pow(first.Y - second.Y, 2d)) <= tolerance;

    private static bool TryGetMatingAnchor(
        ElectricalProject project,
        string componentId,
        string portId,
        out TopologyPortAnchor anchor)
    {
        var component = project.Components.FirstOrDefault(item =>
            string.Equals(item.ComponentInstanceId, componentId, StringComparison.OrdinalIgnoreCase));
        var placement = project.TopologyPlacements.FirstOrDefault(item =>
            string.Equals(item.ObjectId, componentId, StringComparison.OrdinalIgnoreCase));
        var port = component?.Ports.FirstOrDefault(item =>
            string.Equals(item.PortId, portId, StringComparison.OrdinalIgnoreCase));
        if (component is null || placement is null || port is null)
        {
            anchor = null!;
            return false;
        }

        var side = TopologyPortGeometry.DetermineScreenSide(component, port);
        var portsOnSide = component.Ports
            .Where(candidate => TopologyPortGeometry.DetermineScreenSide(component, candidate) == side)
            .ToArray();
        var index = Array.FindIndex(portsOnSide, candidate =>
            string.Equals(candidate.PortId, portId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            anchor = null!;
            return false;
        }

        anchor = TopologyPortGeometry.CalculateRotatedSide(placement, side, index, portsOnSide.Length);
        return true;
    }

    private static bool AreComplementary(ConnectorGender first, ConnectorGender second) =>
        first is ConnectorGender.Female && second is ConnectorGender.Male ||
        first is ConnectorGender.Male && second is ConnectorGender.Female;

    private static bool AreCompatible(ConnectorDefinition first, ConnectorDefinition second)
    {
        if (!AreComplementary(first.Gender, second.Gender) ||
            !string.Equals(first.Family, second.Family, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(first.Coding) && !string.IsNullOrWhiteSpace(second.Coding) &&
            !string.Equals(first.Coding, second.Coding, StringComparison.OrdinalIgnoreCase))
            return false;
        if (first.PinCount is > 0 && second.PinCount is > 0 && first.PinCount != second.PinCount)
            return false;
        return string.IsNullOrWhiteSpace(first.CompatibilityClass) ||
               string.IsNullOrWhiteSpace(second.CompatibilityClass) ||
               string.Equals(first.CompatibilityClass, second.CompatibilityClass, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ConnectsComponents(
        ElectricalProject project,
        ElectricalConnection connection,
        string firstComponentId,
        string secondComponentId)
    {
        if (!TryFindEndpointPort(project, connection.FromEndpointId, out var from) ||
            !TryFindEndpointPort(project, connection.ToEndpointId, out var to))
            return false;
        return string.Equals(from.Component.ComponentInstanceId, firstComponentId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(to.Component.ComponentInstanceId, secondComponentId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(from.Component.ComponentInstanceId, secondComponentId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(to.Component.ComponentInstanceId, firstComponentId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFindConnector(
        ElectricalProject project,
        string? connectorId,
        out (ComponentInstance Component, ComponentPort Port) result)
    {
        if (!string.IsNullOrWhiteSpace(connectorId))
        {
            foreach (var component in project.Components)
            foreach (var port in component.Ports)
            {
                if (port.Connector is not null &&
                    string.Equals(port.Connector.ConnectorId, connectorId, StringComparison.OrdinalIgnoreCase))
                {
                    result = (component, port);
                    return true;
                }
            }
        }
        result = default;
        return false;
    }

    private static bool TryFindEndpointPort(
        ElectricalProject project,
        string endpointId,
        out (ComponentInstance Component, ComponentPort Port) result)
    {
        foreach (var component in project.Components)
        foreach (var port in component.Ports)
        {
            if (string.Equals(port.PortId, endpointId, StringComparison.OrdinalIgnoreCase) ||
                port.Pins.Any(pin => string.Equals(pin.PinId, endpointId, StringComparison.OrdinalIgnoreCase)))
            {
                result = (component, port);
                return true;
            }
        }
        result = default;
        return false;
    }
}
