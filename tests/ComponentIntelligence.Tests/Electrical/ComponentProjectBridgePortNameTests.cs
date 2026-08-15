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
}
