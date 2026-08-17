using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

/// <summary>
/// Creates formal electrical connections between either whole ports/connectors or individual
/// component pins/terminal points. Endpoint identity is preserved so later wiring/drawing export can
/// tell exactly which conductor or terminal was connected.
/// </summary>
public sealed class TopologyEndpointConnectionService
{
    public ElectricalConnection ConnectEndpoints(
        ElectricalProject project,
        string fromEndpointId,
        string toEndpointId,
        string? netId = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromEndpointId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toEndpointId);

        if (string.Equals(fromEndpointId, toEndpointId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("An endpoint cannot be connected to itself.");

        var from = ResolveEndpoint(project, fromEndpointId)
            ?? throw new InvalidOperationException($"Unknown endpoint '{fromEndpointId}'.");
        var to = ResolveEndpoint(project, toEndpointId)
            ?? throw new InvalidOperationException($"Unknown endpoint '{toEndpointId}'.");

        if (project.Connections.Any(connection =>
                (string.Equals(connection.FromEndpointId, fromEndpointId, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(connection.ToEndpointId, toEndpointId, StringComparison.OrdinalIgnoreCase)) ||
                (string.Equals(connection.FromEndpointId, toEndpointId, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(connection.ToEndpointId, fromEndpointId, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("These endpoints are already connected.");

        EnsureCapacity(project, from);
        EnsureCapacity(project, to);

        var kind = from.Kind == EndpointKind.Port && to.Kind == EndpointKind.Port
            ? ConnectionKind.Cable
            : ConnectionKind.Wire;

        var connection = new ElectricalConnection
        {
            ConnectionId = $"conn-{Guid.NewGuid():N}",
            FromEndpointId = fromEndpointId,
            ToEndpointId = toEndpointId,
            NetId = netId,
            Kind = kind
        };
        project.Connections.Add(connection);
        return connection;
    }

    public bool IsKnownEndpoint(ElectricalProject project, string endpointId) =>
        ResolveEndpoint(project, endpointId) is not null;

    private static EndpointInfo? ResolveEndpoint(ElectricalProject project, string endpointId)
    {
        foreach (var component in project.Components)
        foreach (var port in component.Ports)
        {
            if (string.Equals(port.PortId, endpointId, StringComparison.OrdinalIgnoreCase))
                return new EndpointInfo(endpointId, EndpointKind.Port, port.MaxConnections);

            if (port.Pins.Any(pin => string.Equals(pin.PinId, endpointId, StringComparison.OrdinalIgnoreCase)))
                return new EndpointInfo(endpointId, EndpointKind.Pin, 1);
        }

        foreach (var block in project.TerminalBlocks)
        foreach (var point in block.Positions
                     .SelectMany(position => position.Levels)
                     .SelectMany(level => level.ConnectionPoints))
        {
            if (string.Equals(point.ConnectionPointId, endpointId, StringComparison.OrdinalIgnoreCase))
                return new EndpointInfo(endpointId, EndpointKind.TerminalPoint, Math.Max(1, point.MaxConductors));
        }

        return null;
    }

    private static void EnsureCapacity(ElectricalProject project, EndpointInfo endpoint)
    {
        if (endpoint.MaxConnections is not > 0) return;

        var used = project.Connections.Count(connection =>
            string.Equals(connection.FromEndpointId, endpoint.EndpointId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(connection.ToEndpointId, endpoint.EndpointId, StringComparison.OrdinalIgnoreCase));
        if (used >= endpoint.MaxConnections.Value)
            throw new InvalidOperationException(
                $"Endpoint '{endpoint.EndpointId}' already reached its maximum connection count ({endpoint.MaxConnections}).");
    }

    private enum EndpointKind
    {
        Port,
        Pin,
        TerminalPoint
    }

    private sealed record EndpointInfo(string EndpointId, EndpointKind Kind, int? MaxConnections);
}
