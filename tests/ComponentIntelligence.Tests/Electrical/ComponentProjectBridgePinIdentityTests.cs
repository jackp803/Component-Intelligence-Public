using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using Xunit;
using ContractPin = ComponentIntelligence.Contracts.ComponentPin;
using ContractPort = ComponentIntelligence.Contracts.ComponentPort;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ComponentProjectBridgePinIdentityTests
{
    [Fact]
    public void CentralPinIds_PreservePlusMinusAsDistinctRuntimeEndpoints()
    {
        var source = Component(
            new ContractPin
            {
                PinId = "PRO-FACE_PFXGP6540WCDW_PWR_PLUS",
                PortId = "PWR",
                PinNumber = "+",
                Function = "24V+",
                SignalType = "Power",
                Direction = "Input"
            },
            new ContractPin
            {
                PinId = "PRO-FACE_PFXGP6540WCDW_PWR_MINUS",
                PortId = "PWR",
                PinNumber = "-",
                Function = "0V",
                SignalType = "Power",
                Direction = "Return"
            });

        var instance = new ComponentProjectBridge().CreateInstance(source, "HMI-1");
        var pins = Assert.Single(instance.Ports).Pins;

        Assert.Equal(2, pins.Count);
        Assert.NotEqual(pins[0].PinId, pins[1].PinId);
        Assert.Contains("pwr_plus", pins.Single(pin => pin.PinNumber == "+").PinId, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pwr_minus", pins.Single(pin => pin.PinNumber == "-").PinId, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("+", "-")]
    [InlineData("V+", "V-")]
    [InlineData("A+", "B-")]
    public void LegacyPinNumbersWithPunctuation_DoNotCollide(string firstNumber, string secondNumber)
    {
        var source = Component(
            new ContractPin
            {
                PortId = "PWR",
                PinNumber = firstNumber,
                Function = firstNumber,
                SignalType = "Power"
            },
            new ContractPin
            {
                PortId = "PWR",
                PinNumber = secondNumber,
                Function = secondNumber,
                SignalType = "Power"
            });

        var instance = new ComponentProjectBridge().CreateInstance(source, "DEVICE-1");
        var pins = Assert.Single(instance.Ports).Pins;

        Assert.Equal(2, pins.Count);
        Assert.NotEqual(pins[0].PinId, pins[1].PinId);
    }

    private static ComponentIR Component(params ContractPin[] pins) => new()
    {
        Identity = new ComponentIrIdentity
        {
            ComponentId = "TEST_DEVICE",
            Manufacturer = "TEST",
            Model = "DEVICE"
        },
        Ports =
        [
            new ContractPort
            {
                PortId = "PWR",
                PortName = "PWR",
                PortRole = "Power Input",
                Direction = "Input",
                ConnectorFamily = "Terminal"
            }
        ],
        Pins = pins
    };
}
