using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;
using ComponentIntelligence.Electrical.Validation;
using Xunit;
using DomainPort = ComponentIntelligence.Electrical.Domain.ComponentPort;
using ContractPort = ComponentIntelligence.Contracts.ComponentPort;

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
            new CableSegmentOptions(null, "CMP-EVC014", "IFM EVC014"));

        Assert.Same(unresolved, assigned);
        Assert.Single(project.Cables);
        Assert.Equal("CMP-EVC014", assigned.CableDefinitionId);
        Assert.Equal("IFM EVC014", assigned.DisplayName);
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
