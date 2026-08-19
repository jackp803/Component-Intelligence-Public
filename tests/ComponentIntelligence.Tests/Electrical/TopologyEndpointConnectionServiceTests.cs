using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class TopologyEndpointConnectionServiceTests
{
    [Fact]
    public void PinToPinConnection_PreservesExactPinIdsAndUsesWire()
    {
        var project = ProjectWithTwoPinnedPorts();
        var service = new TopologyEndpointConnectionService();

        var connection = service.ConnectEndpoints(project, "P1:1", "P2:1");

        Assert.Equal("P1:1", connection.FromEndpointId);
        Assert.Equal("P2:1", connection.ToEndpointId);
        Assert.Equal(ConnectionKind.Wire, connection.Kind);
    }

    [Fact]
    public void PortToPortConnection_UsesCable()
    {
        var project = ProjectWithTwoPinnedPorts();
        var service = new TopologyEndpointConnectionService();

        var connection = service.ConnectEndpoints(project, "P1", "P2");

        Assert.Equal(ConnectionKind.Cable, connection.Kind);
    }

    [Fact]
    public void PinEndpoint_DefaultCapacityIsOne()
    {
        var project = ProjectWithThreePinnedPorts();
        var service = new TopologyEndpointConnectionService();
        service.ConnectEndpoints(project, "P1:1", "P2:1");

        Assert.Throws<InvalidOperationException>(() => service.ConnectEndpoints(project, "P1:1", "P3:1"));
    }

    private static ElectricalProject ProjectWithTwoPinnedPorts()
    {
        var project = BaseProject();
        project.Components.Add(Component("C1", Port("P1")));
        project.Components.Add(Component("C2", Port("P2")));
        return project;
    }

    private static ElectricalProject ProjectWithThreePinnedPorts()
    {
        var project = ProjectWithTwoPinnedPorts();
        project.Components.Add(Component("C3", Port("P3")));
        return project;
    }

    private static ElectricalProject BaseProject() => new()
    {
        ProjectId = "TEST"
    };

    private static ComponentInstance Component(string id, ComponentPort port) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = id,
        TypeKey = "TEST",
        Ports = { port }
    };

    private static ComponentPort Port(string id) => new()
    {
        PortId = id,
        Name = id,
        Connector = new ConnectorDefinition { ConnectorId = id + ":C", Family = "Terminal" },
        Pins =
        {
            new ComponentPin { PinId = id + ":1", PinNumber = "1", Status = PinStatus.Normal }
        }
    };
}
