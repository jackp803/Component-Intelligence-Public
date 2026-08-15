using System.Text.Json;
using System.Text.Json.Serialization;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Editing;

public sealed class ProjectMutationHistory
{
    private readonly Stack<ProjectSnapshot> _undo = new();
    private readonly Stack<ProjectSnapshot> _redo = new();
    private readonly JsonSerializerOptions _jsonOptions;

    public ProjectMutationHistory()
    {
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => _undo.TryPeek(out var snapshot) ? snapshot.Description : null;
    public string? RedoDescription => _redo.TryPeek(out var snapshot) ? snapshot.Description : null;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public void RecordBeforeMutation(ElectricalProject project, string description)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        _undo.Push(Capture(project, description));
        _redo.Clear();
    }

    public bool TryUndo(ElectricalProject current, out ElectricalProject restored, out string? description)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (_undo.Count == 0)
        {
            restored = current;
            description = null;
            return false;
        }

        var target = _undo.Pop();
        _redo.Push(Capture(current, target.Description));
        restored = Restore(target);
        description = target.Description;
        return true;
    }

    public bool TryRedo(ElectricalProject current, out ElectricalProject restored, out string? description)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (_redo.Count == 0)
        {
            restored = current;
            description = null;
            return false;
        }

        var target = _redo.Pop();
        _undo.Push(Capture(current, target.Description));
        restored = Restore(target);
        description = target.Description;
        return true;
    }

    private ProjectSnapshot Capture(ElectricalProject project, string description) =>
        new(description, JsonSerializer.Serialize(project, _jsonOptions));

    private ElectricalProject Restore(ProjectSnapshot snapshot) =>
        JsonSerializer.Deserialize<ElectricalProject>(snapshot.Json, _jsonOptions)
        ?? throw new InvalidOperationException("Electrical project snapshot could not be restored.");

    private sealed record ProjectSnapshot(string Description, string Json);
}
