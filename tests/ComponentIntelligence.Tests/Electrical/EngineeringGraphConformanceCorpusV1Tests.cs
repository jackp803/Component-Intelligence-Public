using System.Text.Json;
using ComponentIntelligence.Electrical.Export;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class EngineeringGraphConformanceCorpusV1Tests
{
    [Fact]
    public void Manifest_DeclaresRequiredSyntheticCoverage()
    {
        using var document = LoadManifest();
        var root = document.RootElement;

        Assert.Equal(EngineeringGraphConformanceCorpusV1.CorpusSchemaVersion,
            root.GetProperty("corpusSchemaVersion").GetString());
        Assert.True(root.GetProperty("syntheticTestOnly").GetBoolean());
        Assert.Equal(EngineeringGraphConformanceCorpusV1.GraphSchemaVersion,
            root.GetProperty("graphSchemaVersion").GetString());
        Assert.Equal(EngineeringGraphConformanceCorpusV1.FingerprintSchemaVersion,
            root.GetProperty("fingerprintSchemaVersion").GetString());

        var manifestItems = root.GetProperty("fixtures").EnumerateArray().ToArray();
        Assert.Equal(EngineeringGraphConformanceCorpusV1.Cases.Count, manifestItems.Length);

        var fixtureIds = manifestItems
            .Select(item => item.GetProperty("fixtureId").GetString())
            .Where(item => item is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var expected in EngineeringGraphConformanceCorpusV1.Cases)
            Assert.Contains(expected.FixtureId, fixtureIds);
    }

    [Fact]
    public void Manifest_MetadataAndFixtureFiles_MatchExecutableCorpus()
    {
        using var document = LoadManifest();
        var root = document.RootElement;

        foreach (var item in root.GetProperty("fixtures").EnumerateArray())
        {
            var fixtureId = Assert.IsType<string>(item.GetProperty("fixtureId").GetString());
            var expected = EngineeringGraphConformanceCorpusV1.Get(fixtureId);
            Assert.Equal(expected.ExpectedStatus.ToString().ToUpperInvariant(),
                item.GetProperty("expectedStatus").GetString());
            Assert.Equal(expected.ExpectedFingerprint,
                OptionalString(item, "expectedFingerprint"));
            Assert.Equal(expected.Purpose, item.GetProperty("purpose").GetString());

            var blockerCodes = item.GetProperty("blockerCodes").EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => value is not null)
                .Cast<string>()
                .ToArray();
            Assert.Equal(expected.ExpectedBlockerCodes.OrderBy(value => value, StringComparer.Ordinal),
                blockerCodes.OrderBy(value => value, StringComparer.Ordinal));

            var important = item.GetProperty("importantIdentities").EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => value is not null)
                .Cast<string>()
                .ToArray();
            Assert.Equal(expected.ImportantIdentities.OrderBy(value => value, StringComparer.Ordinal),
                important.OrderBy(value => value, StringComparer.Ordinal));

            var fixtureFile = Assert.IsType<string>(item.GetProperty("fixtureFile").GetString());
            using var fixture = LoadFixture(fixtureFile);
            var fixtureRoot = fixture.RootElement;
            Assert.Equal("engineering-graph-conformance-fixture.v1",
                fixtureRoot.GetProperty("fixtureSchemaVersion").GetString());
            Assert.True(fixtureRoot.GetProperty("syntheticTestOnly").GetBoolean());
            Assert.Equal(fixtureId, fixtureRoot.GetProperty("fixtureId").GetString());
            Assert.Equal(EngineeringGraphConformanceCorpusV1.GraphSchemaVersion,
                fixtureRoot.GetProperty("graphSchemaVersion").GetString());
            Assert.Equal(expected.ExpectedStatus.ToString().ToUpperInvariant(),
                fixtureRoot.GetProperty("expectedStatus").GetString());
            Assert.Equal(expected.ExpectedFingerprint,
                OptionalString(fixtureRoot, "expectedFingerprint"));
            Assert.Equal(EngineeringGraphConformanceCorpusV1.CanonicalIdentity(expected.BuildGraph()),
                fixtureRoot.GetProperty("canonicalIdentity").GetString());
        }
    }

    [Theory]
    [InlineData("EGC-READY-DIRECT-001")]
    [InlineData("EGC-READY-FANOUT-001")]
    [InlineData("EGC-READY-CROSS-PAGE-001")]
    public void ReadyFixtures_AreExactSchemaReadyAndFingerprintPinned(string fixtureId)
    {
        var fixture = EngineeringGraphConformanceCorpusV1.Get(fixtureId);
        var graph = fixture.BuildGraph();

        AutocadStagingGraphV2Contract.EnsureSupportedSchema(graph.SchemaVersion);
        var evaluation = EngineeringGraphConformanceCorpusV1.Evaluate(graph);

        Assert.Equal(EngineeringGraphConformanceStatus.Ready, evaluation.Status);
        Assert.Empty(evaluation.BlockerCodes);
        Assert.Equal(fixture.ExpectedFingerprint, evaluation.Fingerprint);
        Assert.Equal(fixture.ExpectedFingerprint, EngineeringGraphConformanceCorpusV1.Fingerprint(graph));

        var actualIdentities = EngineeringGraphConformanceCorpusV1.ImportantIdentitySet(graph);
        foreach (var identity in fixture.ImportantIdentities)
            Assert.Contains(identity, actualIdentities);
    }

    [Fact]
    public void DirectFixture_PreservesExactComponentPinConnectionPointNetAndSegmentIdentity()
    {
        var graph = EngineeringGraphConformanceCorpusV1.Get("EGC-READY-DIRECT-001").BuildGraph();
        var route = Assert.Single(graph.Routes);
        Assert.Equal("route:net-control", route.RouteId);
        Assert.Equal("net-control", route.NetIdentity);

        var source = Assert.Single(route.Nodes, node => node.NodeId == "node:src:pin-1");
        Assert.Equal("cmp-src", source.ComponentInstanceId);
        Assert.Equal("def:synthetic-src", source.ComponentDefinitionId);
        Assert.Equal("src:pin-1", source.PinId);
        Assert.Equal("SYNTH:src", source.ConnectionPoint?.SymbolKey);
        Assert.Equal("XTERM:src", source.ConnectionPoint?.ConnectionPointId);

        var segment = Assert.Single(route.Segments);
        Assert.Equal("segment:wire-direct", segment.SegmentId);
        Assert.Equal("node:src:pin-1", segment.FromNodeId);
        Assert.Equal("node:load:pin-1", segment.ToNodeId);
    }

    [Fact]
    public void FanoutFixture_HasSharedNodeWithInjectiveEndpointAndConnectionPointIdentity()
    {
        var graph = EngineeringGraphConformanceCorpusV1.Get("EGC-READY-FANOUT-001").BuildGraph();
        var route = Assert.Single(graph.Routes);
        Assert.Equal(3, route.Nodes.Count);
        Assert.Equal(2, route.Segments.Count);
        Assert.All(route.Segments, segment => Assert.Contains("node:fan-src:pin-1",
            new[] { segment.FromNodeId, segment.ToNodeId }));

        Assert.Equal(route.Nodes.Count,
            route.Nodes.Select(node => node.PinId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(route.Nodes.Count,
            route.Nodes.Select(node => node.ConnectionPoint?.ConnectionPointId)
                .Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void CrossPageFixture_CarriesExactPairRouteNetSegmentEndpointPageAndNodeIdentity()
    {
        var graph = EngineeringGraphConformanceCorpusV1.Get("EGC-READY-CROSS-PAGE-001").BuildGraph();
        var continuation = Assert.Single(graph.CrossPageContinuations);

        Assert.Equal("pair:cross-001", continuation.PairIdentity);
        Assert.Equal("route:net-cross", continuation.RouteId);
        Assert.Equal("net-cross", continuation.NetIdentity);
        Assert.Equal("segment:wire-cross", continuation.SegmentId);
        Assert.Equal("cross-src:pin-1", continuation.SourceEndpointId);
        Assert.Equal("cross-dst:pin-1", continuation.DestinationEndpointId);
        Assert.Equal("P-01", continuation.SourcePageId);
        Assert.Equal("P-02", continuation.DestinationPageId);
        Assert.Equal("node:cross-src:pin-1", continuation.SourceNodeId);
        Assert.Equal("node:cross-dst:pin-1", continuation.DestinationNodeId);
        Assert.Null(continuation.BlockingReason);
    }

    [Theory]
    [InlineData("EGC-READY-DIRECT-001")]
    [InlineData("EGC-READY-FANOUT-001")]
    [InlineData("EGC-READY-CROSS-PAGE-001")]
    public void CollectionPermutations_ProduceSameCanonicalIdentityAndFingerprint(string fixtureId)
    {
        var canonical = EngineeringGraphConformanceCorpusV1.Get(fixtureId).BuildGraph();
        var permuted = EngineeringGraphConformanceCorpusV1.BuildPermutationVariant(fixtureId);

        Assert.Equal(
            EngineeringGraphConformanceCorpusV1.CanonicalIdentity(canonical),
            EngineeringGraphConformanceCorpusV1.CanonicalIdentity(permuted));
        Assert.Equal(
            EngineeringGraphConformanceCorpusV1.Fingerprint(canonical),
            EngineeringGraphConformanceCorpusV1.Fingerprint(permuted));
    }

    [Theory]
    [InlineData("EGC-READY-DIRECT-001")]
    [InlineData("EGC-READY-FANOUT-001")]
    [InlineData("EGC-READY-CROSS-PAGE-001")]
    public void WeakDisplaySignals_CannotChangeCanonicalFingerprint(string fixtureId)
    {
        var graph = EngineeringGraphConformanceCorpusV1.Get(fixtureId).BuildGraph();
        var noisy = EngineeringGraphConformanceCorpusV1.WithWeakSignalNoise(graph);

        Assert.Equal(
            EngineeringGraphConformanceCorpusV1.CanonicalIdentity(graph),
            EngineeringGraphConformanceCorpusV1.CanonicalIdentity(noisy));
        Assert.Equal(
            EngineeringGraphConformanceCorpusV1.Fingerprint(graph),
            EngineeringGraphConformanceCorpusV1.Fingerprint(noisy));
    }

    [Theory]
    [InlineData("EGC-BLOCKED-DUPLICATE-ENDPOINT-001")]
    [InlineData("EGC-BLOCKED-CONTINUATION-MISSING-001")]
    [InlineData("EGC-BLOCKED-CONTINUATION-AMBIGUOUS-001")]
    [InlineData("EGC-BLOCKED-DUPLICATE-ROUTE-001")]
    [InlineData("EGC-BLOCKED-DUPLICATE-NODE-001")]
    [InlineData("EGC-BLOCKED-DUPLICATE-SEGMENT-001")]
    [InlineData("EGC-BLOCKED-UNKNOWN-EVIDENCE-001")]
    public void BlockedFixtures_FailClosedWithPinnedBlockerCodesAndNoReadyFingerprint(string fixtureId)
    {
        var fixture = EngineeringGraphConformanceCorpusV1.Get(fixtureId);
        var evaluation = EngineeringGraphConformanceCorpusV1.Evaluate(fixture.BuildGraph());

        Assert.Equal(EngineeringGraphConformanceStatus.Blocked, evaluation.Status);
        Assert.Null(evaluation.Fingerprint);
        Assert.Equal(
            fixture.ExpectedBlockerCodes.OrderBy(value => value, StringComparer.Ordinal),
            evaluation.BlockerCodes.OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public void UnsupportedSchema_IsBlockedInsteadOfReconstructed()
    {
        var graph = EngineeringGraphConformanceCorpusV1.Get("EGC-READY-DIRECT-001").BuildGraph() with
        {
            SchemaVersion = "unknown-engineering-graph"
        };

        var evaluation = EngineeringGraphConformanceCorpusV1.Evaluate(graph);

        Assert.Equal(EngineeringGraphConformanceStatus.Blocked, evaluation.Status);
        Assert.Contains("EGC_UNSUPPORTED_GRAPH_SCHEMA", evaluation.BlockerCodes);
        Assert.Null(evaluation.Fingerprint);
    }

    [Fact]
    public void EveryFixture_IsExplicitlySyntheticAndContainsNoPowerOrGeometrySemantics()
    {
        var manifestText = File.ReadAllText(ManifestPath());
        Assert.DoesNotContain("\"powerDomainId\"", manifestText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"conversion", manifestText, StringComparison.OrdinalIgnoreCase);

        using var document = LoadManifest();
        foreach (var item in document.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            var fixtureFile = Assert.IsType<string>(item.GetProperty("fixtureFile").GetString());
            var text = File.ReadAllText(FixturePath(fixtureFile));
            Assert.Contains("\"syntheticTestOnly\": true", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\"powerDomainId\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\"conversion", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"geometry", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static JsonDocument LoadManifest() =>
        JsonDocument.Parse(File.ReadAllText(ManifestPath()));

    private static JsonDocument LoadFixture(string fixtureFile) =>
        JsonDocument.Parse(File.ReadAllText(FixturePath(fixtureFile)));

    private static string ManifestPath() => Path.Combine(
        FindRepositoryRoot(),
        "fixtures",
        "engineering-graph-conformance",
        "v1",
        "manifest.json");

    private static string FixturePath(string fixtureFile) => Path.Combine(
        FindRepositoryRoot(),
        "fixtures",
        "engineering-graph-conformance",
        "v1",
        fixtureFile);

    private static string? OptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ComponentIntelligence.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate ComponentIntelligence.sln from the test output directory.");
    }
}
