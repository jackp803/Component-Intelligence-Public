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
        ValidateBranchPotential(project, from, to);
        var resolvedNetId = ResolveBranchNet(project, from, to, netId);

        var kind = from.Kind == EndpointKind.Port && to.Kind == EndpointKind.Port
            ? AreDirectMates(from.Connector, to.Connector)
                ? ConnectionKind.DirectMating
                : ConnectionKind.Cable
            : ConnectionKind.Wire;

        var connection = new ElectricalConnection
        {
            ConnectionId = $"conn-{Guid.NewGuid():N}",
            FromEndpointId = fromEndpointId,
            ToEndpointId = toEndpointId,
            NetId = resolvedNetId,
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
                return new EndpointInfo(endpointId, EndpointKind.Port, port.MaxConnections, Connector: port.Connector);

            var pin = port.Pins.FirstOrDefault(pin =>
                string.Equals(pin.PinId, endpointId, StringComparison.OrdinalIgnoreCase));
            if (pin is not null)
                return new EndpointInfo(
                    endpointId,
                    EndpointKind.Pin,
                    TopologyEndpointBranchPolicy.MaximumConnections(port, pin),
                    TopologyEndpointBranchPolicy.AllowsBranching(port, pin));
        }

        foreach (var block in project.TerminalBlocks)
        foreach (var point in block.Positions
                     .SelectMany(position => position.Levels)
                     .SelectMany(level => level.ConnectionPoints))
        {
            if (string.Equals(point.ConnectionPointId, endpointId, StringComparison.OrdinalIgnoreCase))
                return new EndpointInfo(endpointId, EndpointKind.TerminalPoint, Math.Max(1, point.MaxConductors), false);
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

    private static string? ResolveBranchNet(
        ElectricalProject project,
        EndpointInfo from,
        EndpointInfo to,
        string? requestedNetId)
    {
        var shared = new[] { from, to }.Where(endpoint => endpoint.AllowsBranching &&
            CountConnections(project, endpoint.EndpointId) > 0).ToArray();
        if (shared.Length == 0) return requestedNetId;

        var related = shared.SelectMany(endpoint => project.Connections.Where(connection =>
                Touches(connection, endpoint.EndpointId)))
            .DistinctBy(connection => connection.ConnectionId)
            .ToArray();
        var netIds = related.Select(connection => connection.NetId)
            .Append(requestedNetId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (netIds.Length > 1)
            throw new InvalidOperationException(
                "This common Pin is already connected to a different Net. Branched wires must share the same electrical Net.");

        var resolved = netIds.SingleOrDefault();
        if (resolved is not null)
            foreach (var connection in related.Where(connection => string.IsNullOrWhiteSpace(connection.NetId)))
                connection.NetId = resolved;
        return resolved;
    }

    private static void ValidateBranchPotential(
        ElectricalProject project,
        EndpointInfo from,
        EndpointInfo to)
    {
        foreach (var shared in new[] { from, to }.Where(endpoint => endpoint.AllowsBranching &&
                     CountConnections(project, endpoint.EndpointId) > 0))
        {
            var newRemote = string.Equals(shared.EndpointId, from.EndpointId, StringComparison.OrdinalIgnoreCase)
                ? to.EndpointId
                : from.EndpointId;
            var endpoints = project.Connections.Where(connection => Touches(connection, shared.EndpointId))
                .Select(connection => string.Equals(connection.FromEndpointId, shared.EndpointId, StringComparison.OrdinalIgnoreCase)
                    ? connection.ToEndpointId
                    : connection.FromEndpointId)
                .Append(shared.EndpointId)
                .Append(newRemote);
            var potentials = endpoints
                .Select(endpointId => TopologyConnectionPotentialClassifier.ClassifyEndpoint(project, endpointId))
                .Where(value => value != TopologyPotentialClass.Unknown)
                .Distinct()
                .ToArray();
            if (potentials.Length > 1)
                throw new InvalidOperationException(
                    "The selected common Pin would join incompatible electrical potentials. Only the same GND / COM / 0V Net may branch here.");
        }
    }

    private static int CountConnections(ElectricalProject project, string endpointId) =>
        project.Connections.Count(connection => Touches(connection, endpointId));

    private static bool Touches(ElectricalConnection connection, string endpointId) =>
        string.Equals(connection.FromEndpointId, endpointId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(connection.ToEndpointId, endpointId, StringComparison.OrdinalIgnoreCase);

    private static bool AreDirectMates(ConnectorDefinition? first, ConnectorDefinition? second)
    {
        if (first is null || second is null) return false;
        if (!string.Equals(first.Family, second.Family, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(first.Coding) && !string.IsNullOrWhiteSpace(second.Coding) &&
            !string.Equals(first.Coding, second.Coding, StringComparison.OrdinalIgnoreCase)) return false;
        if (first.PinCount is > 0 && second.PinCount is > 0 && first.PinCount != second.PinCount) return false;
        return first.Gender is ConnectorGender.Male && second.Gender is ConnectorGender.Female ||
               first.Gender is ConnectorGender.Female && second.Gender is ConnectorGender.Male;
    }

    private enum EndpointKind
    {
        Port,
        Pin,
        TerminalPoint
    }

    private sealed record EndpointInfo(
        string EndpointId,
        EndpointKind Kind,
        int? MaxConnections,
        bool AllowsBranching = false,
        ConnectorDefinition? Connector = null);
}
