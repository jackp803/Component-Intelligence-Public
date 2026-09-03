using System.Text.Json;
using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingExecutorClientTests
{
    [Fact]
    public void SettingsValidation_RequiresAllSixRealLocalPaths()
    {
        using var fixture = new RuntimeFixture();
        var validation = DrawingExecutorRuntimeSettingsValidator.Validate(fixture.Settings);
        Assert.True(validation.IsValid, string.Join("; ", validation.Issues.Select(x => x.Message)));
        Assert.Equal(6, typeof(DrawingExecutorRuntimeSettings).GetProperties().Length);
    }

    [Fact]
    public void SettingsStore_RejectsInvalidAndPersistsOnlyUserLocalSettings()
    {
        using var fixture = new RuntimeFixture();
        var storePath = Path.Combine(fixture.Root, "settings.json");
        var store = new DrawingExecutorRuntimeSettingsStore(storePath);
        store.Save(fixture.Settings);
        var loaded = store.Load();
        Assert.Equal(fixture.Settings, loaded);
        var bad = fixture.Settings with { AccoreConsolePath = Path.Combine(fixture.Root, "missing.exe") };
        Assert.Throws<InvalidOperationException>(() => store.Save(bad));
        Assert.Equal(fixture.Settings, store.Load());
    }

    [Fact]
    public async Task LocalClient_UsesExactPythonExecutorArgvAndParsesAppliedResult()
    {
        using var fixture = new RuntimeFixture();
        var runner = new FakeRunner();
        var client = new LocalDrawingExecutorClient(fixture.Settings, runner, [fixture.ProductionSqlite]);
        var ir = new DrawingIrDocument("READY", new string('A', 64), new string('B',64), new string('C',64), new string('D',64), [], "{\"schemaVersion\":\"electrical-drawing-ir.v2\"}");
        var result = await client.ExecuteAsync(ir, CancellationToken.None);
        Assert.Equal(DrawingExecutorStatus.Applied, result.Status);
        Assert.EndsWith(".wdp", result.ProjectFile, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.PageDrawings);
        Assert.Single(runner.Calls);
        var call = runner.Calls[0];
        Assert.Equal(fixture.Settings.PythonExecutable, call.Executable);
        Assert.Equal(fixture.Settings.AutomationRoot, call.WorkingDirectory);
        Assert.Equal(Path.Combine(fixture.Settings.AutomationRoot, "tools", "electrical_cp3c_executor.py"), call.Arguments[0]);
        Assert.Equal("execute", call.Arguments[1]);
        Assert.Contains("--drawing-ir", call.Arguments);
        Assert.Contains("--runtime-config", call.Arguments);
        Assert.Contains("--output-result", call.Arguments);
        Assert.All(runner.ObservedTemporaryInputs, path => Assert.False(File.Exists(path)));
    }

    private sealed class FakeRunner : IDrawingProcessRunner
    {
        public List<(string Executable,string WorkingDirectory,IReadOnlyList<string> Arguments)> Calls { get; } = [];
        public List<string> ObservedTemporaryInputs { get; } = [];
        public Task<DrawingProcessResult> RunAsync(string executable, string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Calls.Add((executable, workingDirectory, arguments.ToArray()));
            var irPath = arguments[Array.IndexOf(arguments.ToArray(), "--drawing-ir") + 1];
            var runtimePath = arguments[Array.IndexOf(arguments.ToArray(), "--runtime-config") + 1];
            var resultPath = arguments[Array.IndexOf(arguments.ToArray(), "--output-result") + 1];
            ObservedTemporaryInputs.Add(irPath); ObservedTemporaryInputs.Add(runtimePath);
            using var runtime = JsonDocument.Parse(File.ReadAllText(runtimePath));
            Assert.Equal("component-drawing-executor-runtime.v1", runtime.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Contains(runtime.RootElement.GetProperty("protectedPaths").EnumerateArray(), x => x.GetProperty("path").GetString()!.EndsWith("prod.db", StringComparison.Ordinal));
            var raw = JsonSerializer.Serialize(new
            {
                schemaVersion="electrical-execution-result.v1", status="APPLIED", runId="RUN-1",
                sourceDrawingIrHash=new string('A',64), sourceExecutorPlanHash=new string('E',64), sourcePackageHash=new string('F',64),
                stagingRoot=Path.Combine(Path.GetTempPath(),"cp3c-stage"), projectFile=Path.Combine(Path.GetTempPath(),"cp3c-stage","P1.wdp"),
                pageDrawings=new[]{Path.Combine(Path.GetTempPath(),"cp3c-stage","001_PAGE-A.dwg")}, commandEvents=Array.Empty<object>(), issues=Array.Empty<object>(), executionEvidenceHash=new string('9',64)
            });
            File.WriteAllText(resultPath, raw);
            return Task.FromResult(new DrawingProcessResult(0, "", ""));
        }
    }

    private sealed class RuntimeFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"cp3c-settings-{Guid.NewGuid():N}");
        public string ProductionSqlite => Path.Combine(Root, "prod.db");
        public DrawingExecutorRuntimeSettings Settings { get; }
        public RuntimeFixture()
        {
            Directory.CreateDirectory(Root); Directory.CreateDirectory(Path.Combine(Root,"automation","tools")); Directory.CreateDirectory(Path.Combine(Root,"staging"));
            var python=Path.Combine(Root,"python.exe"); var accore=Path.Combine(Root,"accoreconsole.exe"); var baseline=Path.Combine(Root,"baseline.wdp"); var template=Path.Combine(Root,"template.dwt");
            File.WriteAllText(python,"x"); File.WriteAllText(accore,"x"); File.WriteAllText(baseline,"x"); File.WriteAllText(template,"x"); File.WriteAllText(Path.Combine(Root,"automation","tools","electrical_cp3c_executor.py"),"# test"); File.WriteAllText(ProductionSqlite,"db");
            Settings = new DrawingExecutorRuntimeSettings { PythonExecutable=python, AutomationRoot=Path.Combine(Root,"automation"), AccoreConsolePath=accore, StagingRoot=Path.Combine(Root,"staging"), ProjectBaselineWdp=baseline, DrawingTemplatePath=template };
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root,true); }
    }
}
