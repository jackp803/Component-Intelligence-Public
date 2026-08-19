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
        foreach (var connection in project.Connections)
            Connect(adjacency, connection.FromEndpointId, connection.ToEndpointId);

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
        foreach (var component in project.Components.Where(component =>
                     TopologyPaletteMaterialPolicy.Classify(component.TypeKey) == TopologyPaletteMaterialKind.TerminalBlock))
        {
            var pins = component.Ports.SelectMany(port => port.Pins).ToArray();
            if (pins.Length < 2) continue;

            var isQuattroCommonPotential = IsQuattroCommonPotential(component);
            if (isQuattroCommonPotential)
            {
                // Existing projects may connect either an exact terminal Pin or the visible
                // INPUT / OUTPUT / BRIDGE Port.  Both selectors represent the same copper body on
                // this model, so include both identity levels in the continuity graph.
                ConnectGroup(adjacency, component.Ports
                    .SelectMany(port => new[] { port.PortId }.Concat(port.Pins.Select(pin => pin.PinId))));
                continue;
            }

            foreach (var group in pins.GroupBy(TerminalPotentialGroup, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Key is not null && group.Count() > 1))
                ConnectGroup(adjacency, group.Select(pin => pin.PinId));
        }
    }

    private static bool IsQuattroCommonPotential(ComponentInstance component)
    {
        // Archive imports do not guarantee that the manufacturer part number is used as the
        // ComponentDefinitionId.  Older projects commonly retain it only in DisplayName or
        // EquipmentTag.  PT 2,5-QUATTRO (3209578) is one common-potential feed-through terminal,
        // so all IN / OUT / bridge points must carry the same displayed potential.
        var identity = string.Join(' ', new[]
        {
            component.ComponentDefinitionId,
            component.DisplayName,
            component.EquipmentTag,
            component.ReferenceDesignator
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var compact = new string(identity.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        return compact.Contains("3209578", StringComparison.Ordinal) ||
               compact.Contains("PT25QUATTRO", StringComparison.Ordinal);
    }

    private static string? TerminalPotentialGroup(ComponentPin pin)
    {
        var value = $"{pin.PinName} {pin.Function}".ToUpperInvariant();
        if (value.Contains("L1", StringComparison.Ordinal)) return "LEVEL:1";
        if (value.Contains("L2", StringComparison.Ordinal)) return "LEVEL:2";

        var compactName = new string((pin.PinName ?? string.Empty)
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
        if (compactName.StartsWith("IN", StringComparison.Ordinal) ||
            compactName.StartsWith("OUT", StringComparison.Ordinal))
        {
            var channel = new string(compactName.Where(char.IsDigit).ToArray());
            return channel.Length > 0 ? $"CHANNEL:{channel}" : "COMMON";
        }
        return null;
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
