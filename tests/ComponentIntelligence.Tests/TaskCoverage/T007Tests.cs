using ComponentIntelligence.Contracts;
using Xunit;

namespace ComponentIntelligence.Tests.TaskCoverage;

public sealed class T007Tests
{
    [Fact]
    public void ComponentPort_StoresEveryConnectivityField()
    {
        var port = new ComponentPort
        {
            PortId = "P1", PortType = "Power", ConnectorFamily = "M12", SignalType = "DC", Direction = "Input",
            VoltageDomain = "24V", Protocol = "CAN", AllowedConnections = ["Controller", "PowerSupply"]
        };
        Assert.Equal("P1", port.PortId);
        Assert.Equal("Power", port.PortType);
        Assert.Equal("M12", port.ConnectorFamily);
        Assert.Equal("DC", port.SignalType);
        Assert.Equal("Input", port.Direction);
        Assert.Equal("24V", port.VoltageDomain);
        Assert.Equal("CAN", port.Protocol);
        Assert.Equal(["Controller", "PowerSupply"], port.AllowedConnections);
    }

    [Fact]
    public void ComponentPin_StoresEveryElectricalFieldAndEvidence()
    {
        var evidence = new Evidence
        {
            SourceType = default, ExtractionMethod = default,
            RetrievedAt = new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero),
            VerificationStatus = default, RawValue = "Pin 1: 24V input"
        };
        var pin = new ComponentPin
        {
            PinNumber = "1", Function = "Power input", SignalType = "DC", Direction = "Input",
            VoltageDomain = "24V", Description = "Primary supply connection", Evidence = [evidence]
        };
        Assert.Equal("1", pin.PinNumber);
        Assert.Equal("Power input", pin.Function);
        Assert.Equal("DC", pin.SignalType);
        Assert.Equal("Input", pin.Direction);
        Assert.Equal("24V", pin.VoltageDomain);
        Assert.Equal("Primary supply connection", pin.Description);
        Assert.Single(pin.Evidence);
        Assert.Same(evidence, pin.Evidence[0]);
    }
}
