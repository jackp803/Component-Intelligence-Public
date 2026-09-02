using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Export;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class AutocadStagingGraphV2CrossPageContinuationIdentityTests
{
    [Fact]
    public void ConfirmedForwardSegment_BindsExactRouteNetSegmentAndSourceEvidence()
    {
        var source = SourceGraph(
            [Route("route:net-a", "net-a", Segment("segment:wire-1", "node:left:pin", "node:right:pin"))],
            [Continuation("pair-1", "left:pin", "right:pin", "P-01", "P-02")]);

        var graph = CreateV2(source);
        var continuation = Assert.Single(graph.CrossPageContinuations);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(continuation));
        var json = document.RootElement;

        Assert.Equal("pair-1", json.GetProperty("pairIdentity").GetString());
        Assert.Equal("route:net-a", json.GetProperty("routeId").GetString());
        Assert.Equal("net-a", json.GetProperty("netIdentity").GetString());
        Assert.Equal("segment:wire-1", json.GetProperty("segmentId").GetString());
        Assert.Equal("left:pin", json.GetProperty("sourceEndpointId").GetString());
        Assert.Equal("right:pin", json.GetProperty("destinationEndpointId").GetString());
        Assert.Equal("node:left:pin", json.GetProperty("sourceNodeId").GetString());
        Assert.Equal("node:right:pin", json.GetProperty("destinationNodeId").GetString());
        Assert.Equal("P-01", json.GetProperty("sourcePageId").GetString());
        Assert.Equal("P-02", json.GetProperty("destinationPageId").GetString());
        Assert.Equal("Confirmed", json.GetProperty("evidenceStatus").GetString());
        Assert.Equal("engineer-cross-page", json.GetProperty("evidenceSource").GetString());
        Assert.False(json.TryGetProperty("blockingReason", out _));
    }

    [Fact]
    public void ReversedSegmentSerialization_ProducesIdenticalContinuationAndFingerprint()
    {
        var continuation = Continuation("pair-1", "left:pin", "right:pin", "P-01", "P-02");
        var forward = CreateV2(SourceGraph(
            [Route("route:net-a", "net-a", Segment("segment:wire-1", "node:left:pin", "node:right:pin"))],
            [continuation]));
        var reverse = CreateV2(SourceGraph(
            [Route("route:net-a", "net-a", Segment("segment:wire-1", "node:right:pin", "node:left:pin"))],
            [continuation]));

        var forwardJson = JsonSerializer.Serialize(Assert.Single(forward.CrossPageContinuations));
        var reverseJson = JsonSerializer.Serialize(Assert.Single(reverse.CrossPageContinuations));

        Assert.Equal(forwardJson, reverseJson);
        Assert.Equal(Fingerprint(forwardJson), Fingerprint(reverseJson));
    }

    [Fact]
    public void ReversedSegmentSerialization_DoesNotSwapExplicitSourceDestinationRolesOrPages()
    {
        var graph = CreateV2(SourceGraph(
            [Route("route:net-a", "net-a", Segment("segment:wire-1", "node:right:pin", "node:left:pin"))],
            [Continuation("pair-1", "left:pin", "right:pin", "SOURCE-PAGE", "DEST-PAGE")]));

        var continuation = Assert.Single(graph.CrossPageContinuations);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(continuation));
        var json = document.RootElement;

        Assert.Equal("left:pin", json.GetProperty("sourceEndpointId").GetString());
        Assert.Equal("right:pin", json.GetProperty("destinationEndpointId").GetString());
        Assert.Equal("node:left:pin", json.GetProperty("sourceNodeId").GetString());
        Assert.Equal("node:right:pin", json.GetProperty("destinationNodeId").GetString());
        Assert.Equal("SOURCE-PAGE", json.GetProperty("sourcePageId").GetString());
        Assert.Equal("DEST-PAGE", json.GetProperty("destinationPageId").GetString());
    }

    [Fact]
    public void ZeroConfirmedCandidate_FailsClosedDeterministically()
    {
        var graph = CreateV2(SourceGraph(
            [Route("route:net-a", "net-a", Segment("segment:other", "node:x", "node:y"))],
            [Continuation("pair-1", "left:pin", "right:pin", "P-01", "P-02")]));

        var continuation = Assert.Single(graph.CrossPageContinuations);
        Assert.Empty(continuation.SegmentId);
        Assert.Equal("EXACT_CONFIRMED_ROUTE_NET_SEGMENT_REQUIRED", continuation.BlockingReason);
    }

    [Fact]
    public void TwoConfirmedCandidates_FailClosedAsAmbiguousWithoutChoosingWinner()
    {
        var graph = CreateV2(SourceGraph(
            [
                Route("route:net-a", "net-a",
                    Segment("segment:a", "node:left:pin", "node:right:pin"),
                    Segment("segment:b", "node:right:pin", "node:left:pin"))
            ],
            [Continuation("pair-1", "left:pin", "right:pin", "P-01", "P-02")]));

        var continuation = Assert.Single(graph.CrossPageContinuations);
        Assert.Empty(continuation.SegmentId);
        Assert.Equal("EXACT_CONFIRMED_ROUTE_NET_SEGMENT_AMBIGUOUS", continuation.BlockingReason);
    }

    [Fact]
    public void UnconfirmedSegment_CannotSatisfyContinuation()
    {
        var graph = CreateV2(SourceGraph(
            [Route("route:net-a", "net-a", Segment("segment:wire-1", "node:left:pin", "node:right:pin", "Unknown"))],
            [Continuation("pair-1", "left:pin", "right:pin", "P-01", "P-02")]));

        var continuation = Assert.Single(graph.CrossPageContinuations);
        Assert.Empty(continuation.SegmentId);
        Assert.Equal("EXACT_CONFIRMED_ROUTE_NET_SEGMENT_REQUIRED", continuation.BlockingReason);
    }

    [Fact]
    public void UnconfirmedContinuationEvidence_RemainsBlockingEvenWithExactRouteCandidate()
    {
        var graph = CreateV2(SourceGraph(
            [Route("route:net-a", "net-a", Segment("segment:wire-1", "node:left:pin", "node:right:pin"))],
            [Continuation("pair-1", "left:pin", "right:pin", "P-01", "P-02", DrawingEvidenceStatus.Unknown)]));

        var continuation = Assert.Single(graph.CrossPageContinuations);
        Assert.Equal("segment:wire-1", continuation.SegmentId);
        Assert.Equal("CONFIRMED_CROSS_PAGE_CONTINUATION_EVIDENCE_REQUIRED", continuation.BlockingReason);
    }

    [Fact]
    public void DuplicatePairIdentity_EvenWhenOtherwiseIdentical_FailsClosed()
    {
        var duplicate = Continuation("pair-1", "left:pin", "right:pin", "P-01", "P-02");
        var graph = CreateV2(SourceGraph(
            [Route("route:net-a", "net-a", Segment("segment:wire-1", "node:left:pin", "node:right:pin"))],
            [duplicate, duplicate]));

        var continuation = Assert.Single(graph.CrossPageContinuations);
        Assert.Equal("pair-1", continuation.PairIdentity);
        Assert.Equal("DUPLICATE_CROSS_PAGE_PAIR_IDENTITY", continuation.BlockingReason);
    }

    [Fact]
    public void ConflictingDuplicatePairIdentity_FailsClosedWithoutSelectingConflictingEvidence()
    {
        var graph = CreateV2(SourceGraph(
            [
                Route("route:net-a", "net-a", Segment("segment:a", "node:left:pin", "node:right:pin")),
                Route("route:net-b", "net-b", Segment("segment:b", "node:other:pin", "node:third:pin"))
            ],
            [
                Continuation("pair-1", "left:pin", "right:pin", "P-01", "P-02"),
                Continuation("pair-1", "other:pin", "third:pin", "P-03", "P-04")
            ]));

        var continuation = Assert.Single(graph.CrossPageContinuations);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(continuation));
        var json = document.RootElement;

        Assert.Equal("DUPLICATE_CROSS_PAGE_PAIR_IDENTITY", continuation.BlockingReason);
        Assert.Equal(string.Empty, json.GetProperty("sourceEndpointId").GetString());
        Assert.Equal(string.Empty, json.GetProperty("destinationEndpointId").GetString());
        Assert.Equal(string.Empty, json.GetProperty("routeId").GetString());
        Assert.Equal(string.Empty, json.GetProperty("netIdentity").GetString());
        Assert.Empty(continuation.SegmentId);
    }

    [Fact]
    public void RouteSegmentAndContinuationPermutations_ProduceIdenticalLogicalV2Contract()
    {
        var routeAForward = Route("route:net-a", "net-a",
            Segment("segment:a-1", "node:a", "node:b"),
            Segment("segment:a-2", "node:c", "node:d"));
        var routeAReverseOrder = Route("route:net-a", "net-a",
            Segment("segment:a-2", "node:c", "node:d"),
            Segment("segment:a-1", "node:a", "node:b"));
        var routeB = Route("route:net-b", "net-b", Segment("segment:b-1", "node:e", "node:f"));
        var continuationA = Continuation("pair-a", "a", "b", "P-01", "P-02");
        var continuationB = Continuation("pair-b", "e", "f", "P-03", "P-04");

        var first = CreateV2(SourceGraph([routeAForward, routeB], [continuationA, continuationB]));
        var second = CreateV2(SourceGraph([routeB, routeAReverseOrder], [continuationB, continuationA]));

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Fact]
    public void VisibleLabelSignalAndNodeDisplayNoise_CannotChangeContinuationSelection()
    {
        var plain = CreateV2(SourceGraph(
            [Route("route:net-a", "net-a", [Segment("segment:wire-1", "node:left:pin", "node:right:pin", signalCode: "PLAIN")], "PLAIN")],
            [Continuation("pair-1", "left:pin", "right:pin", "P-01", "P-02")]));
        var noisy = CreateV2(SourceGraph(
            [Route("route:net-a", "net-a", [Segment("segment:wire-1", "node:left:pin", "node:right:pin", signalCode: "MISLEADING-SIGNAL")], "MISLEADING-LABEL")],
            [Continuation("pair-1", "left:pin", "right:pin", "P-01", "P-02")]));

        Assert.Equal(
            JsonSerializer.Serialize(Assert.Single(plain.CrossPageContinuations)),
            JsonSerializer.Serialize(Assert.Single(noisy.CrossPageContinuations)));
    }

    [Fact]
    public void ExistingNodeConnectionPointBindingTransport_RemainsExactAndDeterministic()
    {
        var source = SourceGraph(
            [RouteWithAuditedNodes("route:net-a", "net-a", Segment("segment:wire-1", "node:left:pin", "node:right:pin"))],
            [Continuation("pair-1", "left:pin", "right:pin", "P-01", "P-02")]);

        var graph = CreateV2(source);
        var route = Assert.Single(graph.Routes);
        var left = Assert.Single(route.Nodes, node => node.NodeId == "node:left:pin");
        var right = Assert.Single(route.Nodes, node => node.NodeId == "node:right:pin");

        Assert.Equal("left:pin", left.PinId);
        Assert.Equal("SYM:left", left.ConnectionPoint?.SymbolKey);
        Assert.Equal("XTERM:left", left.ConnectionPoint?.ConnectionPointId);
        Assert.Equal("right:pin", right.PinId);
        Assert.Equal("SYM:right", right.ConnectionPoint?.SymbolKey);
        Assert.Equal("XTERM:right", right.ConnectionPoint?.ConnectionPointId);
    }

    private static AutocadStagingGraphV2Contract CreateV2(AutocadStagingGraphContract source)
    {
        var method = typeof(AutocadStagingGraphV2Contract).GetMethod(
            "Create", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var graph = method!.Invoke(null,
        [
            source,
            new AutocadEngineeringDrawingEvidence(),
            new ElectricalProject { ProjectId = source.ProjectId }
        ]);
        return Assert.IsType<AutocadStagingGraphV2Contract>(graph);
    }

    private static AutocadStagingGraphContract SourceGraph(
        IReadOnlyList<AutocadStagingRoute> routes,
        IReadOnlyList<AutocadStagingCrossPageContinuation> continuations) => new()
    {
        ProjectId = "cross-page-test",
        Routes = routes,
        CrossPageContinuations = continuations,
        Interventions = []
    };

    private static AutocadStagingRoute Route(
        string routeId,
        string netIdentity,
        params AutocadStagingSegment[] segments) => Route(routeId, netIdentity, segments, netIdentity);

    private static AutocadStagingRoute Route(
        string routeId,
        string netIdentity,
        IReadOnlyList<AutocadStagingSegment> segments,
        string visibleLabel) => new()
    {
        RouteId = routeId,
        NetIdentity = netIdentity,
        VisibleLabel = visibleLabel,
        TopologyStatus = "Confirmed",
        Responsibility = new AutocadStagingResponsibility { Owner = "LRDU" },
        Nodes = [],
        Segments = segments,
        Shield = new AutocadStagingShieldRoute { Status = "NotApplicable" }
    };

    private static AutocadStagingRoute RouteWithAuditedNodes(
        string routeId,
        string netIdentity,
        params AutocadStagingSegment[] segments) => new()
    {
        RouteId = routeId,
        NetIdentity = netIdentity,
        VisibleLabel = "NET-A",
        TopologyStatus = "Confirmed",
        Responsibility = new AutocadStagingResponsibility { Owner = "LRDU" },
        Nodes =
        [
            new AutocadStagingNode
            {
                NodeId = "node:right:pin",
                Kind = "ComponentPin",
                PinId = "right:pin",
                ConnectionPoint = new AutocadStagingConnectionPoint
                {
                    SymbolKey = "SYM:right",
                    ConnectionPointId = "XTERM:right"
                }
            },
            new AutocadStagingNode
            {
                NodeId = "node:left:pin",
                Kind = "ComponentPin",
                PinId = "left:pin",
                ConnectionPoint = new AutocadStagingConnectionPoint
                {
                    SymbolKey = "SYM:left",
                    ConnectionPointId = "XTERM:left"
                }
            }
        ],
        Segments = segments,
        Shield = new AutocadStagingShieldRoute { Status = "NotApplicable" }
    };

    private static AutocadStagingSegment Segment(
        string segmentId,
        string fromNodeId,
        string toNodeId,
        string topologyStatus = "Confirmed",
        string? signalCode = null) => new()
    {
        SegmentId = segmentId,
        Kind = "InternalWire",
        FromNodeId = fromNodeId,
        ToNodeId = toNodeId,
        TopologyStatus = topologyStatus,
        ProcurementStatus = "NotApplicable",
        DrawingRepresentation = "DirectWire",
        SignalCode = signalCode,
        BomRequired = false,
        InstalledLengthStatus = "NotApplicable"
    };

    private static AutocadStagingCrossPageContinuation Continuation(
        string pairIdentity,
        string sourceEndpointId,
        string destinationEndpointId,
        string sourcePageId,
        string destinationPageId,
        DrawingEvidenceStatus status = DrawingEvidenceStatus.Confirmed) => new()
    {
        PairIdentity = pairIdentity,
        SourceEndpointId = sourceEndpointId,
        DestinationEndpointId = destinationEndpointId,
        SourcePageId = sourcePageId,
        DestinationPageId = destinationPageId,
        EvidenceStatus = status,
        EvidenceSource = "engineer-cross-page"
    };

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}