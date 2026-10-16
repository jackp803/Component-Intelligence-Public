using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

/// <summary>
/// Permanently removes terminal material that was added from the shared archive, or a structured
/// topology terminal junction. Unlike normal BOM components, deleted terminals must not return to
/// the unplaced-component palette.
/// </summary>
public sealed class TopologyTerminalDeletionService
{
    private readonly TopologyConnectionEditor _connectionEditor = new();

    public TopologyTerminalDeletionResult Delete(ElectricalProject project, IEnumerable<string> objectIds)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(objectIds);

        var requested = objectIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requested.Count == 0) return new TopologyTerminalDeletionResult(0, 0, 0, 0);

        var componentIds = project.Components
            .Where(component =>
                requested.Contains(component.ComponentInstanceId) &&
                TopologyPaletteMaterialPolicy.Classify(component.TypeKey) == TopologyPaletteMaterialKind.TerminalBlock)
            .Select(component => component.ComponentInstanceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blockIds = project.TerminalBlocks
            .Where(block => requested.Contains(block.TerminalBlockId))
            .Select(block => block.TerminalBlockId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var endpointIds = project.Components
            .Where(component => componentIds.Contains(component.ComponentInstanceId))
            .SelectMany(component => component.Ports)
            .SelectMany(port => new[] { port.PortId }.Concat(port.Pins.Select(pin => pin.PinId)))
            .Concat(project.TerminalBlocks
                .Where(block => blockIds.Contains(block.TerminalBlockId))
                .SelectMany(block => block.Positions)
                .SelectMany(position => position.Levels)
                .SelectMany(level => level.ConnectionPoints)
                .Select(point => point.ConnectionPointId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var attachedConnections = project.Connections
            .Where(connection =>
                endpointIds.Contains(connection.FromEndpointId) ||
                endpointIds.Contains(connection.ToEndpointId) ||
                IsSelectedStructuredTerminal(connection.FromEndpointId, blockIds) ||
                IsSelectedStructuredTerminal(connection.ToEndpointId, blockIds))
            .ToArray();
        var attachedConnectionIds = attachedConnections
            .Select(connection => connection.ConnectionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var attachedCableIds = attachedConnections
            .Select(connection => connection.CableInstanceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removedConnections = _connectionEditor.DeleteConnections(project, attachedConnectionIds);

        project.CableRoutes.RemoveAll(route =>
            attachedConnectionIds.Contains(route.ConnectionOrCableId) ||
            attachedCableIds.Contains(route.ConnectionOrCableId));
        project.EndpointReviews.RemoveAll(review => endpointIds.Contains(review.EndpointId));
        var removedPlacements = project.TopologyPlacements.RemoveAll(placement =>
            componentIds.Contains(placement.ObjectId) || blockIds.Contains(placement.ObjectId));
        var removedComponents = project.Components.RemoveAll(component => componentIds.Contains(component.ComponentInstanceId));
        var removedBlocks = project.TerminalBlocks.RemoveAll(block => blockIds.Contains(block.TerminalBlockId));

        return new TopologyTerminalDeletionResult(
            removedComponents,
            removedBlocks,
            removedPlacements,
            removedConnections);
    }

    private static bool IsSelectedStructuredTerminal(string endpointId, IReadOnlySet<string> blockIds) =>
        TopologyTerminalJunctionService.TryGetTerminalBlockId(endpointId, out var blockId) &&
        blockIds.Contains(blockId);
}

public sealed record TopologyTerminalDeletionResult(
    int RemovedComponentTerminals,
    int RemovedStructuredTerminals,
    int RemovedPlacements,
    int RemovedConnections)
{
    public int RemovedTerminals => RemovedComponentTerminals + RemovedStructuredTerminals;
}
