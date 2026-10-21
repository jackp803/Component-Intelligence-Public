using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Editing;

public sealed class ProjectMutationHistory
{
    private readonly LinkedList<ProjectSnapshot> _undo = new();
    private readonly LinkedList<ProjectSnapshot> _redo = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly int _capacity;
    private readonly long _maxBytes;

    public ProjectMutationHistory(int capacity = 100, long maxBytes = 64 * 1024 * 1024)
    {
        if (capacity < 1 || maxBytes < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity; _maxBytes = maxBytes;
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => _undo.Last?.Value.Description;
    public string? RedoDescription => _redo.Last?.Value.Description;
    public long RetainedBytes => _undo.Concat(_redo).Sum(s => (long)s.Data.Length);

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public void RecordBeforeMutation(ElectricalProject project, string description)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        _undo.AddLast(Capture(project, description));
        _redo.Clear();
        Trim();
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

        var target = _undo.Last!.Value; _undo.RemoveLast();
        _redo.AddLast(Capture(current, target.Description));
        restored = Restore(target);
        description = target.Description;
        Trim();
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

        var target = _redo.Last!.Value; _redo.RemoveLast();
        _undo.AddLast(Capture(current, target.Description));
        restored = Restore(target);
        description = target.Description;
        Trim();
        return true;
    }

    private void Trim()
    {
        // Retain one usable snapshot even when a single project exceeds the budget.
        while (_undo.Count + _redo.Count > 1 &&
            (_undo.Count + _redo.Count > _capacity || RetainedBytes > _maxBytes))
        {
            if (_undo.Count > 0) _undo.RemoveFirst();
            else _redo.RemoveFirst();
        }
    }

    private ProjectSnapshot Capture(ElectricalProject project, string description)
    {
        using var buffer = new MemoryStream();
        using (var compressed = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            JsonSerializer.Serialize(compressed, project, _jsonOptions);
        return new(description, buffer.ToArray());
    }

    private ElectricalProject Restore(ProjectSnapshot snapshot)
    {
        using var buffer = new MemoryStream(snapshot.Data, writable: false);
        using var compressed = new GZipStream(buffer, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<ElectricalProject>(compressed, _jsonOptions)
            ?? throw new InvalidOperationException("Electrical project snapshot could not be restored.");
    }

    private sealed record ProjectSnapshot(string Description, byte[] Data);
}
