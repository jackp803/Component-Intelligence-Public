using ComponentIntelligence.Electrical.Export;
using ComponentIntelligence.Electrical.PowerTopology;

namespace ComponentIntelligence.Tests.Electrical.PowerTopology;

public sealed class PowerEndpointCoverageConversionEndpointV2Tests
{
    private readonly ElectricalPowerEvidencePowerTopologyAdapter _adapter = new();
    private readonly PowerEndpointCoverageAnalyzer _coverage = new();

    [Fact]
    public void Adapter_carries_exact_converter_endpoint_ids_as_opaque_internal_facts()
    {
        var graph = Graph(
            Evidence(
                [Domain("A"), Domain("B")],
                [Producer("P", "A")],
                [Conversion("X", "A", "B", ["XI2", "XI1"], ["XO2", "XO1"])]),
            [Route("input", Edge("P", "XI1"), Edge("P", "XI2")), Route("output", Edge("XO1", "U1"), Edge("XO2", "U2"))]);

        var adapter = _adapter.AdaptAndAnalyze(graph);

        Assert.Equal(PowerTopologyAdapterStatus.Accepted, adapter.Status);
        var conversion = Assert.Single(adapter.Input!.Conversions);
        Assert.Equal(["XI1", "XI2"], conversion.InputEndpointIds);
        Assert.Equal(["XO1", "XO2"], conversion.OutputEndpointIds);
    }

    [Fact]
    public void Explicit_producer_reaches_converter_input_as_domain_load()
    {
        var graph = Graph(
            Evidence(
                [Domain("A"), Domain("B")],
                [Producer("P", "A")],
                [Conversion("X", "A", "B", ["XIN"], ["XOUT"])]),
            [Route("input", Edge("P", "XIN")), Route("output", Edge("XOUT", "UNUSED"))]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Accepted, result.Status);
        var input = Participant(result, "ConversionInput", "XIN");
        Assert.True(input.Covered);
        Assert.Equal("ConversionInputConnectivity", input.CoverageBasis);
        var output = Participant(result, "ConversionOutput", "XOUT");
        Assert.True(output.Covered);
        Assert.Equal("ConversionOutputSourceAnchor", output.CoverageBasis);
    }

    [Fact]
    public void Converter_input_endpoint_missing_blocks()
    {
        var graph = Graph(
            Evidence(
                [Domain("A"), Domain("B")],
                [Producer("P", "A")],
                [Conversion("X", "A", "B", ["XIN"], ["XOUT"])]),
            [Route("input", Edge("P", "OTHER")), Route("output", Edge("XOUT", "UNUSED"))]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "PWR-COVERAGE-CONVERSION-INPUT-ANCHOR-MISSING" &&
            item.SubjectId == "CONVERSION:X:INPUT:XIN");
    }

    [Fact]
    public void Converter_input_endpoint_ambiguous_blocks()
    {
        var graph = Graph(
            Evidence(
                [Domain("A"), Domain("B")],
                [Producer("P", "A")],
                [Conversion("X", "A", "B", ["XIN"], ["XOUT"])]),
            [
                Route("input-1", Edge("P", "XIN")),
                Route("input-2", Edge("XIN", "OTHER")),
                Route("output", Edge("XOUT", "UNUSED"))
            ]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "PWR-COVERAGE-CONVERSION-INPUT-ANCHOR-AMBIGUOUS" &&
            item.SubjectId == "CONVERSION:X:INPUT:XIN");
    }

    [Fact]
    public void Converter_input_endpoint_unreachable_from_domain_source_blocks()
    {
        var graph = Graph(
            Evidence(
                [Domain("A"), Domain("B")],
                [Producer("P", "A")],
                [Conversion("X", "A", "B", ["XIN"], ["XOUT"])]),
            [
                Route("source", Edge("P", "P-OTHER")),
                Route("input", Edge("XIN", "XIN-OTHER")),
                Route("output", Edge("XOUT", "UNUSED"))
            ]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.False(Participant(result, "ConversionInput", "XIN").Covered);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "PWR-COVERAGE-CONVERSION-INPUT-UNREACHABLE" &&
            item.SubjectId == "CONVERSION:X:INPUT:XIN");
    }

    [Fact]
    public void Converter_output_endpoint_missing_blocks_output_domain_physical_coverage()
    {
        var graph = Graph(
            Evidence(
                [Domain("A"), Domain("B")],
                [Producer("P", "A"), Consumer("C", "B")],
                [Conversion("X", "A", "B", ["XIN"], ["XOUT"])]),
            [Route("input", Edge("P", "XIN")), Route("consumer", Edge("UNRELATED", "C"))]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.False(Participant(result, "Consumer", "C").Covered);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "PWR-COVERAGE-CONVERSION-OUTPUT-ANCHOR-MISSING" &&
            item.SubjectId == "CONVERSION:X:OUTPUT:XOUT");
    }

    [Fact]
    public void Converter_output_endpoint_ambiguous_blocks_and_remaining_anchor_cannot_rescue_consumer()
    {
        var graph = Graph(
            Evidence(
                [Domain("A"), Domain("B")],
                [Producer("P", "A"), Consumer("C", "B")],
                [Conversion("X", "A", "B", ["XIN"], ["XO-GOOD", "XO-AMB"])]),
            [
                Route("input", Edge("P", "XIN")),
                Route("good", Edge("XO-GOOD", "C")),
                Route("amb-1", Edge("XO-AMB", "A1")),
                Route("amb-2", Edge("XO-AMB", "A2"))
            ]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.False(Participant(result, "Consumer", "C").Covered);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "PWR-COVERAGE-CONVERSION-OUTPUT-ANCHOR-AMBIGUOUS" &&
            item.SubjectId == "CONVERSION:X:OUTPUT:XO-AMB");
    }

    [Fact]
    public void Conversion_output_endpoint_covers_consumer_only_by_confirmed_conductive_reachability()
    {
        var graph = Graph(
            Evidence(
                [Domain("A"), Domain("B")],
                [Producer("P", "A"), Consumer("C", "B")],
                [Conversion("X", "A", "B", ["XIN"], ["XOUT"])]),
            [Route("input", Edge("P", "XIN")), Route("output", Edge("XOUT", "C"))]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Accepted, result.Status);
        var consumer = Participant(result, "Consumer", "C");
        Assert.True(consumer.Covered);
        Assert.Equal("ConversionOutputConnectivity", consumer.CoverageBasis);
    }

    [Fact]
    public void Missing_converter_side_endpoint_arrays_fail_closed_without_name_or_adjacency_fallback()
    {
        var graph = Graph(
            Evidence(
                [Domain("A"), Domain("B")],
                [Producer("P", "A"), Consumer("C", "B")],
                [Conversion("X", "A", "B", [], [])]),
            [Route("input", Edge("P", "X-INPUT-ANCHOR")), Route("output", Edge("X-OUTPUT-ANCHOR", "C"))]);

        var adapter = _adapter.AdaptAndAnalyze(graph);
        var result = _coverage.Analyze(graph, adapter);

        Assert.Equal(PowerTopologyAnalysisStatus.Accepted, adapter.Analysis!.Status);
        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.False(Participant(result, "Consumer", "C").Covered);
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-COVERAGE-CONVERSION-INPUT-ENDPOINT-EVIDENCE-REQUIRED");
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-COVERAGE-CONVERSION-OUTPUT-ENDPOINT-EVIDENCE-REQUIRED");
    }

    [Fact]
    public void Multilevel_conversion_chain_passes_with_exact_runtime_endpoint_evidence()
    {
        var graph = Graph(
            Evidence(
                [Domain("A"), Domain("B"), Domain("C")],
                [Producer("P", "A"), Consumer("C", "C")],
                [
                    Conversion("X", "A", "B", ["XIN"], ["XOUT"]),
                    Conversion("Y", "B", "C", ["YIN"], ["YOUT"])
                ]),
            [
                Route("a", Edge("P", "XIN")),
                Route("b", Edge("XOUT", "YIN")),
                Route("c", Edge("YOUT", "C"))
            ]);

        var adapter = _adapter.AdaptAndAnalyze(graph);
        var result = _coverage.Analyze(graph, adapter);

        Assert.Equal(PowerTopologyAnalysisStatus.Accepted, adapter.Analysis!.Status);
        Assert.Equal(["X", "Y"], adapter.Analysis.ConversionTopologicalOrder);
        Assert.Equal(PowerEndpointCoverageStatus.Accepted, result.Status);
        Assert.True(Participant(result, "ConversionInput", "XIN").Covered);
        Assert.True(Participant(result, "ConversionInput", "YIN").Covered);
        Assert.True(Participant(result, "Consumer", "C").Covered);
    }

    [Fact]
    public void Multilevel_chain_blocks_when_intermediate_input_is_unreachable()
    {
        var graph = Graph(
            Evidence(
                [Domain("A"), Domain("B"), Domain("C")],
                [Producer("P", "A"), Consumer("C", "C")],
                [
                    Conversion("X", "A", "B", ["XIN"], ["XOUT"]),
                    Conversion("Y", "B", "C", ["YIN"], ["YOUT"])
                ]),
            [
                Route("a", Edge("P", "XIN")),
                Route("b-source", Edge("XOUT", "B-OTHER")),
                Route("b-load", Edge("YIN", "B-ISOLATED")),
                Route("c", Edge("YOUT", "C"))
            ]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "PWR-COVERAGE-CONVERSION-INPUT-UNREACHABLE" &&
            item.SubjectId == "CONVERSION:Y:INPUT:YIN");
    }

    [Fact]
    public void Endpoint_array_and_collection_permutations_are_logically_identical()
    {
        var firstEvidence = Evidence(
            [Domain("A"), Domain("B")],
            [Producer("P", "A"), Consumer("C", "B")],
            [Conversion("X", "A", "B", ["XI2", "XI1"], ["XO2", "XO1"])]);
        var firstRoutes = new[]
        {
            Route("input", Edge("P", "XI1"), Edge("P", "XI2")),
            Route("output", Edge("XO1", "C"), Edge("XO2", "UNUSED"))
        };
        var first = Analyze(Graph(firstEvidence, firstRoutes));

        var secondEvidence = Evidence(
            [Domain("B"), Domain("A")],
            [Consumer("C", "B"), Producer("P", "A")],
            [Conversion("X", "A", "B", ["XI1", "XI2"], ["XO1", "XO2"])]);
        var secondRoutes = firstRoutes.Reverse().Select(route => route with
        {
            Nodes = route.Nodes.Reverse().ToArray(),
            Segments = route.Segments.Reverse().Select(segment => segment with
            {
                FromNodeId = segment.ToNodeId,
                ToNodeId = segment.FromNodeId
            }).ToArray()
        }).ToArray();
        var second = Analyze(Graph(secondEvidence, secondRoutes));

        Assert.Equal(Fingerprint(first), Fingerprint(second));
    }

    [Fact]
    public void E1_runtime_endpoint_collision_blocker_and_empty_side_cannot_be_bypassed()
    {
        var evidence = Evidence(
            [Domain("A"), Domain("B")],
            [Producer("P", "A"), Consumer("C", "B")],
            [Conversion("X", "A", "B", ["XIN"], [])],
            [new ElectricalPowerEvidenceBlocker
            {
                Code = "POWER_CONVERSION_OUTPUT_RUNTIME_ENDPOINT_ID_COLLISION",
                SubjectId = "X",
                MissingFields = ["runtimeEndpointId:COLLISION", "Port:A", "Pin:B"]
            }]);
        var graph = Graph(evidence, [Route("input", Edge("P", "XIN")), Route("noise", Edge("COLLISION", "C"))]);
        var adapter = _adapter.AdaptAndAnalyze(graph);

        var result = _coverage.Analyze(graph, adapter);

        Assert.Equal(PowerTopologyAdapterStatus.Blocked, adapter.Status);
        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-COVERAGE-ADAPTER-NOT-ACCEPTED");
    }

    [Fact]
    public void Cycle_semantic_blocker_retains_precedence_over_physical_connectivity()
    {
        var graph = Graph(
            Evidence(
                [Domain("A"), Domain("B")],
                [Producer("P", "A")],
                [
                    Conversion("X", "A", "B", ["XIN"], ["XOUT"]),
                    Conversion("Y", "B", "A", ["YIN"], ["YOUT"])
                ]),
            [Route("all", Edge("P", "XIN"), Edge("XOUT", "YIN"), Edge("YOUT", "P"))]);
        var adapter = _adapter.AdaptAndAnalyze(graph);

        var result = _coverage.Analyze(graph, adapter);

        Assert.Equal(PowerTopologyAnalysisStatus.Blocked, adapter.Analysis!.Status);
        Assert.Contains(adapter.Analysis.Diagnostics, item => item.Code == "PWR-CYCLE");
        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-COVERAGE-DOMAIN-ANALYSIS-BLOCKED");
    }

    [Fact]
    public void Weak_signal_node_name_cannot_replace_exact_output_endpoint_identity()
    {
        var evidence = Evidence(
            [Domain("A"), Domain("B")],
            [Producer("P", "A"), Consumer("C", "B")],
            [Conversion("X", "A", "B", ["XIN"], ["XOUT"])]);
        var noiseRoute = RouteWith(
            "noise",
            [
                Node("node:X-OUTPUT-ANCHOR", "NOT-XOUT"),
                Node("node:C", "C")
            ],
            [Segment("segment:noise:00", "node:X-OUTPUT-ANCHOR", "node:C")]);
        var graph = Graph(evidence, [Route("input", Edge("P", "XIN")), noiseRoute]);

        var result = Analyze(graph);

        Assert.Equal(PowerEndpointCoverageStatus.Blocked, result.Status);
        Assert.False(Participant(result, "Consumer", "C").Covered);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "PWR-COVERAGE-CONVERSION-OUTPUT-ANCHOR-MISSING" &&
            item.SubjectId == "CONVERSION:X:OUTPUT:XOUT");
    }

    private PowerEndpointCoverageResult Analyze(AutocadStagingGraphV2Contract graph) =>
        _coverage.Analyze(graph, _adapter.AdaptAndAnalyze(graph));

    private static PowerEndpointCoverageParticipant Participant(
        PowerEndpointCoverageResult result,
        string role,
        string endpointId) =>
        Assert.Single(result.Participants, item => item.Role == role && item.EndpointId == endpointId);

    private static ElectricalPowerEvidenceV1Contract Evidence(
        IReadOnlyList<ElectricalPowerEvidenceDomain> domains,
        IReadOnlyList<ElectricalPowerEvidenceParticipant> participants,
        IReadOnlyList<ElectricalPowerEvidenceConversion> conversions,
        IReadOnlyList<ElectricalPowerEvidenceBlocker>? blockers = null) => new()
    {
        SchemaVersion = ElectricalPowerEvidenceV1Contract.SupportedSchemaVersion,
        Domains = domains,
        Participants = participants,
        Conversions = conversions,
        BlockingRequirements = blockers ?? []
    };

    private static ElectricalPowerEvidenceDomain Domain(string id) => new()
    {
        PowerDomainId = id,
        EvidenceStatus = "Confirmed"
    };

    private static ElectricalPowerEvidenceParticipant Producer(string id, string domain) =>
        ParticipantFact(id, "Producer", domain);

    private static ElectricalPowerEvidenceParticipant Consumer(string id, string domain) =>
        ParticipantFact(id, "Consumer", domain);

    private static ElectricalPowerEvidenceParticipant ParticipantFact(string id, string role, string domain) => new()
    {
        EndpointId = id,
        ComponentInstanceId = "component-" + id,
        Role = role,
        PowerDomainId = domain,
        EvidenceStatus = "Confirmed"
    };

    private static ElectricalPowerEvidenceConversion Conversion(
        string id,
        string inputDomain,
        string outputDomain,
        IReadOnlyList<string> inputEndpoints,
        IReadOnlyList<string> outputEndpoints) => new()
    {
        ConversionId = id,
        ComponentInstanceId = "component-" + id,
        InputPowerDomainId = inputDomain,
        OutputPowerDomainId = outputDomain,
        InputEndpointIds = inputEndpoints,
        OutputEndpointIds = outputEndpoints,
        EvidenceStatus = "Confirmed"
    };

    private static AutocadStagingGraphV2Contract Graph(
        ElectricalPowerEvidenceV1Contract evidence,
        IReadOnlyList<AutocadStagingRoute> routes,
        IReadOnlyList<AutocadStagingTerminalContinuity>? continuities = null) => new()
    {
        SchemaVersion = AutocadStagingGraphV2Contract.SupportedSchemaVersion,
        SourceGraphSchemaVersion = "lrdu-staging-route.v1",
        ProjectId = "conversion-endpoint-v2",
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

    private static (string From, string To) Edge(string from, string to) => (from, to);

    private static AutocadStagingRoute Route(string id, params (string From, string To)[] edges)
    {
        var endpoints = edges.SelectMany(edge => new[] { edge.From, edge.To })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        return RouteWith(
            id,
            endpoints.Select(endpoint => Node("node:" + endpoint, endpoint)).ToArray(),
            edges.Select((edge, index) => Segment(
                $"segment:{id}:{index:D2}",
                "node:" + edge.From,
                "node:" + edge.To)).ToArray());
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

    private static AutocadStagingNode Node(string nodeId, string endpointId) => new()
    {
        NodeId = nodeId,
        Kind = "ComponentPin",
        PinId = endpointId
    };

    private static AutocadStagingSegment Segment(string id, string fromNodeId, string toNodeId) => new()
    {
        SegmentId = id,
        Kind = "InternalWire",
        FromNodeId = fromNodeId,
        ToNodeId = toNodeId,
        TopologyStatus = "Confirmed",
        ProcurementStatus = "NotApplicable",
        DrawingRepresentation = "DirectWire",
        BomRequired = false,
        InstalledLengthStatus = "NotApplicable"
    };

    private static string Fingerprint(PowerEndpointCoverageResult result) => string.Join("|",
        result.Status,
        string.Join(",", result.Participants.Select(item =>
            $"{item.Role}:{item.EndpointId}:{item.DomainId}:{item.NodeId}:{item.Covered}:{item.CoverageBasis}")),
        string.Join(",", result.Diagnostics.Select(item => $"{item.Code}:{item.SubjectId}:{item.Message}")));
}
