using System.Text.Json;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Export;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class AutocadStagingGraphV2ExportCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"ci-week1-cp2b-v2-export-{Guid.NewGuid():N}");

    [Fact]
    public void Export_ValidAuditedEvidence_WritesExactlyOneV2ArtifactWithoutMutatingProject()
    {
        var project = Week1FirstSliceV2Fixture.CreateProject();
        var projectBefore = JsonSerializer.Serialize(project);
        var stagingRoot = Path.Combine(_root, "staging");
        var coordinator = new AutocadStagingGraphV2ExportCoordinator(
            stagingRoot,
            () => "run-001");

        var result = coordinator.Export(
            project,
            WriteBindings("bindings.json"),
            WriteEvidence("evidence.json"));

        Assert.True(result.Succeeded);
        Assert.Equal("lrdu-staging-route.v2", result.SchemaVersion);
        Assert.Equal(Week1FirstSliceV2Fixture.ProjectId, result.ProjectId);
        var graphPath = Assert.IsType<string>(result.GraphPath);
        Assert.Equal(
            Path.Combine(stagingRoot, "run-001", AutocadStagingGraphV2Exporter.ArtifactFileName),
            graphPath);
        Assert.Equal(graphPath, Assert.Single(Directory.GetFiles(stagingRoot, "*", SearchOption.AllDirectories)));
        using var document = JsonDocument.Parse(File.ReadAllBytes(graphPath));
        Assert.Equal("lrdu-staging-route.v2", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(Week1FirstSliceV2Fixture.ProjectId, document.RootElement.GetProperty("projectId").GetString());
        Assert.Equal(projectBefore, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void Export_MissingAuditedBindings_ReturnsBlockingEvidenceAndWritesNothing()
    {
        var stagingRoot = Path.Combine(_root, "missing-bindings-staging");
        var coordinator = new AutocadStagingGraphV2ExportCoordinator(
            stagingRoot,
            () => "run-002");

        var result = coordinator.Export(
            Week1FirstSliceV2Fixture.CreateProject(),
            Path.Combine(_root, "missing-bindings.json"),
            WriteEvidence("missing-bindings-evidence.json"));

        Assert.False(result.Succeeded);
        Assert.Null(result.GraphPath);
        Assert.Null(result.SchemaVersion);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "AuditedBindingsSidecarMissing" && issue.Severity == "Error");
        Assert.False(Directory.Exists(stagingRoot));
    }

    [Fact]
    public void Export_PreparationHardError_ReturnsBlockingEvidenceAndWritesNothing()
    {
        var stagingRoot = Path.Combine(_root, "blocked-staging");
        var coordinator = new AutocadStagingGraphV2ExportCoordinator(
            stagingRoot,
            () => "run-003");

        var result = coordinator.Export(
            Week1FirstSliceV2Fixture.CreateProject(),
            WriteBindings("incomplete-bindings.json", omitLast: true),
            WriteEvidence("blocked-evidence.json"));

        Assert.False(result.Succeeded);
        Assert.Null(result.GraphPath);
        Assert.Contains(result.Issues, issue => issue.Severity == "Error");
        Assert.False(Directory.Exists(stagingRoot));
    }

    [Fact]
    public void CoordinatorContract_HasNoSymbolRegistryOrLaunchDependency()
    {
        var publicSurface = typeof(AutocadStagingGraphV2ExportCoordinator)
            .GetMembers()
            .Select(member => member.ToString())
            .Where(text => text is not null)
            .Select(text => text!)
            .ToArray();

        Assert.DoesNotContain(publicSurface, text =>
            text.Contains("SymbolAcceptance", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Registry", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Process", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Runner", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string WriteBindings(string name, bool omitLast = false)
    {
        var bindings = Week1FirstSliceV2Fixture.AuditedBindings();
        if (omitLast) bindings = bindings.SkipLast(1).ToArray();
        return WriteJson(name, new
        {
            schemaVersion = AutocadConnectionPointBindingLoader.SchemaVersion,
            bindings = bindings.Select(binding => new
            {
                endpointId = binding.EndpointId,
                symbolKey = binding.SymbolKey,
                connectionPointId = binding.ConnectionPointId
            })
        });
    }

    private string WriteEvidence(string name) => WriteJson(name, new
    {
        schemaVersion = AutocadEngineeringDrawingEvidenceLoader.SchemaVersion,
        projectId = Week1FirstSliceV2Fixture.ProjectId,
        componentRoles = new[]
        {
            new
            {
                componentInstanceId = Week1FirstSliceV2Fixture.BelimoComponentId,
                role = "ValveOrPump",
                status = "Confirmed",
                evidenceSource = "test-only-pm-accepted-first-slice"
            },
            new
            {
                componentInstanceId = Week1FirstSliceV2Fixture.X3ComponentId,
                role = "CableOrConnector",
                status = "Confirmed",
                evidenceSource = "test-only-pm-accepted-first-slice"
            }
        },
        powerFlowOrientations = Array.Empty<object>(),
        crossPageContinuations = Array.Empty<object>(),
        cableInstanceOverrides = Array.Empty<object>()
    });

    private string WriteJson(string name, object value)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, JsonSerializer.Serialize(value));
        return path;
    }
}
