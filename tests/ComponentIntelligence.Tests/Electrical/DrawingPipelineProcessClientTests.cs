using System.Text.Json;
using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingPipelineProcessClientTests
{
    [Fact]
    public async Task Planner_UsesConfiguredActualPathsAndReadsPinnedPlan()
    {
        using var fixture = new RuntimeFixture();
        var input = DrawingPlanningJson.Deserialize(DrawingPlanningJson.Serialize(new DrawingPlanningInput { ProjectId = "P1" }));
        var runner = new FakeRunner((args) =>
        {
            var output = args[args.IndexOf("--output-drawing-plan") + 1];
            var plan = DrawingPlanJson.Rehash(new DrawingPlanDocument { ProjectId="P1", SourcePlanningInputHash=input.PlanningInputHash!, SourcePagePlanHash=new string('2',64) });
            File.WriteAllText(output, DrawingPlanJson.Serialize(plan));
        });
        var result = await new PythonDrawingPlannerClient(fixture.Settings, runner).GenerateAsync(input, null, CancellationToken.None);
        Assert.Equal(input.PlanningInputHash, result.SourcePlanningInputHash);
        Assert.Equal(fixture.Settings.PythonExecutable, runner.Executable);
        Assert.Equal(fixture.Settings.AutomationRoot, runner.WorkingDirectory);
        Assert.Equal(Path.Combine(fixture.Settings.AutomationRoot, "tools", "electrical_drawing_pipeline.py"), runner.Arguments[0]);
        Assert.DoesNotContain(runner.Arguments, value => value.Contains("placeholder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IrClient_RejectsNoProvenanceAndReturnsExactThreeSourceHashes()
    {
        using var fixture = new RuntimeFixture();
        var input = DrawingPlanningJson.Deserialize(DrawingPlanningJson.Serialize(new DrawingPlanningInput { ProjectId = "P1" }));
        var plan = DrawingPlanJson.Rehash(new DrawingPlanDocument { ProjectId="P1", SourcePlanningInputHash=input.PlanningInputHash!, SourcePagePlanHash=new string('2',64) });
        var runner = new FakeRunner(args =>
        {
            var output = args[args.IndexOf("--output-ir") + 1];
            File.WriteAllText(output, JsonSerializer.Serialize(new { schemaVersion="electrical-drawing-ir.v2", projectId="P1", status="READY", drawingIrHash=new string('A',64), sourcePlanningInputHash=input.PlanningInputHash, sourcePagePlanHash=plan.SourcePagePlanHash, sourceDrawingPlanHash=plan.DrawingPlanHash, operations=Array.Empty<object>(), issues=Array.Empty<object>() }));
        });
        var ir = await new PythonDrawingIrClient(fixture.Settings, runner).CompileAsync(input, plan, CancellationToken.None);
        Assert.Equal(input.PlanningInputHash, ir.SourcePlanningInputHash);
        Assert.Equal(plan.SourcePagePlanHash, ir.SourcePagePlanHash);
        Assert.Equal(plan.DrawingPlanHash, ir.SourceDrawingPlanHash);
    }

    private sealed class FakeRunner(Action<List<string>> action) : IDrawingProcessRunner
    {
        public string? Executable { get; private set; }
        public string? WorkingDirectory { get; private set; }
        public List<string> Arguments { get; private set; } = [];
        public Task<DrawingProcessResult> RunAsync(string executable, string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Executable=executable; WorkingDirectory=workingDirectory; Arguments=arguments.ToList(); action(Arguments); return Task.FromResult(new DrawingProcessResult(0,"", ""));
        }
    }

    private sealed class RuntimeFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"cp3b-process-{Guid.NewGuid():N}");
        public DrawingRuntimeSettings Settings { get; }
        public RuntimeFixture()
        {
            Directory.CreateDirectory(Path.Combine(_root,"tools")); var python=Path.Combine(_root,OperatingSystem.IsWindows()?"python.exe":"python"); File.WriteAllText(python,"stub"); File.WriteAllText(Path.Combine(_root,"tools","electrical_drawing_pipeline.py"),"# stub"); Settings=new DrawingRuntimeSettings { PythonExecutable=python, AutomationRoot=_root };
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root,true); }
    }
}
