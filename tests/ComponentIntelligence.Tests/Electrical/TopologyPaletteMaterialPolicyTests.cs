using ComponentIntelligence.Electrical.Topology;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class TopologyPaletteMaterialPolicyTests
{
    [Theory]
    [InlineData("Terminal Block")]
    [InlineData("DIN_RAIL_TERMINAL")]
    [InlineData("端子台")]
    public void Classify_TerminalMaterial_IsOptInTerminalGroup(string typeKey) =>
        Assert.Equal(TopologyPaletteMaterialKind.TerminalBlock, TopologyPaletteMaterialPolicy.Classify(typeKey));

    [Theory]
    [InlineData("Shorting Jumper")]
    [InlineData("JUMPER_BAR")]
    [InlineData("短路片")]
    public void Classify_ShortingMaterial_IsOptInJumperGroup(string typeKey) =>
        Assert.Equal(TopologyPaletteMaterialKind.ShortingJumper, TopologyPaletteMaterialPolicy.Classify(typeKey));

    [Theory]
    [InlineData("PLC")]
    [InlineData("Power Supply")]
    [InlineData("Terminal Server")]
    [InlineData(null)]
    public void Classify_NormalDevice_RemainsInStandardPalette(string? typeKey) =>
        Assert.Equal(TopologyPaletteMaterialKind.Standard, TopologyPaletteMaterialPolicy.Classify(typeKey));
}
