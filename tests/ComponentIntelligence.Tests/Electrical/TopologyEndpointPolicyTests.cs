using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class TopologyEndpointPolicyTests
{
    [Theory]
    [InlineData("M12")]
    [InlineData("RJ45")]
    [InlineData("M8")]
    public void StandardWholeMatedConnector_DefaultsToConnector(string family)
    {
        var port = Port(family);

        Assert.Equal(TopologyEndpointDisplayMode.Connector, TopologyEndpointPolicy.DetermineDisplayMode(port));
    }

    [Theory]
    [InlineData("Loose Wire / Flying Leads")]
    [InlineData("Screw Terminal Block")]
    [InlineData("Terminal")]
    [InlineData("Bare Wire")]
    public void IndependentlyWiredInterface_DefaultsToPins(string family)
    {
        var port = Port(family);
        port.Pins.Add(Pin("1"));

        Assert.Equal(TopologyEndpointDisplayMode.Pins, TopologyEndpointPolicy.DetermineDisplayMode(port));
    }

    [Fact]
    public void NoConnectorWithPins_DefaultsToPins()
    {
        var port = new ComponentPort { PortId = "P1", Name = "P1" };
        port.Pins.Add(Pin("1"));

        Assert.Equal(TopologyEndpointDisplayMode.Pins, TopologyEndpointPolicy.DetermineDisplayMode(port));
    }

    [Fact]
    public void ExplicitArchiveMode_OverridesConnectorInference()
    {
        var port = Port("M12");
        port.Capabilities.Add("TOPOLOGY_ENDPOINT_MODE:Pins");

        Assert.Equal(TopologyEndpointDisplayMode.Pins, TopologyEndpointPolicy.DetermineDisplayMode(port));
    }

    private static ComponentPort Port(string family) => new()
    {
        PortId = "P1",
        Name = "P1",
        Connector = new ConnectorDefinition
        {
            ConnectorId = "C1",
            Family = family
        }
    };

    private static ComponentPin Pin(string number) => new()
    {
        PinId = $"P1:{number}",
        PinNumber = number
    };
}
