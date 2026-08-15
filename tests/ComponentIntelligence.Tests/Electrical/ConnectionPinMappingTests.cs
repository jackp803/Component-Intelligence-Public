using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ConnectionPinMappingTests
{
    [Fact]
    public void NewPortConnection_HasNoImplicitPinMapping()
    {
        var project = BuildProject();
        var connection = new TopologyConnectionEditor().ConnectPorts(project, "a:p1", "b:p1");
        var mappings = new ConnectionPinMappingService().GetMappings(project, connection.ConnectionId);
        Assert.Empty(mappings);
        Assert.Null(connection.CableInstanceId);
    }

    [Fact]
    public void ExplicitCrossMapping_PersistsInCableCoreAssignments()
    {
        var project = BuildProject();
        var connection = new TopologyConnectionEditor().ConnectPorts(project, "a:p1", "b:p1");
        var service = new ConnectionPinMappingService();

        var cable = service.SetMappings(project, connection.ConnectionId,
        [
            new PinMappingEntry("a:p1:pin:1", "b:p1:pin:3", "1", "+24V", ElectricalLayer.Power),
            new PinMappingEntry("a:p1:pin:3", "b:p1:pin:1", "3", "0V", ElectricalLayer.Power),
            new PinMappingEntry("a:p1:pin:4", "b:p1:pin:4", "4", "C/Q", ElectricalLayer.Communication)
        ]);

        Assert.Equal(cable.CableInstanceId, connection.CableInstanceId);
        Assert.Equal(3, cable.CoreAssignments.Count);
        var restored = service.GetMappings(project, connection.ConnectionId);
        Assert.Contains(restored, item => item.FromPinId == "a:p1:pin:1" && item.ToPinId == "b:p1:pin:3");
        Assert.Contains(restored, item => item.Signal == "C/Q" && item.Layer == ElectricalLayer.Communication);
    }

    [Fact]
    public void MappingPinFromWrongPort_IsRejected()
    {
        var project = BuildProject();
        var connection = new TopologyConnectionEditor().ConnectPorts(project, "a:p1", "b:p1");
        var service = new ConnectionPinMappingService();

        var error = Assert.Throws<InvalidOperationException>(() => service.SetMappings(project, connection.ConnectionId,
        [
            new PinMappingEntry("b:p1:pin:1", "a:p1:pin:1")
        ]));

        Assert.Contains("side A", error.Message);
    }

    private static ElectricalProject BuildProject()
    {
        var a = new ComponentInstance
        {
            ComponentInstanceId = "a",
            ComponentDefinitionId = "def-a",
            TypeKey = "SENSOR",
            ReferenceDesignator = "A1",
            Ports =
            {
                Port("a:p1", "P1", "a")
            }
        };
        var b = new ComponentInstance
        {
            ComponentInstanceId = "b",
            ComponentDefinitionId = "def-b",
            TypeKey = "PLC",
            ReferenceDesignator = "B1",
            Ports =
            {
                Port("b:p1", "P1", "b")
            }
        };
        return new ElectricalProject { ProjectId = "test-pin-map", Components = { a, b } };
    }

    private static ComponentPort Port(string id, string name, string owner)
    {
        var port = new ComponentPort
        {
            PortId = id,
            Name = name,
            Connector = new ConnectorDefinition { ConnectorId = id + ":connector", Family = "M12", PinCount = 4 }
        };
        foreach (var pin in new[] { "1", "2", "3", "4" })
        {
            port.Pins.Add(new ComponentPin
            {
                PinId = $"{owner}:p1:pin:{pin}",
                PinNumber = pin,
                Function = pin == "1" ? "+24V" : pin == "3" ? "0V" : pin == "4" ? "C/Q" : null
            });
        }
        return port;
    }
}
