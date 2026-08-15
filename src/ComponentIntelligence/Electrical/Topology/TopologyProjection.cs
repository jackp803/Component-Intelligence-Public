using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

public sealed record TopologyNode
{
    public required string ObjectId { get; init; }
    public required string ObjectKind { get; init; }
    public required string Label { get; init; }
    public required TopologyPlacement Placement { get; init; }
    public IReadOnlySet<ElectricalLayer> Layers { get; init; } = new HashSet<ElectricalLayer>();
}

public sealed record TopologyEdge
{
    public required string ConnectionId { get; init; }
    public required string FromObjectId { get; init; }
    public required string ToObjectId { get; init; }
    public required ElectricalLayer Layer { get; init; }
    public string? NetId { get; init; }
    public string? NetLabel { get; init; }
}

public sealed record TopologyGraph
{
    public required IReadOnlyList<TopologyNode> Nodes { get; init; }
    public required IReadOnlyList<TopologyEdge> Edges { get; init; }
}

public sealed class TopologyProjection
{
    public void EnsurePlacements(ElectricalProject project, double startX = 40, double startY = 40, double gapX = 190, double gapY = 130, int columns = 5)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        var known = project.TopologyPlacements.Select(item => item.ObjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var objects = project.Components.Select(component => (Id: component.ComponentInstanceId, Kind: "COMPONENT"))
            .Concat(project.TerminalBlocks.Select(block => (Id: block.TerminalBlockId, Kind: "TERMINAL_BLOCK")))
            .Where(item => !known.Contains(item.Id))
            .ToArray();

        var index = project.TopologyPlacements.Count;
        foreach (var item in objects)
        {
            var column = index % columns;
            var row = index / columns;
            project.TopologyPlacements.Add(new TopologyPlacement
            {
                ObjectId = item.Id,
                ObjectKind = item.Kind,
                X = startX + column * gapX,
                Y = startY + row * gapY,
                Width = item.Kind == "TERMINAL_BLOCK" ? 165 : 140,
                Height = item.Kind == "TERMINAL_BLOCK" ? 88 : 76
            });
            index++;
        }
    }

    public TopologyGraph Build(ElectricalProject project, ElectricalLayer? layerFilter = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var placements = project.TopologyPlacements.ToDictionary(item => item.ObjectId, StringComparer.OrdinalIgnoreCase);
        var endpointOwners = BuildEndpointOwners(project);
        var netMap = project.Nets.ToDictionary(net => net.NetId, StringComparer.OrdinalIgnoreCase);
        var objectLayers = new Dictionary<string, HashSet<ElectricalLayer>>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<TopologyEdge>();

        foreach (var connection in project.Connections)
        {
            if (!endpointOwners.TryGetValue(connection.FromEndpointId, out var fromOwner) ||
                !endpointOwners.TryGetValue(connection.ToEndpointId, out var toOwner)) continue;
            if (string.Equals(fromOwner, toOwner, StringComparison.OrdinalIgnoreCase)) continue;

            netMap.TryGetValue(connection.NetId ?? string.Empty, out var net);
            var layer = net?.Layer ?? ResolveLayerFromEndpoints(project, connection) ?? ElectricalLayer.Unknown;
            AddLayer(objectLayers, fromOwner, layer);
            AddLayer(objectLayers, toOwner, layer);
            if (layerFilter is not null && layer != layerFilter.Value) continue;

            edges.Add(new TopologyEdge
            {
                ConnectionId = connection.ConnectionId,
                FromObjectId = fromOwner,
                ToObjectId = toOwner,
                Layer = layer,
                NetId = connection.NetId,
                NetLabel = net?.Label
            });
        }

        foreach (var component in project.Components)
        {
            foreach (var port in component.Ports)
            {
                if (!string.IsNullOrWhiteSpace(port.Protocol)) AddLayer(objectLayers, component.ComponentInstanceId, ElectricalLayer.Communication);
                foreach (var pin in port.Pins) AddLayer(objectLayers, component.ComponentInstanceId, pin.Layer);
            }
        }

        var nodes = new List<TopologyNode>();
        foreach (var component in project.Components)
        {
            if (!placements.TryGetValue(component.ComponentInstanceId, out var placement)) continue;
            var layers = GetLayers(objectLayers, component.ComponentInstanceId);
            if (!ShouldShowNode(layers, layerFilter, edges, component.ComponentInstanceId)) continue;
            nodes.Add(new TopologyNode
            {
                ObjectId = component.ComponentInstanceId,
                ObjectKind = "COMPONENT",
                Label = component.ReferenceDesignator ?? component.EquipmentTag ?? component.DisplayName ?? component.ComponentInstanceId,
                Placement = placement,
                Layers = layers
            });
        }

        foreach (var block in project.TerminalBlocks)
        {
            if (!placements.TryGetValue(block.TerminalBlockId, out var placement)) continue;
            var layers = GetLayers(objectLayers, block.TerminalBlockId);
            if (!ShouldShowNode(layers, layerFilter, edges, block.TerminalBlockId)) continue;
            nodes.Add(new TopologyNode
            {
                ObjectId = block.TerminalBlockId,
                ObjectKind = "TERMINAL_BLOCK",
                Label = string.IsNullOrWhiteSpace(block.FunctionTag) ? block.ReferenceDesignator : $"{block.ReferenceDesignator}\n{block.FunctionTag}",
                Placement = placement,
                Layers = layers
            });
        }

        var visibleIds = nodes.Select(node => node.ObjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        edges = edges.Where(edge => visibleIds.Contains(edge.FromObjectId) && visibleIds.Contains(edge.ToObjectId)).ToList();
        return new TopologyGraph { Nodes = nodes, Edges = edges };
    }

    public TopologyPlacement GetPlacement(ElectricalProject project, string objectId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        return project.TopologyPlacements.First(item => string.Equals(item.ObjectId, objectId, StringComparison.OrdinalIgnoreCase));
    }

    public void Move(ElectricalProject project, string objectId, double x, double y)
    {
        var placement = GetPlacement(project, objectId);
        placement.X = Math.Max(0, x);
        placement.Y = Math.Max(0, y);
    }

    public void Rotate(ElectricalProject project, string objectId, int deltaDegrees = 90)
    {
        var placement = GetPlacement(project, objectId);
        var normalized = (placement.RotationDegrees + deltaDegrees) % 360;
        if (normalized < 0) normalized += 360;
        placement.RotationDegrees = normalized;
    }

    private static IReadOnlyDictionary<string, string> BuildEndpointOwners(ElectricalProject project)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in project.Components)
        foreach (var port in component.Ports)
        {
            map[port.PortId] = component.ComponentInstanceId;
            foreach (var pin in port.Pins)
                map[pin.PinId] = component.ComponentInstanceId;
        }

        foreach (var block in project.TerminalBlocks)
        foreach (var point in block.Positions.SelectMany(position => position.Levels).SelectMany(level => level.ConnectionPoints))
            map[point.ConnectionPointId] = block.TerminalBlockId;
        return map;
    }

    private static ElectricalLayer? ResolveLayerFromEndpoints(ElectricalProject project, ElectricalConnection connection)
    {
        foreach (var component in project.Components)
        foreach (var port in component.Ports)
        {
            if (string.Equals(port.PortId, connection.FromEndpointId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(port.PortId, connection.ToEndpointId, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(port.Protocol)) return ElectricalLayer.Communication;
                var portLayer = port.Pins.Select(pin => pin.Layer).FirstOrDefault(layer => layer != ElectricalLayer.Unknown);
                if (portLayer != ElectricalLayer.Unknown) return portLayer;
            }

            foreach (var pin in port.Pins)
            {
                if (string.Equals(pin.PinId, connection.FromEndpointId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pin.PinId, connection.ToEndpointId, StringComparison.OrdinalIgnoreCase))
                {
                    if (pin.Layer != ElectricalLayer.Unknown) return pin.Layer;
                }
            }
        }
        return null;
    }

    private static void AddLayer(IDictionary<string, HashSet<ElectricalLayer>> map, string objectId, ElectricalLayer layer)
    {
        if (layer == ElectricalLayer.Unknown) return;
        if (!map.TryGetValue(objectId, out var layers))
        {
            layers = new HashSet<ElectricalLayer>();
            map[objectId] = layers;
        }
        layers.Add(layer);
    }

    private static IReadOnlySet<ElectricalLayer> GetLayers(IReadOnlyDictionary<string, HashSet<ElectricalLayer>> map, string objectId) =>
        map.TryGetValue(objectId, out var layers) ? layers : new HashSet<ElectricalLayer>();

    private static bool ShouldShowNode(IReadOnlySet<ElectricalLayer> layers, ElectricalLayer? filter, IReadOnlyList<TopologyEdge> edges, string objectId)
    {
        if (filter is null) return true;
        if (layers.Contains(filter.Value)) return true;
        return edges.Any(edge => string.Equals(edge.FromObjectId, objectId, StringComparison.OrdinalIgnoreCase) || string.Equals(edge.ToObjectId, objectId, StringComparison.OrdinalIgnoreCase));
    }
}
