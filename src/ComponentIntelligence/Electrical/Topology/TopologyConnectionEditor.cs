using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

public sealed record InlineConnectorOptions(
    string Family,
    string? Coding,
    int? PinCount,
    ConnectorGender SideAGender,
    ConnectorGender SideBGender,
    string? ReferenceDesignator = null);

public sealed record InlineTerminalOptions(
    string? ReferenceDesignator = null,
    string? FunctionTag = null);

public sealed record CableSegmentOptions(
    string? ReferenceDesignator = null,
    string? CableDefinitionId = null);

public sealed class TopologyConnectionEditor
{
    public ElectricalConnection ConnectPorts(
        ElectricalProject project,
        string fromPortId,
        string toPortId,
        string? netId = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromPortId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toPortId);
        if (string.Equals(fromPortId, toPortId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A port cannot be connected to itself.");

        var from = FindPort(project, fromPortId) ?? throw new InvalidOperationException($"Unknown port '{fromPortId}'.");
        var to = FindPort(project, toPortId) ?? throw new InvalidOperationException($"Unknown port '{toPortId}'.");

        if (project.Connections.Any(connection =>
                (string.Equals(connection.FromEndpointId, fromPortId, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(connection.ToEndpointId, toPortId, StringComparison.OrdinalIgnoreCase)) ||
                (string.Equals(connection.FromEndpointId, toPortId, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(connection.ToEndpointId, fromPortId, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("These ports are already connected.");

        EnsureConnectionCapacity(project, from);
        EnsureConnectionCapacity(project, to);

        var connection = new ElectricalConnection
        {
            ConnectionId = $"conn-{Guid.NewGuid():N}",
            FromEndpointId = fromPortId,
            ToEndpointId = toPortId,
            NetId = netId,
            Kind = ConnectionKind.Cable
        };
        project.Connections.Add(connection);
        return connection;
    }

    public ComponentInstance InsertInlineConnector(
        ElectricalProject project,
        string connectionId,
        InlineConnectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);
        var connection = FindConnection(project, connectionId);
        if (string.IsNullOrWhiteSpace(options.Family))
            throw new InvalidOperationException("Connector family is required.");

        var componentId = $"cmp-inline-{Guid.NewGuid():N}";
        var reference = string.IsNullOrWhiteSpace(options.ReferenceDesignator)
            ? NextReference(project, "X")
            : options.ReferenceDesignator.Trim();

        var sideA = BuildConnectorPort(componentId, "A", options.Family, options.Coding, options.PinCount, options.SideAGender);
        var sideB = BuildConnectorPort(componentId, "B", options.Family, options.Coding, options.PinCount, options.SideBGender);
        var component = new ComponentInstance
        {
            ComponentInstanceId = componentId,
            ComponentDefinitionId = $"inline-connector:{options.Family}",
            TypeKey = "INLINE_CONNECTOR",
            ReferenceDesignator = reference,
            ReferenceSource = ReferenceSource.Manual,
            ReferenceLocked = true,
            DisplayName = $"{options.Family} inline connector",
            ResponsibilityScope = ResponsibilityScope.InScope,
            Ports = { sideA, sideB }
        };

        project.Components.Add(component);
        InsertPlacementAtConnectionMidpoint(project, connection, componentId, "COMPONENT", 130, 68);
        ReplaceWithTwoSegments(project, connection, sideA.PortId, sideB.PortId);
        return component;
    }

    public TerminalBlock InsertInlineTerminal(
        ElectricalProject project,
        string connectionId,
        InlineTerminalOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);
        var connection = FindConnection(project, connectionId);
        var reference = string.IsNullOrWhiteSpace(options.ReferenceDesignator)
            ? NextTerminalReference(project)
            : options.ReferenceDesignator.Trim();
        var blockId = $"tb-inline-{Guid.NewGuid():N}";
        var positionId = $"{blockId}:pos:1";
        var pointA = $"{positionId}:A";
        var pointB = $"{positionId}:B";

        var block = new TerminalBlock
        {
            TerminalBlockId = blockId,
            ReferenceDesignator = reference,
            FunctionTag = string.IsNullOrWhiteSpace(options.FunctionTag) ? "INLINE" : options.FunctionTag.Trim(),
            DisplayName = "Inline terminal",
            Positions =
            {
                new TerminalPosition
                {
                    TerminalPositionId = positionId,
                    PositionLabel = $"{reference}:1",
                    TerminalType = "FEED_THROUGH",
                    Levels =
                    {
                        new TerminalLevel
                        {
                            LevelId = $"{positionId}:L1",
                            LevelName = "L1",
                            ConnectionPoints =
                            {
                                new TerminalConnectionPoint { ConnectionPointId = pointA, Type = ConnectionPointType.ConductorEntry, PhysicalSide = "A", MaxConductors = 1 },
                                new TerminalConnectionPoint { ConnectionPointId = pointB, Type = ConnectionPointType.ConductorEntry, PhysicalSide = "B", MaxConductors = 1 }
                            },
                            InternalConnections =
                            {
                                new InternalTerminalConnection { FromConnectionPointId = pointA, ToConnectionPointId = pointB }
                            }
                        }
                    }
                }
            }
        };

        project.TerminalBlocks.Add(block);
        InsertPlacementAtConnectionMidpoint(project, connection, blockId, "TERMINAL_BLOCK", 145, 72);
        ReplaceWithTwoSegments(project, connection, pointA, pointB);
        return block;
    }

    public CableInstance AssignCableSegment(
        ElectricalProject project,
        string connectionId,
        CableSegmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);
        var connection = FindConnection(project, connectionId);

        var cable = new CableInstance
        {
            CableInstanceId = $"cbl-{Guid.NewGuid():N}",
            CableDefinitionId = string.IsNullOrWhiteSpace(options.CableDefinitionId) ? "UNRESOLVED-CABLE" : options.CableDefinitionId.Trim(),
            ReferenceDesignator = string.IsNullOrWhiteSpace(options.ReferenceDesignator) ? NextCableReference(project) : options.ReferenceDesignator.Trim()
        };
        project.Cables.Add(cable);
        connection.CableInstanceId = cable.CableInstanceId;
        connection.Kind = ConnectionKind.Cable;
        return cable;
    }

    public void DeleteConnection(ElectricalProject project, string connectionId)
    {
        ArgumentNullException.ThrowIfNull(project);
        var connection = FindConnection(project, connectionId);
        project.Connections.Remove(connection);
    }

    private static ComponentPort BuildConnectorPort(
        string componentId,
        string side,
        string family,
        string? coding,
        int? pinCount,
        ConnectorGender gender)
    {
        var port = new ComponentPort
        {
            PortId = $"{componentId}:PORT:{side}",
            Name = side,
            MaxConnections = 1,
            Connector = new ConnectorDefinition
            {
                ConnectorId = $"{componentId}:CONN:{side}",
                Family = family.Trim(),
                Coding = string.IsNullOrWhiteSpace(coding) ? null : coding.Trim(),
                PinCount = pinCount,
                Gender = gender,
                MountType = ConnectorMountType.Cable
            }
        };

        if (pinCount is > 0 and <= 64)
        {
            for (var index = 1; index <= pinCount.Value; index++)
            {
                port.Pins.Add(new ComponentPin
                {
                    PinId = $"{port.PortId}:PIN:{index}",
                    PinNumber = index.ToString(),
                    Status = PinStatus.Unknown,
                    Layer = ElectricalLayer.Unknown
                });
            }
        }
        return port;
    }

    private static void ReplaceWithTwoSegments(
        ElectricalProject project,
        ElectricalConnection original,
        string firstInsertedEndpointId,
        string secondInsertedEndpointId)
    {
        project.Connections.Remove(original);
        project.Connections.Add(CloneSegment(original, original.FromEndpointId, firstInsertedEndpointId));
        project.Connections.Add(CloneSegment(original, secondInsertedEndpointId, original.ToEndpointId));
    }

    private static ElectricalConnection CloneSegment(ElectricalConnection source, string from, string to) => new()
    {
        ConnectionId = $"conn-{Guid.NewGuid():N}",
        FromEndpointId = from,
        ToEndpointId = to,
        NetId = source.NetId,
        Kind = source.Kind,
        CableInstanceId = source.CableInstanceId,
        CableCoreId = source.CableCoreId,
        ConductorAreaMm2 = source.ConductorAreaMm2
    };

    private static void InsertPlacementAtConnectionMidpoint(
        ElectricalProject project,
        ElectricalConnection connection,
        string objectId,
        string objectKind,
        double width,
        double height)
    {
        var fromOwner = FindEndpointOwner(project, connection.FromEndpointId);
        var toOwner = FindEndpointOwner(project, connection.ToEndpointId);
        var from = project.TopologyPlacements.FirstOrDefault(item => string.Equals(item.ObjectId, fromOwner, StringComparison.OrdinalIgnoreCase));
        var to = project.TopologyPlacements.FirstOrDefault(item => string.Equals(item.ObjectId, toOwner, StringComparison.OrdinalIgnoreCase));

        var x = 120d;
        var y = 120d;
        if (from is not null && to is not null)
        {
            x = ((from.X + from.Width / 2) + (to.X + to.Width / 2)) / 2 - width / 2;
            y = ((from.Y + from.Height / 2) + (to.Y + to.Height / 2)) / 2 - height / 2;
        }

        project.TopologyPlacements.Add(new TopologyPlacement
        {
            ObjectId = objectId,
            ObjectKind = objectKind,
            X = Math.Max(0, x),
            Y = Math.Max(0, y),
            Width = width,
            Height = height
        });
    }

    private static string? FindEndpointOwner(ElectricalProject project, string endpointId)
    {
        foreach (var component in project.Components)
        {
            foreach (var port in component.Ports)
            {
                if (string.Equals(port.PortId, endpointId, StringComparison.OrdinalIgnoreCase)) return component.ComponentInstanceId;
                if (port.Pins.Any(pin => string.Equals(pin.PinId, endpointId, StringComparison.OrdinalIgnoreCase))) return component.ComponentInstanceId;
            }
        }
        foreach (var block in project.TerminalBlocks)
        foreach (var point in block.Positions.SelectMany(position => position.Levels).SelectMany(level => level.ConnectionPoints))
            if (string.Equals(point.ConnectionPointId, endpointId, StringComparison.OrdinalIgnoreCase)) return block.TerminalBlockId;
        return null;
    }

    private static ComponentPort? FindPort(ElectricalProject project, string portId) =>
        project.Components.SelectMany(component => component.Ports)
            .FirstOrDefault(port => string.Equals(port.PortId, portId, StringComparison.OrdinalIgnoreCase));

    private static ElectricalConnection FindConnection(ElectricalProject project, string connectionId) =>
        project.Connections.FirstOrDefault(item => string.Equals(item.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Connection '{connectionId}' does not exist.");

    private static void EnsureConnectionCapacity(ElectricalProject project, ComponentPort port)
    {
        if (port.MaxConnections is not > 0) return;
        var used = project.Connections.Count(connection =>
            string.Equals(connection.FromEndpointId, port.PortId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(connection.ToEndpointId, port.PortId, StringComparison.OrdinalIgnoreCase));
        if (used >= port.MaxConnections.Value)
            throw new InvalidOperationException($"Port '{port.Name}' already reached its maximum connection count ({port.MaxConnections}).");
    }

    private static string NextReference(ElectricalProject project, string prefix)
    {
        var used = project.Components.Select(component => component.ReferenceDesignator)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < 10000; index++)
        {
            var candidate = $"{prefix}{index}";
            if (!used.Contains(candidate)) return candidate;
        }
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private static string NextTerminalReference(ElectricalProject project)
    {
        var used = project.TerminalBlocks.Select(block => block.ReferenceDesignator).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < 10000; index++)
        {
            var candidate = $"TB{index}";
            if (!used.Contains(candidate)) return candidate;
        }
        return $"TB-{Guid.NewGuid():N}";
    }

    private static string NextCableReference(ElectricalProject project)
    {
        var used = project.Cables.Select(cable => cable.ReferenceDesignator).Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < 10000; index++)
        {
            var candidate = $"CBL-{index:000}";
            if (!used.Contains(candidate)) return candidate;
        }
        return $"CBL-{Guid.NewGuid():N}";
    }
}
