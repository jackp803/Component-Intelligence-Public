using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class PwmSixTerminalTopologyEndpointTests
{
    [Fact]
    public void UnknownConnectorFamilyWithRealTerminalPins_UsesIndividualPinMode()
    {
        var port = CreatePort("PWM_INPUT", "Unknown", "PWM", "GND_PWM");

        Assert.Equal(TopologyEndpointDisplayMode.Pins,
            TopologyEndpointPolicy.DetermineDisplayMode(port));
        Assert.True(TopologyEndpointPolicy.ShouldRenderWiringEndpoint(port));
    }

    [Fact]
    public void ThreeTerminalPairsExposeSixDistinctPins_WithoutInternalSelector()
    {
        var ports = new[]
        {
            CreatePort("POWER", "Unknown", "VCC", "GND_SUPPLY"),
            CreatePort("PWM_INPUT", "Unknown", "PWM", "GND_PWM"),
            CreatePort("AO_OUTPUT", "Unknown", "VOUT", "GND_OUT"),
            CreatePort("PWM_LEVEL_SEL", "Unknown")
        };

        var endpoints = ports
            .Where(TopologyEndpointPolicy.ShouldRenderWiringEndpoint)
            .SelectMany(port =>
                TopologyEndpointPolicy.DetermineDisplayMode(port) == TopologyEndpointDisplayMode.Pins
                    ? port.Pins.Select(pin => pin.PinId)
                    : new[] { port.PortId })
            .ToArray();

        Assert.Equal(6, endpoints.Length);
        Assert.Equal(6, endpoints.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(ports[3].PortId, endpoints);
        Assert.Contains("PWM_INPUT:GND_PWM", endpoints);
        Assert.Contains("AO_OUTPUT:GND_OUT", endpoints);
    }

    [Fact]
    public void KnownWholeMatedConnectorWithoutPinRows_IsNotHidden()
    {
        var port = CreatePort("USB", "USB");

        Assert.True(TopologyEndpointPolicy.ShouldRenderWiringEndpoint(port));
        Assert.Equal(TopologyEndpointDisplayMode.Connector,
            TopologyEndpointPolicy.DetermineDisplayMode(port));
    }

    [Fact]
    public void ExplicitConnectorMode_RemainsCollapsedDespiteAvailablePins()
    {
        var port = CreatePort("ETHERNET", "Unknown", "1", "2");
        port.Capabilities.Add("TOPOLOGY_ENDPOINT_MODE:CONNECTOR");

        Assert.Equal(TopologyEndpointDisplayMode.Connector,
            TopologyEndpointPolicy.DetermineDisplayMode(port));
    }

    private static ComponentPort CreatePort(string name, string family, params string[] pinNumbers)
    {
        var port = new ComponentPort
        {
            PortId = name,
            Name = name,
            Connector = new ConnectorDefinition
            {
                ConnectorId = name + ":connector",
                Family = family
            }
        };

        foreach (var number in pinNumbers)
        {
            port.Pins.Add(new ComponentPin
            {
                PinId = name + ":" + number,
                PinNumber = number,
                PinName = number
            });
        }

        return port;
    }
}
