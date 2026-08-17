using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class TopologyObservedSideRulesTests
{
    [Fact]
    public void K7lSensingPort_SensorInputRole_RendersLeftEvenWhenDirectionIsMixed()
    {
        var port = Port("SENSING");
        port.Capabilities.Add("ROLE:Sensor Input");
        port.Capabilities.Add("DIRECTION:Mixed");

        Assert.Equal(TopologyScreenSide.Left, TopologyPortGeometry.DetermineScreenSide(port));
    }

    [Fact]
    public void Al1342ClassAPort_MixedIoLinkRole_DefaultsRight()
    {
        var port = Port("X01");
        port.Capabilities.Add("ROLE:IO-Link Port Class A");
        port.Capabilities.Add("DIRECTION:Mixed");

        Assert.Equal(TopologyScreenSide.Right, TopologyPortGeometry.DetermineScreenSide(port));
    }

    private static ComponentPort Port(string name) => new()
    {
        PortId = name,
        Name = name
    };
}
