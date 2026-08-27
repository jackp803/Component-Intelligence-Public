using ComponentIntelligence.Electrical.PowerTopology;

namespace ComponentIntelligence.Tests.Electrical.PowerTopology;

public sealed class PowerEndpointCoverageAnalyzerTests
{
    [Fact]
    public void Production_coverage_analyzer_type_exists()
    {
        var type = typeof(PowerTopologyAnalyzer).Assembly.GetType(
            "ComponentIntelligence.Electrical.PowerTopology.PowerEndpointCoverageAnalyzer");

        Assert.NotNull(type);
    }
}
