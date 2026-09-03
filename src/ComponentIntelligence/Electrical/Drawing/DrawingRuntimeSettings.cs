using System.Text.Json;

namespace ComponentIntelligence.Electrical.Drawing;

public sealed record DrawingRuntimeSettings
{
    public required string PythonExecutable { get; init; }
    public required string AutomationRoot { get; init; }
}

public sealed record DrawingRuntimeValidation(bool IsValid, IReadOnlyList<DrawingActionableIssue> Issues)
{
    public static DrawingRuntimeValidation Valid() => new(true, []);
}

public static class DrawingRuntimeSettingsValidator
{
    public static DrawingRuntimeValidation Validate(DrawingRuntimeSettings? settings)
    {
        var issues = new List<DrawingActionableIssue>();
        if (settings is null)
        {
            issues.Add(DrawingActionableIssue.Runtime("RUNTIME-SETTINGS-MISSING", "DRAWING_RUNTIME_SETTINGS_MISSING", "Drawing runtime settings are not configured.", "RuntimeSettings"));
            return new DrawingRuntimeValidation(false, issues);
        }
        if (string.IsNullOrWhiteSpace(settings.PythonExecutable) || !File.Exists(Path.GetFullPath(settings.PythonExecutable)))
            issues.Add(DrawingActionableIssue.Runtime("RUNTIME-PYTHON-MISSING", "DRAWING_RUNTIME_PYTHON_MISSING", "Configured Python executable does not exist.", "RuntimeSettings"));
        if (string.IsNullOrWhiteSpace(settings.AutomationRoot) || !Directory.Exists(Path.GetFullPath(settings.AutomationRoot)))
            issues.Add(DrawingActionableIssue.Runtime("RUNTIME-ROOT-MISSING", "DRAWING_RUNTIME_ROOT_MISSING", "Configured AutoCAD automation root does not exist.", "RuntimeSettings"));
        else if (!File.Exists(Path.Combine(Path.GetFullPath(settings.AutomationRoot), "tools", "electrical_drawing_pipeline.py")))
            issues.Add(DrawingActionableIssue.Runtime("RUNTIME-PIPELINE-MISSING", "DRAWING_RUNTIME_PIPELINE_MISSING", "electrical_drawing_pipeline.py is missing under the configured automation root.", "RuntimeSettings"));
        return new DrawingRuntimeValidation(issues.Count == 0, issues);
    }
}

public sealed class DrawingRuntimeSettingsStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false, WriteIndented = true };
    private readonly string _path;

    public DrawingRuntimeSettingsStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComponentIntelligence", "drawing-runtime.json")
            : Path.GetFullPath(path);
    }

    public string PathOnDisk => _path;

    public DrawingRuntimeSettings? Load()
    {
        if (!File.Exists(_path)) return null;
        return JsonSerializer.Deserialize<DrawingRuntimeSettings>(File.ReadAllText(_path), Json)
            ?? throw new InvalidDataException("Drawing runtime settings are empty.");
    }

    public DrawingRuntimeValidation ValidateCurrent() => DrawingRuntimeSettingsValidator.Validate(Load());

    public void Save(DrawingRuntimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var validation = DrawingRuntimeSettingsValidator.Validate(settings);
        if (!validation.IsValid) throw new InvalidOperationException(string.Join("; ", validation.Issues.Select(x => x.Message)));
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Json));
    }
}
