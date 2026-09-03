using System.Diagnostics;
using System.Text.Json;

namespace ComponentIntelligence.Electrical.Drawing;

public sealed record DrawingProcessResult(int ExitCode, string StandardOutput, string StandardError);
public interface IDrawingProcessRunner
{
    Task<DrawingProcessResult> RunAsync(string executable, string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public sealed class DrawingProcessRunner : IDrawingProcessRunner
{
    public async Task<DrawingProcessResult> RunAsync(string executable, string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo { FileName = executable, WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("Unable to start drawing planning process.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new DrawingProcessResult(process.ExitCode, await stdout, await stderr);
    }
}

public sealed class PythonDrawingPlannerClient : IDrawingPlannerClient
{
    private readonly DrawingRuntimeSettings _settings;
    private readonly IDrawingProcessRunner _runner;
    public PythonDrawingPlannerClient(DrawingRuntimeSettings settings, IDrawingProcessRunner? runner = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        var validation = DrawingRuntimeSettingsValidator.Validate(settings); if (!validation.IsValid) throw new InvalidOperationException(string.Join("; ", validation.Issues.Select(x => x.Message)));
        _runner = runner ?? new DrawingProcessRunner();
    }

    public async Task<DrawingPlanDocument> GenerateAsync(DrawingPlanningInput input, DrawingPlanDocument? priorPlan, CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp3b-plan-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        try
        {
            var inputPath = Path.Combine(root, "planning-input.json"); var pagePath = Path.Combine(root, "page-plan.json"); var drawingPath = Path.Combine(root, "drawing-plan.json");
            await File.WriteAllTextAsync(inputPath, DrawingPlanningJson.Serialize(input), cancellationToken);
            var args = new List<string> { PipelineScript(), "plan", "--planning-input", inputPath, "--output-page-plan", pagePath, "--output-drawing-plan", drawingPath };
            if (priorPlan is not null) { var priorPath = Path.Combine(root, "prior-drawing-plan.json"); await File.WriteAllTextAsync(priorPath, DrawingPlanJson.Serialize(priorPlan), cancellationToken); args.Add("--prior-drawing-plan"); args.Add(priorPath); }
            var result = await _runner.RunAsync(_settings.PythonExecutable, _settings.AutomationRoot, args, cancellationToken);
            if (result.ExitCode != 0 || !File.Exists(drawingPath)) throw new InvalidOperationException($"Drawing planner failed ({result.ExitCode}): {result.StandardError}");
            var plan = DrawingPlanJson.Deserialize(await File.ReadAllTextAsync(drawingPath, cancellationToken));
            if (!string.Equals(plan.SourcePlanningInputHash, input.PlanningInputHash, StringComparison.Ordinal)) throw new InvalidDataException("Planner output sourcePlanningInputHash mismatch.");
            return plan;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
    private string PipelineScript() => Path.Combine(_settings.AutomationRoot, "tools", "electrical_drawing_pipeline.py");
}

public sealed class PythonDrawingIrClient : IDrawingIrClient
{
    private readonly DrawingRuntimeSettings _settings;
    private readonly IDrawingProcessRunner _runner;
    public PythonDrawingIrClient(DrawingRuntimeSettings settings, IDrawingProcessRunner? runner = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        var validation = DrawingRuntimeSettingsValidator.Validate(settings); if (!validation.IsValid) throw new InvalidOperationException(string.Join("; ", validation.Issues.Select(x => x.Message)));
        _runner = runner ?? new DrawingProcessRunner();
    }

    public async Task<DrawingIrDocument> CompileAsync(DrawingPlanningInput input, DrawingPlanDocument plan, CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp3b-ir-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        try
        {
            var inputPath = Path.Combine(root, "planning-input.json"); var planPath = Path.Combine(root, "drawing-plan.json"); var irPath = Path.Combine(root, "drawing-ir.json");
            await File.WriteAllTextAsync(inputPath, DrawingPlanningJson.Serialize(input), cancellationToken); await File.WriteAllTextAsync(planPath, DrawingPlanJson.Serialize(plan), cancellationToken);
            var args = new[] { Path.Combine(_settings.AutomationRoot, "tools", "electrical_drawing_pipeline.py"), "compile-ir", "--planning-input", inputPath, "--drawing-plan", planPath, "--output-ir", irPath };
            var result = await _runner.RunAsync(_settings.PythonExecutable, _settings.AutomationRoot, args, cancellationToken);
            if (result.ExitCode != 0 || !File.Exists(irPath)) throw new InvalidOperationException($"Drawing IR compiler failed ({result.ExitCode}): {result.StandardError}");
            var raw = await File.ReadAllTextAsync(irPath, cancellationToken); using var doc = JsonDocument.Parse(raw); var rootElement = doc.RootElement;
            var issues = new List<DrawingActionableIssue>();
            if (rootElement.TryGetProperty("issues", out var issueArray)) foreach (var issue in issueArray.EnumerateArray()) issues.Add(new DrawingActionableIssue { IssueId = issue.GetProperty("issueId").GetString()!, Severity = issue.GetProperty("severity").GetString() == "Blocker" ? DrawingActionableSeverity.Blocker : issue.GetProperty("severity").GetString() == "Warning" ? DrawingActionableSeverity.Warning : DrawingActionableSeverity.Info, Scope = DrawingActionableScope.Object, BlockerClass = DrawingBlockerClass.Drawing, Code = issue.GetProperty("code").GetString()!, Message = issue.GetProperty("message").GetString()!, ObjectId = issue.GetProperty("targetId").GetString(), RepairAction = "IssueTarget" });
            var ir = new DrawingIrDocument(rootElement.GetProperty("status").GetString()!, rootElement.GetProperty("drawingIrHash").ValueKind == JsonValueKind.Null ? null : rootElement.GetProperty("drawingIrHash").GetString(), rootElement.GetProperty("sourcePlanningInputHash").GetString()!, rootElement.GetProperty("sourcePagePlanHash").GetString()!, rootElement.GetProperty("sourceDrawingPlanHash").GetString()!, issues, raw);
            if (!string.Equals(ir.SourcePlanningInputHash, input.PlanningInputHash, StringComparison.Ordinal) || !string.Equals(ir.SourcePagePlanHash, plan.SourcePagePlanHash, StringComparison.Ordinal) || !string.Equals(ir.SourceDrawingPlanHash, plan.DrawingPlanHash, StringComparison.Ordinal)) throw new InvalidDataException("Drawing IR provenance mismatch.");
            if (ir.Status == "READY" && string.IsNullOrWhiteSpace(ir.DrawingIrHash)) throw new InvalidDataException("READY Drawing IR requires drawingIrHash.");
            return ir;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
