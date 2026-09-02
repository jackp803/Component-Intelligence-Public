using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;
using ComponentIntelligence.Electrical.Validation;
using Xunit;
using DomainPort = ComponentIntelligence.Electrical.Domain.ComponentPort;
using ContractPort = ComponentIntelligence.Contracts.ComponentPort;
using ContractPin = ComponentIntelligence.Contracts.ComponentPin;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class TopologyInteractionTests
{
    [Fact]
    public void PortToPortConnectionIsAValidTopologyEndpoint()
    {
        var project = ProjectWithTwoPorts();
        var editor = new TopologyConnectionEditor();

        var connection = editor.ConnectPorts(project, "cmp-a:port:p1", "cmp-b:port:p1");
        var report = new ElectricalProjectValidator().Validate(project);

        Assert.Single(project.Connections);
        Assert.Equal(connection.ConnectionId, project.Connections[0].ConnectionId);
        Assert.DoesNotContain(report.Results, item => item.RuleId == "RULE-CONN-001");
    }

    [Fact]
    public void OrdinaryPortConnectionRemainsWireWithoutCableAuthority()
    {
        var project = ProjectWithTwoPorts();

        var connection = new TopologyConnectionEditor().ConnectPorts(
            project,
            "cmp-a:port:p1",
            "cmp-b:port:p1");

        Assert.Equal(ConnectionKind.Wire, connection.Kind);
        Assert.Null(connection.CableInstanceId);
        Assert.Empty(project.Cables);
    }

    [Theory]
    [InlineData(CableConstructionType.Purchased)]
    [InlineData(CableConstructionType.Custom)]
    public void ExplicitCableClassificationCreatesAndBindsOneCable(CableConstructionType constructionType)
    {
        var project = ProjectWithTwoPorts();
        var editor = new TopologyConnectionEditor();
        var connection = editor.ConnectPorts(project, "cmp-a:port:p1", "cmp-b:port:p1");

        var cable = editor.AssignCableSegment(
            project,
            connection.ConnectionId,
            new CableSegmentOptions("CBL-01", "DEF-01", "Cable 01", constructionType));

        Assert.Equal(ConnectionKind.Cable, connection.Kind);
        Assert.Equal(cable.CableInstanceId, connection.CableInstanceId);
        Assert.Equal(constructionType, cable.CableConstructionType);
        Assert.Single(project.Cables);
    }

    [Fact]
    public void DoubleClickStyleInlineConnectorOperationSplitsOneConnectionIntoTwo()
    {
        var project = ProjectWithTwoPorts();
        project.TopologyPlacements.Add(new TopologyPlacement { ObjectId = "cmp-a", ObjectKind = "COMPONENT", X = 10, Y = 20, Width = 140, Height = 76 });
        project.TopologyPlacements.Add(new TopologyPlacement { ObjectId = "cmp-b", ObjectKind = "COMPONENT", X = 500, Y = 20, Width = 140, Height = 76 });
        var editor = new TopologyConnectionEditor();
        var original = editor.ConnectPorts(project, "cmp-a:port:p1", "cmp-b:port:p1");

        var inline = editor.InsertInlineConnector(project, original.ConnectionId, new InlineConnectorOptions(
            "M12", "A", 4, ConnectorGender.Female, ConnectorGender.Male));

        Assert.Equal(2, project.Connections.Count);
        Assert.Equal("INLINE_CONNECTOR", inline.TypeKey);
        Assert.Equal(2, inline.Ports.Count);
        Assert.Contains(project.Connections, item => item.FromEndpointId == "cmp-a:port:p1" && item.ToEndpointId == inline.Ports[0].PortId);
        Assert.Contains(project.Connections, item => item.FromEndpointId == inline.Ports[1].PortId && item.ToEndpointId == "cmp-b:port:p1");
        Assert.Contains(project.TopologyPlacements, item => item.ObjectId == inline.ComponentInstanceId);
    }

    [Fact]
    public void LooseWireM12PairExpandsOneConnectionIntoTwoAdaptersAndThreeSegments()
    {
        var project = ProjectWithTwoPorts();
        project.TopologyPlacements.Add(new TopologyPlacement { ObjectId = "cmp-a", ObjectKind = "COMPONENT", X = 10, Y = 20, Width = 140, Height = 76 });
        project.TopologyPlacements.Add(new TopologyPlacement { ObjectId = "cmp-b", ObjectKind = "COMPONENT", X = 600, Y = 20, Width = 140, Height = 76 });
        var editor = new TopologyConnectionEditor();
        var original = editor.ConnectPorts(project, "cmp-a:port:p1", "cmp-b:port:p1");

        var pair = editor.InsertLooseWireMatedConnectorPair(project, original.ConnectionId, new InlineConnectorOptions(
            "M12", "A", 4, ConnectorGender.Female, ConnectorGender.Male, "X99"));

        Assert.Equal(3, project.Connections.Count);
        Assert.Equal("X99-F", pair.FemaleAdapter.ReferenceDesignator);
        Assert.Equal("X99-M", pair.MaleAdapter.ReferenceDesignator);
        Assert.Equal(ConnectionKind.DirectMating, pair.FemaleToMaleMating.Kind);
        var femaleMating = pair.FemaleAdapter.Ports.Single(port => port.Connector?.Gender == ConnectorGender.Female);
        var maleMating = pair.MaleAdapter.Ports.Single(port => port.Connector?.Gender == ConnectorGender.Male);
        Assert.Equal("M12", femaleMating.Connector!.Family);
        Assert.Equal("A", femaleMating.Connector.Coding);
        Assert.Equal(femaleMating.PortId, pair.FemaleToMaleMating.FromEndpointId);
        Assert.Equal(maleMating.PortId, pair.FemaleToMaleMating.ToEndpointId);
        Assert.Contains(project.Connections, connection =>
            connection.FromEndpointId == "cmp-a:port:p1" &&
            connection.ToEndpointId == pair.FemaleAdapter.Ports.Single(port => port.Connector is null).PortId);
        Assert.Contains(project.Connections, connection =>
            connection.FromEndpointId == pair.MaleAdapter.Ports.Single(port => port.Connector is null).PortId &&
            connection.ToEndpointId == "cmp-b:port:p1");
        Assert.Contains(project.TopologyPlacements, placement => placement.ObjectId == pair.FemaleAdapter.ComponentInstanceId);
        Assert.Contains(project.TopologyPlacements, placement => placement.ObjectId == pair.MaleAdapter.ComponentInstanceId);
    }

    [Fact]
    public void BulkConnectionDeleteAlsoRemovesSavedRoutesAndOrphanCable()
    {
        var project = ProjectWithTwoPorts();
        var editor = new TopologyConnectionEditor();
        var connection = editor.ConnectPorts(project, "cmp-a:port:p1", "cmp-b:port:p1");
        var cable = editor.AssignCableSegment(project, connection.ConnectionId, new CableSegmentOptions(
            "CBL-001", "EVC014", "IFM EVC014"));
        project.TopologyRoutes.Add(new TopologyRouteGeometry
        {
            ConnectionId = connection.ConnectionId,
            Points =
            {
                new TopologyRoutePoint { X = 10, Y = 20 },
                new TopologyRoutePoint { X = 100, Y = 20 }
            }
        });

        var removed = editor.DeleteConnections(project, [connection.ConnectionId]);

        Assert.Equal(1, removed);
        Assert.Empty(project.Connections);
        Assert.DoesNotContain(project.Cables, item => item.CableInstanceId == cable.CableInstanceId);
        Assert.DoesNotContain(project.TopologyRoutes, route => route.ConnectionId == connection.ConnectionId);
    }

    [Fact]
    public void DeletingArchivedTerminal_RemovesItInsteadOfReturningItToComponentPalette()
    {
        var project = ProjectWithTwoPorts();
        var terminal = new ComponentInstance
        {
            ComponentInstanceId = "terminal-component",
            ComponentDefinitionId = "PHOENIX-3209578",
            TypeKey = "DIN Rail Terminal Block",
            EquipmentTag = "PT 2,5-QUATTRO #1",
            Placement = new PhysicalPlacement { ParentContainerId = "cab-1", XMm = 10, YMm = 20 },
            Ports =
            {
                new DomainPort
                {
                    PortId = "terminal-component:port:in",
                    Name = "IN",
                    Pins =
                    {
                        new ComponentIntelligence.Electrical.Domain.ComponentPin
                        {
                            PinId = "terminal-component:pin:1",
                            PinNumber = "1"
                        }
                    }
                }
            }
        };
        project.Components.Add(terminal);
        project.TopologyPlacements.Add(new TopologyPlacement
        {
            ObjectId = terminal.ComponentInstanceId,
            ObjectKind = "COMPONENT",
            X = 50,
            Y = 60
        });
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "terminal-wire",
            FromEndpointId = "terminal-component:pin:1",
            ToEndpointId = "cmp-a:port:p1"
        });
        project.TopologyRoutes.Add(new TopologyRouteGeometry { ConnectionId = "terminal-wire" });

        var result = new TopologyTerminalDeletionService().Delete(project, [terminal.ComponentInstanceId]);

        Assert.Equal(1, result.RemovedComponentTerminals);
        Assert.Equal(1, result.RemovedConnections);
        Assert.DoesNotContain(project.Components, component => component.ComponentInstanceId == terminal.ComponentInstanceId);
        Assert.DoesNotContain(project.TopologyPlacements, placement => placement.ObjectId == terminal.ComponentInstanceId);
        Assert.DoesNotContain(project.Connections, connection => connection.ConnectionId == "terminal-wire");
        Assert.DoesNotContain(project.TopologyRoutes, route => route.ConnectionId == "terminal-wire");
        Assert.Contains(project.Components, component => component.ComponentInstanceId == "cmp-a");
    }

    [Fact]
    public void DeletingStructuredTerminal_RemovesJunctionAndAttachedWire()
    {
        var project = ProjectWithTwoPorts();
        var block = new TerminalBlock
        {
            TerminalBlockId = "tb-1",
            ReferenceDesignator = "TB1",
            Placement = new PhysicalPlacement { ParentContainerId = "cab-1", XMm = 5, YMm = 6 },
            Positions =
            {
                new TerminalPosition
                {
                    TerminalPositionId = "tb-1:pos:1",
                    PositionLabel = "1",
                    Levels =
                    {
                        new TerminalLevel
                        {
                            LevelId = "tb-1:level:1",
                            LevelName = "L1",
                            ConnectionPoints =
                            {
                                new TerminalConnectionPoint
                                {
                                    ConnectionPointId = "tb-1:cp:1",
                                    Type = ConnectionPointType.ConductorEntry
                                }
                            }
                        }
                    }
                }
            }
        };
        project.TerminalBlocks.Add(block);
        project.TopologyPlacements.Add(new TopologyPlacement
        {
            ObjectId = block.TerminalBlockId,
            ObjectKind = "TERMINAL_BLOCK",
            X = 100,
            Y = 120
        });
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "junction-wire",
            FromEndpointId = "cmp-a:port:p1",
            ToEndpointId = "tb-1:cp:1"
        });

        var result = new TopologyTerminalDeletionService().Delete(project, [block.TerminalBlockId]);

        Assert.Equal(1, result.RemovedStructuredTerminals);
        Assert.Empty(project.TerminalBlocks);
        Assert.DoesNotContain(project.TopologyPlacements, placement => placement.ObjectId == block.TerminalBlockId);
        Assert.DoesNotContain(project.Connections, connection => connection.ConnectionId == "junction-wire");
    }

    [Fact]
    public void KnowledgeSynchronizationAddsPortsWithoutChangingProjectPlacementOrReference()
    {
        var instance = new ComponentInstance
        {
            ComponentInstanceId = "cmp-1",
            ComponentDefinitionId = "def-1",
            TypeKey = "COMPONENT",
            ReferenceDesignator = "S01",
            ReferenceSource = ReferenceSource.Manual,
            ReferenceLocked = true,
            Placement = new PhysicalPlacement { ParentContainerId = "cab", XMm = 12, YMm = 34, RotationDegrees = 0 }
        };
        var component = new ComponentIR
        {
            Identity = new ComponentIrIdentity { ComponentId = "def-1", Manufacturer = "IFM", Model = "TEST-1" },
            Classification = new ComponentClassification { Category = "Sensor" },
            Ports = new[]
            {
                new ContractPort
                {
                    PortId = "P1",
                    PortType = "M12",
                    ConnectorFamily = "M12",
                    Protocol = "IO-Link"
                }
            }
        };

        new ComponentInstanceKnowledgeSynchronizer().Apply(instance, component);

        Assert.Equal("S01", instance.ReferenceDesignator);
        Assert.Equal(12, instance.Placement!.XMm);
        Assert.Single(instance.Ports);
        Assert.Equal("IO-Link", instance.Ports[0].Protocol);
        Assert.Equal("M12", instance.Ports[0].Connector!.Family);
    }

    [Fact]
    public void AuthoritativeCentralSync_UpdatesKnowledgeButPreservesProjectEngineeringState()
    {
        var project = ProjectWithTwoPorts();
        var instance = project.Components[0];
        var connection = new TopologyConnectionEditor().ConnectPorts(project, "cmp-a:port:p1", "cmp-b:port:p1");
        instance.DisplayName = "OLD NAME";
        instance.Footprint = new PhysicalFootprint { WidthMm = 10, HeightMm = 20, DepthMm = 30 };
        instance.Ports[0].Pins.Add(new ComponentIntelligence.Electrical.Domain.ComponentPin
        {
            PinId = "cmp-a:port:p1:pin:existing",
            PinNumber = "1",
            PinName = "OLD PIN"
        });
        instance.Ports[0].Capabilities.Add("SOURCE_PORT_ID:OLD-P1");
        instance.Ports[0].Capabilities.Add("ROLE:Modbus TCP Ethernet Port");
        instance.Ports[0].Capabilities.Add("DIRECTION:Bidirectional");
        instance.Ports[0].PhysicalLocation = new PhysicalPortLocation { Side = "Right" };
        instance.Placement = new PhysicalPlacement
        {
            ParentContainerId = "cab",
            XMm = 123,
            YMm = 456,
            RotationDegrees = 90
        };
        var connectedPinId = instance.Ports[0].Pins[0].PinId;
        var archive = new ComponentIR
        {
            Identity = new ComponentIrIdentity { ComponentId = instance.ComponentDefinitionId, Manufacturer = "IFM", Model = "UPDATED" },
            Classification = new ComponentClassification { Category = "IO-Link Sensor" },
            Ports =
            [
                new ContractPort
                {
                    PortId = "P1",
                    PortName = "P1",
                    PortRole = "Modbus TCP Network Input",
                    Direction = "Bidirectional",
                    PhysicalSide = "Left",
                    Protocol = "IO-Link",
                    PinCount = 1
                }
            ],
            Pins =
            [
                new ContractPin { PinId = "PIN-1", PortId = "P1", PinNumber = "1", PinName = "UPDATED PIN", Function = "Signal" }
            ],
            Specifications =
            [
                new ComponentSpecification { Key = "dimensions", Name = "Dimensions", Value = "40 x 50 x 60 mm", Status = VerificationStatus.Verified }
            ]
        };

        var result = new CentralArchiveProjectSynchronizer().Synchronize(project, [archive]);

        Assert.Equal(1, result.UpdatedInstances);
        Assert.Equal("IFM UPDATED", instance.DisplayName);
        Assert.Equal("IO-Link Sensor", instance.TypeKey);
        Assert.Equal(40, instance.Footprint!.WidthMm);
        Assert.Equal(123, instance.Placement.XMm);
        Assert.Equal(456, instance.Placement.YMm);
        Assert.Equal(90, instance.Placement.RotationDegrees);
        Assert.Equal(connectedPinId, instance.Ports[0].Pins[0].PinId);
        Assert.Equal(["ROLE:Modbus TCP Network Input"], instance.Ports[0].Capabilities.Where(value => value.StartsWith("ROLE:", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(["DIRECTION:Bidirectional"], instance.Ports[0].Capabilities.Where(value => value.StartsWith("DIRECTION:", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("Left", instance.Ports[0].PhysicalLocation!.Side);
        Assert.Equal(TopologyScreenSide.Left, TopologyPortGeometry.DetermineScreenSide(instance.Ports[0]));
        Assert.Single(project.Connections);
        Assert.Equal("cmp-a:port:p1", connection.FromEndpointId);
        Assert.Equal("cmp-b:port:p1", connection.ToEndpointId);
    }

    [Fact]
    public void AuthoritativeCentralSync_PreservesExplicitLayoutFootprintOverride()
    {
        var project = ProjectWithTwoPorts();
        var instance = project.Components[0];
        instance.Footprint = new PhysicalFootprint { WidthMm = 111, HeightMm = 222, DepthMm = 333 };
        instance.FootprintOverride = true;
        var archive = new ComponentIR
        {
            Identity = new ComponentIrIdentity { ComponentId = instance.ComponentDefinitionId, Manufacturer = "IFM", Model = "UPDATED" },
            Classification = new ComponentClassification { Category = "Sensor" },
            Specifications =
            [
                new ComponentSpecification { Key = "dimensions", Name = "Dimensions", Value = "40 x 50 x 60 mm", Status = VerificationStatus.Verified }
            ]
        };

        new CentralArchiveProjectSynchronizer().Synchronize(project, [archive]);

        Assert.Equal(111, instance.Footprint.WidthMm);
        Assert.Equal(222, instance.Footprint.HeightMm);
        Assert.Equal(333, instance.Footprint.DepthMm);
    }

    [Fact]
    public void AssigningBomCableUpdatesExistingUnresolvedCableAndPreservesPinMappings()
    {
        var project = ProjectWithTwoPorts();
        var editor = new TopologyConnectionEditor();
        var connection = editor.ConnectPorts(project, "cmp-a:port:p1", "cmp-b:port:p1");
        var unresolved = new CableInstance
        {
            CableInstanceId = "cbl-existing",
            CableDefinitionId = "UNRESOLVED-CABLE",
            CoreAssignments =
            {
                new CoreAssignment { CoreId = "1", FromEndpointId = "cmp-a:port:p1", ToEndpointId = "cmp-b:port:p1" }
            }
        };
        project.Cables.Add(unresolved);
        connection.CableInstanceId = unresolved.CableInstanceId;

        var assigned = editor.AssignCableSegment(
            project,
            connection.ConnectionId,
            new CableSegmentOptions(null, "CMP-EVC014", "IFM EVC014", CableConstructionType.Custom));

        Assert.Same(unresolved, assigned);
        Assert.Single(project.Cables);
        Assert.Equal("CMP-EVC014", assigned.CableDefinitionId);
        Assert.Equal("IFM EVC014", assigned.DisplayName);
        Assert.Equal(CableConstructionType.Custom, assigned.CableConstructionType);
        Assert.Equal(ConnectionKind.Cable, connection.Kind);
        Assert.Equal("cbl-existing", connection.CableInstanceId);
        Assert.Single(assigned.CoreAssignments);
    }

    private static ElectricalProject ProjectWithTwoPorts()
    {
        var project = new ElectricalProject { ProjectId = "p1" };
        project.Components.Add(new ComponentInstance
        {
            ComponentInstanceId = "cmp-a",
            ComponentDefinitionId = "def-a",
            TypeKey = "SENSOR",
            ReferenceDesignator = "S01",
            Ports =
            {
                new DomainPort
                {
                    PortId = "cmp-a:port:p1",
                    Name = "P1",
                    Protocol = "Ethernet",
                    MaxConnections = 1,
                    Connector = new ConnectorDefinition { ConnectorId = "a-rj45", Family = "RJ45", Gender = ConnectorGender.Female }
                }
            }
        });
        project.Components.Add(new ComponentInstance
        {
            ComponentInstanceId = "cmp-b",
            ComponentDefinitionId = "def-b",
            TypeKey = "SWITCH",
            ReferenceDesignator = "SW01",
            Ports =
            {
                new DomainPort
                {
                    PortId = "cmp-b:port:p1",
                    Name = "P1",
                    Protocol = "Ethernet",
                    MaxConnections = 1,
                    Connector = new ConnectorDefinition { ConnectorId = "b-rj45", Family = "RJ45", Gender = ConnectorGender.Female }
                }
            }
        });
        return project;
    }
}
