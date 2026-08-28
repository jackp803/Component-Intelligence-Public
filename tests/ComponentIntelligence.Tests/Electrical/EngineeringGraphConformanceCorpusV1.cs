using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Export;

namespace ComponentIntelligence.Tests.Electrical;

internal enum EngineeringGraphConformanceStatus
{
    Ready,
    Blocked
}

internal sealed record EngineeringGraphConformanceCase
{
    public required string FixtureId { get; init; }
    public required EngineeringGraphConformanceStatus ExpectedStatus { get; init; }
    public string? ExpectedFingerprint { get; init; }
    public required IReadOnlyList<string> ImportantIdentities { get; init; }
    public required IReadOnlyList<string> ExpectedBlockerCodes { get; init; }
    public required string Purpose { get; init; }
    public required Func<AutocadStagingGraphV2Contract> BuildGraph { get; init; }
}

internal sealed record EngineeringGraphConformanceEvaluation
{
    public required EngineeringGraphConformanceStatus Status { get; init; }
    public string? Fingerprint { get; init; }
    public required IReadOnlyList<string> BlockerCodes { get; init; }
}

internal static class EngineeringGraphConformanceCorpusV1
{
    public const string CorpusSchemaVersion = "engineering-graph-conformance.v1";
    public const string GraphSchemaVersion = "lrdu-staging-route.v2";
    public const string FingerprintSchemaVersion = "engineering-graph-conformance-fingerprint.v1";

    public static IReadOnlyList<EngineeringGraphConformanceCase> Cases { get; } =
    [
        new()
        {
            FixtureId = "EGC-READY-DIRECT-001",
            ExpectedStatus = EngineeringGraphConformanceStatus.Ready,
            ExpectedFingerprint = "89A5F9A6001FFCC7F4D5F43801BA04F0A8737350EB844481B2E32D6566F3102A",
            ImportantIdentities =
            [
                "route:net-control", "net-control", "node:src:pin-1", "node:load:pin-1",
                "src:pin-1", "load:pin-1", "XTERM:src", "XTERM:load", "segment:wire-direct"
            ],
            ExpectedBlockerCodes = [],
            Purpose = "READY direct single-route identity chain with exact component, node, pin, connection-point, net and segment identity.",
            BuildGraph = () => BuildDirect(reverseCollections: false)
        },
        new()
        {
            FixtureId = "EGC-READY-FANOUT-001",
            ExpectedStatus = EngineeringGraphConformanceStatus.Ready,
            ExpectedFingerprint = "834BEED7010BAB7CDBE11EF93C7FD93B1C68DEFC16B3EA114CADFC5AE6D8746D",
            ImportantIdentities =
            [
                "route:net-shared", "net-shared", "node:fan-src:pin-1", "node:fan-a:pin-1",
                "node:fan-b:pin-1", "fan-src:pin-1", "fan-a:pin-1", "fan-b:pin-1",
                "XTERM:fan-src", "XTERM:fan-a", "XTERM:fan-b", "segment:fan-a", "segment:fan-b"
            ],
            ExpectedBlockerCodes = [],
            Purpose = "READY fanout/shared-node graph with one shared source node and injective endpoint identities.",
            BuildGraph = () => BuildFanout(reverseCollections: false)
        },
        new()
        {
            FixtureId = "EGC-READY-CROSS-PAGE-001",
            ExpectedStatus = EngineeringGraphConformanceStatus.Ready,
            ExpectedFingerprint = "2AF3081230AA113C3A9F365346E00092D2376B2CE869AE398B3DF74358453920",
            ImportantIdentities =
            [
                "pair:cross-001", "route:net-cross", "net-cross", "segment:wire-cross",
                "cross-src:pin-1", "cross-dst:pin-1", "node:cross-src:pin-1", "node:cross-dst:pin-1",
                "P-01", "P-02", "XTERM:cross-src", "XTERM:cross-dst"
            ],
            ExpectedBlockerCodes = [],
            Purpose = "READY cross-page continuation with exact pair, route, net, segment, endpoint, page and node identity.",
            BuildGraph = () => BuildCrossPage(reverseCollections: false)
        },
        new()
        {
            FixtureId = "EGC-BLOCKED-DUPLICATE-ENDPOINT-001",
            ExpectedStatus = EngineeringGraphConformanceStatus.Blocked,
            ImportantIdentities = ["dup-pin:pin-1", "node:dup-a", "node:dup-b"],
            ExpectedBlockerCodes = ["EGC_DUPLICATE_PIN_ENDPOINT_IDENTITY"],
            Purpose = "BLOCKED two distinct nodes claiming the same exact pin endpoint identity.",
            BuildGraph = BuildDuplicateEndpoint
        },
        new()
        {
            FixtureId = "EGC-BLOCKED-CONTINUATION-MISSING-001",
            ExpectedStatus = EngineeringGraphConformanceStatus.Blocked,
            ImportantIdentities = ["pair:missing-001", "missing-src:pin-1", "missing-dst:pin-1"],
            ExpectedBlockerCodes = ["EXACT_CONFIRMED_ROUTE_NET_SEGMENT_REQUIRED"],
            Purpose = "BLOCKED cross-page evidence with no exact confirmed route/net/segment candidate.",
            BuildGraph = BuildMissingContinuationCandidate
        },
        new()
        {
            FixtureId = "EGC-BLOCKED-CONTINUATION-AMBIGUOUS-001",
            ExpectedStatus = EngineeringGraphConformanceStatus.Blocked,
            ImportantIdentities = ["pair:ambiguous-001", "segment:amb-a", "segment:amb-b"],
            ExpectedBlockerCodes = ["EXACT_CONFIRMED_ROUTE_NET_SEGMENT_AMBIGUOUS"],
            Purpose = "BLOCKED cross-page evidence with more than one exact confirmed route/net/segment candidate.",
            BuildGraph = BuildAmbiguousContinuationCandidate
        },
        new()
        {
            FixtureId = "EGC-BLOCKED-DUPLICATE-ROUTE-001",
            ExpectedStatus = EngineeringGraphConformanceStatus.Blocked,
            ImportantIdentities = ["route:duplicate"],
            ExpectedBlockerCodes = ["EGC_DUPLICATE_ROUTE_IDENTITY"],
            Purpose = "BLOCKED duplicate routeId where route identity must be unique in the conformance graph.",
            BuildGraph = BuildDuplicateRoute
        },
        new()
        {
            FixtureId = "EGC-BLOCKED-DUPLICATE-NODE-001",
            ExpectedStatus = EngineeringGraphConformanceStatus.Blocked,
            ImportantIdentities = ["node:duplicate"],
            ExpectedBlockerCodes = ["EGC_DUPLICATE_NODE_IDENTITY"],
            Purpose = "BLOCKED duplicate nodeId where node identity must be unique in the conformance graph.",
            BuildGraph = BuildDuplicateNode
        },
        new()
        {
            FixtureId = "EGC-BLOCKED-DUPLICATE-SEGMENT-001",
            ExpectedStatus = EngineeringGraphConformanceStatus.Blocked,
            ImportantIdentities = ["segment:duplicate"],
            ExpectedBlockerCodes = ["EGC_DUPLICATE_SEGMENT_IDENTITY"],
            Purpose = "BLOCKED duplicate segmentId where segment identity must be unique in the conformance graph.",
            BuildGraph = BuildDuplicateSegment
        },
        new()
        {
            FixtureId = "EGC-BLOCKED-UNKNOWN-EVIDENCE-001",
            ExpectedStatus = EngineeringGraphConformanceStatus.Blocked,
            ImportantIdentities = ["pair:unknown-001", "route:net-unknown", "segment:unknown"],
            ExpectedBlockerCodes = ["CONFIRMED_CROSS_PAGE_CONTINUATION_EVIDENCE_REQUIRED"],
            Purpose = "BLOCKED unknown continuation evidence; semantic direction/page meaning may not be reconstructed.",
            BuildGraph = BuildUnknownContinuationEvidence
        }
    ];

    public static EngineeringGraphConformanceCase Get(string fixtureId) =>
        Cases.Single(item => string.Equals(item.FixtureId, fixtureId, StringComparison.Ordinal));

    public static AutocadStagingGraphV2Contract BuildPermutationVariant(string fixtureId) => fixtureId switch
    {
        "EGC-READY-DIRECT-001" => BuildDirect(reverseCollections: true),
        "EGC-READY-FANOUT-001" => BuildFanout(reverseCollections: true),
        "EGC-READY-CROSS-PAGE-001" => BuildCrossPage(reverseCollections: true),
        _ => throw new ArgumentOutOfRangeException(nameof(fixtureId), fixtureId, "Only READY fixtures have permutation variants.")
    };

    public static EngineeringGraphConformanceEvaluation Evaluate(AutocadStagingGraphV2Contract graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var blockers = new SortedSet<string>(StringComparer.Ordinal);

        try
        {
            AutocadStagingGraphV2Contract.EnsureSupportedSchema(graph.SchemaVersion);
        }
        catch (NotSupportedException)
        {
            blockers.Add("EGC_UNSUPPORTED_GRAPH_SCHEMA");
        }

        AddDuplicateIdentityBlocker(graph.Routes.Select(route => route.RouteId), "EGC_DUPLICATE_ROUTE_IDENTITY", blockers);
        AddDuplicateIdentityBlocker(graph.Routes.SelectMany(route => route.Nodes).Select(node => node.NodeId), "EGC_DUPLICATE_NODE_IDENTITY", blockers);
        AddDuplicateIdentityBlocker(graph.Routes.SelectMany(route => route.Segments).Select(segment => segment.SegmentId), "EGC_DUPLICATE_SEGMENT_IDENTITY", blockers);

        var nodes = graph.Routes.SelectMany(route => route.Nodes).ToArray();
        AddDuplicateIdentityBlocker(
            nodes.Where(node => !string.IsNullOrWhiteSpace(node.PinId)).Select(node => node.PinId!),
            "EGC_DUPLICATE_PIN_ENDPOINT_IDENTITY",
            blockers);
        AddDuplicateIdentityBlocker(
            nodes.Where(node => !string.IsNullOrWhiteSpace(node.ConnectionPoint?.ConnectionPointId))
                .Select(node => node.ConnectionPoint!.ConnectionPointId),
            "EGC_DUPLICATE_CONNECTION_POINT_IDENTITY",
            blockers);

        foreach (var route in graph.Routes)
        {
            if (string.IsNullOrWhiteSpace(route.RouteId) || string.IsNullOrWhiteSpace(route.NetIdentity))
                blockers.Add("EGC_EXACT_ROUTE_IDENTITY_REQUIRED");

            foreach (var node in route.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.NodeId))
                    blockers.Add("EGC_EXACT_NODE_IDENTITY_REQUIRED");
                if (string.Equals(node.Kind, "ComponentPin", StringComparison.Ordinal) &&
                    (string.IsNullOrWhiteSpace(node.PinId) ||
                     string.IsNullOrWhiteSpace(node.ConnectionPoint?.SymbolKey) ||
                     string.IsNullOrWhiteSpace(node.ConnectionPoint?.ConnectionPointId)))
                    blockers.Add("EGC_EXACT_ENDPOINT_IDENTITY_REQUIRED");
            }

            foreach (var segment in route.Segments)
                if (string.IsNullOrWhiteSpace(segment.SegmentId) ||
                    string.IsNullOrWhiteSpace(segment.FromNodeId) ||
                    string.IsNullOrWhiteSpace(segment.ToNodeId))
                    blockers.Add("EGC_EXACT_SEGMENT_IDENTITY_REQUIRED");
        }

        foreach (var continuation in graph.CrossPageContinuations)
        {
            if (!string.IsNullOrWhiteSpace(continuation.BlockingReason))
            {
                blockers.Add(continuation.BlockingReason!);
                continue;
            }

            if (string.IsNullOrWhiteSpace(continuation.PairIdentity) ||
                string.IsNullOrWhiteSpace(continuation.RouteId) ||
                string.IsNullOrWhiteSpace(continuation.NetIdentity) ||
                string.IsNullOrWhiteSpace(continuation.SegmentId) ||
                string.IsNullOrWhiteSpace(continuation.SourceEndpointId) ||
                string.IsNullOrWhiteSpace(continuation.DestinationEndpointId) ||
                string.IsNullOrWhiteSpace(continuation.SourcePageId) ||
                string.IsNullOrWhiteSpace(continuation.DestinationPageId) ||
                string.IsNullOrWhiteSpace(continuation.SourceNodeId) ||
                string.IsNullOrWhiteSpace(continuation.DestinationNodeId))
                blockers.Add("EGC_EXACT_CROSS_PAGE_IDENTITY_REQUIRED");
        }

        var status = blockers.Count == 0
            ? EngineeringGraphConformanceStatus.Ready
            : EngineeringGraphConformanceStatus.Blocked;

        return new EngineeringGraphConformanceEvaluation
        {
            Status = status,
            Fingerprint = status == EngineeringGraphConformanceStatus.Ready ? Fingerprint(graph) : null,
            BlockerCodes = blockers.ToArray()
        };
    }

    public static string Fingerprint(AutocadStagingGraphV2Contract graph) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalIdentity(graph))));

    public static string CanonicalIdentity(AutocadStagingGraphV2Contract graph)
    {
        var lines = new List<string>
        {
            $"SCHEMA|{graph.SchemaVersion}",
            $"PROJECT|{graph.ProjectId}"
        };

        foreach (var route in graph.Routes)
        {
            lines.Add($"R|{route.RouteId}|{route.NetIdentity}|{route.TopologyStatus}");

            foreach (var node in route.Nodes)
                lines.Add(
                    $"N|{route.RouteId}|{node.NodeId}|{node.Kind}|{node.ComponentInstanceId}|{node.ComponentDefinitionId}|{node.PinId}|{node.ConnectionPoint?.SymbolKey}|{node.ConnectionPoint?.ConnectionPointId}");

            foreach (var segment in route.Segments)
                lines.Add(
                    $"S|{route.RouteId}|{segment.SegmentId}|{segment.Kind}|{segment.FromNodeId}|{segment.ToNodeId}|{segment.TopologyStatus}");
        }

        foreach (var continuation in graph.CrossPageContinuations)
            lines.Add(
                $"C|{continuation.PairIdentity}|{continuation.RouteId}|{continuation.NetIdentity}|{continuation.SegmentId}|{continuation.SourceEndpointId}|{continuation.DestinationEndpointId}|{continuation.SourcePageId}|{continuation.DestinationPageId}|{continuation.SourceNodeId}|{continuation.DestinationNodeId}|{continuation.EvidenceStatus}|{continuation.EvidenceSource}|{continuation.BlockingReason}");

        return string.Join("\n", lines.OrderBy(line => line, StringComparer.Ordinal));
    }

    public static IReadOnlySet<string> ImportantIdentitySet(AutocadStagingGraphV2Contract graph)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in graph.Routes)
        {
            Add(values, route.RouteId);
            Add(values, route.NetIdentity);
            foreach (var node in route.Nodes)
            {
                Add(values, node.NodeId);
                Add(values, node.ComponentInstanceId);
                Add(values, node.ComponentDefinitionId);
                Add(values, node.PinId);
                Add(values, node.ConnectionPoint?.SymbolKey);
                Add(values, node.ConnectionPoint?.ConnectionPointId);
            }
            foreach (var segment in route.Segments)
            {
                Add(values, segment.SegmentId);
                Add(values, segment.FromNodeId);
                Add(values, segment.ToNodeId);
            }
        }

        foreach (var continuation in graph.CrossPageContinuations)
        {
            Add(values, continuation.PairIdentity);
            Add(values, continuation.RouteId);
            Add(values, continuation.NetIdentity);
            Add(values, continuation.SegmentId);
            Add(values, continuation.SourceEndpointId);
            Add(values, continuation.DestinationEndpointId);
            Add(values, continuation.SourcePageId);
            Add(values, continuation.DestinationPageId);
            Add(values, continuation.SourceNodeId);
            Add(values, continuation.DestinationNodeId);
        }

        return values;
    }

    public static AutocadStagingGraphV2Contract WithWeakSignalNoise(AutocadStagingGraphV2Contract graph) => graph with
    {
        Routes = graph.Routes.Select(route => route with
        {
            VisibleLabel = $"NOISE:{route.VisibleLabel}",
            Nodes = route.Nodes.Select(node => node with
            {
                ComponentTypeKey = "MISLEADING-TYPEKEY",
                ComponentDisplayName = "MISLEADING-DISPLAY-NAME",
                PinName = "MISLEADING-PIN-NAME",
                PortName = "MISLEADING-PORT-NAME",
                SignalCode = "MISLEADING-NODE-SIGNAL"
            }).ToArray(),
            Segments = route.Segments.Select(segment => segment with
            {
                SignalCode = "MISLEADING-SEGMENT-SIGNAL"
            }).ToArray()
        }).ToArray()
    };

    private static void AddDuplicateIdentityBlocker(
        IEnumerable<string> values,
        string blockerCode,
        ISet<string> blockers)
    {
        if (values.Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
            blockers.Add(blockerCode);
    }

    private static void Add(ISet<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            values.Add(value);
    }

    private static AutocadStagingGraphV2Contract BuildDirect(bool reverseCollections)
    {
        var nodes = new[]
        {
            Node("node:src:pin-1", "cmp-src", "def:synthetic-src", "src:pin-1", "SYNTH:src", "XTERM:src"),
            Node("node:load:pin-1", "cmp-load", "def:synthetic-load", "load:pin-1", "SYNTH:load", "XTERM:load")
        };
        var segments = new[]
        {
            Segment("segment:wire-direct", "node:src:pin-1", "node:load:pin-1")
        };

        return CreateV2(SourceGraph(
            "egc-ready-direct",
            [Route("route:net-control", "net-control", Order(nodes, reverseCollections), Order(segments, reverseCollections))],
            []));
    }

    private static AutocadStagingGraphV2Contract BuildFanout(bool reverseCollections)
    {
        var nodes = new[]
        {
            Node("node:fan-src:pin-1", "cmp-fan-src", "def:synthetic-fan-src", "fan-src:pin-1", "SYNTH:fan-src", "XTERM:fan-src"),
            Node("node:fan-a:pin-1", "cmp-fan-a", "def:synthetic-fan-a", "fan-a:pin-1", "SYNTH:fan-a", "XTERM:fan-a"),
            Node("node:fan-b:pin-1", "cmp-fan-b", "def:synthetic-fan-b", "fan-b:pin-1", "SYNTH:fan-b", "XTERM:fan-b")
        };
        var segments = new[]
        {
            Segment("segment:fan-a", "node:fan-src:pin-1", "node:fan-a:pin-1"),
            Segment("segment:fan-b", "node:fan-src:pin-1", "node:fan-b:pin-1")
        };

        return CreateV2(SourceGraph(
            "egc-ready-fanout",
            [Route("route:net-shared", "net-shared", Order(nodes, reverseCollections), Order(segments, reverseCollections))],
            []));
    }

    private static AutocadStagingGraphV2Contract BuildCrossPage(bool reverseCollections)
    {
        var nodes = new[]
        {
            Node("node:cross-src:pin-1", "cmp-cross-src", "def:synthetic-cross-src", "cross-src:pin-1", "SYNTH:cross-src", "XTERM:cross-src"),
            Node("node:cross-dst:pin-1", "cmp-cross-dst", "def:synthetic-cross-dst", "cross-dst:pin-1", "SYNTH:cross-dst", "XTERM:cross-dst")
        };
        var segments = new[]
        {
            Segment("segment:wire-cross", "node:cross-src:pin-1", "node:cross-dst:pin-1")
        };
        var continuations = new[]
        {
            Continuation("pair:cross-001", "cross-src:pin-1", "cross-dst:pin-1", "P-01", "P-02")
        };

        return CreateV2(SourceGraph(
            "egc-ready-cross-page",
            [Route("route:net-cross", "net-cross", Order(nodes, reverseCollections), Order(segments, reverseCollections))],
            Order(continuations, reverseCollections)));
    }

    private static AutocadStagingGraphV2Contract BuildDuplicateEndpoint()
    {
        var nodes = new[]
        {
            Node("node:dup-a", "cmp-dup-a", "def:dup-a", "dup-pin:pin-1", "SYNTH:dup-a", "XTERM:dup-a"),
            Node("node:dup-b", "cmp-dup-b", "def:dup-b", "dup-pin:pin-1", "SYNTH:dup-b", "XTERM:dup-b")
        };

        return CreateV2(SourceGraph(
            "egc-blocked-duplicate-endpoint",
            [Route("route:dup-endpoint", "net-dup-endpoint", nodes, [Segment("segment:dup-endpoint", "node:dup-a", "node:dup-b")])],
            []));
    }

    private static AutocadStagingGraphV2Contract BuildMissingContinuationCandidate()
    {
        var nodes = new[]
        {
            Node("node:other-a", "cmp-other-a", "def:other-a", "other-a:pin-1", "SYNTH:other-a", "XTERM:other-a"),
            Node("node:other-b", "cmp-other-b", "def:other-b", "other-b:pin-1", "SYNTH:other-b", "XTERM:other-b")
        };

        return CreateV2(SourceGraph(
            "egc-blocked-continuation-missing",
            [Route("route:net-other", "net-other", nodes, [Segment("segment:other", "node:other-a", "node:other-b")])],
            [Continuation("pair:missing-001", "missing-src:pin-1", "missing-dst:pin-1", "P-01", "P-02")]));
    }

    private static AutocadStagingGraphV2Contract BuildAmbiguousContinuationCandidate()
    {
        var nodes = new[]
        {
            Node("node:amb-src:pin-1", "cmp-amb-src", "def:amb-src", "amb-src:pin-1", "SYNTH:amb-src", "XTERM:amb-src"),
            Node("node:amb-dst:pin-1", "cmp-amb-dst", "def:amb-dst", "amb-dst:pin-1", "SYNTH:amb-dst", "XTERM:amb-dst")
        };

        return CreateV2(SourceGraph(
            "egc-blocked-continuation-ambiguous",
            [Route(
                "route:net-ambiguous",
                "net-ambiguous",
                nodes,
                [
                    Segment("segment:amb-a", "node:amb-src:pin-1", "node:amb-dst:pin-1"),
                    Segment("segment:amb-b", "node:amb-dst:pin-1", "node:amb-src:pin-1")
                ])],
            [Continuation("pair:ambiguous-001", "amb-src:pin-1", "amb-dst:pin-1", "P-01", "P-02")]));
    }

    private static AutocadStagingGraphV2Contract BuildDuplicateRoute()
    {
        var first = Route(
            "route:duplicate",
            "net-dup-a",
            [
                Node("node:route-a-1", "cmp-route-a-1", "def:route-a-1", "route-a-1:pin-1", "SYNTH:route-a-1", "XTERM:route-a-1"),
                Node("node:route-a-2", "cmp-route-a-2", "def:route-a-2", "route-a-2:pin-1", "SYNTH:route-a-2", "XTERM:route-a-2")
            ],
            [Segment("segment:route-a", "node:route-a-1", "node:route-a-2")]);
        var second = Route(
            "route:duplicate",
            "net-dup-b",
            [
                Node("node:route-b-1", "cmp-route-b-1", "def:route-b-1", "route-b-1:pin-1", "SYNTH:route-b-1", "XTERM:route-b-1"),
                Node("node:route-b-2", "cmp-route-b-2", "def:route-b-2", "route-b-2:pin-1", "SYNTH:route-b-2", "XTERM:route-b-2")
            ],
            [Segment("segment:route-b", "node:route-b-1", "node:route-b-2")]);

        return CreateV2(SourceGraph("egc-blocked-duplicate-route", [first, second], []));
    }

    private static AutocadStagingGraphV2Contract BuildDuplicateNode()
    {
        var nodes = new[]
        {
            Node("node:duplicate", "cmp-node-a", "def:node-a", "node-a:pin-1", "SYNTH:node-a", "XTERM:node-a"),
            Node("node:duplicate", "cmp-node-b", "def:node-b", "node-b:pin-1", "SYNTH:node-b", "XTERM:node-b")
        };

        return CreateV2(SourceGraph(
            "egc-blocked-duplicate-node",
            [Route("route:dup-node", "net-dup-node", nodes, [Segment("segment:dup-node", "node:duplicate", "node:duplicate")])],
            []));
    }

    private static AutocadStagingGraphV2Contract BuildDuplicateSegment()
    {
        var nodes = new[]
        {
            Node("node:seg-a", "cmp-seg-a", "def:seg-a", "seg-a:pin-1", "SYNTH:seg-a", "XTERM:seg-a"),
            Node("node:seg-b", "cmp-seg-b", "def:seg-b", "seg-b:pin-1", "SYNTH:seg-b", "XTERM:seg-b")
        };
        var segments = new[]
        {
            Segment("segment:duplicate", "node:seg-a", "node:seg-b"),
            Segment("segment:duplicate", "node:seg-b", "node:seg-a")
        };

        return CreateV2(SourceGraph(
            "egc-blocked-duplicate-segment",
            [Route("route:dup-segment", "net-dup-segment", nodes, segments)],
            []));
    }

    private static AutocadStagingGraphV2Contract BuildUnknownContinuationEvidence()
    {
        var nodes = new[]
        {
            Node("node:unknown-src:pin-1", "cmp-unknown-src", "def:unknown-src", "unknown-src:pin-1", "SYNTH:unknown-src", "XTERM:unknown-src"),
            Node("node:unknown-dst:pin-1", "cmp-unknown-dst", "def:unknown-dst", "unknown-dst:pin-1", "SYNTH:unknown-dst", "XTERM:unknown-dst")
        };

        return CreateV2(SourceGraph(
            "egc-blocked-unknown-evidence",
            [Route("route:net-unknown", "net-unknown", nodes, [Segment("segment:unknown", "node:unknown-src:pin-1", "node:unknown-dst:pin-1")])],
            [Continuation(
                "pair:unknown-001",
                "unknown-src:pin-1",
                "unknown-dst:pin-1",
                "P-01",
                "P-02",
                DrawingEvidenceStatus.Unknown)]));
    }

    private static AutocadStagingGraphV2Contract CreateV2(AutocadStagingGraphContract source)
    {
        var method = typeof(AutocadStagingGraphV2Contract).GetMethod(
            "Create",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (method is null)
            throw new InvalidOperationException("Accepted lrdu-staging-route.v2 adapter boundary is unavailable.");

        var graph = method.Invoke(
            null,
            [
                source,
                new AutocadEngineeringDrawingEvidence(),
                new ElectricalProject { ProjectId = source.ProjectId }
            ]);
        return (AutocadStagingGraphV2Contract)(graph
            ?? throw new InvalidOperationException("Accepted lrdu-staging-route.v2 adapter returned no graph."));
    }

    private static AutocadStagingGraphContract SourceGraph(
        string projectId,
        IReadOnlyList<AutocadStagingRoute> routes,
        IReadOnlyList<AutocadStagingCrossPageContinuation> continuations) => new()
    {
        ProjectId = projectId,
        Routes = routes,
        CrossPageContinuations = continuations,
        Interventions = []
    };

    private static AutocadStagingRoute Route(
        string routeId,
        string netIdentity,
        IReadOnlyList<AutocadStagingNode> nodes,
        IReadOnlyList<AutocadStagingSegment> segments) => new()
    {
        RouteId = routeId,
        NetIdentity = netIdentity,
        VisibleLabel = $"SYNTHETIC:{netIdentity}",
        TopologyStatus = "Confirmed",
        Responsibility = new AutocadStagingResponsibility { Owner = "SYNTHETIC-TEST-ONLY" },
        Nodes = nodes,
        Segments = segments,
        Shield = new AutocadStagingShieldRoute { Status = "NotApplicable" }
    };

    private static AutocadStagingNode Node(
        string nodeId,
        string componentInstanceId,
        string componentDefinitionId,
        string pinId,
        string symbolKey,
        string connectionPointId) => new()
    {
        NodeId = nodeId,
        Kind = "ComponentPin",
        ComponentInstanceId = componentInstanceId,
        ComponentDefinitionId = componentDefinitionId,
        ComponentTypeKey = "SYNTHETIC-TEST-ONLY",
        ComponentDisplayName = "SYNTHETIC-TEST-ONLY",
        PinId = pinId,
        PinNumber = "1",
        PinName = "SYNTHETIC-TEST-ONLY",
        PortName = "SYNTHETIC-TEST-ONLY",
        ConnectionPoint = new AutocadStagingConnectionPoint
        {
            SymbolKey = symbolKey,
            ConnectionPointId = connectionPointId
        }
    };

    private static AutocadStagingSegment Segment(
        string segmentId,
        string fromNodeId,
        string toNodeId) => new()
    {
        SegmentId = segmentId,
        Kind = "InternalWire",
        FromNodeId = fromNodeId,
        ToNodeId = toNodeId,
        TopologyStatus = "Confirmed",
        ProcurementStatus = "NotApplicable",
        DrawingRepresentation = "DirectWire",
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
        EvidenceSource = "synthetic-test-evidence"
    };

    private static IReadOnlyList<T> Order<T>(IReadOnlyList<T> values, bool reverse) =>
        reverse ? values.Reverse().ToArray() : values.ToArray();
}
