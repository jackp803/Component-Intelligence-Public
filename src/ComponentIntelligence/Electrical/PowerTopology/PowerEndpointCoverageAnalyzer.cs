using ComponentIntelligence.Electrical.Export;

namespace ComponentIntelligence.Electrical.PowerTopology;

/// <summary>
/// Result state for the evidence-bounded E2 endpoint coverage stage. This stage is downstream of
/// the accepted power-evidence adapter and never upgrades Unknown evidence into engineering facts.
/// </summary>
public enum PowerEndpointCoverageStatus
{
    Accepted,
    Blocked
}

public sealed record PowerEndpointCoverageParticipant
{
    public required string EndpointId { get; init; }
    public required string DomainId { get; init; }
    public required string Role { get; init; }
    public string? NodeId { get; init; }
    public required bool Covered { get; init; }
    public required string CoverageBasis { get; init; }
}

public sealed record PowerEndpointCoverageDiagnostic
{
    public required string Code { get; init; }
    public required string SubjectId { get; init; }
    public required string Message { get; init; }
}

public sealed record PowerEndpointCoverageResult
{
    public required PowerEndpointCoverageStatus Status { get; init; }
    public required IReadOnlyList<PowerEndpointCoverageParticipant> Participants { get; init; }
    public required IReadOnlyList<PowerEndpointCoverageDiagnostic> Diagnostics { get; init; }
}

/// <summary>
/// Deterministic physical endpoint-coverage analysis over already accepted E2 domain/conversion
/// semantics. Confirmed route segments and confirmed ordinary-terminal continuities are undirected
/// conductive facts only. Conversion input/output runtime endpoint identities are opaque E1 facts:
/// E2 resolves them only by exact equality with the accepted staging-node endpoint field PinId.
/// Conversion semantics move power truth between domains; this analyzer never creates a conductive
/// edge between a conversion's input and output anchors.
/// </summary>
public sealed class PowerEndpointCoverageAnalyzer
{
    private static readonly StringComparer IdComparer = StringComparer.Ordinal;

    public PowerEndpointCoverageResult Analyze(
        AutocadStagingGraphV2Contract graph,
        PowerTopologyAdapterResult adapterResult)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(adapterResult);

        var diagnostics = new List<PowerEndpointCoverageDiagnostic>();
        if (adapterResult.Status != PowerTopologyAdapterStatus.Accepted ||
            adapterResult.Input is null ||
            adapterResult.Analysis is null)
        {
            diagnostics.Add(Diagnostic(
                "PWR-COVERAGE-ADAPTER-NOT-ACCEPTED",
                "POWER_TOPOLOGY_ADAPTER",
                "Endpoint coverage requires an accepted E2 adapter result; upstream blockers are never bypassed."));
            return Result([], diagnostics);
        }

        if (adapterResult.Analysis.Status != PowerTopologyAnalysisStatus.Accepted)
        {
            diagnostics.Add(Diagnostic(
                "PWR-COVERAGE-DOMAIN-ANALYSIS-BLOCKED",
                "POWER_TOPOLOGY_ANALYSIS",
                "Endpoint coverage cannot override blocked domain-level Power Topology analysis."));
            return Result([], diagnostics);
        }

        var topology = BuildConfirmedTopology(graph.Routes, diagnostics);
        AddConfirmedTerminalContinuity(graph.SourceEvidence.TerminalContinuities, topology, diagnostics);

        var resolvedProducers = adapterResult.Input.Producers
            .OrderBy(item => item.ProducerId, IdComparer)
            .ThenBy(item => item.DomainId, IdComparer)
            .Select(item => ResolveParticipant(
                item.ProducerId,
                item.DomainId,
                "Producer",
                topology,
                diagnostics))
            .ToArray();

        var resolvedConsumers = adapterResult.Input.Consumers
            .OrderBy(item => item.ConsumerId, IdComparer)
            .ThenBy(item => item.DomainId, IdComparer)
            .Select(item => ResolveParticipant(
                item.ConsumerId,
                item.DomainId,
                "Consumer",
                topology,
                diagnostics))
            .ToArray();

        var resolvedConversions = adapterResult.Input.Conversions
            .OrderBy(item => item.ConversionId, IdComparer)
            .ThenBy(item => item.InputDomainId, IdComparer)
            .ThenBy(item => item.OutputDomainId, IdComparer)
            .Select(item => ResolveConversion(item, topology, diagnostics))
            .ToArray();

        var explicitProducerDomains = resolvedProducers
            .Select(item => item.DomainId)
            .ToHashSet(IdComparer);

        var sourceAnchorsByDomain = new Dictionary<string, SortedSet<string>>(IdComparer);
        foreach (var producer in resolvedProducers.Where(item => item.Anchor is not null))
            AddSourceAnchor(sourceAnchorsByDomain, producer.DomainId, producer.Anchor!.Key);

        // A conversion output side is an authoritative physical source boundary only when the side
        // is non-empty and every declared runtime endpoint resolves uniquely. A partial side can
        // never be used as proof for downstream loads.
        foreach (var conversion in resolvedConversions.Where(item => item.OutputComplete))
        foreach (var endpoint in conversion.Outputs)
            AddSourceAnchor(sourceAnchorsByDomain, conversion.Fact.OutputDomainId, endpoint.Anchor!.Key);

        var participants = new List<PowerEndpointCoverageParticipant>();
        participants.AddRange(resolvedProducers.Select(item => ToParticipant(
            item.EndpointId,
            item.DomainId,
            item.Role,
            item.Anchor,
            item.Anchor is not null,
            item.Anchor is null ? "Unresolved" : "ConfirmedTopologyAnchor")));

        foreach (var conversion in resolvedConversions)
        {
            foreach (var output in conversion.Outputs)
            {
                var authoritative = conversion.OutputComplete && output.Anchor is not null;
                participants.Add(ToParticipant(
                    output.EndpointId,
                    conversion.Fact.OutputDomainId,
                    "ConversionOutput",
                    output.Anchor,
                    authoritative,
                    authoritative ? "ConversionOutputSourceAnchor" :
                    output.Anchor is null ? "Unresolved" : "IncompleteConversionOutputSide"));
            }
        }

        // Conversion inputs are physical loads in their declared input domain. They do not become
        // conductive links to the output side; each input must independently reach that domain's
        // accepted physical source boundary.
        foreach (var conversion in resolvedConversions)
        {
            foreach (var input in conversion.Inputs)
            {
                if (input.Anchor is null)
                {
                    participants.Add(ToParticipant(
                        input.EndpointId,
                        conversion.Fact.InputDomainId,
                        "ConversionInput",
                        null,
                        false,
                        "Unresolved"));
                    continue;
                }

                if (!conversion.InputComplete)
                {
                    participants.Add(ToParticipant(
                        input.EndpointId,
                        conversion.Fact.InputDomainId,
                        "ConversionInput",
                        input.Anchor,
                        false,
                        "IncompleteConversionInputSide"));
                    continue;
                }

                var sourceAnchors = SourceAnchors(sourceAnchorsByDomain, conversion.Fact.InputDomainId);
                var covered = sourceAnchors.Count > 0 &&
                              IsReachable(input.Anchor.Key, sourceAnchors, topology.Adjacency);
                if (!covered)
                {
                    diagnostics.Add(Diagnostic(
                        "PWR-COVERAGE-CONVERSION-INPUT-UNREACHABLE",
                        $"CONVERSION:{conversion.Fact.ConversionId}:INPUT:{input.EndpointId}",
                        $"Conversion '{conversion.Fact.ConversionId}' input endpoint '{input.EndpointId}' is not conductively reachable from the accepted source boundary of power domain '{conversion.Fact.InputDomainId}'."));
                }

                participants.Add(ToParticipant(
                    input.EndpointId,
                    conversion.Fact.InputDomainId,
                    "ConversionInput",
                    input.Anchor,
                    covered,
                    "ConversionInputConnectivity"));
            }
        }

        var conversionsByOutputDomain = resolvedConversions
            .GroupBy(item => item.Fact.OutputDomainId, IdComparer)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(item => item.Fact.ConversionId, IdComparer).ToArray(),
                IdComparer);

        foreach (var consumer in resolvedConsumers)
        {
            if (consumer.Anchor is null)
            {
                participants.Add(ToParticipant(
                    consumer.EndpointId,
                    consumer.DomainId,
                    consumer.Role,
                    null,
                    false,
                    "Unresolved"));
                continue;
            }

            var sourceAnchors = SourceAnchors(sourceAnchorsByDomain, consumer.DomainId);
            if (sourceAnchors.Count > 0)
            {
                var covered = IsReachable(consumer.Anchor.Key, sourceAnchors, topology.Adjacency);
                var basis = explicitProducerDomains.Contains(consumer.DomainId)
                    ? "ExplicitProducerConnectivity"
                    : "ConversionOutputConnectivity";
                if (!covered)
                {
                    diagnostics.Add(Diagnostic(
                        "PWR-COVERAGE-CONSUMER-UNREACHABLE",
                        $"CONSUMER:{consumer.EndpointId}",
                        $"Consumer '{consumer.EndpointId}' is not conductively connected to the accepted source boundary in power domain '{consumer.DomainId}'."));
                }

                participants.Add(ToParticipant(
                    consumer.EndpointId,
                    consumer.DomainId,
                    consumer.Role,
                    consumer.Anchor,
                    covered,
                    basis));
                continue;
            }

            if (conversionsByOutputDomain.TryGetValue(consumer.DomainId, out var producerConversions))
            {
                var emptyOutputEvidence = producerConversions
                    .Where(item => item.Fact.OutputEndpointIds.Count == 0)
                    .OrderBy(item => item.Fact.ConversionId, IdComparer)
                    .ToArray();
                if (emptyOutputEvidence.Length > 0)
                {
                    var conversionIds = emptyOutputEvidence.Select(item => item.Fact.ConversionId).ToArray();
                    var conversionDescription = conversionIds.Length == 1
                        ? $"conversion '{conversionIds[0]}'"
                        : $"conversions [{string.Join(", ", conversionIds.Select(id => $"'{id}'"))}]";
                    diagnostics.Add(Diagnostic(
                        "PWR-COVERAGE-CONVERSION-OUTPUT-ENDPOINT-EVIDENCE-REQUIRED",
                        $"CONSUMER:{consumer.EndpointId}",
                        $"Consumer '{consumer.EndpointId}' is in conversion-produced power domain '{consumer.DomainId}' from {conversionDescription}, but the accepted conversion evidence declares no runtime output endpoint identity for the physical source boundary."));
                    participants.Add(ToParticipant(
                        consumer.EndpointId,
                        consumer.DomainId,
                        consumer.Role,
                        consumer.Anchor,
                        false,
                        "ConversionOutputEndpointEvidenceRequired"));
                    continue;
                }

                participants.Add(ToParticipant(
                    consumer.EndpointId,
                    consumer.DomainId,
                    consumer.Role,
                    consumer.Anchor,
                    false,
                    "IncompleteConversionOutputSide"));
                continue;
            }

            diagnostics.Add(Diagnostic(
                "PWR-COVERAGE-CONSUMER-UNREACHABLE",
                $"CONSUMER:{consumer.EndpointId}",
                $"Consumer '{consumer.EndpointId}' has no accepted physical source endpoint path for power domain '{consumer.DomainId}'."));
            participants.Add(ToParticipant(
                consumer.EndpointId,
                consumer.DomainId,
                consumer.Role,
                consumer.Anchor,
                false,
                "None"));
        }

        return Result(participants, diagnostics);
    }

    private static ResolvedConversion ResolveConversion(
        PowerConversionFact conversion,
        ConductiveTopology topology,
        ICollection<PowerEndpointCoverageDiagnostic> diagnostics)
    {
        var inputIds = conversion.InputEndpointIds.OrderBy(item => item, IdComparer).ToArray();
        var outputIds = conversion.OutputEndpointIds.OrderBy(item => item, IdComparer).ToArray();

        if (inputIds.Length == 0)
        {
            diagnostics.Add(Diagnostic(
                "PWR-COVERAGE-CONVERSION-INPUT-ENDPOINT-EVIDENCE-REQUIRED",
                $"CONVERSION:{conversion.ConversionId}:INPUT",
                $"Conversion '{conversion.ConversionId}' declares no accepted runtime input endpoint identity for power domain '{conversion.InputDomainId}'."));
        }
        if (outputIds.Length == 0)
        {
            diagnostics.Add(Diagnostic(
                "PWR-COVERAGE-CONVERSION-OUTPUT-ENDPOINT-EVIDENCE-REQUIRED",
                $"CONVERSION:{conversion.ConversionId}:OUTPUT",
                $"Conversion '{conversion.ConversionId}' declares no accepted runtime output endpoint identity for power domain '{conversion.OutputDomainId}'."));
        }

        var inputs = inputIds.Select(endpointId => new ResolvedConversionEndpoint(
                endpointId,
                ResolveEndpointAnchor(
                    endpointId,
                    $"CONVERSION:{conversion.ConversionId}:INPUT:{endpointId}",
                    "conversion input runtime endpoint",
                    topology,
                    diagnostics,
                    "PWR-COVERAGE-CONVERSION-INPUT-ANCHOR-MISSING",
                    "PWR-COVERAGE-CONVERSION-INPUT-ANCHOR-AMBIGUOUS")))
            .ToArray();
        var outputs = outputIds.Select(endpointId => new ResolvedConversionEndpoint(
                endpointId,
                ResolveEndpointAnchor(
                    endpointId,
                    $"CONVERSION:{conversion.ConversionId}:OUTPUT:{endpointId}",
                    "conversion output runtime endpoint",
                    topology,
                    diagnostics,
                    "PWR-COVERAGE-CONVERSION-OUTPUT-ANCHOR-MISSING",
                    "PWR-COVERAGE-CONVERSION-OUTPUT-ANCHOR-AMBIGUOUS")))
            .ToArray();

        return new ResolvedConversion(
            conversion,
            inputs,
            outputs,
            inputIds.Length > 0 && inputs.All(item => item.Anchor is not null),
            outputIds.Length > 0 && outputs.All(item => item.Anchor is not null));
    }

    private static void AddSourceAnchor(
        IDictionary<string, SortedSet<string>> sources,
        string domainId,
        string anchorKey)
    {
        if (!sources.TryGetValue(domainId, out var anchors))
        {
            anchors = new SortedSet<string>(IdComparer);
            sources[domainId] = anchors;
        }
        anchors.Add(anchorKey);
    }

    private static IReadOnlyCollection<string> SourceAnchors(
        IReadOnlyDictionary<string, SortedSet<string>> sources,
        string domainId) =>
        sources.TryGetValue(domainId, out var anchors)
            ? anchors
            : Array.Empty<string>();

    private static ConductiveTopology BuildConfirmedTopology(
        IReadOnlyList<AutocadStagingRoute> routes,
        ICollection<PowerEndpointCoverageDiagnostic> diagnostics)
    {
        var topology = new ConductiveTopology();
        foreach (var duplicate in routes
                     .GroupBy(item => item.RouteId, IdComparer)
                     .Where(group => group.Count() > 1)
                     .OrderBy(group => group.Key, IdComparer))
        {
            diagnostics.Add(Diagnostic(
                "PWR-COVERAGE-ROUTE-ID-DUPLICATE",
                $"ROUTE:{duplicate.Key}",
                $"Confirmed topology route identity '{duplicate.Key}' is not unique."));
        }

        var orderedRoutes = routes
            .OrderBy(item => item.RouteId, IdComparer)
            .ThenBy(RouteCanonicalKey, IdComparer)
            .ToArray();

        for (var routeOrdinal = 0; routeOrdinal < orderedRoutes.Length; routeOrdinal++)
        {
            var route = orderedRoutes[routeOrdinal];
            var nodeGroups = route.Nodes
                .OrderBy(item => item.NodeId, IdComparer)
                .ThenBy(item => item.PinId ?? string.Empty, IdComparer)
                .ThenBy(item => item.Kind, IdComparer)
                .GroupBy(item => item.NodeId, IdComparer)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select((node, index) => new Anchor(
                            $"{route.RouteId}\u001f{routeOrdinal:D8}\u001f{node.NodeId}\u001f{index:D8}",
                            route.RouteId,
                            node.NodeId,
                            StableEndpointIdentity(node.PinId)))
                        .ToArray(),
                    IdComparer);

            foreach (var segment in route.Segments
                         .Where(IsConfirmed)
                         .OrderBy(item => item.SegmentId, IdComparer)
                         .ThenBy(item => CanonicalPair(item.FromNodeId, item.ToNodeId), IdComparer))
            {
                var from = ResolveSegmentAnchor(route, segment, "FROM", segment.FromNodeId, nodeGroups, diagnostics);
                var to = ResolveSegmentAnchor(route, segment, "TO", segment.ToNodeId, nodeGroups, diagnostics);
                if (from is null || to is null) continue;

                topology.AddAnchor(from);
                topology.AddAnchor(to);
                topology.AddEdge(from.Key, to.Key);
            }
        }

        return topology;
    }

    private static Anchor? ResolveSegmentAnchor(
        AutocadStagingRoute route,
        AutocadStagingSegment segment,
        string side,
        string nodeId,
        IReadOnlyDictionary<string, Anchor[]> nodeGroups,
        ICollection<PowerEndpointCoverageDiagnostic> diagnostics)
    {
        if (!nodeGroups.TryGetValue(nodeId, out var matches) || matches.Length == 0)
        {
            diagnostics.Add(Diagnostic(
                "PWR-COVERAGE-SEGMENT-ANCHOR-MISSING",
                $"SEGMENT:{segment.SegmentId}:{side}",
                $"Confirmed segment '{segment.SegmentId}' in route '{route.RouteId}' references missing topology node '{nodeId}'."));
            return null;
        }
        if (matches.Length > 1)
        {
            diagnostics.Add(Diagnostic(
                "PWR-COVERAGE-SEGMENT-ANCHOR-AMBIGUOUS",
                $"SEGMENT:{segment.SegmentId}:{side}",
                $"Confirmed segment '{segment.SegmentId}' in route '{route.RouteId}' resolves topology node '{nodeId}' to {matches.Length} anchors."));
            return null;
        }
        return matches[0];
    }

    private static void AddConfirmedTerminalContinuity(
        IReadOnlyList<AutocadStagingTerminalContinuity> continuities,
        ConductiveTopology topology,
        ICollection<PowerEndpointCoverageDiagnostic> diagnostics)
    {
        foreach (var duplicate in continuities
                     .Where(IsConfirmed)
                     .GroupBy(item => item.ContinuityId, IdComparer)
                     .Where(group => group.Count() > 1)
                     .OrderBy(group => group.Key, IdComparer))
        {
            diagnostics.Add(Diagnostic(
                "PWR-COVERAGE-CONTINUITY-ID-DUPLICATE",
                $"CONTINUITY:{duplicate.Key}",
                $"Confirmed terminal continuity identity '{duplicate.Key}' is not unique."));
        }

        foreach (var continuity in continuities
                     .Where(IsConfirmed)
                     .OrderBy(item => item.ContinuityId, IdComparer)
                     .ThenBy(item => CanonicalPair(item.FromConnectionPointId, item.ToConnectionPointId), IdComparer))
        {
            var from = ResolveEndpointAnchor(
                continuity.FromConnectionPointId,
                $"CONTINUITY:{continuity.ContinuityId}:FROM",
                "terminal continuity connection point",
                topology,
                diagnostics,
                "PWR-COVERAGE-CONTINUITY-ANCHOR-MISSING",
                "PWR-COVERAGE-CONTINUITY-ANCHOR-AMBIGUOUS");
            var to = ResolveEndpointAnchor(
                continuity.ToConnectionPointId,
                $"CONTINUITY:{continuity.ContinuityId}:TO",
                "terminal continuity connection point",
                topology,
                diagnostics,
                "PWR-COVERAGE-CONTINUITY-ANCHOR-MISSING",
                "PWR-COVERAGE-CONTINUITY-ANCHOR-AMBIGUOUS");
            if (from is null || to is null) continue;
            topology.AddEdge(from.Key, to.Key);
        }
    }

    private static ResolvedParticipant ResolveParticipant(
        string endpointId,
        string domainId,
        string role,
        ConductiveTopology topology,
        ICollection<PowerEndpointCoverageDiagnostic> diagnostics)
    {
        var subject = $"{role.ToUpperInvariant()}:{endpointId}";
        var anchor = ResolveEndpointAnchor(
            endpointId,
            subject,
            $"{role} endpoint",
            topology,
            diagnostics,
            "PWR-COVERAGE-PARTICIPANT-ANCHOR-MISSING",
            "PWR-COVERAGE-PARTICIPANT-ANCHOR-AMBIGUOUS");
        return new ResolvedParticipant(endpointId, domainId, role, anchor);
    }

    private static Anchor? ResolveEndpointAnchor(
        string endpointId,
        string subjectId,
        string kind,
        ConductiveTopology topology,
        ICollection<PowerEndpointCoverageDiagnostic> diagnostics,
        string missingCode,
        string ambiguousCode)
    {
        if (!topology.ByEndpointId.TryGetValue(endpointId, out var matches) || matches.Count == 0)
        {
            diagnostics.Add(Diagnostic(
                missingCode,
                subjectId,
                $"Explicit {kind} identity '{endpointId}' does not resolve to a confirmed topology anchor."));
            return null;
        }
        if (matches.Count > 1)
        {
            diagnostics.Add(Diagnostic(
                ambiguousCode,
                subjectId,
                $"Explicit {kind} identity '{endpointId}' resolves to {matches.Count} confirmed topology anchors."));
            return null;
        }
        return matches[0];
    }

    private static bool IsReachable(
        string start,
        IReadOnlyCollection<string> targets,
        IReadOnlyDictionary<string, SortedSet<string>> adjacency)
    {
        var targetSet = targets.ToHashSet(IdComparer);
        if (targetSet.Contains(start)) return true;

        var visited = new HashSet<string>(IdComparer) { start };
        var ready = new SortedSet<string>(IdComparer) { start };
        while (ready.Count > 0)
        {
            var current = ready.Min!;
            ready.Remove(current);
            if (!adjacency.TryGetValue(current, out var neighbors)) continue;
            foreach (var neighbor in neighbors)
            {
                if (targetSet.Contains(neighbor)) return true;
                if (visited.Add(neighbor)) ready.Add(neighbor);
            }
        }
        return false;
    }

    private static PowerEndpointCoverageParticipant ToParticipant(
        string endpointId,
        string domainId,
        string role,
        Anchor? anchor,
        bool covered,
        string basis) => new()
    {
        EndpointId = endpointId,
        DomainId = domainId,
        Role = role,
        NodeId = anchor?.NodeId,
        Covered = covered,
        CoverageBasis = basis
    };

    private static PowerEndpointCoverageResult Result(
        IEnumerable<PowerEndpointCoverageParticipant> participants,
        IEnumerable<PowerEndpointCoverageDiagnostic> diagnostics)
    {
        var canonicalDiagnostics = diagnostics
            .DistinctBy(item => (item.Code, item.SubjectId, item.Message))
            .OrderBy(item => item.Code, IdComparer)
            .ThenBy(item => item.SubjectId, IdComparer)
            .ThenBy(item => item.Message, IdComparer)
            .ToArray();
        var canonicalParticipants = participants
            .OrderBy(item => item.Role, IdComparer)
            .ThenBy(item => item.EndpointId, IdComparer)
            .ThenBy(item => item.DomainId, IdComparer)
            .ThenBy(item => item.NodeId ?? string.Empty, IdComparer)
            .ToArray();
        return new PowerEndpointCoverageResult
        {
            Status = canonicalDiagnostics.Length == 0
                ? PowerEndpointCoverageStatus.Accepted
                : PowerEndpointCoverageStatus.Blocked,
            Participants = canonicalParticipants,
            Diagnostics = canonicalDiagnostics
        };
    }

    private static bool IsConfirmed(AutocadStagingSegment segment) =>
        string.Equals(segment.TopologyStatus, "Confirmed", StringComparison.Ordinal);

    private static bool IsConfirmed(AutocadStagingTerminalContinuity continuity) =>
        continuity.EvidenceStatus == DrawingEvidenceStatus.Confirmed;

    private static string? StableEndpointIdentity(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
        value.Any(char.IsControl)
            ? null
            : value;

    private static string CanonicalPair(string first, string second) =>
        string.CompareOrdinal(first, second) <= 0 ? $"{first}\u001f{second}" : $"{second}\u001f{first}";

    private static string RouteCanonicalKey(AutocadStagingRoute route) => string.Join("\u001e",
        route.Nodes.Select(item => $"N:{item.NodeId}:{item.PinId}").OrderBy(item => item, IdComparer)
            .Concat(route.Segments.Select(item =>
                    $"S:{item.SegmentId}:{item.TopologyStatus}:{CanonicalPair(item.FromNodeId, item.ToNodeId)}")
                .OrderBy(item => item, IdComparer)));

    private static PowerEndpointCoverageDiagnostic Diagnostic(string code, string subjectId, string message) => new()
    {
        Code = code,
        SubjectId = subjectId,
        Message = message
    };

    private sealed record Anchor(string Key, string RouteId, string NodeId, string? EndpointId);

    private sealed record ResolvedParticipant(
        string EndpointId,
        string DomainId,
        string Role,
        Anchor? Anchor);

    private sealed record ResolvedConversionEndpoint(string EndpointId, Anchor? Anchor);

    private sealed record ResolvedConversion(
        PowerConversionFact Fact,
        IReadOnlyList<ResolvedConversionEndpoint> Inputs,
        IReadOnlyList<ResolvedConversionEndpoint> Outputs,
        bool InputComplete,
        bool OutputComplete);

    private sealed class ConductiveTopology
    {
        public Dictionary<string, Anchor> Anchors { get; } = new(IdComparer);
        public Dictionary<string, List<Anchor>> ByEndpointId { get; } = new(IdComparer);
        public Dictionary<string, SortedSet<string>> Adjacency { get; } = new(IdComparer);

        public void AddAnchor(Anchor anchor)
        {
            if (Anchors.ContainsKey(anchor.Key)) return;
            Anchors[anchor.Key] = anchor;
            Adjacency[anchor.Key] = new SortedSet<string>(IdComparer);
            if (anchor.EndpointId is null) return;
            if (!ByEndpointId.TryGetValue(anchor.EndpointId, out var matches))
            {
                matches = new List<Anchor>();
                ByEndpointId[anchor.EndpointId] = matches;
            }
            matches.Add(anchor);
            matches.Sort((left, right) => IdComparer.Compare(left.Key, right.Key));
        }

        public void AddEdge(string first, string second)
        {
            if (!Adjacency.ContainsKey(first) || !Adjacency.ContainsKey(second)) return;
            Adjacency[first].Add(second);
            Adjacency[second].Add(first);
        }
    }
}
