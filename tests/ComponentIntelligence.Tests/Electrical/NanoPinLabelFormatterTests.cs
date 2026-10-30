using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class NanoPinLabelFormatterTests
{
    [Theory]
    [InlineData("H1-1", "D13 / SCK")]
    [InlineData("H1-4", "A0")]
    [InlineData("H1-8", "A4 / SDA")]
    [InlineData("H1-12", "5V")]
    [InlineData("H2-1", "D12 / MISO")]
    [InlineData("H2-2", "D11~ / MOSI")]
    [InlineData("H2-14", "D0 / RX")]
    [InlineData("H2-15", "D1 / TX")]
    public void NanoShowsFunctionalNameOnly_AndRetainsPhysicalIdentity(
        string physicalNumber, string functionalName)
    {
        var component = Nano();
        var pin = new ComponentPin
        {
            PinId = "電子料_1806251545_HEADER_A_" + physicalNumber,
            PinNumber = physicalNumber,
            PinName = functionalName
        };

        Assert.Equal(functionalName, TopologyPinLabelFormatter.Format(component, pin));
        Assert.Equal(physicalNumber, pin.PinNumber);
        Assert.Equal("電子料_1806251545_HEADER_A_" + physicalNumber, pin.PinId);
    }

    [Fact]
    public void NanoWithoutFunctionName_FallsBackToPhysicalNumber()
    {
        var pin = new ComponentPin { PinId = "stable-key", PinNumber = "H1-4" };

        Assert.Equal("H1-4", TopologyPinLabelFormatter.Format(Nano(), pin));
    }

    [Fact]
    public void UnrelatedComponent_RetainsExistingNumberPlusNameDisplay()
    {
        var component = new ComponentInstance
        {
            ComponentInstanceId = "U2",
            ComponentDefinitionId = "EXAMPLE_OTHER"
        };
        var pin = new ComponentPin
        {
            PinId = "stable-key",
            PinNumber = "2",
            PinName = "GND"
        };

        Assert.Equal("2 GND", TopologyPinLabelFormatter.Format(component, pin));
    }

    [Fact]
    public void NanoFunctionFallback_UsesFunctionWithoutShowingHNumber()
    {
        var pin = new ComponentPin
        {
            PinId = "stable-key",
            PinNumber = "H1-4",
            PinName = "",
            Function = "Analog Input A0"
        };

        Assert.Equal("Analog Input A0", TopologyPinLabelFormatter.Format(Nano(), pin));
    }

    private static ComponentInstance Nano() => new()
    {
        ComponentInstanceId = "U1",
        ComponentDefinitionId = "電子料_1806251545"
    };
}
