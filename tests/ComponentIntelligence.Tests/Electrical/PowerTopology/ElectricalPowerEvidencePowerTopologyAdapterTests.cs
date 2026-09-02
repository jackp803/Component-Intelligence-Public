using ComponentIntelligence.Electrical.Export;
using ComponentIntelligence.Electrical.PowerTopology;

namespace ComponentIntelligence.Tests.Electrical.PowerTopology;

public sealed class ElectricalPowerEvidencePowerTopologyAdapterTests
{
    private readonly ElectricalPowerEvidencePowerTopologyAdapter _adapter = new();

    [Fact]
    public void Supported_schemas_map_explicit_producer_consumer_and_accept_direct_analysis()
    {
        var evidence = Evidence(
            domains: [Domain("A")],
            participants: [Producer("P", "A"), Consumer("C", "A")]);

        var result = _adapter.AdaptAndAnalyze(Graph(evidence));

        Assert.Equal(PowerTopologyAdapterStatus.Accepted, result.Status);
        Assert.NotNull(result.Input);
        Assert.NotNull(result.Analysis);
        Assert.Equal(PowerTopologyAnalysisStatus.Accepted, result.Analysis!.Status);
        Assert.Equal(["A"], result.Input!.Domains.Select(item => item.DomainId));
        Assert.Equal(["P"], result.Input.Producers.Select(item => item.ProducerId));
        Assert.Equal(["C"], result.Input.Consumers.Select(item => item.ConsumerId));
        Assert.Empty(result.Input.Conversions);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Explicit_multilevel_conversions_produce_canonical_topological_order()
    {
        var evidence = Evidence(
            domains: [Domain("C"), Domain("A"), Domain("B")],
            participants: [Consumer("LOAD", "C"), Producer("SOURCE", "A")],
            conversions:
            [
                Conversion("Y", "B", "C"),
                Conversion("X", "A", "B")
            ]);

        var result = _adapter.AdaptAndAnalyze(Graph(evidence));

        Assert.Equal(PowerTopologyAdapterStatus.Accepted, result.Status);
        Assert.Equal(PowerTopologyAnalysisStatus.Accepted, result.Analysis!.Status);
        Assert.Equal(["X", "Y"], result.Analysis.ConversionTopologicalOrder);
    }

    [Fact]
    public void Upstream_power_domain_blocker_fails_closed_before_analysis()
    {
        var evidence = Evidence(
            domains: [Domain("A")],
            participants: [Producer("P", "A")],
            blockers:
            [
                new ElectricalPowerEvidenceBlocker
                {
                    Code = "POWER_DOMAIN_ID_REQUIRED",
                    SubjectId = "consumer-endpoint",
                    MissingFields = ["powerDomainId"]
                }
            ]);

        var result = _adapter.AdaptAndAnalyze(Graph(evidence));

        Assert.Equal(PowerTopologyAdapterStatus.Blocked, result.Status);
        Assert.Null(result.Input);
        Assert.Null(result.Analysis);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("POWER_DOMAIN_ID_REQUIRED", diagnostic.Code);
        Assert.Equal("consumer-endpoint", diagnostic.SubjectId);
        Assert.Equal(["powerDomainId"], diagnostic.MissingFields);
    }

    [Fact]
    public void Incomplete_conversion_blocker_fails_closed_without_inferred_conversion()
    {
        var evidence = Evidence(
            domains: [Domain("A"), Domain("B")],
            participants: [Producer("P", "A"), Consumer("C", "B")],
            conversions:
            [
                new ElectricalPowerEvidenceConversion
                {
                    ConversionId = "X",
                    ComponentInstanceId = "device-X",
                    InputPowerDomainId = "A",
                    OutputPowerDomainId = null,
                    EvidenceStatus = "Unknown",
                    BlockingReason = "POWER_CONVERSION_FIELDS_REQUIRED"
                }
            ],
            blockers:
            [
                new ElectricalPowerEvidenceBlocker
                {
                    Code = "POWER_CONVERSION_FIELDS_REQUIRED",
                    SubjectId = "X",
                    MissingFields = ["outputPowerDomainId"]
                }
            ]);

        var result = _adapter.AdaptAndAnalyze(Graph(evidence));

        Assert.Equal(PowerTopologyAdapterStatus.Blocked, result.Status);
        Assert.Null(result.Input);
        Assert.Null(result.Analysis);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "POWER_CONVERSION_FIELDS_REQUIRED" &&
            item.MissingFields.SequenceEqual(["outputPowerDomainId"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("electrical-power-evidence.v0")]
    public void Missing_or_unsupported_power_evidence_schema_is_blocked(string schemaVersion)
    {
        var evidence = Evidence([Domain("A")], [Producer("P", "A")]) with
        {
            SchemaVersion = schemaVersion
        };

        var result = _adapter.AdaptAndAnalyze(Graph(evidence));

        Assert.Equal(PowerTopologyAdapterStatus.Blocked, result.Status);
        Assert.Null(result.Analysis);
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-ADAPTER-POWER-SCHEMA-UNSUPPORTED");
    }

    [Fact]
    public void Unsupported_outer_engineering_graph_schema_is_blocked()
    {
        var graph = Graph(Evidence([Domain("A")], [Producer("P", "A")])) with
        {
            SchemaVersion = "lrdu-staging-route.v1"
        };

        var result = _adapter.AdaptAndAnalyze(graph);

        Assert.Equal(PowerTopologyAdapterStatus.Blocked, result.Status);
        Assert.Null(result.Analysis);
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-ADAPTER-OUTER-SCHEMA-UNSUPPORTED");
    }

    [Theory]
    [InlineData("domain")]
    [InlineData("participant")]
    [InlineData("conversion")]
    public void Duplicate_stable_identity_is_blocked(string duplicateKind)
    {
        var domains = new List<ElectricalPowerEvidenceDomain> { Domain("A"), Domain("B") };
        var participants = new List<ElectricalPowerEvidenceParticipant> { Producer("P", "A"), Consumer("C", "B") };
        var conversions = new List<ElectricalPowerEvidenceConversion> { Conversion("X", "A", "B") };

        switch (duplicateKind)
        {
            case "domain": domains.Add(Domain("A")); break;
            case "participant": participants.Add(Consumer("P", "B")); break;
            case "conversion": conversions.Add(Conversion("X", "A", "B")); break;
        }

        var result = _adapter.AdaptAndAnalyze(Graph(Evidence(domains, participants, conversions)));

        Assert.Equal(PowerTopologyAdapterStatus.Blocked, result.Status);
        Assert.Null(result.Analysis);
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-ADAPTER-DUPLICATE-IDENTITY");
    }

    [Fact]
    public void Return_and_unknown_participants_are_not_mapped_as_producer_or_consumer()
    {
        var evidence = Evidence(
            domains: [Domain("A")],
            participants:
            [
                Producer("P", "A"),
                Consumer("C", "A"),
                Participant("R", "Return", "A", "Confirmed"),
                Participant("U", "Unknown", "A", "Unknown")
            ]);

        var result = _adapter.AdaptAndAnalyze(Graph(evidence));

        Assert.Equal(PowerTopologyAdapterStatus.Accepted, result.Status);
        Assert.Equal(["P"], result.Input!.Producers.Select(item => item.ProducerId));
        Assert.Equal(["C"], result.Input.Consumers.Select(item => item.ConsumerId));
    }

    [Fact]
    public void Drawing_page_role_and_endpoint_order_noise_does_not_change_adapter_facts()
    {
        var evidence = Evidence(
            domains: [Domain("A")],
            participants: [Producer("P", "A"), Consumer("C", "A")]);

        var quiet = _adapter.AdaptAndAnalyze(Graph(evidence, noisyDrawingEvidence: false));
        var noisy = _adapter.AdaptAndAnalyze(Graph(evidence, noisyDrawingEvidence: true));

        Assert.Equal(Fingerprint(quiet), Fingerprint(noisy));
    }

    [Fact]
    public void Input_collection_permutation_produces_identical_canonical_adapter_and_analysis_result()
    {
        var first = Evidence(
            domains: [Domain("A"), Domain("B"), Domain("C")],
            participants: [Producer("P", "A"), Consumer("C2", "C"), Consumer("C1", "C")],
            conversions: [Conversion("X", "A", "B"), Conversion("Y", "B", "C")]);
        var reversed = Evidence(
            domains: first.Domains.Reverse().ToArray(),
            participants: first.Participants.Reverse().ToArray(),
            conversions: first.Conversions.Reverse().ToArray());

        var left = _adapter.AdaptAndAnalyze(Graph(first));
        var right = _adapter.AdaptAndAnalyze(Graph(reversed));

        Assert.Equal(Fingerprint(left), Fingerprint(right));
    }

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
        Participant(id, "Producer", domain, "Confirmed");

    private static ElectricalPowerEvidenceParticipant Consumer(string id, string domain) =>
        Participant(id, "Consumer", domain, "Confirmed");

    private static ElectricalPowerEvidenceParticipant Participant(
        string id,
        string role,
        string? domain,
        string status) => new()
    {
        EndpointId = id,
        ComponentInstanceId = "component-" + id,
        Role = role,
        PowerDomainId = domain,
        EvidenceStatus = status
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
        bool noisyDrawingEvidence = false) => new()
    {
        SchemaVersion = AutocadStagingGraphV2Contract.SupportedSchemaVersion,
        SourceGraphSchemaVersion = noisyDrawingEvidence ? "source-with-model-name-TypeKey-48V" : "source",
        ProjectId = noisyDrawingEvidence ? "project-with-geometry-page-position-noise" : "project",
        ExportMode = noisyDrawingEvidence ? "drawing-noise" : "ValidateOnly",
        Routes = [],
        PageIntents = [],
        PowerFlowOrientation = noisyDrawingEvidence
            ?
            [
                new AutocadStagingV2PowerFlowEvidence
                {
                    PageId = "PAGE-NOISE",
                    NetIdentity = "NET-LABEL-NOISE",
                    Orientation = "LeftToRight",
                    SourceDirectionStatus = "Confirmed",
                    ConfirmedSourceTrunks = [new object()],
                    VerticalDrops = [new object()],
                    Conversions = [new object()],
                    SourceEndpointId = "destination-if-order-were-used",
                    DestinationEndpointId = "source-if-order-were-used",
                    BlockingReason = "DRAWING_ONLY"
                }
            ]
            : [],
        PowerEvidence = evidence,
        CableFamilies = [],
        CableInstances = [],
        TerminalContinuities = [],
        CrossPageContinuations = [],
        DeviceRoles = noisyDrawingEvidence
            ?
            [
                new AutocadStagingV2DeviceRole
                {
                    ComponentInstanceId = "MODEL-TYPEKEY-VOLTAGE-NAME-NOISE",
                    DeviceRole = "FunctionalPowerDevice",
                    RepresentationEvidence = "x=42,y=99,page=P99"
                }
            ]
            : [],
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

    private static string Fingerprint(PowerTopologyAdapterResult result)
    {
        var input = result.Input;
        var analysis = result.Analysis;
        return string.Join("|",
            result.Status,
            input is null ? "-" : string.Join(",", input.Domains.Select(item => item.DomainId)),
            input is null ? "-" : string.Join(",", input.Producers.Select(item => item.ProducerId + ":" + item.DomainId)),
            input is null ? "-" : string.Join(",", input.Consumers.Select(item => item.ConsumerId + ":" + item.DomainId)),
            input is null ? "-" : string.Join(",", input.Conversions.Select(item => item.ConversionId + ":" + item.InputDomainId + ">" + item.OutputDomainId)),
            analysis?.Status.ToString() ?? "-",
            analysis is null ? "-" : string.Join(",", analysis.ConversionTopologicalOrder),
            analysis is null ? "-" : string.Join(",", analysis.Diagnostics.Select(item => item.Code + ":" + item.SubjectId)),
            string.Join(",", result.Diagnostics.Select(item => item.Code + ":" + item.SubjectId + ":" + string.Join("+", item.MissingFields))));
    }
}
