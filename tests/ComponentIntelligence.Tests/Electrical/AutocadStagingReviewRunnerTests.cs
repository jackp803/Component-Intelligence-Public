using System.Diagnostics;
using System.Text.Json;
using ComponentIntelligence.Desktop;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class AutocadStagingReviewRunnerTests : IDisposable
{
    private readonly string _fixtureRoot = Path.Combine(Path.GetTempPath(), $"ci-acade-runner-{Guid.NewGuid():N}");
    private readonly string _outputRoot = Path.Combine(AutocadStagingReviewRunner.DefaultStagingRoot, $"runner-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task MissingSymbolAcceptanceRegistry_StartsZeroSubprocesses()
    {
        var executor = new FakeProcessExecutor();
        var runner = Runner(executor);
        var request = Request(Graph(), Path.Combine(_fixtureRoot, "missing-registry.json"));

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() => runner.RunAsync(request));

        Assert.Contains("symbol acceptance registry", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, executor.ExecutionCount);
    }

    [Fact]
    public async Task BlockingDrawingIntervention_StartsZeroSubprocesses()
    {
        var executor = new FakeProcessExecutor();
        var runner = Runner(executor);
        var request = Request(Graph(new { interventionId = "role:K1", drawingMayContinue = false }), Touch("registry.json"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => runner.RunAsync(request));

        Assert.Contains("role:K1", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, executor.ExecutionCount);
    }

    [Fact]
    public async Task CompleteGates_BuildEngineeringStagingCommandWithRegistryPath()
    {
        var executor = new FakeProcessExecutor
        {
            Result = new AutocadStagingProcessResult(23, string.Empty, "fake process intentionally stopped")
        };
        var runner = Runner(executor);
        var registryPath = Touch("registry.json");
        var request = Request(Graph(), registryPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(request));

        Assert.Equal(1, executor.ExecutionCount);
        var startInfo = Assert.IsType<ProcessStartInfo>(executor.StartInfo);
        var arguments = startInfo.ArgumentList.ToArray();
        var registryIndex = Array.IndexOf(arguments, AutocadStagingReviewRunner.SymbolAcceptanceRegistryParameter);
        Assert.True(registryIndex >= 0);
        Assert.Equal(Path.GetFullPath(registryPath), arguments[registryIndex + 1]);
        Assert.Contains(arguments, argument =>
            argument.EndsWith(AutocadStagingReviewRunner.EngineeringStagingScriptFileName, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("-PocMode", arguments);
    }

    [Fact]
    public void ManifestWithoutPdfPaths_AcceptsRequiredWdpAndDwgOutputs()
    {
        Directory.CreateDirectory(_outputRoot);
        var projectPath = Path.Combine(_outputRoot, "LRDU-TEST.wdp");
        var drawingPath = Path.Combine(_outputRoot, "01-LRDU.dwg");
        var manifestPath = Path.Combine(_outputRoot, "staging-manifest.json");
        File.WriteAllText(projectPath, string.Empty);
        File.WriteAllText(drawingPath, string.Empty);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            projectPath,
            drawingPaths = new[] { drawingPath },
            writerExecuted = true,
            formalDwgModified = "NO"
        }));

        var result = AutocadStagingReviewManifest.Load(manifestPath, _outputRoot);

        Assert.Equal(projectPath, result.ProjectPath);
        Assert.Equal(new[] { drawingPath }, result.DrawingPaths);
        Assert.Empty(result.PdfPaths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixtureRoot)) Directory.Delete(_fixtureRoot, true);
        if (Directory.Exists(_outputRoot)) Directory.Delete(_outputRoot, true);
    }

    private AutocadStagingReviewRunner Runner(FakeProcessExecutor executor)
    {
        Directory.CreateDirectory(_fixtureRoot);
        var automationRoot = Path.Combine(_fixtureRoot, "automation");
        Directory.CreateDirectory(automationRoot);
        Touch(Path.Combine("automation", "Invoke-CMLrduEngineeringStaging.ps1"));
        return new AutocadStagingReviewRunner(executor, automationRoot);
    }

    private AutocadStagingReviewRequest Request(string graphPath, string registryPath) => new()
    {
        GraphPath = graphPath,
        OutputRoot = _outputRoot,
        ProjectName = "LRDU-TEST",
        SymbolAcceptanceRegistryPath = registryPath
    };

    private string Graph(object? intervention = null)
    {
        Directory.CreateDirectory(_fixtureRoot);
        var path = Path.Combine(_fixtureRoot, $"graph-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion = "lrdu-staging-route.v1",
            interventions = intervention is null ? Array.Empty<object>() : new[] { intervention }
        }));
        return path;
    }

    private string Touch(string relativePath)
    {
        var path = Path.Combine(_fixtureRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private sealed class FakeProcessExecutor : IAutocadStagingProcessExecutor
    {
        public int ExecutionCount { get; private set; }
        public ProcessStartInfo? StartInfo { get; private set; }
        public AutocadStagingProcessResult Result { get; init; } = new(0, string.Empty, string.Empty);

        public Task<AutocadStagingProcessResult> ExecuteAsync(
            ProcessStartInfo startInfo,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            StartInfo = startInfo;
            return Task.FromResult(Result);
        }
    }
}
