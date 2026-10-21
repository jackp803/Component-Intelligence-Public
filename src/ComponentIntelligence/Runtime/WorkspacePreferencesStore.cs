using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComponentIntelligence.Runtime;

public sealed record WorkspacePreferences
{
    public double LeftWidth { get; init; } = 240;
    public double InspectorWidth { get; init; } = 260;
    public bool LeftCollapsed { get; init; }
    public double Zoom { get; init; } = .65;
    public bool AllPages { get; init; }
    public string? ImportDirectory { get; init; }
    public string? ExportDirectory { get; init; }
}

public sealed class WorkspacePreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
    private readonly string _path;

    public WorkspacePreferencesStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public WorkspacePreferences Load()
    {
        try
        {
            return Normalize(JsonSerializer.Deserialize<WorkspacePreferences>(File.ReadAllText(_path), JsonOptions)
                ?? new WorkspacePreferences());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new WorkspacePreferences();
        }
    }

    public void Save(WorkspacePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var json = JsonSerializer.Serialize(Normalize(preferences), JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(_path)) File.Replace(temporaryPath, _path, null);
            else File.Move(temporaryPath, _path);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static WorkspacePreferences Normalize(WorkspacePreferences preferences) => preferences with
    {
        LeftWidth = Clamp(preferences.LeftWidth, 180, 400, 240),
        InspectorWidth = Clamp(preferences.InspectorWidth, 220, 400, 260),
        Zoom = Clamp(preferences.Zoom, .25, 2, .65)
    };

    private static double Clamp(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}
