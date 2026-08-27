using ComponentIntelligence.Electrical.Export;
using ComponentIntelligence.Electrical.PowerTopology;

namespace ComponentIntelligence.Tests.Electrical.PowerTopology;

public sealed class PowerEndpointCoverageConversionEvidenceTests
{
    [Fact]
    public void Conversion_domain_consumer_with_unrelated_confirmed_neighbor_requires_output_endpoint_evidence()
    {
        var graph = Graph(
            Evidence(
                [Domain("A"), Domain("B")],
                [Producer("P", "A"), Consumer("C", "B")],
                [Conversion("X", "A", "B")]),
            [Route("input", Edge("P", "INPUT")), Route("output", Edge("UNRELATED", "C"))]);
        var adapter = new ElectricalPowerEvidencePowerTopologyAdapter().AdaptAndAnalyze(graph);

        var result = new PowerEndpointCoverageAnalyzer().Analyze(graph, adapter);

        Assert.Equal(PowerTopologyAnalysisStatus.Accepted, adapter.Analysis!.Status);
        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        var consumer = Assert.Single(result.Participants, item => item.EndpointId == "C");
        Assert.False(consumer.Covered);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "PWR-COVERAGE-CONVERSION-OUTPUT-ENDPOINT-EVIDENCE-REQUIRED" &&
            item.SubjectId == "CONSUMER:C");
    }

    [Fact]
    public void Arbitrarily_named_output_anchor_does_not_establish_converter_output_coverage()
    {
        var graph = Graph(
            Evidence(
                [Domain("A"), Domain("B")],
                [Producer("P", "A"), Consumer("C", "B")],
                [Conversion("X", "A", "B")]),
            [Route("input", Edge("P", "INPUT")), Route("output", Edge("X-OUTPUT-ANCHOR", "C"))]);
        var adapter = new ElectricalPowerEvidencePowerTopologyAdapter().AdaptAndAnalyze(graph);

        var result = new PowerEndpointCoverageAnalyzer().Analyze(graph, adapter);

        Assert.Equal(PowerTopologyAnalysisStatus.Accepted, adapter.Analysis!.Status);
        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.False(Assert.Single(result.Participants, item => item.EndpointId == "C").Covered);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "PWR-COVERAGE-CONVERSION-OUTPUT-ENDPOINT-EVIDENCE-REQUIRED" &&
            item.Message.Contains("conversion 'X'", StringComparison.Ordinal));
    }

    [Fact]
    public void Multilevel_conversion_domain_semantics_remain_accepted_while_physical_output_coverage_blocks()
    {
        var graph = Graph(
            Evidence(
                [Domain("A"), Domain("B"), Domain("C")],
                [Producer("P", "A"), Consumer("CA", "A"), Consumer("CB", "B"), Consumer("CC", "C")],
                [Conversion("X", "A", "B"), Conversion("Y", "B", "C")]),
            [
                Route("a", Edge("P", "CA")),
                Route("b", Edge("X-OUTPUT-ANCHOR", "CB")),
                Route("c", Edge("Y-OUTPUT-ANCHOR", "CC"))
            ]);
        var adapter = new ElectricalPowerEvidencePowerTopologyAdapter().AdaptAndAnalyze(graph);

        var result = new PowerEndpointCoverageAnalyzer().Analyze(graph, adapter);

        Assert.Equal(PowerTopologyAnalysisStatus.Accepted, adapter.Analysis!.Status);
        Assert.Equal(["X", "Y"], adapter.Analysis.ConversionTopologicalOrder);
        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.True(Assert.Single(result.Participants, item => item.EndpointId == "CA").Covered);
        Assert.False(Assert.Single(result.Participants, item => item.EndpointId == "CB").Covered);
        Assert.False(Assert.Single(result.Participants, item => item.EndpointId == "CC").Covered);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "PWR-COVERAGE-CONVERSION-OUTPUT-ENDPOINT-EVIDENCE-REQUIRED" &&
            item.SubjectId == "CONSUMER:CB");
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "PWR-COVERAGE-CONVERSION-OUTPUT-ENDPOINT-EVIDENCE-REQUIRED" &&
            item.SubjectId == "CONSUMER:CC");
    }

    private static ElectricalPowerEvidenceV1Contract Evidence(
        IReadOnlyList<ElectricalPowerEvidenceDomain> domains,
        IReadOnlyList<ElectricalPowerEvidenceParticipant> participants,
        IReadOnlyList<ElectricalPowerEvidenceConversion> conversions) => new()
    {
        SchemaVersion = ElectricalPowerEvidenceV1Contract.SupportedSchemaVersion,
        Domains = domains,
        Participants = participants,
        Conversions = conversions,
        BlockingRequirements = []
    };

    private static ElectricalPowerEvidenceDomain Domain(string id) => new()
    {
        PowerDomainId = id,
        EvidenceStatus = "Confirmed"
    };

    private static ElectricalPowerEvidenceParticipant Producer(string id, string domain) =>
        Participant(id, "Producer", domain);

    private static ElectricalPowerEvidenceParticipant Consumer(string id, string domain) =>
        Participant(id, "Consumer", domain);

    private static ElectricalPowerEvidenceParticipant Participant(string id, string role, string domain) => new()
    {
        EndpointId = id,
        ComponentInstanceId = "component-" + id,
        Role = role,
        PowerDomainId = domain,
        EvidenceStatus = "Confirmed"
    };

    private static ElectricalPowerEvidenceConversion Conversion(string id, string input, string output) => new()
    {
        ConversionId = id,
        ComponentInstanceId = "component-" + id,
        InputPowerDomainId = input,
        OutputPowerDomainId = output,
        EvidenceStatus = "Confirmed"
    };

    private static AutocadStagingGraphV2Contract Graph(
        ElectricalPowerEvidenceV1Contract evidence,
        IReadOnlyList<AutocadStagingRoute> routes) => new()
    {
        SchemaVersion = AutocadStagingGraphV2Contract.SupportedSchemaVersion,
        SourceGraphSchemaVersion = "lrdu-staging-route.v1",
        ProjectId = "conversion-endpoint-evidence",
        Routes = routes,
        PageIntents = [],
        PowerFlowOrientation = [],
        PowerEvidence = evidence,
        CableFamilies = [],
        CableInstances = [],
        TerminalContinuities = [],
        CrossPageContinuations = [],
        DeviceRoles = [],
        HeavyDutyConnectors = [],
        SourceEvidence = new AutocadStagingV2SourceEvidence
        {
            CableFamilies = [],
            CableInstances = [],
            TerminalContinuities = [],
            CrossPageContinuations = []
        },
        Interventions = []
    };

    private static (string From, string To) Edge(string from, string to) => (from, to);

    private static AutocadStagingRoute Route(string id, params (string From, string To)[] edges)
    {
        var endpoints = edges.SelectMany(edge => new[] { edge.From, edge.To })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        return new AutocadStagingRoute
        {
            RouteId = "route:" + id,
            NetIdentity = "net:" + id,
            VisibleLabel = id,
            TopologyStatus = "Confirmed",
            Responsibility = new AutocadStagingResponsibility { Owner = "LRDU" },
            Nodes = endpoints.Select(endpoint => new AutocadStagingNode
            {
                NodeId = "node:" + endpoint,
                Kind = "ComponentPin",
                PinId = endpoint
            }).ToArray(),
            Segments = edges.Select((edge, index) => new AutocadStagingSegment
            {
                SegmentId = $"segment:{id}:{index:D2}",
                Kind = "InternalWire",
                FromNodeId = "node:" + edge.From,
                ToNodeId = "node:" + edge.To,
                TopologyStatus = "Confirmed",
                ProcurementStatus = "NotApplicable",
                DrawingRepresentation = "DirectWire",
                BomRequired = false,
                InstalledLengthStatus = "NotApplicable"
            }).ToArray(),
            Shield = new AutocadStagingShieldRoute { Status = "NotApplicable" }
        };
    }
}
