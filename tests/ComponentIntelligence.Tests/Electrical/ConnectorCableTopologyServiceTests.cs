using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ConnectorCableTopologyServiceTests
{
    [Theory]
    [InlineData(CommonConnectorCatalog.Rj45Male8PinCableEndId, "RJ45", ConnectorGender.Male, 8)]
    [InlineData(CommonConnectorCatalog.Rj45Female8PinCableEndId, "RJ45", ConnectorGender.Female, 8)]
    public void CommonRj45CableEndCatalog_CreatesMatingFaceAndExpandableCableSide(
        string definitionId,
        string family,
        ConnectorGender gender,
        int pinCount)
    {
        var component = CommonConnectorCatalog.Create(definitionId, "X1");
        Assert.Equal(2, component.Ports.Count);
        var port = Assert.Single(component.Ports, candidate => candidate.Connector is not null);
        var cable = Assert.Single(component.Ports, candidate => candidate.Connector is null);

        Assert.Equal(family, port.Connector!.Family);
        Assert.Equal(gender, port.Connector.Gender);
        Assert.Empty(port.Pins);
        Assert.Equal("CABLE", cable.Name);
        Assert.Equal(TopologyEndpointDisplayMode.Connector, TopologyEndpointPolicy.DetermineDisplayMode(cable));
        Assert.Equal(pinCount, cable.Pins.Count);
        Assert.Contains("ALLOW_MANUAL_BRANCHING", cable.Capabilities);
        Assert.Equal(pinCount, cable.Pins.Select(pin => pin.PinNumber).Distinct().Count());
    }

    [Theory]
    [InlineData(CommonConnectorCatalog.M12MaleACode4PinCableEndId, ConnectorGender.Male, "WIRE-B", "ROLE:Mating Input")]
    [InlineData(CommonConnectorCatalog.M12FemaleACode4PinCableEndId, ConnectorGender.Female, "WIRE-A", "ROLE:Mating Output")]
    public void CommonM12Button_CreatesOldStyleLooseWirePinsOppositeMatingFace(
        string definitionId,
        ConnectorGender gender,
        string wirePortName,
        string matingRole)
    {
        var component = CommonConnectorCatalog.Create(definitionId, "X1");

        Assert.Equal(2, component.Ports.Count);
        var mating = Assert.Single(component.Ports, port => port.Connector is not null);
        var wire = Assert.Single(component.Ports, port => port.Connector is null);
        Assert.Equal("M12", mating.Connector!.Family);
        Assert.Equal("A", mating.Connector.Coding);
        Assert.Equal(gender, mating.Connector.Gender);
        Assert.Equal("M12-A-4", mating.Connector.CompatibilityClass);
        Assert.Contains(matingRole, mating.Capabilities);
        Assert.Equal(wirePortName, wire.Name);
        Assert.Equal(4, wire.Pins.Count);
        Assert.Contains("ALLOW_MANUAL_BRANCHING", wire.Capabilities);
    }

    [Fact]
    public void CommonM12MaleAndFemaleMatingPorts_CreateDirectMating()
    {
        var project = new ElectricalProject { ProjectId = "common-m12-pair" };
        var male = CommonConnectorCatalog.Create(CommonConnectorCatalog.M12MaleACode4PinCableEndId, "X1-M");
        var female = CommonConnectorCatalog.Create(CommonConnectorCatalog.M12FemaleACode4PinCableEndId, "X1-F");
        project.Components.Add(male);
        project.Components.Add(female);
        var malePort = Assert.Single(male.Ports, port => port.Connector is not null);
        var femalePort = Assert.Single(female.Ports, port => port.Connector is not null);

        var connection = new TopologyEndpointConnectionService().ConnectEndpoints(
            project,
            femalePort.PortId,
            malePort.PortId);

        Assert.Equal(ConnectionKind.DirectMating, connection.Kind);
    }

    [Fact]
    public void SavedOnePortM12_IsUpgradedWithoutChangingExistingWireEndpointIds()
    {
        var project = new ElectricalProject { ProjectId = "legacy-m12" };
        var component = new ComponentInstance
        {
            ComponentInstanceId = "legacy-x1",
            ComponentDefinitionId = CommonConnectorCatalog.M12FemaleACode4PinCableEndId,
            TypeKey = "INLINE_CONNECTOR",
            ReferenceDesignator = "X1-F"
        };
        var oldPort = new ComponentPort
        {
            PortId = "legacy-x1:port:m12",
            Name = "M12-F",
            Connector = new ConnectorDefinition
            {
                ConnectorId = "legacy-x1:connector:m12",
                Family = "M12",
                Coding = "A",
                PinCount = 4,
                Gender = ConnectorGender.Female
            }
        };
        oldPort.Pins.Add(new ComponentPin { PinId = "legacy-x1:pin:1", PinNumber = "1" });
        component.Ports.Add(oldPort);
        project.Components.Add(component);
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "old-wire",
            FromEndpointId = "source:pin",
            ToEndpointId = "legacy-x1:pin:1",
            Kind = ConnectionKind.Wire
        });

        Assert.Equal(1, CommonConnectorCatalog.UpgradeLegacyM12CableEnds(project));

        Assert.Equal(2, component.Ports.Count);
        var mating = Assert.Single(component.Ports, port => port.Connector is not null);
        var wire = Assert.Single(component.Ports, port => port.Connector is null);
        Assert.Equal("legacy-x1:port:m12", mating.PortId);
        Assert.Empty(mating.Pins);
        Assert.Equal("legacy-x1:pin:1", Assert.Single(wire.Pins).PinId);
        Assert.Equal("legacy-x1:pin:1", project.Connections[0].ToEndpointId);
    }

    [Fact]
    public void SavedOnePortRj45_GainsCableEndpointWithoutChangingPinIds()
    {
        var project = new ElectricalProject { ProjectId = "legacy-rj45" };
        var component = new ComponentInstance
        {
            ComponentInstanceId = "legacy-rj45-x1",
            ComponentDefinitionId = CommonConnectorCatalog.Rj45Male8PinCableEndId,
            TypeKey = "INLINE_CONNECTOR",
            ReferenceDesignator = "X1"
        };
        var oldPort = new ComponentPort
        {
            PortId = "legacy-rj45-x1:face",
            Name = "RJ45",
            Connector = new ConnectorDefinition
            {
                ConnectorId = "legacy-rj45-x1:connector",
                Family = "RJ45",
                PinCount = 8,
                Gender = ConnectorGender.Male
            }
        };
        oldPort.Pins.Add(new ComponentPin { PinId = "legacy-rj45-x1:pin:1", PinNumber = "1" });
        component.Ports.Add(oldPort);
        project.Components.Add(component);
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "existing-rj45-wire",
            FromEndpointId = "legacy-rj45-x1:pin:1",
            ToEndpointId = "remote:pin",
            Kind = ConnectionKind.Wire
        });

        Assert.Equal(1, CommonConnectorCatalog.UpgradeLegacyRj45CableEnds(project));

        Assert.Equal(2, component.Ports.Count);
        var mating = Assert.Single(component.Ports, port => port.Connector is not null);
        var cable = Assert.Single(component.Ports, port => port.Connector is null);
        Assert.Equal("legacy-rj45-x1:face", mating.PortId);
        Assert.Equal("CABLE", cable.Name);
        Assert.Equal("legacy-rj45-x1:pin:1", Assert.Single(cable.Pins).PinId);
        Assert.Equal("legacy-rj45-x1:pin:1", project.Connections[0].FromEndpointId);
    }

    [Fact]
    public void VisibleTouchingM12Pair_ReplacesUnplacedLegacyCableThatOccupiedTheSocket()
    {
        var project = new ElectricalProject { ProjectId = "superseded-legacy-m12" };
        var visibleFemale = CommonConnectorCatalog.Create(CommonConnectorCatalog.M12FemaleACode4PinCableEndId, "X7");
        var visibleMale = CommonConnectorCatalog.Create(CommonConnectorCatalog.M12MaleACode4PinCableEndId, "X12");
        var hiddenFemale = LegacyEnd("hidden-f", ConnectorGender.Female, "M12-F");
        var hiddenMale = LegacyEnd("hidden-m", ConnectorGender.Male, "M12-M");
        project.Components.Add(visibleFemale);
        project.Components.Add(visibleMale);
        project.Components.Add(hiddenFemale);
        project.Components.Add(hiddenMale);
        project.TopologyPlacements.Add(new TopologyPlacement
        {
            ObjectId = visibleFemale.ComponentInstanceId, ObjectKind = "COMPONENT",
            X = 100, Y = 80, Width = 120, Height = 76
        });
        project.TopologyPlacements.Add(new TopologyPlacement
        {
            ObjectId = visibleMale.ComponentInstanceId, ObjectKind = "COMPONENT",
            X = 220, Y = 80, Width = 120, Height = 76
        });
        var visibleFemalePort = Assert.Single(visibleFemale.Ports, port => port.Connector is not null);
        var visibleMalePort = Assert.Single(visibleMale.Ports, port => port.Connector is not null);
        var hiddenFemalePort = Assert.Single(hiddenFemale.Ports);
        var hiddenMalePort = Assert.Single(hiddenMale.Ports);
        project.Cables.Add(new CableInstance { CableInstanceId = "old-cable", CableDefinitionId = "old-def" });
        project.CableAssemblies.Add(new CableAssembly
        {
            CableAssemblyId = "old-assembly",
            EndAConnectorId = hiddenFemalePort.Connector!.ConnectorId,
            EndBConnectorId = hiddenMalePort.Connector!.ConnectorId,
            Members = { new CableAssemblyMember { CableInstanceId = "old-cable" } }
        });
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "old-core", Kind = ConnectionKind.Cable,
            FromEndpointId = hiddenFemalePort.Pins[0].PinId,
            ToEndpointId = hiddenMalePort.Pins[0].PinId,
            CableInstanceId = "old-cable"
        });
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "old-mate", Kind = ConnectionKind.DirectMating,
            FromEndpointId = visibleFemalePort.PortId,
            ToEndpointId = hiddenMalePort.PortId
        });

        Assert.True(MatedConnectorPresentationPolicy.TryGetAvailableSnapTarget(
            project, visibleMale.ComponentInstanceId, 6, out var target));
        var removed = CommonConnectorCatalog.RemoveSupersededLegacyMate(project, visibleFemalePort.PortId);
        var replacement = new TopologyEndpointConnectionService().ConnectEndpoints(
            project, target.MovedPortId, target.PartnerPortId);

        Assert.Equal(2, removed.Count);
        Assert.Equal(ConnectionKind.DirectMating, replacement.Kind);
        Assert.DoesNotContain(project.Components, component => component.ComponentInstanceId.StartsWith("hidden-"));
        Assert.Empty(project.Cables);
        Assert.Empty(project.CableAssemblies);
        Assert.Single(project.Connections);

        static ComponentInstance LegacyEnd(string id, ConnectorGender gender, string name)
        {
            var port = new ComponentPort
            {
                PortId = $"{id}:port",
                Name = name,
                Connector = new ConnectorDefinition
                {
                    ConnectorId = $"{id}:connector",
                    Family = "M12",
                    Coding = "A",
                    PinCount = 4,
                    Gender = gender
                }
            };
            port.Pins.Add(new ComponentPin { PinId = $"{id}:pin:1", PinNumber = "1" });
            return new ComponentInstance
            {
                ComponentInstanceId = id,
                ComponentDefinitionId = "inline-mated-adapter:legacy M12 cable end",
                TypeKey = "INLINE_CONNECTOR",
                ReferenceDesignator = name,
                Ports = { port }
            };
        }
    }

    [Fact]
    public void ManuallyDrawnRj45ToM12Pins_BecomeOneThreeCoreCableWithoutRewiring()
    {
        var project = ProjectWithThreeConnectors();
        AddWire(project, "w1", "rj45:pin:1", "m12-a:pin:1");
        AddWire(project, "w4", "rj45:pin:4", "m12-a:pin:2");
        AddWire(project, "w5", "rj45:pin:5", "m12-a:pin:3");
        var before = project.Connections.Select(connection =>
            (connection.ConnectionId, connection.FromEndpointId, connection.ToEndpointId)).ToArray();
        var service = new ConnectorCableTopologyService();

        var topology = service.AnalyzeConnector(project, "rj45");
        var candidate = Assert.Single(topology.Candidates);
        var result = service.AssignCandidateAsCable(
            project,
            candidate,
            new CableSegmentOptions("CBL-RJ-M12", null, "RJ45 male to M12 female"),
            1800);

        Assert.Equal("M12-F-A", candidate.RemoteLabel.Split('·')[0].Trim());
        Assert.Equal(3, candidate.Connections.Count);
        Assert.Equal(3, result.Cable.CoreAssignments.Count);
        Assert.Equal("UNRESOLVED-CABLE", result.Cable.CableDefinitionId);
        Assert.Equal("rj45:connector", result.Assembly.EndAConnectorId);
        Assert.Equal("m12-a:connector", result.Assembly.EndBConnectorId);
        Assert.Equal(before, project.Connections.Select(connection =>
            (connection.ConnectionId, connection.FromEndpointId, connection.ToEndpointId)).ToArray());
        Assert.All(project.Connections, connection => Assert.Equal(result.Cable.CableInstanceId, connection.CableInstanceId));
    }

    [Fact]
    public void OneRj45ManuallyBranchedToTwoM12Heads_IsDetectedAsTwoEditableCableRanges()
    {
        var project = ProjectWithThreeConnectors();
        AddWire(project, "a1", "rj45:pin:1", "m12-a:pin:1");
        AddWire(project, "a4", "rj45:pin:4", "m12-a:pin:2");
        AddWire(project, "b1", "rj45:pin:1", "m12-b:pin:1");
        AddWire(project, "b4", "rj45:pin:4", "m12-b:pin:2");
        var service = new ConnectorCableTopologyService();

        var topology = service.AnalyzeConnector(project, "rj45");

        Assert.Equal(2, topology.Candidates.Count);
        Assert.All(topology.Candidates, candidate => Assert.Equal(2, candidate.Connections.Count));
        foreach (var candidate in topology.Candidates)
            service.AssignCandidateAsCable(project, candidate, new CableSegmentOptions(DisplayName: candidate.RemoteLabel));
        Assert.Equal(2, project.Cables.Count);
        Assert.Equal(2, project.CableAssemblies.Count);
        Assert.Equal(2, project.Connections.Select(connection => connection.CableInstanceId).Distinct().Count());
    }

    [Fact]
    public void DirectMating_IsExcluded_AndAnalysisDoesNotModifyOldConnections()
    {
        var project = ProjectWithThreeConnectors();
        AddWire(project, "wire", "rj45:pin:1", "m12-a:pin:1");
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "mate",
            FromEndpointId = "m12-a",
            ToEndpointId = "m12-b",
            Kind = ConnectionKind.DirectMating
        });
        var snapshot = project.Connections.Select(connection =>
            (connection.ConnectionId, connection.Kind, connection.CableInstanceId)).ToArray();

        var topology = new ConnectorCableTopologyService().AnalyzeConnector(project, "m12-a");

        Assert.Single(topology.Candidates);
        Assert.DoesNotContain(topology.Candidates.SelectMany(candidate => candidate.Connections),
            connection => connection.ConnectionId == "mate");
        Assert.Equal(snapshot, project.Connections.Select(connection =>
            (connection.ConnectionId, connection.Kind, connection.CableInstanceId)).ToArray());
    }

    [Fact]
    public void M12ToLooseWire_IsOneCableCandidateWithoutASecondConnector()
    {
        var project = ProjectWithThreeConnectors();
        project.Components.Add(SimpleEndpoint("loose-1", "loose-1:p"));
        project.Components.Add(SimpleEndpoint("loose-2", "loose-2:p"));
        AddWire(project, "loose-a", "m12-a:pin:1", "loose-1:p");
        AddWire(project, "loose-b", "m12-a:pin:2", "loose-2:p");
        var service = new ConnectorCableTopologyService();

        var candidate = Assert.Single(service.AnalyzeConnector(project, "m12-a").Candidates);
        var result = service.AssignCandidateAsCable(
            project,
            candidate,
            new CableSegmentOptions("CBL-PIGTAIL", null, "M12 to loose wire"));

        Assert.Null(candidate.RemoteConnectorId);
        Assert.Equal("散線端 / Loose wire", candidate.RemoteLabel);
        Assert.Equal(2, result.Cable.CoreAssignments.Count);
        Assert.Equal("CONNECTOR-TO-LOOSE-WIRE", Assert.Single(result.Assembly.Members).Purpose);
    }

    private static ElectricalProject ProjectWithThreeConnectors()
    {
        var project = new ElectricalProject { ProjectId = "manual-cables" };
        project.Components.Add(Connector("RJ45-M", "rj45", "RJ45", null, 8, ConnectorGender.Male));
        project.Components.Add(Connector("M12-F-A", "m12-a", "M12", "A", 4, ConnectorGender.Female));
        project.Components.Add(Connector("M12-F-B", "m12-b", "M12", "A", 4, ConnectorGender.Female));
        return project;
    }

    private static ComponentInstance Connector(
        string reference,
        string portId,
        string family,
        string? coding,
        int pinCount,
        ConnectorGender gender)
    {
        var port = new ComponentPort
        {
            PortId = portId,
            Name = family,
            Connector = new ConnectorDefinition
            {
                ConnectorId = $"{portId}:connector",
                Family = family,
                Coding = coding,
                PinCount = pinCount,
                Gender = gender
            }
        };
        for (var index = 1; index <= pinCount; index++)
            port.Pins.Add(new ComponentPin { PinId = $"{portId}:pin:{index}", PinNumber = index.ToString() });
        return new ComponentInstance
        {
            ComponentInstanceId = $"cmp:{portId}",
            ComponentDefinitionId = $"connector:{portId}",
            TypeKey = "INLINE_CONNECTOR",
            ReferenceDesignator = reference,
            Ports = { port }
        };
    }

    private static ComponentInstance SimpleEndpoint(string id, string portId) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = id,
        TypeKey = "COMPONENT",
        Ports = { new ComponentPort { PortId = portId, Name = portId } }
    };

    private static void AddWire(ElectricalProject project, string id, string from, string to) =>
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = id,
            FromEndpointId = from,
            ToEndpointId = to,
            Kind = ConnectionKind.Wire
        });
}
