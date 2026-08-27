using ComponentIntelligence.Electrical.Export;
using ComponentIntelligence.Electrical.PowerTopology;

namespace ComponentIntelligence.Tests.Electrical.PowerTopology;

public sealed class PowerEndpointCoverageAnalyzerTests
{
    private readonly ElectricalPowerEvidencePowerTopologyAdapter _adapter = new();
    private readonly PowerEndpointCoverageAnalyzer _coverage = new();

    [Fact]
    public void Direct_producer_to_consumer_is_covered()
    {
        var graph = Graph(
            Evidence([Domain("A")], [Producer("P", "A"), Consumer("C", "A")]),
            [Route("direct", Edge("P", "C"))]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Accepted, result.Status);
        Assert.All(result.Participants, item => Assert.True(item.Covered));
        Assert.Equal("ExplicitProducerConnectivity", Participant(result, "C").CoverageBasis);
    }

    [Fact]
    public void Fanout_consumers_are_all_covered_from_one_producer()
    {
        var graph = Graph(
            Evidence([Domain("A")], [Producer("P", "A"), Consumer("C1", "A"), Consumer("C2", "A")]),
            [Route("fanout", Edge("P", "C1"), Edge("P", "C2"))]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Accepted, result.Status);
        Assert.Equal(["C1", "C2"], result.Participants.Where(item => item.Role == "Consumer" && item.Covered)
            .Select(item => item.EndpointId));
    }

    [Fact]
    public void Multilevel_conversion_preserves_domain_semantics_and_covers_each_admitted_domain_side()
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

        var adapter = _adapter.AdaptAndAnalyze(graph);
        var result = _coverage.Analyze(graph, adapter);

        Assert.Equal(PowerTopologyAnalysisStatus.Accepted, adapter.Analysis!.Status);
        Assert.Equal(["X", "Y"], adapter.Analysis.ConversionTopologicalOrder);
        Assert.Equal(PowerEndpointCoverageStatus.Accepted, result.Status);
        Assert.Equal("ExplicitProducerConnectivity", Participant(result, "CA").CoverageBasis);
        Assert.Equal("ReachableConversionDomainAndConfirmedConductiveAnchor", Participant(result, "CB").CoverageBasis);
        Assert.Equal("ReachableConversionDomainAndConfirmedConductiveAnchor", Participant(result, "CC").CoverageBasis);
    }

    [Fact]
    public void One_confirmed_ordinary_terminal_is_transparent()
    {
        var graph = Graph(
            Evidence([Domain("A")], [Producer("P", "A"), Consumer("C", "A")]),
            [Route("left", Edge("P", "T1")), Route("right", Edge("T2", "C"))],
            [Continuity("K1", "T1", "T2")]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Accepted, result.Status);
        Assert.True(Participant(result, "C").Covered);
    }

    [Fact]
    public void Serial_confirmed_ordinary_terminals_are_transparent()
    {
        var graph = Graph(
            Evidence([Domain("A")], [Producer("P", "A"), Consumer("C", "A")]),
            [
                Route("r1", Edge("P", "T1A")),
                Route("r2", Edge("T1B", "T2A")),
                Route("r3", Edge("T2B", "C"))
            ],
            [Continuity("K1", "T1A", "T1B"), Continuity("K2", "T2A", "T2B")]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Accepted, result.Status);
        Assert.True(Participant(result, "C").Covered);
    }

    [Fact]
    public void Reversing_confirmed_route_segment_endpoints_does_not_change_logical_result()
    {
        var evidence = Evidence([Domain("A")], [Producer("P", "A"), Consumer("C", "A")]);
        var forward = Analyze(Graph(evidence, [Route("r", Edge("P", "C"))]));
        var reversed = Analyze(Graph(evidence, [Route("r", Edge("C", "P"))]));

        Assert.Equal(Fingerprint(forward), Fingerprint(reversed));
    }

    [Fact]
    public void Reversing_confirmed_terminal_from_to_does_not_change_logical_result()
    {
        var evidence = Evidence([Domain("A")], [Producer("P", "A"), Consumer("C", "A")]);
        var routes = new[] { Route("left", Edge("P", "T1")), Route("right", Edge("T2", "C")) };
        var forward = Analyze(Graph(evidence, routes, [Continuity("K", "T1", "T2")]));
        var reversed = Analyze(Graph(evidence, routes, [Continuity("K", "T2", "T1")]));

        Assert.Equal(Fingerprint(forward), Fingerprint(reversed));
    }

    [Fact]
    public void Missing_participant_topology_anchor_blocks()
    {
        var graph = Graph(
            Evidence([Domain("A")], [Producer("P", "A"), Consumer("C", "A")]),
            [Route("r", Edge("P", "OTHER"))]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "PWR-COVERAGE-PARTICIPANT-ANCHOR-MISSING" && item.SubjectId == "CONSUMER:C");
    }

    [Fact]
    public void Duplicate_participant_anchor_across_confirmed_routes_blocks_as_ambiguous()
    {
        var graph = Graph(
            Evidence([Domain("A")], [Producer("P", "A"), Consumer("C", "A")]),
            [Route("r1", Edge("P", "C")), Route("r2", Edge("C", "OTHER"))]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "PWR-COVERAGE-PARTICIPANT-ANCHOR-AMBIGUOUS" && item.SubjectId == "CONSUMER:C");
    }

    [Fact]
    public void Confirmed_segment_referencing_missing_topology_node_blocks()
    {
        var route = RouteWith(
            "r",
            [Node("P")],
            [Segment("s", "node:P", "node:MISSING", "Confirmed")]);
        var graph = Graph(Evidence([Domain("A")], [Producer("P", "A")]), [route]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-COVERAGE-SEGMENT-ANCHOR-MISSING");
    }

    [Fact]
    public void Confirmed_continuity_referencing_missing_connection_point_blocks()
    {
        var graph = Graph(
            Evidence([Domain("A")], [Producer("P", "A"), Consumer("C", "A")]),
            [Route("left", Edge("P", "T1")), Route("right", Edge("T2", "C"))],
            [Continuity("K", "T1", "T-MISSING")]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "PWR-COVERAGE-CONTINUITY-ANCHOR-MISSING" && item.SubjectId == "CONTINUITY:K:TO");
    }

    [Fact]
    public void Unknown_terminal_continuity_does_not_rescue_coverage()
    {
        var graph = Graph(
            Evidence([Domain("A")], [Producer("P", "A"), Consumer("C", "A")]),
            [Route("left", Edge("P", "T1")), Route("right", Edge("T2", "C"))],
            [Continuity("K", "T1", "T2", DrawingEvidenceStatus.Unknown)]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "PWR-COVERAGE-CONSUMER-UNREACHABLE" && item.SubjectId == "CONSUMER:C");
        Assert.DoesNotContain(result.Diagnostics, item => item.Code.StartsWith("PWR-COVERAGE-CONTINUITY-", StringComparison.Ordinal));
    }

    [Fact]
    public void Terminal_traversal_cannot_create_power_semantics()
    {
        var graph = Graph(
            Evidence([Domain("A")], [Producer("P", "A"), Consumer("C", "A")]),
            [Route("left", Edge("P", "T1")), Route("right", Edge("T2", "C"))],
            [Continuity("K", "T1", "T2")]);
        var adapter = _adapter.AdaptAndAnalyze(graph);
        var before = AdapterSemanticFingerprint(adapter);

        var result = _coverage.Analyze(graph, adapter);

        Assert.Equal(PowerEndpointCoverageStatus.Accepted, result.Status);
        Assert.Equal(before, AdapterSemanticFingerprint(adapter));
        Assert.Single(adapter.Input!.Domains);
        Assert.Single(adapter.Input.Producers);
        Assert.Single(adapter.Input.Consumers);
        Assert.Empty(adapter.Input.Conversions);
    }

    [Fact]
    public void Collection_permutations_produce_identical_canonical_result_and_diagnostics()
    {
        var evidence = Evidence(
            [Domain("A")],
            [Producer("P", "A"), Consumer("C1", "A"), Consumer("C2", "A")]);
        var routes = new[]
        {
            Route("left", Edge("P", "T1")),
            Route("right", Edge("T2", "C1"), Edge("T2", "C2"))
        };
        var first = Analyze(Graph(evidence, routes, [Continuity("K", "T1", "T2")]));

        var permutedEvidence = evidence with
        {
            Domains = evidence.Domains.Reverse().ToArray(),
            Participants = evidence.Participants.Reverse().ToArray()
        };
        var permutedRoutes = routes.Reverse().Select(route => route with
        {
            Nodes = route.Nodes.Reverse().ToArray(),
            Segments = route.Segments.Reverse().Select(segment => segment with
            {
                FromNodeId = segment.ToNodeId,
                ToNodeId = segment.FromNodeId
            }).ToArray()
        }).ToArray();
        var second = Analyze(Graph(permutedEvidence, permutedRoutes, [Continuity("K", "T2", "T1")]));

        Assert.Equal(Fingerprint(first), Fingerprint(second));
    }

    [Fact]
    public void Blocked_upstream_adapter_is_never_bypassed_by_topology()
    {
        var evidence = Evidence(
            [Domain("A")],
            [Producer("P", "A")],
            blockers:
            [
                new ElectricalPowerEvidenceBlocker
                {
                    Code = "POWER_DOMAIN_ID_REQUIRED",
                    SubjectId = "C",
                    MissingFields = ["powerDomainId"]
                }
            ]);
        var graph = Graph(evidence, [Route("r", Edge("P", "C"))]);
        var adapter = _adapter.AdaptAndAnalyze(graph);

        var result = _coverage.Analyze(graph, adapter);

        Assert.Equal(PowerTopologyAdapterStatus.Blocked, adapter.Status);
        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-COVERAGE-ADAPTER-NOT-ACCEPTED");
    }

    private PowerEndpointCoverageResult Analyze(AutocadStagingGraphV2Contract graph) =>
        _coverage.Analyze(graph, _adapter.AdaptAndAnalyze(graph));

    private static PowerEndpointCoverageParticipant Participant(PowerEndpointCoverageResult result, string endpointId) =>
        Assert.Single(result.Participants, item => item.EndpointId == endpointId);

    private static ElectricalPowerEvidenceV1Contract Evidence(
        IReadOnlyList<ElectricalPowerEvidenceDomain>? domains = null,
        IReadOnlyList<ElectricalPowerEvidenceParticipant>? participants = null,
        IReadOnlyList<ElectricalPowerEvidenceConversion>? conversions = null,
        IReadOnlyList<ElectricalPowerEvidenceBlocker>? blockers = null) => new()
    {
        SchemaVersion = ElectricalPowerEvidenceV1Contract.SupportedSchemaVersion,
        Domains = domains ?? [],
        Participants = participants ?? [],
        Conversions = conversions ?? [],
        BlockingRequirements = blockers ?? []
    };

    private static ElectricalPowerEvidenceDomain Domain(string id) => new()
    {
        PowerDomainId = id,
        EvidenceStatus = "Confirmed"
    };

    private static ElectricalPowerEvidenceParticipant Producer(string id, string domain) =>
        PowerParticipant(id, "Producer", domain);

    private static ElectricalPowerEvidenceParticipant Consumer(string id, string domain) =>
        PowerParticipant(id, "Consumer", domain);

    private static ElectricalPowerEvidenceParticipant PowerParticipant(string id, string role, string domain) => new()
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
        IReadOnlyList<AutocadStagingRoute> routes,
        IReadOnlyList<AutocadStagingTerminalContinuity>? continuities = null) => new()
    {
        SchemaVersion = AutocadStagingGraphV2Contract.SupportedSchemaVersion,
        SourceGraphSchemaVersion = "lrdu-staging-route.v1",
        ProjectId = "endpoint-coverage",
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
            TerminalContinuities = continuities ?? [],
            CrossPageContinuations = []
        },
        Interventions = []
    };

    private static (string From, string To, string Status) Edge(string from, string to, string status = "Confirmed") =>
        (from, to, status);

    private static AutocadStagingRoute Route(
        string id,
        params (string From, string To, string Status)[] edges)
    {
        var endpoints = edges.SelectMany(edge => new[] { edge.From, edge.To })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var segments = edges.Select((edge, index) =>
                Segment($"segment:{id}:{index:D2}", $"node:{edge.From}", $"node:{edge.To}", edge.Status))
            .ToArray();
        return RouteWith(id, endpoints.Select(Node).ToArray(), segments);
    }

    private static AutocadStagingRoute RouteWith(
        string id,
        IReadOnlyList<AutocadStagingNode> nodes,
        IReadOnlyList<AutocadStagingSegment> segments) => new()
    {
        RouteId = "route:" + id,
        NetIdentity = "net:" + id,
        VisibleLabel = id,
        TopologyStatus = "Confirmed",
        Responsibility = new AutocadStagingResponsibility { Owner = "LRDU" },
        Nodes = nodes,
        Segments = segments,
        Shield = new AutocadStagingShieldRoute { Status = "NotApplicable" }
    };

    private static AutocadStagingNode Node(string endpointId) => new()
    {
        NodeId = "node:" + endpointId,
        Kind = endpointId.StartsWith("T", StringComparison.Ordinal) ? "Terminal" : "ComponentPin",
        PinId = endpointId
    };

    private static AutocadStagingSegment Segment(string id, string fromNodeId, string toNodeId, string status) => new()
    {
        SegmentId = id,
        Kind = "InternalWire",
        FromNodeId = fromNodeId,
        ToNodeId = toNodeId,
        TopologyStatus = status,
        ProcurementStatus = "NotApplicable",
        DrawingRepresentation = "DirectWire",
        BomRequired = false,
        InstalledLengthStatus = "NotApplicable"
    };

    private static AutocadStagingTerminalContinuity Continuity(
        string id,
        string from,
        string to,
        DrawingEvidenceStatus status = DrawingEvidenceStatus.Confirmed) => new()
    {
        ContinuityId = id,
        TerminalBlockId = "TB",
        TerminalPositionId = "POS",
        LevelId = "L1",
        FromConnectionPointId = from,
        ToConnectionPointId = to,
        EvidenceStatus = status
    };

    private static string Fingerprint(PowerEndpointCoverageResult result) => string.Join("|",
        result.Status,
        string.Join(",", result.Participants.Select(item =>
            $"{item.Role}:{item.EndpointId}:{item.DomainId}:{item.NodeId}:{item.Covered}:{item.CoverageBasis}")),
        string.Join(",", result.Diagnostics.Select(item => $"{item.Code}:{item.SubjectId}:{item.Message}")));

    private static string AdapterSemanticFingerprint(PowerTopologyAdapterResult result) => string.Join("|",
        result.Status,
        result.Input is null ? "-" : string.Join(",", result.Input.Domains.Select(item => item.DomainId)),
        result.Input is null ? "-" : string.Join(",", result.Input.Producers.Select(item => item.ProducerId + ":" + item.DomainId)),
        result.Input is null ? "-" : string.Join(",", result.Input.Consumers.Select(item => item.ConsumerId + ":" + item.DomainId)),
        result.Input is null ? "-" : string.Join(",", result.Input.Conversions.Select(item => item.ConversionId + ":" + item.InputDomainId + ">" + item.OutputDomainId)));
}
