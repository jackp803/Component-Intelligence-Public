using ComponentIntelligence.Electrical.PowerTopology;

namespace ComponentIntelligence.Tests.Electrical.PowerTopology.Conformance;

/// <summary>
/// SYNTHETIC / TEST-ONLY conformance verification. Passing these tests is ordinary .NET evidence only;
/// it is not AutoCAD, DWG/WDP, formal ACADE, hardware, or Product Owner local-UAT evidence.
/// </summary>
public sealed class PowerTopologyConformanceCorpusV1Tests
{
    private readonly ElectricalPowerEvidencePowerTopologyAdapter _adapter = new();
    private readonly PowerEndpointCoverageAnalyzer _coverage = new();

    [Fact]
    public void Corpus_manifest_is_complete_unique_and_deterministic()
    {
        var cases = PowerTopologyConformanceCorpusV1.Cases;
        var expectedIds = new[]
        {
            "blocked-conversion-cycle",
            "blocked-converter-empty-side",
            "blocked-converter-input-ambiguous",
            "blocked-converter-output-missing",
            "blocked-duplicate-producer",
            "blocked-missing-producer",
            "blocked-orphan-converter",
            "blocked-stale-endpoint-identity",
            "ready-direct",
            "ready-fanout",
            "ready-multilevel-conversion",
            "ready-order-invariant-conversion",
            "ready-terminal-transparency"
        };

        Assert.Equal(13, cases.Count);
        Assert.Equal(expectedIds, cases.Select(item => item.CaseId).OrderBy(item => item, StringComparer.Ordinal));
        Assert.Equal(cases.Count, cases.Select(item => item.CaseId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(cases, item => Assert.Contains(item.ExpectedDisposition, new[] { "READY", "BLOCKED" }));

        var first = PowerTopologyConformanceCorpusV1.ManifestIndex();
        var second = PowerTopologyConformanceCorpusV1.ManifestIndex();
        Assert.Equal(first, second);
        Assert.Equal(13, first.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.All(expectedIds, caseId => Assert.Contains($"|{caseId}|", first, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_manifest_case_matches_exact_accepted_E1_E2_outcome()
    {
        foreach (var item in PowerTopologyConformanceCorpusV1.Cases)
        {
            var adapter = _adapter.AdaptAndAnalyze(item.Graph);
            Assert.True(
                adapter.Status == PowerTopologyAdapterStatus.Accepted,
                $"{item.CaseId}: corpus uses only accepted E1 evidence and must reach E2 analysis; adapter diagnostics: {string.Join(",", adapter.Diagnostics.Select(d => d.Code + "@" + d.SubjectId))}");
            Assert.NotNull(adapter.Input);
            Assert.NotNull(adapter.Analysis);

            var analysis = adapter.Analysis!;
            var coverage = _coverage.Analyze(item.Graph, adapter);
            var actualDisposition = analysis.Status == PowerTopologyAnalysisStatus.Accepted &&
                                    coverage.Status == PowerEndpointCoverageStatus.Accepted
                ? "READY"
                : "BLOCKED";

            Assert.Equal(item.ExpectedDisposition, actualDisposition);
            Assert.Equal(item.ExpectedDomainIds, analysis.DomainIds);
            Assert.Equal(item.ExpectedProducers, analysis.Producers
                .Select(value => $"{value.ProducerId}>{value.DomainId}")
                .OrderBy(value => value, StringComparer.Ordinal));
            Assert.Equal(item.ExpectedConsumers, analysis.Consumers
                .Select(value => $"{value.ConsumerId}<{value.DomainId}")
                .OrderBy(value => value, StringComparer.Ordinal));
            Assert.Equal(item.ExpectedConversionEdges, adapter.Input!.Conversions
                .Select(value => $"{value.ConversionId}:{value.InputDomainId}>{value.OutputDomainId}")
                .OrderBy(value => value, StringComparer.Ordinal));
            Assert.Equal(item.ExpectedTopologicalOrder, analysis.ConversionTopologicalOrder);
            Assert.Equal(
                item.ExpectedCoverage.OrderBy(value => value, StringComparer.Ordinal),
                coverage.Participants.Select(value =>
                        $"{value.Role}:{value.EndpointId}:{value.DomainId}:{value.Covered.ToString().ToLowerInvariant()}")
                    .OrderBy(value => value, StringComparer.Ordinal));
            Assert.Equal(
                item.ExpectedSemanticBlockers.OrderBy(value => value, StringComparer.Ordinal),
                analysis.Diagnostics.Select(value => $"{value.Code}@{value.SubjectId}")
                    .OrderBy(value => value, StringComparer.Ordinal));
            Assert.Equal(
                item.ExpectedCoverageBlockers.OrderBy(value => value, StringComparer.Ordinal),
                coverage.Diagnostics.Select(value => $"{value.Code}@{value.SubjectId}")
                    .OrderBy(value => value, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void Reversed_non_semantic_collection_and_endpoint_order_is_logically_identical()
    {
        var item = Assert.Single(
            PowerTopologyConformanceCorpusV1.Cases,
            candidate => candidate.CaseId == "ready-order-invariant-conversion");
        var permuted = PowerTopologyConformanceCorpusV1.PermuteOrderInvariantCase(item.Graph);

        var first = Analyze(item.Graph);
        var second = Analyze(permuted);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Corpus_never_relies_on_weak_signal_fields_to_reconstruct_stale_identity()
    {
        var item = Assert.Single(
            PowerTopologyConformanceCorpusV1.Cases,
            candidate => candidate.CaseId == "blocked-stale-endpoint-identity");
        var adapter = _adapter.AdaptAndAnalyze(item.Graph);
        var coverage = _coverage.Analyze(item.Graph, adapter);

        Assert.Equal(PowerTopologyAnalysisStatus.Accepted, adapter.Analysis!.Status);
        Assert.Equal(PowerEndpointCoverageStatus.Blocked, coverage.Status);
        Assert.Contains(
            coverage.Diagnostics,
            diagnostic => diagnostic.Code == "PWR-COVERAGE-PARTICIPANT-ANCHOR-MISSING" &&
                          diagnostic.SubjectId == "CONSUMER:C-NEW");
        Assert.DoesNotContain(
            coverage.Participants,
            participant => participant.EndpointId == "C-NEW" && participant.Covered);
    }

    private string Analyze(ComponentIntelligence.Electrical.Export.AutocadStagingGraphV2Contract graph)
    {
        var adapter = _adapter.AdaptAndAnalyze(graph);
        var analysis = adapter.Analysis!;
        var coverage = _coverage.Analyze(graph, adapter);
        return string.Join("|",
            adapter.Status,
            analysis.Status,
            string.Join(",", analysis.DomainIds),
            string.Join(",", analysis.Producers.Select(item => $"{item.ProducerId}>{item.DomainId}")),
            string.Join(",", analysis.Consumers.Select(item => $"{item.ConsumerId}<{item.DomainId}")),
            string.Join(",", adapter.Input!.Conversions.Select(item =>
                $"{item.ConversionId}:{item.InputDomainId}>{item.OutputDomainId}:{string.Join("+", item.InputEndpointIds)}:{string.Join("+", item.OutputEndpointIds)}")),
            string.Join(",", analysis.ConversionTopologicalOrder),
            string.Join(",", analysis.Diagnostics.Select(item => $"{item.Code}@{item.SubjectId}")),
            coverage.Status,
            string.Join(",", coverage.Participants.Select(item =>
                $"{item.Role}:{item.EndpointId}:{item.DomainId}:{item.NodeId}:{item.Covered}:{item.CoverageBasis}")),
            string.Join(",", coverage.Diagnostics.Select(item => $"{item.Code}@{item.SubjectId}")));
    }
}