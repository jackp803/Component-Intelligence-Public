namespace ComponentIntelligence.Electrical.PowerTopology;

/// <summary>
/// Standalone deterministic E2 analysis kernel over explicit normalized power facts only.
/// It intentionally has no dependency on ElectricalProject, Engineering Graph export contracts,
/// drawing evidence, endpoint ordering, labels, names, TypeKey, geometry, or page placement.
/// </summary>
public sealed class PowerTopologyAnalyzer
{
    private static readonly StringComparer IdComparer = StringComparer.Ordinal;

    public PowerTopologyResult Analyze(PowerTopologyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var diagnostics = new List<PowerTopologyDiagnostic>();
        AddIdentityDiagnostics(input, diagnostics);

        var domainIds = input.Domains
            .Select(item => item.DomainId)
            .Where(IsStableIdentity)
            .Distinct(IdComparer)
            .OrderBy(id => id, IdComparer)
            .ToArray();
        var domainSet = domainIds.ToHashSet(IdComparer);

        AddDuplicateIdentityDiagnostics(input.Domains.Select(item => item.DomainId), "DOMAIN", diagnostics);
        AddDuplicateIdentityDiagnostics(input.Producers.Select(item => item.ProducerId), "PRODUCER", diagnostics);
        AddDuplicateIdentityDiagnostics(input.Consumers.Select(item => item.ConsumerId), "CONSUMER", diagnostics);
        AddDuplicateIdentityDiagnostics(input.Conversions.Select(item => item.ConversionId), "CONVERSION", diagnostics);

        AddDomainReferenceDiagnostics(input, domainSet, diagnostics);

        var validProducers = input.Producers
            .Where(item => IsStableIdentity(item.ProducerId) && IsStableIdentity(item.DomainId) && domainSet.Contains(item.DomainId))
            .OrderBy(item => item.ProducerId, IdComparer)
            .ThenBy(item => item.DomainId, IdComparer)
            .ToArray();
        var validConsumers = input.Consumers
            .Where(item => IsStableIdentity(item.ConsumerId) && IsStableIdentity(item.DomainId) && domainSet.Contains(item.DomainId))
            .OrderBy(item => item.ConsumerId, IdComparer)
            .ThenBy(item => item.DomainId, IdComparer)
            .ToArray();
        var validConversions = input.Conversions
            .Where(item => IsStableIdentity(item.ConversionId) &&
                           IsStableIdentity(item.InputDomainId) &&
                           IsStableIdentity(item.OutputDomainId) &&
                           domainSet.Contains(item.InputDomainId) &&
                           domainSet.Contains(item.OutputDomainId))
            .OrderBy(item => item.ConversionId, IdComparer)
            .ThenBy(item => item.InputDomainId, IdComparer)
            .ThenBy(item => item.OutputDomainId, IdComparer)
            .ToArray();

        AddDuplicateProducerDiagnostics(validProducers, validConversions, diagnostics);
        AddMissingProducerDiagnostics(validProducers, validConsumers, validConversions, diagnostics);

        // Duplicate conversion identities are already blocking. For graph operations, select one
        // deterministic representative per ID so diagnostics remain deterministic instead of
        // depending on input collection order.
        var graphConversions = validConversions
            .GroupBy(item => item.ConversionId, IdComparer)
            .Select(group => group
                .OrderBy(item => item.InputDomainId, IdComparer)
                .ThenBy(item => item.OutputDomainId, IdComparer)
                .First())
            .OrderBy(item => item.ConversionId, IdComparer)
            .ToArray();

        var conversionGraph = BuildConversionGraph(graphConversions);
        var topologicalOrder = BuildCanonicalTopologicalOrder(conversionGraph);
        var cycleDiagnostics = BuildCycleDiagnostics(conversionGraph);
        diagnostics.AddRange(cycleDiagnostics);
        if (cycleDiagnostics.Count > 0)
            topologicalOrder = Array.Empty<string>();

        var reachableDomains = BuildReachableDomains(validProducers, graphConversions);
        AddOrphanConversionDiagnostics(graphConversions, reachableDomains, diagnostics);
        AddUnreachableConsumerDiagnostics(validConsumers, reachableDomains, diagnostics);

        var canonicalDiagnostics = diagnostics
            .DistinctBy(item => (item.Code, item.SubjectId, item.Message))
            .OrderBy(item => item.Code, IdComparer)
            .ThenBy(item => item.SubjectId, IdComparer)
            .ThenBy(item => item.Message, IdComparer)
            .ToArray();

        return new PowerTopologyResult
        {
            Status = canonicalDiagnostics.Length == 0
                ? PowerTopologyAnalysisStatus.Accepted
                : PowerTopologyAnalysisStatus.Blocked,
            DomainIds = domainIds,
            Producers = validProducers,
            Consumers = validConsumers,
            ConversionTopologicalOrder = topologicalOrder,
            Diagnostics = canonicalDiagnostics
        };
    }

    private static void AddIdentityDiagnostics(PowerTopologyInput input, ICollection<PowerTopologyDiagnostic> diagnostics)
    {
        foreach (var item in input.Domains)
            AddIdentityDiagnostic(item.DomainId, "DOMAIN_ID", diagnostics);
        foreach (var item in input.Producers)
        {
            AddIdentityDiagnostic(item.ProducerId, "PRODUCER_ID", diagnostics);
            AddIdentityDiagnostic(item.DomainId, "PRODUCER_DOMAIN_ID", diagnostics);
        }
        foreach (var item in input.Consumers)
        {
            AddIdentityDiagnostic(item.ConsumerId, "CONSUMER_ID", diagnostics);
            AddIdentityDiagnostic(item.DomainId, "CONSUMER_DOMAIN_ID", diagnostics);
        }
        foreach (var item in input.Conversions)
        {
            AddIdentityDiagnostic(item.ConversionId, "CONVERSION_ID", diagnostics);
            AddIdentityDiagnostic(item.InputDomainId, "CONVERSION_INPUT_DOMAIN_ID", diagnostics);
            AddIdentityDiagnostic(item.OutputDomainId, "CONVERSION_OUTPUT_DOMAIN_ID", diagnostics);
        }
    }

    private static void AddIdentityDiagnostic(
        string? value,
        string kind,
        ICollection<PowerTopologyDiagnostic> diagnostics)
    {
        if (IsStableIdentity(value)) return;
        var rendered = RenderIdentity(value);
        diagnostics.Add(Diagnostic(
            "PWR-IDENTITY-INVALID",
            $"{kind}:{rendered}",
            $"{kind} must be an explicit non-empty stable identity with no surrounding whitespace or control characters."));
    }

    private static void AddDuplicateIdentityDiagnostics(
        IEnumerable<string> ids,
        string kind,
        ICollection<PowerTopologyDiagnostic> diagnostics)
    {
        foreach (var group in ids.Where(IsStableIdentity)
                     .GroupBy(id => id, IdComparer)
                     .Where(group => group.Count() > 1)
                     .OrderBy(group => group.Key, IdComparer))
        {
            diagnostics.Add(Diagnostic(
                "PWR-IDENTITY-DUPLICATE",
                $"{kind}:{group.Key}",
                $"Stable {kind.ToLowerInvariant()} identity '{group.Key}' appears {group.Count()} times."));
        }
    }

    private static void AddDomainReferenceDiagnostics(
        PowerTopologyInput input,
        IReadOnlySet<string> domains,
        ICollection<PowerTopologyDiagnostic> diagnostics)
    {
        foreach (var item in input.Producers.Where(item => IsStableIdentity(item.ProducerId) && IsStableIdentity(item.DomainId)))
            if (!domains.Contains(item.DomainId))
                diagnostics.Add(DomainNotFound("PRODUCER", item.ProducerId, item.DomainId));

        foreach (var item in input.Consumers.Where(item => IsStableIdentity(item.ConsumerId) && IsStableIdentity(item.DomainId)))
            if (!domains.Contains(item.DomainId))
                diagnostics.Add(DomainNotFound("CONSUMER", item.ConsumerId, item.DomainId));

        foreach (var item in input.Conversions.Where(item => IsStableIdentity(item.ConversionId)))
        {
            if (IsStableIdentity(item.InputDomainId) && !domains.Contains(item.InputDomainId))
                diagnostics.Add(DomainNotFound("CONVERSION_INPUT", item.ConversionId, item.InputDomainId));
            if (IsStableIdentity(item.OutputDomainId) && !domains.Contains(item.OutputDomainId))
                diagnostics.Add(DomainNotFound("CONVERSION_OUTPUT", item.ConversionId, item.OutputDomainId));
        }
    }

    private static PowerTopologyDiagnostic DomainNotFound(string kind, string ownerId, string domainId) =>
        Diagnostic(
            "PWR-DOMAIN-NOT-FOUND",
            $"{kind}:{ownerId}",
            $"Referenced power domain '{domainId}' does not exist in the explicit domain set.");

    private static void AddDuplicateProducerDiagnostics(
        IReadOnlyList<PowerProducerFact> producers,
        IReadOnlyList<PowerConversionFact> conversions,
        ICollection<PowerTopologyDiagnostic> diagnostics)
    {
        var declarations = producers
            .Select(item => (DomainId: item.DomainId, ProducerIdentity: $"PRODUCER:{item.ProducerId}"))
            .Concat(conversions.Select(item =>
                (DomainId: item.OutputDomainId, ProducerIdentity: $"CONVERSION:{item.ConversionId}")))
            .GroupBy(item => item.DomainId, IdComparer)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, IdComparer);

        foreach (var group in declarations)
        {
            var producerIds = group.Select(item => item.ProducerIdentity).OrderBy(id => id, IdComparer).ToArray();
            diagnostics.Add(Diagnostic(
                "PWR-DUPLICATE-PRODUCER",
                $"DOMAIN:{group.Key}",
                $"Power domain '{group.Key}' has multiple explicit producers: {string.Join(", ", producerIds)}."));
        }
    }

    private static void AddMissingProducerDiagnostics(
        IReadOnlyList<PowerProducerFact> producers,
        IReadOnlyList<PowerConsumerFact> consumers,
        IReadOnlyList<PowerConversionFact> conversions,
        ICollection<PowerTopologyDiagnostic> diagnostics)
    {
        var producedDomains = producers.Select(item => item.DomainId)
            .Concat(conversions.Select(item => item.OutputDomainId))
            .ToHashSet(IdComparer);
        var requiredInputDomains = consumers.Select(item => item.DomainId)
            .Concat(conversions.Select(item => item.InputDomainId))
            .Distinct(IdComparer)
            .OrderBy(id => id, IdComparer);

        foreach (var domainId in requiredInputDomains)
        {
            if (producedDomains.Contains(domainId)) continue;
            diagnostics.Add(Diagnostic(
                "PWR-MISSING-PRODUCER",
                $"DOMAIN:{domainId}",
                $"Power domain '{domainId}' is consumed but has no explicit producer."));
        }
    }

    private static ConversionGraph BuildConversionGraph(IReadOnlyList<PowerConversionFact> conversions)
    {
        var byId = conversions.ToDictionary(item => item.ConversionId, IdComparer);
        var adjacency = byId.Keys.ToDictionary(id => id, _ => new SortedSet<string>(IdComparer), IdComparer);
        var indegree = byId.Keys.ToDictionary(id => id, _ => 0, IdComparer);

        foreach (var successor in conversions)
        foreach (var predecessor in conversions)
        {
            if (!string.Equals(predecessor.OutputDomainId, successor.InputDomainId, StringComparison.Ordinal)) continue;
            if (!adjacency[predecessor.ConversionId].Add(successor.ConversionId)) continue;
            indegree[successor.ConversionId]++;
        }

        return new ConversionGraph(byId, adjacency, indegree);
    }

    private static IReadOnlyList<string> BuildCanonicalTopologicalOrder(ConversionGraph graph)
    {
        if (graph.ById.Count == 0) return Array.Empty<string>();

        var indegree = graph.Indegree.ToDictionary(item => item.Key, item => item.Value, IdComparer);
        var ready = new SortedSet<string>(
            indegree.Where(item => item.Value == 0).Select(item => item.Key),
            IdComparer);
        var order = new List<string>(graph.ById.Count);

        while (ready.Count > 0)
        {
            var current = ready.Min!;
            ready.Remove(current);
            order.Add(current);
            foreach (var successor in graph.Adjacency[current])
            {
                indegree[successor]--;
                if (indegree[successor] == 0) ready.Add(successor);
            }
        }

        return order.Count == graph.ById.Count ? order.ToArray() : Array.Empty<string>();
    }

    private static IReadOnlyList<PowerTopologyDiagnostic> BuildCycleDiagnostics(ConversionGraph graph)
    {
        if (graph.ById.Count == 0) return Array.Empty<PowerTopologyDiagnostic>();

        var index = 0;
        var indexByNode = new Dictionary<string, int>(IdComparer);
        var lowLink = new Dictionary<string, int>(IdComparer);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(IdComparer);
        var cyclicComponents = new List<string[]>();

        void StrongConnect(string node)
        {
            indexByNode[node] = index;
            lowLink[node] = index;
            index++;
            stack.Push(node);
            onStack.Add(node);

            foreach (var successor in graph.Adjacency[node])
            {
                if (!indexByNode.ContainsKey(successor))
                {
                    StrongConnect(successor);
                    lowLink[node] = Math.Min(lowLink[node], lowLink[successor]);
                }
                else if (onStack.Contains(successor))
                {
                    lowLink[node] = Math.Min(lowLink[node], indexByNode[successor]);
                }
            }

            if (lowLink[node] != indexByNode[node]) return;

            var component = new List<string>();
            string member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                component.Add(member);
            } while (!string.Equals(member, node, StringComparison.Ordinal));

            component.Sort(IdComparer);
            var isCycle = component.Count > 1 || graph.Adjacency[component[0]].Contains(component[0]);
            if (isCycle) cyclicComponents.Add(component.ToArray());
        }

        foreach (var node in graph.ById.Keys.OrderBy(id => id, IdComparer))
            if (!indexByNode.ContainsKey(node)) StrongConnect(node);

        return cyclicComponents
            .OrderBy(component => component[0], IdComparer)
            .Select(component => Diagnostic(
                "PWR-CYCLE",
                $"CONVERSIONS:{string.Join(",", component)}",
                $"Conversion cycle detected among stable identities [{string.Join(", ", component)}]."))
            .ToArray();
    }

    private static HashSet<string> BuildReachableDomains(
        IReadOnlyList<PowerProducerFact> producers,
        IReadOnlyList<PowerConversionFact> conversions)
    {
        var reachable = producers.Select(item => item.DomainId).ToHashSet(IdComparer);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var conversion in conversions.OrderBy(item => item.ConversionId, IdComparer))
            {
                if (!reachable.Contains(conversion.InputDomainId)) continue;
                if (reachable.Add(conversion.OutputDomainId)) changed = true;
            }
        }
        return reachable;
    }

    private static void AddOrphanConversionDiagnostics(
        IReadOnlyList<PowerConversionFact> conversions,
        IReadOnlySet<string> reachableDomains,
        ICollection<PowerTopologyDiagnostic> diagnostics)
    {
        foreach (var conversion in conversions.OrderBy(item => item.ConversionId, IdComparer))
        {
            if (reachableDomains.Contains(conversion.InputDomainId)) continue;
            diagnostics.Add(Diagnostic(
                "PWR-ORPHAN-CONVERSION",
                $"CONVERSION:{conversion.ConversionId}",
                $"Conversion '{conversion.ConversionId}' input domain '{conversion.InputDomainId}' is not reachable from an explicit producer."));
        }
    }

    private static void AddUnreachableConsumerDiagnostics(
        IReadOnlyList<PowerConsumerFact> consumers,
        IReadOnlySet<string> reachableDomains,
        ICollection<PowerTopologyDiagnostic> diagnostics)
    {
        foreach (var consumer in consumers.OrderBy(item => item.ConsumerId, IdComparer))
        {
            if (reachableDomains.Contains(consumer.DomainId)) continue;
            diagnostics.Add(Diagnostic(
                "PWR-UNREACHABLE-CONSUMER",
                $"CONSUMER:{consumer.ConsumerId}",
                $"Consumer '{consumer.ConsumerId}' domain '{consumer.DomainId}' is not reachable from an explicit producer."));
        }
    }

    private static bool IsStableIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
        !value.Any(char.IsControl);

    private static string RenderIdentity(string? value)
    {
        if (value is null) return "<NULL>";
        if (string.IsNullOrWhiteSpace(value)) return "<EMPTY>";
        return new string(value.Select(character => char.IsControl(character) ? '?' : character).ToArray());
    }

    private static PowerTopologyDiagnostic Diagnostic(string code, string subjectId, string message) => new()
    {
        Code = code,
        SubjectId = subjectId,
        Message = message
    };

    private sealed record ConversionGraph(
        IReadOnlyDictionary<string, PowerConversionFact> ById,
        IReadOnlyDictionary<string, SortedSet<string>> Adjacency,
        IReadOnlyDictionary<string, int> Indegree);
}
