using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class TopologyElectricalContinuityTests
{
    [Fact]
    public void DirectlyWiredQuattroTerminalsPropagatePowerLayerAndPotentialToOutput()
    {
        var project = new ElectricalProject { ProjectId = "terminal-continuity" };
        project.Components.Add(Device("SOURCE", "SOURCE:+24V", "+24V", ElectricalLayer.Power, Polarity.Positive));
        project.Components.Add(QuattroTerminal("TB1"));
        project.Components.Add(QuattroTerminal("TB2"));
        project.Components.Add(Device("LOAD", "LOAD:AI", "AI1", ElectricalLayer.Analog, Polarity.Unknown));
        foreach (var (id, x) in new[] { ("SOURCE", 0d), ("TB1", 200d), ("TB2", 400d), ("LOAD", 600d) })
            project.TopologyPlacements.Add(new TopologyPlacement
            {
                ObjectId = id,
                ObjectKind = "COMPONENT",
                X = x,
                Y = 0,
                Width = 140,
                Height = 76
            });

        AddConnection(project, "C1", "SOURCE:+24V", "TB1:IN1");
        AddConnection(project, "C2", "TB1:OUT1", "TB2:IN1");
        var output = AddConnection(project, "C3", "TB2:OUT1", "LOAD:AI");

        var graph = new TopologyProjection().Build(project);

        Assert.Equal(3, graph.Edges.Count);
        Assert.All(graph.Edges, edge => Assert.Equal(ElectricalLayer.Power, edge.Layer));
        Assert.Equal(TopologyPotentialClass.PositiveDc, TopologyConnectionPotentialClassifier.Classify(project, output));
    }

    [Fact]
    public void QuattroPartNumberInDisplayNameStillPropagatesPositivePotentialThroughBridgePin()
    {
        var project = new ElectricalProject { ProjectId = "terminal-display-identity" };
        project.Components.Add(Device("SOURCE", "SOURCE:+24V", "+24V", ElectricalLayer.Power, Polarity.Positive));
        project.Components.Add(new ComponentInstance
        {
            ComponentInstanceId = "TB",
            ComponentDefinitionId = "archive-component-guid",
            TypeKey = "DIN Rail Terminal Block",
            DisplayName = "PHOENIX CONTACT PT 2,5-QUATTRO (3209578)",
            Ports =
            {
                Port("TB", "INPUT", Pin("TB:IN1", "IN1", ElectricalLayer.Unknown)),
                Port("TB", "BRIDGE", Pin("TB:BRIDGE", "BRIDGE", ElectricalLayer.Unknown)),
                Port("TB", "OUTPUT", Pin("TB:OUT1", "OUT1", ElectricalLayer.Unknown))
            }
        });
        project.Components.Add(Device("LOAD", "LOAD:AI", "AI1", ElectricalLayer.Analog, Polarity.Unknown));

        AddConnection(project, "C1", "SOURCE:+24V", "TB:IN1");
        AddConnection(project, "C2", "TB:BRIDGE:PORT", "LOAD:AI");

        Assert.Equal(
            TopologyPotentialClass.PositiveDc,
            TopologyConnectionPotentialClassifier.Classify(project, project.Connections[1]));
        Assert.Equal(ElectricalLayer.Power, TopologyElectricalContinuity.ResolveLayer(project, project.Connections[1]));
    }

    private static ComponentInstance QuattroTerminal(string id) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = $"PHOENIX_CONTACT_3209578:{id}",
        TypeKey = "Feed-through Terminal Block",
        Ports =
        {
            Port(id, "INPUT", Pin($"{id}:IN1", "IN1", ElectricalLayer.Unknown)),
            Port(id, "OUTPUT", Pin($"{id}:OUT1", "OUT1", ElectricalLayer.Unknown))
        }
    };

    private static ComponentInstance Device(
        string id,
        string pinId,
        string pinName,
        ElectricalLayer layer,
        Polarity polarity) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = id,
        TypeKey = "TEST",
        Ports =
        {
            Port(id, "P1", new ComponentPin
            {
                PinId = pinId,
                PinNumber = "1",
                PinName = pinName,
                Layer = layer,
                Status = PinStatus.Normal,
                Power = polarity == Polarity.Unknown ? null : new PowerCapability { Polarity = polarity }
            })
        }
    };

    private static ComponentPort Port(string id, string name, ComponentPin pin) => new()
    {
        PortId = $"{id}:{name}:PORT",
        Name = name,
        Pins = { pin }
    };

    private static ComponentPin Pin(string id, string name, ElectricalLayer layer) => new()
    {
        PinId = id,
        PinNumber = "1",
        PinName = name,
        Layer = layer,
        Status = PinStatus.Normal
    };

    private static ElectricalConnection AddConnection(
        ElectricalProject project,
        string id,
        string from,
        string to)
    {
        var connection = new ElectricalConnection
        {
            ConnectionId = id,
            FromEndpointId = from,
            ToEndpointId = to,
            Kind = ConnectionKind.Wire
        };
        project.Connections.Add(connection);
        return connection;
    }
}
