using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;

namespace ComponentIntelligence.Electrical.Editing;

public sealed class ProjectRevisionService(ProjectRevisionRepository repository)
{
    private readonly ProjectRevisionRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public async Task<ProjectRevisionRow> CreateCheckpointAsync(ElectricalProject project, ProjectRevisionTrigger trigger, string? label = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ct.ThrowIfCancellationRequested();
        var captured = ProjectSaveSession.Clone(project);
        var snapshot = JsonSerializer.Serialize(captured, JsonOptions);
        var previous = await _repository.LatestAsync(captured.ProjectId, ct);
        ElectricalProject? before = previous is null ? null : JsonSerializer.Deserialize<ElectricalProject>(previous.SnapshotJson, JsonOptions);
        var summary = ProjectChangeSummary.Compare(before, captured);
        var created = DateTimeOffset.UtcNow;
        var identity = $"{captured.ProjectId}|{created:O}|{trigger}|{SHA256Hex(snapshot)}";
        var row = new ProjectRevisionRow("REV-" + SHA256Hex(identity)[..24], captured.ProjectId, created, trigger.ToString(), label, snapshot, JsonSerializer.Serialize(summary, JsonOptions));
        await _repository.InsertAsync(row, ct); return row;
    }

    public Task<IReadOnlyList<ProjectRevisionRow>> ListAsync(string projectId, CancellationToken ct = default) => _repository.ListAsync(projectId, ct);

    public Task<ProjectRevisionRow> CreateRecoveryCheckpointAsync(ElectricalProject project, string? label = null, CancellationToken ct = default) =>
        CreateCheckpointAsync(project, ProjectRevisionTrigger.AutoRecovery, label, ct);

    public Task<IReadOnlyList<ProjectRevisionSummary>> ListRecoveryAsync(string projectId, CancellationToken ct = default) =>
        _repository.ListRecoveryAsync(projectId, ct);

    public async Task<ElectricalProject> RestoreAsync(ElectricalProject current, string revisionId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(current); ArgumentException.ThrowIfNullOrWhiteSpace(revisionId);
        ct.ThrowIfCancellationRequested();
        var captured = ProjectSaveSession.Clone(current);
        var target = await _repository.GetAsync(revisionId, ct) ?? throw new InvalidOperationException("Revision not found.");
        if (!string.Equals(target.ProjectId, captured.ProjectId, StringComparison.Ordinal)) throw new InvalidOperationException("Revision belongs to another project.");
        await CreateCheckpointAsync(captured, ProjectRevisionTrigger.ManualRestore, $"Before restore {revisionId}", ct);
        return JsonSerializer.Deserialize<ElectricalProject>(target.SnapshotJson, JsonOptions) ?? throw new InvalidDataException("Revision snapshot is invalid.");
    }

    private static string SHA256Hex(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static JsonSerializerOptions CreateOptions() { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true }; options.Converters.Add(new JsonStringEnumConverter()); return options; }
}
