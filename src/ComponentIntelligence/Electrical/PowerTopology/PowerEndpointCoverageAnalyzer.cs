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
/// Deterministic endpoint-coverage analysis over accepted E2 power semantics plus explicit confirmed
/// route connectivity and source terminal continuity. Route segments and terminal continuity are
/// undirected conductive facts only. They never create source direction, PowerDomain membership,
/// Producer/Consumer roles, conversions, or new engineering connections.
///
/// Endpoint joins deliberately use AutocadStagingNode.PinId because the accepted E1 staging builder
/// explicitly copies the resolved ElectricalProject endpoint identity into that field. No name,
/// label, TypeKey, model, voltage, coordinate, page position, endpoint order, or undocumented string
/// prefix is used as an identity bridge.
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

        var conversionsByOutputDomain = adapterResult.Input.Conversions
            .OrderBy(item => item.OutputDomainId, IdComparer)
            .ThenBy(item => item.ConversionId, IdComparer)
            .GroupBy(item => item.OutputDomainId, IdComparer)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.ConversionId).OrderBy(item => item, IdComparer).ToArray(),
                IdComparer);
        var producersByDomain = resolvedProducers
            .Where(item => item.Anchor is not null)
            .GroupBy(item => item.DomainId, IdComparer)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Anchor!.Key).OrderBy(item => item, IdComparer).ToArray(),
                IdComparer);

        var producerParticipants = resolvedProducers
            .Select(item => ToParticipant(
                item,
                item.Anchor is not null,
                item.Anchor is null ? "Unresolved" : "ConfirmedTopologyAnchor"))
            .ToArray();

        var consumerParticipants = new List<PowerEndpointCoverageParticipant>();
        foreach (var consumer in resolvedConsumers)
        {
            if (consumer.Anchor is null)
            {
                consumerParticipants.Add(ToParticipant(consumer, false, "Unresolved"));
                continue;
            }

            var covered = false;
            var basis = "None";
            if (producersByDomain.TryGetValue(consumer.DomainId, out var producerAnchors) && producerAnchors.Length > 0)
            {
                covered = IsReachable(consumer.Anchor.Key, producerAnchors, topology.Adjacency);
                basis = "ExplicitProducerConnectivity";
                if (!covered)
                {
                    diagnostics.Add(Diagnostic(
                        "PWR-COVERAGE-CONSUMER-UNREACHABLE",
                        $"CONSUMER:{consumer.EndpointId}",
                        $"Consumer '{consumer.EndpointId}' is not conductively connected to an explicit Producer anchor in power domain '{consumer.DomainId}'."));
                }
            }
            else if (conversionsByOutputDomain.TryGetValue(consumer.DomainId, out var conversionIds))
            {
                basis = "ConversionOutputEndpointEvidenceRequired";
                var conversionDescription = conversionIds.Length == 1
                    ? $"conversion '{conversionIds[0]}'"
                    : $"conversions [{string.Join(", ", conversionIds.Select(id => $"'{id}'"))}]";
                diagnostics.Add(Diagnostic(
                    "PWR-COVERAGE-CONVERSION-OUTPUT-ENDPOINT-EVIDENCE-REQUIRED",
                    $"CONSUMER:{consumer.EndpointId}",
                    $"Consumer '{consumer.EndpointId}' is in conversion-produced power domain '{consumer.DomainId}' from {conversionDescription}, but no explicit Producer participant anchor exists in that output domain and the accepted contract defines no converter-output runtime endpoint bridge."));
            }
            else
            {
                diagnostics.Add(Diagnostic(
                    "PWR-COVERAGE-CONSUMER-UNREACHABLE",
                    $"CONSUMER:{consumer.EndpointId}",
                    $"Consumer '{consumer.EndpointId}' has no explicit Producer endpoint path for power domain '{consumer.DomainId}'."));
            }

            consumerParticipants.Add(ToParticipant(consumer, covered, basis));
        }

        return Result(producerParticipants.Concat(consumerParticipants), diagnostics);
    }

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
        ResolvedParticipant item,
        bool covered,
        string basis) => new()
    {
        EndpointId = item.EndpointId,
        DomainId = item.DomainId,
        Role = item.Role,
        NodeId = item.Anchor?.NodeId,
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
        string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal) || value.Any(char.IsControl)
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
