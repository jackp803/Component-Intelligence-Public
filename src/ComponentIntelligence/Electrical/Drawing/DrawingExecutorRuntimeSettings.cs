using System.Text.Json;

namespace ComponentIntelligence.Electrical.Drawing;

public sealed record DrawingExecutorRuntimeSettings
{
    public required string PythonExecutable { get; init; }
    public required string AutomationRoot { get; init; }
    public required string AccoreConsolePath { get; init; }
    public required string StagingRoot { get; init; }
    public required string ProjectBaselineWdp { get; init; }
    public required string DrawingTemplatePath { get; init; }
}

public sealed record DrawingExecutorRuntimeValidation(bool IsValid, IReadOnlyList<DrawingActionableIssue> Issues);

public static class DrawingExecutorRuntimeSettingsValidator
{
    public static DrawingExecutorRuntimeValidation Validate(DrawingExecutorRuntimeSettings? settings)
    {
        var issues = new List<DrawingActionableIssue>();
        if (settings is null)
        {
            issues.Add(DrawingActionableIssue.Runtime("EXECUTOR-SETTINGS-MISSING", "DRAWING_EXECUTOR_SETTINGS_MISSING", "AutoCAD executor settings are not configured.", "ExecutorRuntimeSettings"));
            return new(false, issues);
        }

        ValidateFile(settings.PythonExecutable, "EXECUTOR-PYTHON-MISSING", "DRAWING_EXECUTOR_PYTHON_MISSING", "Configured Python executable does not exist.", issues);
        ValidateDirectory(settings.AutomationRoot, "EXECUTOR-AUTOMATION-ROOT-MISSING", "DRAWING_EXECUTOR_AUTOMATION_ROOT_MISSING", "Configured AutoCAD automation root does not exist.", issues);
        if (TryFullPath(settings.AutomationRoot, out var automationRoot) && Directory.Exists(automationRoot) && !File.Exists(Path.Combine(automationRoot, "tools", "electrical_cp3c_executor.py")))
            issues.Add(DrawingActionableIssue.Runtime("EXECUTOR-CLI-MISSING", "DRAWING_EXECUTOR_CLI_MISSING", "electrical_cp3c_executor.py is missing under the configured automation root.", "ExecutorRuntimeSettings"));
        ValidateFile(settings.AccoreConsolePath, "EXECUTOR-ACCORE-MISSING", "DRAWING_EXECUTOR_ACCORECONSOLE_MISSING", "Configured accoreconsole executable does not exist.", issues);
        ValidateDirectory(settings.StagingRoot, "EXECUTOR-STAGING-MISSING", "DRAWING_EXECUTOR_STAGING_ROOT_MISSING", "Configured isolated staging parent directory does not exist.", issues);
        ValidateFile(settings.ProjectBaselineWdp, "EXECUTOR-BASELINE-MISSING", "DRAWING_EXECUTOR_BASELINE_MISSING", "Configured project baseline WDP does not exist.", issues, ".wdp");
        ValidateFile(settings.DrawingTemplatePath, "EXECUTOR-TEMPLATE-MISSING", "DRAWING_EXECUTOR_TEMPLATE_MISSING", "Configured drawing template DWT does not exist.", issues, ".dwt");

        if (TryFullPath(settings.StagingRoot, out var staging) && TryFullPath(settings.AutomationRoot, out automationRoot) && PathsOverlap(staging, automationRoot))
            issues.Add(DrawingActionableIssue.Runtime("EXECUTOR-STAGING-IMPLEMENTATION-OVERLAP", "DRAWING_EXECUTOR_STAGING_OVERLAPS_IMPLEMENTATION", "Isolated staging root must not overlap the automation implementation root.", "ExecutorRuntimeSettings"));
        if (TryFullPath(settings.StagingRoot, out staging) && TryFullPath(settings.ProjectBaselineWdp, out var baseline) && PathsOverlap(staging, Path.GetDirectoryName(baseline)!))
            issues.Add(DrawingActionableIssue.Runtime("EXECUTOR-STAGING-FORMAL-OVERLAP", "DRAWING_EXECUTOR_STAGING_OVERLAPS_FORMAL_PROJECT", "Isolated staging root must not overlap the project baseline directory.", "ExecutorRuntimeSettings"));

        return new(issues.Count == 0, issues);
    }

    private static void ValidateFile(string value, string id, string code, string message, List<DrawingActionableIssue> issues, string? extension = null)
    {
        if (!TryFullPath(value, out var path) || !File.Exists(path) || (extension is not null && !string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase)))
            issues.Add(DrawingActionableIssue.Runtime(id, code, message, "ExecutorRuntimeSettings"));
    }

    private static void ValidateDirectory(string value, string id, string code, string message, List<DrawingActionableIssue> issues)
    {
        if (!TryFullPath(value, out var path) || !Directory.Exists(path)) issues.Add(DrawingActionableIssue.Runtime(id, code, message, "ExecutorRuntimeSettings"));
    }

    private static bool TryFullPath(string? value, out string path)
    {
        try { path = string.IsNullOrWhiteSpace(value) ? string.Empty : Path.GetFullPath(value); return path.Length > 0; }
        catch { path = string.Empty; return false; }
    }

    private static bool PathsOverlap(string left, string right)
    {
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
            || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class DrawingExecutorRuntimeSettingsStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false, WriteIndented = true };
    private readonly string _path;

    public DrawingExecutorRuntimeSettingsStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComponentIntelligence", "drawing-executor-runtime.json")
            : Path.GetFullPath(path);
    }

    public string PathOnDisk => _path;
    public DrawingExecutorRuntimeSettings? Load() => !File.Exists(_path) ? null : JsonSerializer.Deserialize<DrawingExecutorRuntimeSettings>(File.ReadAllText(_path), Json) ?? throw new InvalidDataException("Drawing executor runtime settings are empty.");

    public void Save(DrawingExecutorRuntimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var validation = DrawingExecutorRuntimeSettingsValidator.Validate(settings);
        if (!validation.IsValid) throw new InvalidOperationException(string.Join("; ", validation.Issues.Select(x => x.Message)));
        var directory = Path.GetDirectoryName(_path); if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Json));
    }
}
