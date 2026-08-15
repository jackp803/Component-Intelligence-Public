using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ComponentProjectBridgePortNameTests
{
    [Fact]
    public void CreateInstance_PreservesPortIdAsVisibleName_AndProtocolSeparately()
    {
        var source = new ComponentIR
        {
            Identity = new ComponentIrIdentity
            {
                ComponentId = "notion:moxa",
                Manufacturer = "MOXA",
                Model = "EDS-2005-EL"
            },
            Ports =
            [
                new ComponentIntelligence.Contracts.ComponentPort
                {
                    PortId = "ETH1",
                    PortType = "Ethernet",
                    ConnectorFamily = "RJ45",
                    SignalType = "Ethernet",
                    Protocol = "Ethernet"
                }
            ]
        };

        var instance = new ComponentProjectBridge().CreateInstance(source, "switch-1");

        var port = Assert.Single(instance.Ports);
        Assert.Equal("ETH1", port.Name);
        Assert.Equal("Ethernet", port.Protocol);
        Assert.Equal("RJ45", port.Connector?.Family);
    }

    [Fact]
    public void CreateInstance_AttachesExplicitPinsToTheirParentPort()
    {
        var source = new ComponentIR
        {
            Identity = new ComponentIrIdentity
            {
                ComponentId = "notion:moxa",
                Manufacturer = "MOXA",
                Model = "EDS-2005-EL"
            },
            Ports =
            [
                new ComponentIntelligence.Contracts.ComponentPort { PortId = "ETH1", PortType = "Ethernet", ConnectorFamily = "RJ45" },
                new ComponentIntelligence.Contracts.ComponentPort { PortId = "PWR", PortType = "Power Input", ConnectorFamily = "Terminal Block" }
            ],
            Pins =
            [
                new ComponentIntelligence.Contracts.ComponentPin { PortId = "PWR", PinNumber = "1", Function = "24V", SignalType = "Power", Direction = "Input", VoltageDomain = "24VDC" },
                new ComponentIntelligence.Contracts.ComponentPin { PortId = "PWR", PinNumber = "2", Function = "0V", SignalType = "Power", Direction = "Return", VoltageDomain = "24VDC return" }
            ]
        };

        var instance = new ComponentProjectBridge().CreateInstance(source, "switch-1");

        var pwr = Assert.Single(instance.Ports.Where(port => port.Name == "PWR"));
        Assert.Equal(2, pwr.Pins.Count);
        Assert.Equal("24V", pwr.Pins.Single(pin => pin.PinNumber == "1").Function);
        Assert.Equal("0V", pwr.Pins.Single(pin => pin.PinNumber == "2").Function);
        Assert.DoesNotContain(instance.Ports, port => port.Name == "UNASSIGNED-PINS");
        Assert.Empty(instance.Ports.Single(port => port.Name == "ETH1").Pins);
    }

    [Fact]
    public void CreateInstance_DoesNotGuessWhenDeclaredPortDoesNotExist()
    {
        var source = new ComponentIR
        {
            Identity = new ComponentIrIdentity
            {
                ComponentId = "demo",
                Manufacturer = "DEMO",
                Model = "X1"
            },
            Ports =
            [
                new ComponentIntelligence.Contracts.ComponentPort { PortId = "PWR" },
                new ComponentIntelligence.Contracts.ComponentPort { PortId = "IO1" }
            ],
            Pins =
            [
                new ComponentIntelligence.Contracts.ComponentPin { PortId = "UNKNOWN-CONNECTOR", PinNumber = "1", Function = "24V" }
            ]
        };

        var instance = new ComponentProjectBridge().CreateInstance(source, "demo-1");

        var unresolved = Assert.Single(instance.Ports.Where(port => port.Name == "UNASSIGNED-PINS"));
        Assert.Contains("NEEDS_PORT_MAPPING", unresolved.Capabilities);
        Assert.Single(unresolved.Pins);
        Assert.Empty(instance.Ports.Single(port => port.Name == "PWR").Pins);
    }
}
