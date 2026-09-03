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
        var previous = (await _repository.ListAsync(project.ProjectId, ct)).FirstOrDefault();
        ElectricalProject? before = previous is null ? null : JsonSerializer.Deserialize<ElectricalProject>(previous.SnapshotJson, JsonOptions);
        var summary = ProjectChangeSummary.Compare(before, project);
        var snapshot = JsonSerializer.Serialize(project, JsonOptions);
        var created = DateTimeOffset.UtcNow;
        var identity = $"{project.ProjectId}|{created:O}|{trigger}|{SHA256Hex(snapshot)}";
        var row = new ProjectRevisionRow("REV-" + SHA256Hex(identity)[..24], project.ProjectId, created, trigger.ToString(), label, snapshot, JsonSerializer.Serialize(summary, JsonOptions));
        await _repository.InsertAsync(row, ct); return row;
    }

    public Task<IReadOnlyList<ProjectRevisionRow>> ListAsync(string projectId, CancellationToken ct = default) => _repository.ListAsync(projectId, ct);

    public async Task<ElectricalProject> RestoreAsync(ElectricalProject current, string revisionId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(current); ArgumentException.ThrowIfNullOrWhiteSpace(revisionId);
        var target = await _repository.GetAsync(revisionId, ct) ?? throw new InvalidOperationException("Revision not found.");
        if (!string.Equals(target.ProjectId, current.ProjectId, StringComparison.Ordinal)) throw new InvalidOperationException("Revision belongs to another project.");
        await CreateCheckpointAsync(current, ProjectRevisionTrigger.ManualRestore, $"Before restore {revisionId}", ct);
        return JsonSerializer.Deserialize<ElectricalProject>(target.SnapshotJson, JsonOptions) ?? throw new InvalidDataException("Revision snapshot is invalid.");
    }

    private static string SHA256Hex(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static JsonSerializerOptions CreateOptions() { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true }; options.Converters.Add(new JsonStringEnumConverter()); return options; }
}
