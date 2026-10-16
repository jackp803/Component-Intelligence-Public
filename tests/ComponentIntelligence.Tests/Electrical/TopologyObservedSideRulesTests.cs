using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class TopologyObservedSideRulesTests
{
    [Fact]
    public void K7lApprovedPresentation_PutsSensingRightAndPowerOutputLeft()
    {
        var component = new ComponentInstance
        {
            ComponentInstanceId = "K7L-1",
            ComponentDefinitionId = "OMRON_K7L-AT50DP",
            TypeKey = "Liquid Leakage Sensor Amplifier",
            DisplayName = "OMRON K7L-AT50DP"
        };

        var power = Port("POWER");
        power.Capabilities.Add("SOURCE_PORT_ID:OMRON_K7L-AT50DP_POWER");
        power.Capabilities.Add("ROLE:Power Input");
        power.Capabilities.Add("DIRECTION:Input");

        var sensing = Port("SENSING");
        sensing.Capabilities.Add("SOURCE_PORT_ID:OMRON_K7L-AT50DP_SENSING");
        sensing.Capabilities.Add("ROLE:Sensor Input");
        sensing.Capabilities.Add("DIRECTION:Mixed");

        var output = Port("OUTPUT");
        output.Capabilities.Add("SOURCE_PORT_ID:OMRON_K7L-AT50DP_OUTPUT");
        output.Capabilities.Add("ROLE:Control Output");
        output.Capabilities.Add("DIRECTION:Output");

        component.Ports.AddRange([power, sensing, output]);

        Assert.Equal(TopologyScreenSide.Left, TopologyPortGeometry.DetermineScreenSide(component, power));
        Assert.Equal(TopologyScreenSide.Right, TopologyPortGeometry.DetermineScreenSide(component, sensing));
        Assert.Equal(TopologyScreenSide.Left, TopologyPortGeometry.DetermineScreenSide(component, output));
    }

    [Fact]
    public void Al1342ClassAPort_WithRealPowerReturnDiPins_StillDefaultsRight()
    {
        var component = new ComponentInstance
        {
            ComponentInstanceId = "AL1342-1",
            ComponentDefinitionId = "IFM_AL1342",
            TypeKey = "IO-Link Master",
            DisplayName = "IFM AL1342"
        };
        var port = Port("X01");
        port.Capabilities.Add("ROLE:IO-Link Port Class A");
        port.Capabilities.Add("DIRECTION:Mixed");
        port.Pins.Add(new ComponentPin
        {
            PinId = "X01:1",
            PinNumber = "1",
            Function = "L+",
            Power = new PowerCapability { Role = PowerRole.Input, Polarity = Polarity.Positive }
        });
        port.Pins.Add(new ComponentPin
        {
            PinId = "X01:3",
            PinNumber = "3",
            Function = "L-",
            Power = new PowerCapability { Role = PowerRole.Return, Polarity = Polarity.Return }
        });
        port.Pins.Add(new ComponentPin
        {
            PinId = "X01:2",
            PinNumber = "2",
            Function = "DI",
            Digital = new DigitalCapability { IoType = DigitalIoType.Di }
        });
        port.Pins.Add(new ComponentPin
        {
            PinId = "X01:4",
            PinNumber = "4",
            Function = "C/Q",
            Protocol = "IO-Link"
        });
        component.Ports.Add(port);

        Assert.Equal(TopologyScreenSide.Right, TopologyPortGeometry.DetermineScreenSide(component, port));
    }

    private static ComponentPort Port(string name) => new()
    {
        PortId = name,
        Name = name
    };
}
