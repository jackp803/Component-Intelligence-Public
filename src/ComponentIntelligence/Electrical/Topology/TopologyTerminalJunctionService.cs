using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

/// <summary>
/// Provides the topology-level terminal/junction abstraction used by the drawing UI.
/// A visible junction is still backed by a real TerminalBlock / TerminalPosition /
/// TerminalConnectionPoint graph. Additional branches receive additional connection
/// points and explicit internal continuity; the topology never creates continuity from
/// a visual line crossing alone.
/// </summary>
public sealed class TopologyTerminalJunctionService
{
    public const string SelectorPrefix = "terminal:";

    public static string Selector(string terminalBlockId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalBlockId);
        return SelectorPrefix + terminalBlockId.Trim();
    }

    public static bool TryGetTerminalBlockId(string selector, out string terminalBlockId)
    {
        if (!string.IsNullOrWhiteSpace(selector) && selector.StartsWith(SelectorPrefix, StringComparison.OrdinalIgnoreCase))
        {
            terminalBlockId = selector[SelectorPrefix.Length..].Trim();
            return terminalBlockId.Length > 0;
        }

        terminalBlockId = string.Empty;
        return false;
    }

    public ElectricalConnection Connect(
        ElectricalProject project,
        string fromSelector,
        string toSelector,
        string? netId = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromSelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(toSelector);

        if (string.Equals(fromSelector, toSelector, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A topology endpoint cannot be connected to itself.");

        var fromTerminal = TryGetTerminalBlockId(fromSelector, out var fromTerminalId);
        var toTerminal = TryGetTerminalBlockId(toSelector, out var toTerminalId);
        if (fromTerminal && toTerminal && string.Equals(fromTerminalId, toTerminalId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The same terminal junction cannot be wired back to itself.");

        var fromEndpoint = fromTerminal ? null : FindComponentEndpoint(project, fromSelector)
            ?? throw new InvalidOperationException($"Unknown endpoint '{fromSelector}'.");
        var toEndpoint = toTerminal ? null : FindComponentEndpoint(project, toSelector)
            ?? throw new InvalidOperationException($"Unknown endpoint '{toSelector}'.");
        var fromBlock = fromTerminal ? FindTerminalBlock(project, fromTerminalId) : null;
        var toBlock = toTerminal ? FindTerminalBlock(project, toTerminalId) : null;

        if (fromTerminal && fromBlock is null)
            throw new InvalidOperationException($"Unknown terminal junction '{fromTerminalId}'.");
        if (toTerminal && toBlock is null)
            throw new InvalidOperationException($"Unknown terminal junction '{toTerminalId}'.");

        if (fromEndpoint is not null) EnsureEndpointCapacity(project, fromEndpoint);
        if (toEndpoint is not null) EnsureEndpointCapacity(project, toEndpoint);
        if (fromEndpoint is not null && toBlock is not null && IsEndpointConnectedToTerminal(project, fromEndpoint.EndpointId, toBlock))
            throw new InvalidOperationException("This port is already connected to the selected terminal junction.");
        if (toEndpoint is not null && fromBlock is not null && IsEndpointConnectedToTerminal(project, toEndpoint.EndpointId, fromBlock))
            throw new InvalidOperationException("This port is already connected to the selected terminal junction.");

        var fromNet = fromBlock is null ? null : ResolveSingleTerminalNet(project, fromBlock);
        var toNet = toBlock is null ? null : ResolveSingleTerminalNet(project, toBlock);
        if (!string.IsNullOrWhiteSpace(fromNet) && !string.IsNullOrWhiteSpace(toNet) &&
            !string.Equals(fromNet, toNet, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The selected terminal junctions belong to different nets ('{fromNet}' vs '{toNet}').");

        var resolvedNetId = string.IsNullOrWhiteSpace(netId) ? fromNet ?? toNet : netId.Trim();
        var fromEndpointId = fromBlock is null ? fromEndpoint!.EndpointId : AllocateConnectionPoint(project, fromBlock);
        var toEndpointId = toBlock is null ? toEndpoint!.EndpointId : AllocateConnectionPoint(project, toBlock);

        var connection = new ElectricalConnection
        {
            ConnectionId = $"conn-{Guid.NewGuid():N}",
            FromEndpointId = fromEndpointId,
            ToEndpointId = toEndpointId,
            NetId = resolvedNetId,
            Kind = ConnectionKind.Wire
        };
        project.Connections.Add(connection);
        return connection;
    }

    private static ComponentEndpoint? FindComponentEndpoint(ElectricalProject project, string endpointId)
    {
        foreach (var port in project.Components.SelectMany(component => component.Ports))
        {
            if (string.Equals(port.PortId, endpointId, StringComparison.OrdinalIgnoreCase))
                return new ComponentEndpoint(port.PortId, port.MaxConnections);

            var pin = port.Pins.FirstOrDefault(item =>
                string.Equals(item.PinId, endpointId, StringComparison.OrdinalIgnoreCase));
            if (pin is not null) return new ComponentEndpoint(pin.PinId, 1);
        }
        return null;
    }

    private static TerminalBlock? FindTerminalBlock(ElectricalProject project, string terminalBlockId) =>
        project.TerminalBlocks.FirstOrDefault(block =>
            string.Equals(block.TerminalBlockId, terminalBlockId, StringComparison.OrdinalIgnoreCase));

    private static void EnsureEndpointCapacity(ElectricalProject project, ComponentEndpoint endpoint)
    {
        if (endpoint.MaxConnections is not > 0) return;
        var used = project.Connections.Count(connection =>
            string.Equals(connection.FromEndpointId, endpoint.EndpointId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(connection.ToEndpointId, endpoint.EndpointId, StringComparison.OrdinalIgnoreCase));
        if (used >= endpoint.MaxConnections.Value)
            throw new InvalidOperationException(
                $"Endpoint '{endpoint.EndpointId}' already reached its maximum connection count ({endpoint.MaxConnections}).");
    }

    private static string AllocateConnectionPoint(ElectricalProject project, TerminalBlock block)
    {
        var position = block.Positions.FirstOrDefault();
        if (position is null)
            throw new InvalidOperationException($"Terminal junction '{block.ReferenceDesignator}' has no terminal position.");
        var level = position.Levels.FirstOrDefault();
        if (level is null)
            throw new InvalidOperationException($"Terminal junction '{block.ReferenceDesignator}' has no terminal level.");

        foreach (var point in level.ConnectionPoints)
        {
            var used = project.Connections.Count(connection =>
                string.Equals(connection.FromEndpointId, point.ConnectionPointId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(connection.ToEndpointId, point.ConnectionPointId, StringComparison.OrdinalIgnoreCase));
            if (used < Math.Max(1, point.MaxConductors)) return point.ConnectionPointId;
        }

        var anchor = level.ConnectionPoints.FirstOrDefault()
            ?? throw new InvalidOperationException($"Terminal junction '{block.ReferenceDesignator}' has no connection point to extend.");
        var branchIndex = level.ConnectionPoints.Count + 1;
        string pointId;
        do
        {
            pointId = $"{position.TerminalPositionId}:BR:{branchIndex++}";
        }
        while (level.ConnectionPoints.Any(point => string.Equals(point.ConnectionPointId, pointId, StringComparison.OrdinalIgnoreCase)));

        level.ConnectionPoints.Add(new TerminalConnectionPoint
        {
            ConnectionPointId = pointId,
            Type = ConnectionPointType.ConductorEntry,
            PhysicalSide = "BRANCH",
            MaxConductors = 1
        });
        level.InternalConnections.Add(new InternalTerminalConnection
        {
            FromConnectionPointId = anchor.ConnectionPointId,
            ToConnectionPointId = pointId
        });
        position.TerminalType = "TOPOLOGY_JUNCTION";
        if (string.Equals(block.FunctionTag, "INLINE", StringComparison.OrdinalIgnoreCase))
            block.FunctionTag = "JUNCTION";
        block.DisplayName = "Topology terminal junction";
        return pointId;
    }

    private static bool IsEndpointConnectedToTerminal(ElectricalProject project, string endpointId, TerminalBlock block)
    {
        var terminalPointIds = block.Positions
            .SelectMany(position => position.Levels)
            .SelectMany(level => level.ConnectionPoints)
            .Select(point => point.ConnectionPointId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return project.Connections.Any(connection =>
            (string.Equals(connection.FromEndpointId, endpointId, StringComparison.OrdinalIgnoreCase) && terminalPointIds.Contains(connection.ToEndpointId)) ||
            (string.Equals(connection.ToEndpointId, endpointId, StringComparison.OrdinalIgnoreCase) && terminalPointIds.Contains(connection.FromEndpointId)));
    }

    private static string? ResolveSingleTerminalNet(ElectricalProject project, TerminalBlock block)
    {
        var pointIds = block.Positions
            .SelectMany(position => position.Levels)
            .SelectMany(level => level.ConnectionPoints)
            .Select(point => point.ConnectionPointId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var netIds = project.Connections
            .Where(connection => pointIds.Contains(connection.FromEndpointId) || pointIds.Contains(connection.ToEndpointId))
            .Select(connection => connection.NetId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return netIds.Length == 1 ? netIds[0] : null;
    }

    private sealed record ComponentEndpoint(string EndpointId, int? MaxConnections);
}
