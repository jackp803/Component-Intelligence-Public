using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

/// <summary>
/// Builds the electrically continuous endpoint group for one visible connection. External wires
/// are continuous by definition; terminal-block internal links and installed jumpers extend that
/// continuity without changing the saved project model.
/// </summary>
public static class TopologyElectricalContinuity
{
    public static IReadOnlySet<string> ConnectedEndpoints(
        ElectricalProject project,
        ElectricalConnection seed)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(seed);

        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (from, to) in Connectivity.ProjectConductiveEdges.External(project).Concat(Connectivity.ProjectConductiveEdges.CableInteriors(project)))
            Connect(adjacency, from, to);

        AddStructuredTerminalContinuity(project, adjacency);
        AddArchivedTerminalComponentContinuity(project, adjacency);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(seed.FromEndpointId);
        queue.Enqueue(seed.ToEndpointId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current) || !adjacency.TryGetValue(current, out var neighbours)) continue;
            foreach (var neighbour in neighbours)
                if (!visited.Contains(neighbour)) queue.Enqueue(neighbour);
        }
        return visited;
    }

    public static ElectricalLayer ResolveLayer(
        ElectricalProject project,
        ElectricalConnection connection)
    {
        var endpoints = ConnectedEndpoints(project, connection);
        var layers = project.Components
            .SelectMany(component => component.Ports)
            .SelectMany(port => port.Pins)
            .Where(pin => endpoints.Contains(pin.PinId))
            .Select(pin => pin.Layer)
            .Where(layer => layer != ElectricalLayer.Unknown)
            .ToHashSet();

        // A supply feeding an analogue/digital input remains a power conductor. This deterministic
        // precedence also removes the previous dependence on BOM component ordering.
        foreach (var preferred in new[]
                 {
                     ElectricalLayer.Power,
                     ElectricalLayer.Grounding,
                     ElectricalLayer.Safety,
                     ElectricalLayer.Communication,
                     ElectricalLayer.Analog,
                     ElectricalLayer.Digital
                 })
            if (layers.Contains(preferred)) return preferred;
        return ElectricalLayer.Unknown;
    }

    private static void AddStructuredTerminalContinuity(
        ElectricalProject project,
        IDictionary<string, HashSet<string>> adjacency)
    {
        foreach (var block in project.TerminalBlocks)
        {
            foreach (var level in block.Positions.SelectMany(position => position.Levels))
            foreach (var connection in level.InternalConnections)
                Connect(adjacency, connection.FromConnectionPointId, connection.ToConnectionPointId);

            foreach (var jumper in block.Jumpers)
                ConnectGroup(adjacency, jumper.ConnectionPointIds);
        }
    }

    private static void AddArchivedTerminalComponentContinuity(
        ElectricalProject project,
        IDictionary<string, HashSet<string>> adjacency)
    {
        foreach (var (from, to) in Connectivity.ProjectConductiveEdges.KnownTerminalInteriors(project)) Connect(adjacency, from, to);
    }

    private static void ConnectGroup(
        IDictionary<string, HashSet<string>> adjacency,
        IEnumerable<string> endpointIds)
    {
        var ids = endpointIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        for (var index = 1; index < ids.Length; index++)
            Connect(adjacency, ids[0], ids[index]);
    }

    private static void Connect(
        IDictionary<string, HashSet<string>> adjacency,
        string first,
        string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return;
        if (!adjacency.TryGetValue(first, out var firstNeighbours))
            adjacency[first] = firstNeighbours = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!adjacency.TryGetValue(second, out var secondNeighbours))
            adjacency[second] = secondNeighbours = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        firstNeighbours.Add(second);
        secondNeighbours.Add(first);
    }
}
