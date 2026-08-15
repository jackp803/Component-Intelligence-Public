using ComponentIntelligence.Contracts;
using ComponentIntelligence.Extraction;
using ComponentIntelligence.Verification;
using Xunit;

namespace ComponentIntelligence.Tests.Extraction;

public sealed class NetworkEquipmentExtractorTests
{
    [Fact]
    public void Extract_CreatesRj45AndSfpPortsFromMixedSwitchSpecification()
    {
        var extractor = new NetworkEquipmentExtractor();
        var result = extractor.Extract(
        [
            new RawSpecification
            {
                RawName = "Ethernet interfaces",
                Section = "Interface",
                RawValue = "8 x RJ45 Ethernet ports; 2 x SFP slots"
            }
        ]);

        Assert.True(result.NetworkEvidenceDetected);
        Assert.Equal(10, result.ExpectedPortCount);
        Assert.Equal(10, result.Ports.Count);
        Assert.Equal(8, result.Ports.Count(port => port.PortId.StartsWith("ETH", StringComparison.Ordinal)));
        Assert.Equal(2, result.Ports.Count(port => port.PortId.StartsWith("SFP", StringComparison.Ordinal)));
        Assert.All(result.Ports.Where(port => port.PortId.StartsWith("ETH", StringComparison.Ordinal)), port =>
        {
            Assert.Equal("RJ45", port.ConnectorFamily);
            Assert.Equal("Ethernet", port.Protocol);
        });
    }

    [Fact]
    public void Evaluate_TypedExplicitPortsCanBeTopologyReadyWithoutExpandingRj45Pins()
    {
        var component = new ComponentIR
        {
            Identity = new ComponentIrIdentity
            {
                ComponentId = "CMP-SWITCH",
                Manufacturer = "MOXA",
                Model = "TEST-SWITCH"
            },
            Ports = Enumerable.Range(1, 8)
                .Select(index => new ComponentPort
                {
                    PortId = $"ETH{index}",
                    PortType = "Network",
                    ConnectorFamily = "RJ45",
                    SignalType = "Communication",
                    Direction = "Bidirectional",
                    Protocol = "Ethernet"
                })
                .ToArray()
        };

        var assessment = TopologyKnowledgePolicy.Evaluate(component);

        Assert.True(assessment.IsReady);
        Assert.Equal(ReadinessStatus.Ready, assessment.Status);
        Assert.DoesNotContain(assessment.Issues, issue => issue.StartsWith("TOPOLOGY_MISSING_PIN", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_PortWithoutConnectorRemainsPartial()
    {
        var component = new ComponentIR
        {
            Identity = new ComponentIrIdentity
            {
                ComponentId = "CMP-SWITCH",
                Manufacturer = "MOXA",
                Model = "TEST-SWITCH"
            },
            Ports =
            [
                new ComponentPort
                {
                    PortId = "ETH1",
                    PortType = "Network",
                    SignalType = "Communication",
                    Direction = "Bidirectional",
                    Protocol = "Ethernet"
                }
            ]
        };

        var assessment = TopologyKnowledgePolicy.Evaluate(component);

        Assert.False(assessment.IsReady);
        Assert.Equal(ReadinessStatus.Partial, assessment.Status);
        Assert.Contains("TOPOLOGY_PORT_CONNECTOR_COVERAGE:0/1", assessment.Issues);
    }
}
