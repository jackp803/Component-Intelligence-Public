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
    string? CableDefinitionId = null,
    string? DisplayName = null);

public sealed record CustomCableAssemblyOptions(
    string? ReferenceDesignator,
    string? DisplayName,
    double? TrunkLengthMm,
    double? BranchALengthMm = null,
    double? BranchBLengthMm = null);

public sealed record CustomCableAssemblyResult(
    CableAssembly Assembly,
    IReadOnlyList<CableInstance> CableMembers);

public sealed record InlineMatedConnectorPair(
    ComponentInstance FemaleAdapter,
    ComponentInstance MaleAdapter,
    ElectricalConnection FemaleToMaleMating);

public sealed record MultiCoreCableBundle(
    ComponentInstance FemaleAdapter,
    ComponentInstance MaleAdapter,
    CableInstance Cable,
    CableAssembly Assembly,
    IReadOnlyList<ElectricalConnection> CableCoreConnections);

public sealed class TopologyConnectionEditor
{
    /// <summary>
    /// Groups one ordinary route, or exactly three routes for a Y harness, into one project-local
    /// fabricated cable assembly. No imported BOM product is required.
    /// </summary>
    public CustomCableAssemblyResult CreateCustomCableAssembly(
        ElectricalProject project,
        IReadOnlyCollection<string> connectionIds,
        bool isYHarness,
        CustomCableAssemblyOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(connectionIds);
        ArgumentNullException.ThrowIfNull(options);

        var requested = connectionIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var expectedCount = isYHarness ? 3 : 1;
        if (requested.Length != expectedCount)
            throw new InvalidOperationException(isYHarness
                ? "A custom Y harness requires exactly three selected wire segments."
                : "A custom two-end harness requires exactly one selected wire segment.");

        var connections = requested.Select(id => FindConnection(project, id)).ToArray();
        var existingAssemblyCableIds = project.CableAssemblies
            .SelectMany(assembly => assembly.Members)
            .Select(member => member.CableInstanceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (connections.Any(connection => !string.IsNullOrWhiteSpace(connection.CableInstanceId) &&
                                          existingAssemblyCableIds.Contains(connection.CableInstanceId!)))
            throw new InvalidOperationException("At least one selected segment already belongs to another cable assembly.");

        var reference = string.IsNullOrWhiteSpace(options.ReferenceDesignator)
            ? NextCableReference(project)
            : options.ReferenceDesignator.Trim();
        if (project.CableAssemblies.Any(assembly =>
                string.Equals(assembly.ReferenceDesignator, reference, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Cable assembly reference '{reference}' is already in use.");

        var displayName = string.IsNullOrWhiteSpace(options.DisplayName)
            ? isYHarness ? $"Custom Y harness {reference}" : $"Custom cable {reference}"
            : options.DisplayName.Trim();
        var roles = isYHarness ? new[] { "TRUNK", "BRANCH-A", "BRANCH-B" } : new[] { "MAIN" };
        var lengths = isYHarness
            ? new[] { options.TrunkLengthMm, options.BranchALengthMm, options.BranchBLengthMm }
            : new[] { options.TrunkLengthMm };
        if (lengths.Any(length => length is <= 0))
            throw new InvalidOperationException("Cable length must be greater than zero when supplied.");

        var cableMembers = new List<CableInstance>(connections.Length);
        for (var index = 0; index < connections.Length; index++)
        {
            var connection = connections[index];
            var role = roles[index];
            var cable = string.IsNullOrWhiteSpace(connection.CableInstanceId)
                ? null
                : project.Cables.FirstOrDefault(item =>
                    string.Equals(item.CableInstanceId, connection.CableInstanceId, StringComparison.OrdinalIgnoreCase));
            if (cable is null)
            {
                cable = new CableInstance
                {
                    CableInstanceId = $"cbl-{Guid.NewGuid():N}",
                    CableDefinitionId = "UNRESOLVED-CABLE"
                };
                project.Cables.Add(cable);
                connection.CableInstanceId = cable.CableInstanceId;
            }

            cable.ReferenceDesignator = isYHarness ? $"{reference}-{role}" : reference;
            cable.DisplayName = isYHarness ? $"{displayName} / {role}" : displayName;
            cable.ProvidedLengthMm = lengths[index];
            cable.LengthSource = lengths[index] is null ? CableLengthSource.Unknown : CableLengthSource.User;
            connection.Kind = ConnectionKind.Cable;

            if (cable.CoreAssignments.Count == 0)
            {
                var net = string.IsNullOrWhiteSpace(connection.NetId)
                    ? null
                    : project.Nets.FirstOrDefault(item =>
                        string.Equals(item.NetId, connection.NetId, StringComparison.OrdinalIgnoreCase));
                cable.CoreAssignments.Add(new CoreAssignment
                {
                    CoreId = "1",
                    NetId = connection.NetId,
                    Signal = net?.Label,
                    Layer = net?.Layer ?? ElectricalLayer.Unknown,
                    Status = "ASSIGNED",
                    FromEndpointId = connection.FromEndpointId,
                    ToEndpointId = connection.ToEndpointId
                });
                connection.CableCoreId ??= "1";
            }
            cableMembers.Add(cable);
        }

        var assembly = new CableAssembly
        {
            CableAssemblyId = $"ca-{Guid.NewGuid():N}",
            ReferenceDesignator = reference,
            IsCustom = true
        };
        for (var index = 0; index < cableMembers.Count; index++)
        {
            assembly.Members.Add(new CableAssemblyMember
            {
                CableInstanceId = cableMembers[index].CableInstanceId,
                Purpose = roles[index]
            });
        }
        project.CableAssemblies.Add(assembly);
        return new CustomCableAssemblyResult(assembly, cableMembers);
    }

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

    /// <summary>
    /// Expands one loose-wire connection into the explicit physical chain:
    /// source wire -> M12 female field connector <-> M12 male field connector -> destination wire.
    /// The two outer wire segments remain independently editable for cable selection and Pin Mapping;
    /// the center segment is a formal DirectMating relationship validated by connector family/coding/gender.
    /// </summary>
    public InlineMatedConnectorPair InsertLooseWireMatedConnectorPair(
        ElectricalProject project,
        string connectionId,
        InlineConnectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);
        var original = FindConnection(project, connectionId);
        var family = string.IsNullOrWhiteSpace(options.Family) ? "M12" : options.Family.Trim();
        var baseReference = string.IsNullOrWhiteSpace(options.ReferenceDesignator)
            ? NextReference(project, "X")
            : options.ReferenceDesignator.Trim();

        var femaleId = $"cmp-inline-{Guid.NewGuid():N}";
        var femaleWire = BuildLooseWirePort(femaleId, "WIRE-A", options.PinCount, topologyInput: true);
        var femaleMating = BuildConnectorPort(femaleId, "M12-F", family, options.Coding, options.PinCount, ConnectorGender.Female);
        femaleMating.Capabilities.Add("ROLE:Mating Output");
        var female = BuildFieldConnectorAdapter(
            femaleId,
            $"{baseReference}-F",
            $"{family} female field connector (loose wire)",
            femaleWire,
            femaleMating);

        var maleId = $"cmp-inline-{Guid.NewGuid():N}";
        var maleMating = BuildConnectorPort(maleId, "M12-M", family, options.Coding, options.PinCount, ConnectorGender.Male);
        maleMating.Capabilities.Add("ROLE:Mating Input");
        var maleWire = BuildLooseWirePort(maleId, "WIRE-B", options.PinCount, topologyInput: false);
        var male = BuildFieldConnectorAdapter(
            maleId,
            $"{baseReference}-M",
            $"{family} male field connector (loose wire)",
            maleMating,
            maleWire);

        project.Components.Add(female);
        project.Components.Add(male);
        InsertPairPlacementsAtConnection(project, original, femaleId, maleId);

        project.Connections.Remove(original);
        project.Connections.Add(CloneSegment(original, original.FromEndpointId, femaleWire.PortId));
        var mating = new ElectricalConnection
        {
            ConnectionId = $"conn-{Guid.NewGuid():N}",
            FromEndpointId = femaleMating.PortId,
            ToEndpointId = maleMating.PortId,
            NetId = original.NetId,
            Kind = ConnectionKind.DirectMating
        };
        project.Connections.Add(mating);
        project.Connections.Add(CloneSegment(original, maleWire.PortId, original.ToEndpointId));
        return new InlineMatedConnectorPair(female, male, mating);
    }

    /// <summary>
    /// Replaces two or more selected loose-wire connections with one explicit multi-core cable:
    /// original A endpoints -> M12 female pins -> cable cores -> M12 male pins -> original B endpoints.
    /// Every selected circuit keeps its own net while the physical cable and its two connectors are
    /// represented once. Core assignment is deterministic in the caller-provided connection order.
    /// </summary>
    public MultiCoreCableBundle BundleLooseWireConnections(
        ElectricalProject project,
        IReadOnlyCollection<string> connectionIds,
        InlineConnectorOptions connectorOptions,
        CableSegmentOptions cableOptions)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(connectionIds);
        ArgumentNullException.ThrowIfNull(connectorOptions);
        ArgumentNullException.ThrowIfNull(cableOptions);

        var requested = connectionIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requested.Length < 2)
            throw new InvalidOperationException("Select at least two loose-wire connections to create a multi-core cable.");

        var originals = requested.Select(id => FindConnection(project, id)).ToArray();
        var pinCount = connectorOptions.PinCount ?? originals.Length;
        if (pinCount < originals.Length)
            throw new InvalidOperationException($"Connector Pin Count ({pinCount}) must be at least the selected connection count ({originals.Length}).");
        if (pinCount is < 1 or > 64)
            throw new InvalidOperationException("Connector Pin Count must be between 1 and 64.");

        var family = string.IsNullOrWhiteSpace(connectorOptions.Family) ? "M12" : connectorOptions.Family.Trim();
        var baseReference = string.IsNullOrWhiteSpace(connectorOptions.ReferenceDesignator)
            ? NextReference(project, "X")
            : connectorOptions.ReferenceDesignator.Trim();

        var femaleId = $"cmp-inline-{Guid.NewGuid():N}";
        var femaleWire = BuildLooseWirePort(femaleId, "WIRE-A", pinCount, topologyInput: true);
        var femaleMating = BuildConnectorPort(femaleId, "M12-F", family, connectorOptions.Coding, pinCount, ConnectorGender.Female);
        femaleMating.Capabilities.Add("ROLE:Mating Output");
        femaleMating.Capabilities.Add("ROLE:Cable End A");
        var female = BuildFieldConnectorAdapter(
            femaleId,
            $"{baseReference}-F",
            $"{family} female cable end ({originals.Length} cores)",
            femaleWire,
            femaleMating);

        var maleId = $"cmp-inline-{Guid.NewGuid():N}";
        var maleMating = BuildConnectorPort(maleId, "M12-M", family, connectorOptions.Coding, pinCount, ConnectorGender.Male);
        maleMating.Capabilities.Add("ROLE:Mating Input");
        maleMating.Capabilities.Add("ROLE:Cable End B");
        var maleWire = BuildLooseWirePort(maleId, "WIRE-B", pinCount, topologyInput: false);
        var male = BuildFieldConnectorAdapter(
            maleId,
            $"{baseReference}-M",
            $"{family} male cable end ({originals.Length} cores)",
            maleMating,
            maleWire);

        project.Components.Add(female);
        project.Components.Add(male);
        InsertPairPlacementsAtConnection(project, originals[0], femaleId, maleId);

        var oldCableIds = originals
            .Select(connection => connection.CableInstanceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        project.Connections.RemoveAll(connection => requested.Contains(connection.ConnectionId, StringComparer.OrdinalIgnoreCase));
        project.TopologyRoutes.RemoveAll(route => requested.Contains(route.ConnectionId, StringComparer.OrdinalIgnoreCase));

        var definitionId = string.IsNullOrWhiteSpace(cableOptions.CableDefinitionId)
            ? "UNRESOLVED-CABLE"
            : cableOptions.CableDefinitionId.Trim();
        var cableReference = string.IsNullOrWhiteSpace(cableOptions.ReferenceDesignator)
            ? NextCableReference(project)
            : cableOptions.ReferenceDesignator.Trim();
        var cable = new CableInstance
        {
            CableInstanceId = $"cbl-{Guid.NewGuid():N}",
            CableDefinitionId = definitionId,
            DisplayName = string.IsNullOrWhiteSpace(cableOptions.DisplayName) ? null : cableOptions.DisplayName.Trim(),
            ReferenceDesignator = cableReference
        };
        project.Cables.Add(cable);

        var coreConnections = new List<ElectricalConnection>(originals.Length);
        for (var index = 0; index < originals.Length; index++)
        {
            var original = originals[index];
            var coreId = (index + 1).ToString();
            var femaleWirePin = femaleWire.Pins[index];
            var femaleMatingPin = femaleMating.Pins[index];
            var maleMatingPin = maleMating.Pins[index];
            var maleWirePin = maleWire.Pins[index];
            var net = string.IsNullOrWhiteSpace(original.NetId)
                ? null
                : project.Nets.FirstOrDefault(item => string.Equals(item.NetId, original.NetId, StringComparison.OrdinalIgnoreCase));

            project.Connections.Add(CloneLooseWireSegment(original, original.FromEndpointId, femaleWirePin.PinId));
            var coreConnection = new ElectricalConnection
            {
                ConnectionId = $"conn-{Guid.NewGuid():N}",
                FromEndpointId = femaleMatingPin.PinId,
                ToEndpointId = maleMatingPin.PinId,
                NetId = original.NetId,
                Kind = ConnectionKind.Cable,
                CableInstanceId = cable.CableInstanceId,
                CableCoreId = coreId,
                ConductorAreaMm2 = original.ConductorAreaMm2,
                MaxVoltageDropPercent = original.MaxVoltageDropPercent,
                ConductorMaterial = original.ConductorMaterial,
                InstallationMethod = original.InstallationMethod
            };
            project.Connections.Add(coreConnection);
            coreConnections.Add(coreConnection);
            project.Connections.Add(CloneLooseWireSegment(original, maleWirePin.PinId, original.ToEndpointId));

            cable.CoreAssignments.Add(new CoreAssignment
            {
                CoreId = coreId,
                NetId = original.NetId,
                Signal = net?.Label,
                Layer = net?.Layer ?? ElectricalLayer.Unknown,
                Status = "ASSIGNED",
                FromEndpointId = femaleMatingPin.PinId,
                ToEndpointId = maleMatingPin.PinId
            });
        }

        var assembly = new CableAssembly
        {
            CableAssemblyId = $"ca-{Guid.NewGuid():N}",
            ReferenceDesignator = cableReference,
            IsCustom = string.Equals(definitionId, "UNRESOLVED-CABLE", StringComparison.OrdinalIgnoreCase),
            EndAConnectorId = femaleMating.Connector!.ConnectorId,
            EndBConnectorId = maleMating.Connector!.ConnectorId,
            Members =
            {
                new CableAssemblyMember
                {
                    CableInstanceId = cable.CableInstanceId,
                    Purpose = $"{family} female-to-male multi-core cable"
                }
            }
        };
        project.CableAssemblies.Add(assembly);
        project.Cables.RemoveAll(existing =>
            oldCableIds.Contains(existing.CableInstanceId) &&
            project.Connections.All(connection =>
                !string.Equals(connection.CableInstanceId, existing.CableInstanceId, StringComparison.OrdinalIgnoreCase)) &&
            project.CableAssemblies.SelectMany(item => item.Members).All(member =>
                !string.Equals(member.CableInstanceId, existing.CableInstanceId, StringComparison.OrdinalIgnoreCase)));

        return new MultiCoreCableBundle(female, male, cable, assembly, coreConnections);
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
        var definitionId = string.IsNullOrWhiteSpace(options.CableDefinitionId)
            ? "UNRESOLVED-CABLE"
            : options.CableDefinitionId.Trim();
        var displayName = string.IsNullOrWhiteSpace(options.DisplayName) ? null : options.DisplayName.Trim();

        var existing = string.IsNullOrWhiteSpace(connection.CableInstanceId)
            ? null
            : project.Cables.FirstOrDefault(item =>
                string.Equals(item.CableInstanceId, connection.CableInstanceId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.CableDefinitionId = definitionId;
            existing.DisplayName = displayName;
            if (!string.IsNullOrWhiteSpace(options.ReferenceDesignator))
                existing.ReferenceDesignator = options.ReferenceDesignator.Trim();
            else if (string.IsNullOrWhiteSpace(existing.ReferenceDesignator))
                existing.ReferenceDesignator = NextCableReference(project);
            connection.Kind = ConnectionKind.Cable;
            return existing;
        }

        var cable = new CableInstance
        {
            CableInstanceId = $"cbl-{Guid.NewGuid():N}",
            CableDefinitionId = definitionId,
            DisplayName = displayName,
            ReferenceDesignator = string.IsNullOrWhiteSpace(options.ReferenceDesignator) ? NextCableReference(project) : options.ReferenceDesignator.Trim()
        };
        project.Cables.Add(cable);
        connection.CableInstanceId = cable.CableInstanceId;
        connection.Kind = ConnectionKind.Cable;
        return cable;
    }

    public void DeleteConnection(ElectricalProject project, string connectionId)
    {
        DeleteConnections(project, [connectionId]);
    }

    public int DeleteConnections(ElectricalProject project, IEnumerable<string> connectionIds)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(connectionIds);
        var requested = connectionIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requested.Count == 0) return 0;

        var removed = project.Connections.Where(connection => requested.Contains(connection.ConnectionId)).ToArray();
        if (removed.Length == 0) return 0;
        project.Connections.RemoveAll(connection => requested.Contains(connection.ConnectionId));
        project.TopologyRoutes.RemoveAll(route => requested.Contains(route.ConnectionId));

        var removedCableIds = removed
            .Select(connection => connection.CableInstanceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var affectedAssemblyIds = project.CableAssemblies
            .Where(assembly => assembly.Members.Any(member => removedCableIds.Contains(member.CableInstanceId)))
            .Select(assembly => assembly.CableAssemblyId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        project.CableAssemblies.RemoveAll(assembly => affectedAssemblyIds.Contains(assembly.CableAssemblyId));
        project.Cables.RemoveAll(cable =>
            removedCableIds.Contains(cable.CableInstanceId) &&
            project.Connections.All(connection =>
                !string.Equals(connection.CableInstanceId, cable.CableInstanceId, StringComparison.OrdinalIgnoreCase)));
        return removed.Length;
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

    private static ComponentPort BuildLooseWirePort(
        string componentId,
        string name,
        int? pinCount,
        bool topologyInput)
    {
        var port = new ComponentPort
        {
            PortId = $"{componentId}:PORT:{name}",
            Name = name,
            MaxConnections = 1
        };
        port.Capabilities.Add(topologyInput ? "ROLE:Loose Wire Input" : "ROLE:Loose Wire Output");
        port.Capabilities.Add("ALLOW_MANUAL_BRANCHING");
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

    private static ComponentInstance BuildFieldConnectorAdapter(
        string componentId,
        string reference,
        string displayName,
        ComponentPort first,
        ComponentPort second) => new()
    {
        ComponentInstanceId = componentId,
        ComponentDefinitionId = $"inline-mated-adapter:{displayName}",
        TypeKey = "INLINE_CONNECTOR",
        ReferenceDesignator = reference,
        ReferenceSource = ReferenceSource.Manual,
        ReferenceLocked = true,
        DisplayName = displayName,
        ResponsibilityScope = ResponsibilityScope.InScope,
        Ports = { first, second }
    };

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

    private static ElectricalConnection CloneLooseWireSegment(ElectricalConnection source, string from, string to) => new()
    {
        ConnectionId = $"conn-{Guid.NewGuid():N}",
        FromEndpointId = from,
        ToEndpointId = to,
        NetId = source.NetId,
        Kind = ConnectionKind.Wire,
        ConductorAreaMm2 = source.ConductorAreaMm2,
        MaxVoltageDropPercent = source.MaxVoltageDropPercent,
        ConductorMaterial = source.ConductorMaterial,
        InstallationMethod = source.InstallationMethod
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

    private static void InsertPairPlacementsAtConnection(
        ElectricalProject project,
        ElectricalConnection connection,
        string femaleId,
        string maleId)
    {
        const double width = 88d;
        const double height = 52d;
        const double gap = 52d;
        var fromOwner = FindEndpointOwner(project, connection.FromEndpointId);
        var toOwner = FindEndpointOwner(project, connection.ToEndpointId);
        var from = project.TopologyPlacements.FirstOrDefault(item => string.Equals(item.ObjectId, fromOwner, StringComparison.OrdinalIgnoreCase));
        var to = project.TopologyPlacements.FirstOrDefault(item => string.Equals(item.ObjectId, toOwner, StringComparison.OrdinalIgnoreCase));
        var centerX = 360d;
        var centerY = 220d;
        if (from is not null && to is not null)
        {
            centerX = ((from.X + from.Width / 2d) + (to.X + to.Width / 2d)) / 2d;
            centerY = ((from.Y + from.Height / 2d) + (to.Y + to.Height / 2d)) / 2d;
        }

        project.TopologyPlacements.Add(new TopologyPlacement
        {
            ObjectId = femaleId,
            ObjectKind = "COMPONENT",
            X = Math.Max(0d, centerX - gap / 2d - width),
            Y = Math.Max(0d, centerY - height / 2d),
            Width = width,
            Height = height
        });
        project.TopologyPlacements.Add(new TopologyPlacement
        {
            ObjectId = maleId,
            ObjectKind = "COMPONENT",
            X = Math.Max(0d, centerX + gap / 2d),
            Y = Math.Max(0d, centerY - height / 2d),
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
