using ComponentIntelligence.Electrical.Export;
using ComponentIntelligence.Electrical.PowerTopology;

namespace ComponentIntelligence.Tests.Electrical.PowerTopology.Conformance;

/// <summary>
/// SYNTHETIC / TEST-ONLY Power Topology Conformance Corpus v1.
/// These fixtures are not hardware evidence, AutoCAD acceptance, DWG/WDP evidence, or Product Owner UAT.
/// They exercise only accepted E1 Engineering Graph identities and accepted E2 semantics.
/// </summary>
internal static class PowerTopologyConformanceCorpusV1
{
    internal const string Version = "power-topology-conformance.v1";

    internal static IReadOnlyList<PowerTopologyConformanceCase> Cases { get; } =
    [
        new(
            "ready-direct",
            "READY",
            Graph(
                Evidence([Domain("A")], [Producer("P", "A"), Consumer("C", "A")]),
                [Route("direct", Edge("P", "C"))]),
            ["A"], ["P>A"], ["C<A"], [], [],
            ["Consumer:C:A:true", "Producer:P:A:true"], [], []),

        new(
            "ready-fanout",
            "READY",
            Graph(
                Evidence([Domain("A")], [Producer("P", "A"), Consumer("C1", "A"), Consumer("C2", "A")]),
                [Route("fanout", Edge("P", "C1"), Edge("P", "C2"))]),
            ["A"], ["P>A"], ["C1<A", "C2<A"], [], [],
            ["Consumer:C1:A:true", "Consumer:C2:A:true", "Producer:P:A:true"], [], []),

        new(
            "ready-multilevel-conversion",
            "READY",
            Graph(
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
                ]),
            ["A", "B", "C"], ["P>A"], ["C<C"], ["X:A>B", "Y:B>C"], ["X", "Y"],
            [
                "Consumer:C:C:true",
                "ConversionInput:XIN:A:true",
                "ConversionInput:YIN:B:true",
                "ConversionOutput:XOUT:B:true",
                "ConversionOutput:YOUT:C:true",
                "Producer:P:A:true"
            ], [], []),

        new(
            "ready-terminal-transparency",
            "READY",
            Graph(
                Evidence([Domain("A")], [Producer("P", "A"), Consumer("C", "A")]),
                [Route("left", Edge("P", "T1")), Route("right", Edge("T2", "C"))],
                [Continuity("K1", "T1", "T2")]),
            ["A"], ["P>A"], ["C<A"], [], [],
            ["Consumer:C:A:true", "Producer:P:A:true"], [], []),

        new(
            "ready-order-invariant-conversion",
            "READY",
            Graph(
                Evidence(
                    [Domain("C"), Domain("A"), Domain("B")],
                    [Consumer("C", "C"), Producer("P", "A")],
                    [
                        Conversion("Y", "B", "C", ["YIN"], ["YOUT"]),
                        Conversion("X", "A", "B", ["XIN"], ["XOUT"])
                    ]),
                [
                    Route("c", Edge("C", "YOUT")),
                    Route("a", Edge("XIN", "P")),
                    Route("b", Edge("YIN", "XOUT"))
                ]),
            ["A", "B", "C"], ["P>A"], ["C<C"], ["X:A>B", "Y:B>C"], ["X", "Y"],
            [
                "Consumer:C:C:true",
                "ConversionInput:XIN:A:true",
                "ConversionInput:YIN:B:true",
                "ConversionOutput:XOUT:B:true",
                "ConversionOutput:YOUT:C:true",
                "Producer:P:A:true"
            ], [], []),

        new(
            "blocked-missing-producer",
            "BLOCKED",
            Graph(
                Evidence([Domain("A")], [Consumer("C", "A")]),
                [Route("load", Edge("C", "OTHER"))]),
            ["A"], [], ["C<A"], [], [], [],
            ["PWR-MISSING-PRODUCER@DOMAIN:A", "PWR-UNREACHABLE-CONSUMER@CONSUMER:C"],
            ["PWR-COVERAGE-DOMAIN-ANALYSIS-BLOCKED@POWER_TOPOLOGY_ANALYSIS"]),

        new(
            "blocked-orphan-converter",
            "BLOCKED",
            Graph(
                Evidence(
                    [Domain("A"), Domain("B")],
                    [],
                    [Conversion("X", "A", "B", ["XIN"], ["XOUT"])]),
                [Route("converter", Edge("XIN", "XOUT"))]),
            ["A", "B"], [], [], ["X:A>B"], ["X"], [],
            ["PWR-MISSING-PRODUCER@DOMAIN:A", "PWR-ORPHAN-CONVERSION@CONVERSION:X"],
            ["PWR-COVERAGE-DOMAIN-ANALYSIS-BLOCKED@POWER_TOPOLOGY_ANALYSIS"]),

        new(
            "blocked-duplicate-producer",
            "BLOCKED",
            Graph(
                Evidence(
                    [Domain("A")],
                    [Producer("P1", "A"), Producer("P2", "A"), Consumer("C", "A")]),
                [Route("p1", Edge("P1", "C")), Route("p2", Edge("P2", "OTHER"))]),
            ["A"], ["P1>A", "P2>A"], ["C<A"], [], [], [],
            ["PWR-DUPLICATE-PRODUCER@DOMAIN:A"],
            ["PWR-COVERAGE-DOMAIN-ANALYSIS-BLOCKED@POWER_TOPOLOGY_ANALYSIS"]),

        new(
            "blocked-conversion-cycle",
            "BLOCKED",
            Graph(
                Evidence(
                    [Domain("A"), Domain("B")],
                    [],
                    [
                        Conversion("Y", "B", "A", ["YIN"], ["YOUT"]),
                        Conversion("X", "A", "B", ["XIN"], ["XOUT"])
                    ]),
                [Route("cycle-a", Edge("XOUT", "YIN")), Route("cycle-b", Edge("YOUT", "XIN"))]),
            ["A", "B"], [], [], ["X:A>B", "Y:B>A"], [], [],
            [
                "PWR-CYCLE@CONVERSIONS:X,Y",
                "PWR-ORPHAN-CONVERSION@CONVERSION:X",
                "PWR-ORPHAN-CONVERSION@CONVERSION:Y"
            ],
            ["PWR-COVERAGE-DOMAIN-ANALYSIS-BLOCKED@POWER_TOPOLOGY_ANALYSIS"]),

        new(
            "blocked-converter-output-missing",
            "BLOCKED",
            Graph(
                Evidence(
                    [Domain("A"), Domain("B")],
                    [Producer("P", "A"), Consumer("C", "B")],
                    [Conversion("X", "A", "B", ["XIN"], ["XOUT"])]),
                [Route("input", Edge("P", "XIN")), Route("consumer", Edge("C", "UNRELATED"))]),
            ["A", "B"], ["P>A"], ["C<B"], ["X:A>B"], ["X"],
            [
                "Consumer:C:B:false",
                "ConversionInput:XIN:A:true",
                "ConversionOutput:XOUT:B:false",
                "Producer:P:A:true"
            ], [],
            ["PWR-COVERAGE-CONVERSION-OUTPUT-ANCHOR-MISSING@CONVERSION:X:OUTPUT:XOUT"]),

        new(
            "blocked-converter-input-ambiguous",
            "BLOCKED",
            Graph(
                Evidence(
                    [Domain("A"), Domain("B")],
                    [Producer("P", "A"), Consumer("C", "B")],
                    [Conversion("X", "A", "B", ["XIN"], ["XOUT"])]),
                [
                    Route("input-1", Edge("P", "XIN")),
                    Route("input-2", Edge("XIN", "OTHER")),
                    Route("output", Edge("XOUT", "C"))
                ]),
            ["A", "B"], ["P>A"], ["C<B"], ["X:A>B"], ["X"],
            [
                "Consumer:C:B:true",
                "ConversionInput:XIN:A:false",
                "ConversionOutput:XOUT:B:true",
                "Producer:P:A:true"
            ], [],
            ["PWR-COVERAGE-CONVERSION-INPUT-ANCHOR-AMBIGUOUS@CONVERSION:X:INPUT:XIN"]),

        new(
            "blocked-converter-empty-side",
            "BLOCKED",
            Graph(
                Evidence(
                    [Domain("A"), Domain("B")],
                    [Producer("P", "A"), Consumer("C", "B")],
                    [Conversion("X", "A", "B", [], [])]),
                [Route("input-noise", Edge("P", "X-INPUT-NAME")), Route("output-noise", Edge("X-OUTPUT-NAME", "C"))]),
            ["A", "B"], ["P>A"], ["C<B"], ["X:A>B"], ["X"],
            ["Consumer:C:B:false", "Producer:P:A:true"], [],
            [
                "PWR-COVERAGE-CONVERSION-INPUT-ENDPOINT-EVIDENCE-REQUIRED@CONVERSION:X:INPUT",
                "PWR-COVERAGE-CONVERSION-OUTPUT-ENDPOINT-EVIDENCE-REQUIRED@CONSUMER:C",
                "PWR-COVERAGE-CONVERSION-OUTPUT-ENDPOINT-EVIDENCE-REQUIRED@CONVERSION:X:OUTPUT"
            ]),

        new(
            "blocked-stale-endpoint-identity",
            "BLOCKED",
            Graph(
                Evidence([Domain("A")], [Producer("P", "A"), Consumer("C-NEW", "A")]),
                [RouteWith(
                    "stale",
                    [Node("node:P", "P"), Node("node:C-NEW", "C-OLD")],
                    [Segment("segment:stale:00", "node:P", "node:C-NEW")])]),
            ["A"], ["P>A"], ["C-NEW<A"], [], [],
            ["Consumer:C-NEW:A:false", "Producer:P:A:true"], [],
            ["PWR-COVERAGE-PARTICIPANT-ANCHOR-MISSING@CONSUMER:C-NEW"])
    ];

    internal static string ManifestIndex() => string.Join("\n", Cases
        .OrderBy(item => item.CaseId, StringComparer.Ordinal)
        .Select(item => string.Join("|",
            Version,
            item.CaseId,
            item.ExpectedDisposition,
            string.Join(",", item.ExpectedDomainIds),
            string.Join(",", item.ExpectedProducers),
            string.Join(",", item.ExpectedConsumers),
            string.Join(",", item.ExpectedConversionEdges),
            string.Join(",", item.ExpectedTopologicalOrder),
            string.Join(",", item.ExpectedCoverage),
            string.Join(",", item.ExpectedSemanticBlockers),
            string.Join(",", item.ExpectedCoverageBlockers))));

    internal static AutocadStagingGraphV2Contract PermuteOrderInvariantCase(AutocadStagingGraphV2Contract graph)
    {
        var evidence = graph.PowerEvidence!;
        return graph with
        {
            PowerEvidence = evidence with
            {
                Domains = evidence.Domains.Reverse().ToArray(),
                Participants = evidence.Participants.Reverse().ToArray(),
                Conversions = evidence.Conversions.Reverse().Select(item => item with
                {
                    InputEndpointIds = item.InputEndpointIds.Reverse().ToArray(),
                    OutputEndpointIds = item.OutputEndpointIds.Reverse().ToArray()
                }).ToArray()
            },
            Routes = graph.Routes.Reverse().Select(route => route with
            {
                Nodes = route.Nodes.Reverse().ToArray(),
                Segments = route.Segments.Reverse().Select(segment => segment with
                {
                    FromNodeId = segment.ToNodeId,
                    ToNodeId = segment.FromNodeId
                }).ToArray()
            }).ToArray()
        };
    }

    private static ElectricalPowerEvidenceV1Contract Evidence(
        IReadOnlyList<ElectricalPowerEvidenceDomain> domains,
        IReadOnlyList<ElectricalPowerEvidenceParticipant> participants,
        IReadOnlyList<ElectricalPowerEvidenceConversion>? conversions = null) => new()
    {
        SchemaVersion = ElectricalPowerEvidenceV1Contract.SupportedSchemaVersion,
        Domains = domains,
        Participants = participants,
        Conversions = conversions ?? [],
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
        ComponentInstanceId = "synthetic-component-" + id,
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
        ComponentInstanceId = "synthetic-converter-" + id,
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
        SourceGraphSchemaVersion = "synthetic-power-topology-conformance.v1",
        ProjectId = "synthetic-power-topology-conformance-v1",
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
        NetIdentity = "synthetic-net:" + id,
        VisibleLabel = "SYNTHETIC-" + id,
        TopologyStatus = "Confirmed",
        Responsibility = new AutocadStagingResponsibility { Owner = "TEST-ONLY" },
        Nodes = nodes,
        Segments = segments,
        Shield = new AutocadStagingShieldRoute { Status = "NotApplicable" }
    };

    private static AutocadStagingNode Node(string nodeId, string endpointId) => new()
    {
        NodeId = nodeId,
        Kind = endpointId.StartsWith("T", StringComparison.Ordinal) ? "Terminal" : "ComponentPin",
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

    private static AutocadStagingTerminalContinuity Continuity(string id, string from, string to) => new()
    {
        ContinuityId = id,
        TerminalBlockId = "SYNTHETIC-TB",
        TerminalPositionId = "SYNTHETIC-POS",
        LevelId = "SYNTHETIC-L1",
        FromConnectionPointId = from,
        ToConnectionPointId = to,
        EvidenceStatus = DrawingEvidenceStatus.Confirmed
    };
}

internal sealed record PowerTopologyConformanceCase(
    string CaseId,
    string ExpectedDisposition,
    AutocadStagingGraphV2Contract Graph,
    IReadOnlyList<string> ExpectedDomainIds,
    IReadOnlyList<string> ExpectedProducers,
    IReadOnlyList<string> ExpectedConsumers,
    IReadOnlyList<string> ExpectedConversionEdges,
    IReadOnlyList<string> ExpectedTopologicalOrder,
    IReadOnlyList<string> ExpectedCoverage,
    IReadOnlyList<string> ExpectedSemanticBlockers,
    IReadOnlyList<string> ExpectedCoverageBlockers);