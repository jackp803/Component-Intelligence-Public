using System.Text.Json;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class EngineeringGraphConformanceCorpusV1Tests
{
    [Fact]
    public void Manifest_DeclaresRequiredSyntheticCoverage()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "fixtures",
            "engineering-graph-conformance",
            "v1",
            "manifest.json");

        Assert.True(File.Exists(path), $"Missing synthetic conformance corpus manifest: {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.Equal("engineering-graph-conformance.v1", root.GetProperty("corpusSchemaVersion").GetString());
        Assert.True(root.GetProperty("syntheticTestOnly").GetBoolean());

        var fixtures = root.GetProperty("fixtures").EnumerateArray().ToArray();
        var fixtureIds = fixtures
            .Select(item => item.GetProperty("fixtureId").GetString())
            .Where(item => item is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        var required = new[]
        {
            "EGC-READY-DIRECT-001",
            "EGC-READY-FANOUT-001",
            "EGC-READY-CROSS-PAGE-001",
            "EGC-BLOCKED-DUPLICATE-ENDPOINT-001",
            "EGC-BLOCKED-CONTINUATION-MISSING-001",
            "EGC-BLOCKED-CONTINUATION-AMBIGUOUS-001",
            "EGC-BLOCKED-DUPLICATE-ROUTE-001",
            "EGC-BLOCKED-DUPLICATE-NODE-001",
            "EGC-BLOCKED-DUPLICATE-SEGMENT-001",
            "EGC-BLOCKED-UNKNOWN-EVIDENCE-001"
        };

        foreach (var fixtureId in required)
            Assert.Contains(fixtureId, fixtureIds);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ComponentIntelligence.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate ComponentIntelligence.sln from the test output directory.");
    }
}
