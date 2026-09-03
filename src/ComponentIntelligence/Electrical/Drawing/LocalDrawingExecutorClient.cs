using System.Text.Json;

namespace ComponentIntelligence.Electrical.Drawing;

public sealed class LocalDrawingExecutorClient : IDrawingExecutorClient
{
    private readonly DrawingExecutorRuntimeSettings _settings;
    private readonly IDrawingProcessRunner _runner;
    private readonly IReadOnlyList<string> _productionSqlitePaths;

    public LocalDrawingExecutorClient(DrawingExecutorRuntimeSettings settings, IDrawingProcessRunner? runner = null, IReadOnlyList<string>? productionSqlitePaths = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        var validation = DrawingExecutorRuntimeSettingsValidator.Validate(settings);
        if (!validation.IsValid) throw new InvalidOperationException(string.Join("; ", validation.Issues.Select(x => x.Message)));
        _runner = runner ?? new DrawingProcessRunner();
        _productionSqlitePaths = productionSqlitePaths?.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
    }

    public async Task<DrawingExecutorResult> ExecuteAsync(DrawingIrDocument drawingIr, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(drawingIr);
        if (!string.Equals(drawingIr.Status, "READY", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(drawingIr.DrawingIrHash))
            return Failed("EXECUTOR-IR-BLOCKED", "DRAWING_EXECUTOR_IR_NOT_READY", "BLOCKED or unhashed Drawing IR cannot execute.", drawingIr.RawJson);

        var tempRoot = Path.Combine(Path.GetTempPath(), $"cp3c-component-exec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var irPath = Path.Combine(tempRoot, "drawing-ir.json");
            var runtimePath = Path.Combine(tempRoot, "runtime.json");
            var resultPath = Path.Combine(tempRoot, "execution-result.json");
            await File.WriteAllTextAsync(irPath, drawingIr.RawJson, cancellationToken);
            await File.WriteAllTextAsync(runtimePath, BuildRuntimeJson(), cancellationToken);

            var arguments = new[]
            {
                Path.Combine(_settings.AutomationRoot, "tools", "electrical_cp3c_executor.py"),
                "execute",
                "--drawing-ir", irPath,
                "--runtime-config", runtimePath,
                "--output-result", resultPath,
            };
            var process = await _runner.RunAsync(_settings.PythonExecutable, _settings.AutomationRoot, arguments, cancellationToken);
            if (!File.Exists(resultPath))
                return Failed("EXECUTOR-RESULT-MISSING", "DRAWING_EXECUTOR_RESULT_MISSING", $"CP3-C executor returned exit code {process.ExitCode} without a result document.", "{}");

            var raw = await File.ReadAllTextAsync(resultPath, cancellationToken);
            var parsed = ParseResult(raw, drawingIr.DrawingIrHash!);
            if (process.ExitCode != 0 && parsed.Status == DrawingExecutorStatus.Applied)
                return Failed("EXECUTOR-NONZERO-APPLIED-REJECTED", "DRAWING_EXECUTOR_NONZERO_APPLIED_REJECTED", "A nonzero executor process cannot be accepted as APPLIED.", raw);
            return parsed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed("EXECUTOR-CLIENT-FAILED", "DRAWING_EXECUTOR_CLIENT_FAILED", ex.Message, "{}");
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    private string BuildRuntimeJson()
    {
        var payload = new
        {
            schemaVersion = "component-drawing-executor-runtime.v1",
            pythonExecutable = _settings.PythonExecutable,
            automationRoot = _settings.AutomationRoot,
            accoreConsolePath = _settings.AccoreConsolePath,
            stagingRoot = _settings.StagingRoot,
            projectBaselineWdp = _settings.ProjectBaselineWdp,
            drawingTemplatePath = _settings.DrawingTemplatePath,
            protectedPaths = _productionSqlitePaths.Select(path => new { kind = "COMPONENT_INTELLIGENCE_RUNTIME_SQLITE", path }).ToArray(),
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = false });
    }

    private static DrawingExecutorResult ParseResult(string raw, string expectedDrawingIrHash)
    {
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetString() != "electrical-execution-result.v1") throw new InvalidDataException("Unsupported execution result schema.");
        if (!string.Equals(root.GetProperty("sourceDrawingIrHash").GetString(), expectedDrawingIrHash, StringComparison.Ordinal)) throw new InvalidDataException("Execution result sourceDrawingIrHash mismatch.");

        var statusText = root.GetProperty("status").GetString();
        var status = statusText switch
        {
            "APPLIED" => DrawingExecutorStatus.Applied,
            "BLOCKED" => DrawingExecutorStatus.Blocked,
            "FAILED" => DrawingExecutorStatus.Failed,
            _ => throw new InvalidDataException("Execution result status is invalid."),
        };
        var projectFile = NullOrString(root.GetProperty("projectFile"));
        var stagingRoot = NullOrString(root.GetProperty("stagingRoot"));
        var evidenceHash = NullOrString(root.GetProperty("executionEvidenceHash"));
        var pageDrawings = root.GetProperty("pageDrawings").EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToArray();
        var issues = ParseIssues(root);

        if (status == DrawingExecutorStatus.Applied && (string.IsNullOrWhiteSpace(projectFile) || pageDrawings.Length == 0 || string.IsNullOrWhiteSpace(evidenceHash)))
            return Failed("EXECUTOR-APPLIED-EVIDENCE-INCOMPLETE", "DRAWING_EXECUTOR_APPLIED_EVIDENCE_INCOMPLETE", "APPLIED execution result lacks required WDP/DWG/evidence identity.", raw);

        return new DrawingExecutorResult(status, stagingRoot, projectFile, pageDrawings, evidenceHash, issues, raw);
    }

    private static IReadOnlyList<DrawingActionableIssue> ParseIssues(JsonElement root)
    {
        if (!root.TryGetProperty("issues", out var array) || array.ValueKind != JsonValueKind.Array) return [];
        var issues = new List<DrawingActionableIssue>();
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            var code = item.TryGetProperty("code", out var codeElement) ? codeElement.GetString() ?? "CP3C_EXECUTION_ISSUE" : "CP3C_EXECUTION_ISSUE";
            var message = item.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? "CP3-C execution issue." : "CP3-C execution issue.";
            issues.Add(DrawingActionableIssue.Runtime($"CP3C-EXECUTION-{index++:D3}", code, message, "ExecutorRuntimeSettings"));
        }
        return issues;
    }

    private static string? NullOrString(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetString();

    private static DrawingExecutorResult Failed(string id, string code, string message, string raw) =>
        new(DrawingExecutorStatus.Failed, null, null, [], null, [DrawingActionableIssue.Runtime(id, code, message, "ExecutorRuntimeSettings")], raw);
}
