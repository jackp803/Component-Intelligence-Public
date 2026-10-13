using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Editing;

public sealed record ProjectSaveCapture(ElectricalProject Snapshot, long Epoch, string Fingerprint);

public sealed class ProjectSaveSession
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();
    private string? _projectId;
    private string? _savedFingerprint;

    public long Epoch { get; private set; }

    public void Begin(ElectricalProject project, bool persisted)
    {
        ArgumentNullException.ThrowIfNull(project);
        var fingerprint = Fingerprint(project);
        Epoch++;
        _projectId = project.ProjectId;
        // Dirty tracks edits within this session; disk persistence is a separate UI state.
        _savedFingerprint = fingerprint;
    }

    public bool IsDirty(ElectricalProject project) =>
        !string.Equals(_savedFingerprint, Fingerprint(project), StringComparison.Ordinal);

    public ProjectSaveCapture Capture(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var json = JsonSerializer.Serialize(project, JsonOptions);
        return new ProjectSaveCapture(Deserialize(json), Epoch, Hash(json));
    }

    public bool Complete(ProjectSaveCapture capture, ElectricalProject persisted, ElectricalProject current)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(persisted);
        ArgumentNullException.ThrowIfNull(current);
        if (capture.Epoch != Epoch ||
            !string.Equals(_projectId, capture.Snapshot.ProjectId, StringComparison.Ordinal) ||
            !string.Equals(_projectId, persisted.ProjectId, StringComparison.Ordinal) ||
            !string.Equals(_projectId, current.ProjectId, StringComparison.Ordinal)) return false;

        _savedFingerprint = Fingerprint(persisted);
        return string.Equals(capture.Fingerprint, Fingerprint(current), StringComparison.Ordinal);
    }

    public static ElectricalProject Clone(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Deserialize(JsonSerializer.Serialize(project, JsonOptions));
    }

    public static string Fingerprint(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Hash(JsonSerializer.Serialize(project, JsonOptions));
    }

    private static ElectricalProject Deserialize(string json) =>
        JsonSerializer.Deserialize<ElectricalProject>(json, JsonOptions)
        ?? throw new InvalidDataException("Electrical project snapshot is invalid.");

    private static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
