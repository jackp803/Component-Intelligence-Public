using System.Text.Json;
using System.Text.Json.Serialization;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Electrical.Persistence;

public sealed class ElectricalProjectRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly string _databasePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public ElectricalProjectRepository(SqliteConnectionFactory connectionFactory, string databasePath)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = _connectionFactory.Open(_databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ElectricalProjects (
                ProjectId TEXT NOT NULL PRIMARY KEY,
                SchemaVersion TEXT NOT NULL,
                Name TEXT NULL,
                SnapshotJson TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveAsync(ElectricalProject project, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ct.ThrowIfCancellationRequested();
        await InitializeAsync(ct);
        var current = ElectricalProjectMigrator.Migrate(project);
        var json = JsonSerializer.Serialize(current, _jsonOptions);

        using var connection = _connectionFactory.Open(_databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ElectricalProjects (ProjectId, SchemaVersion, Name, SnapshotJson, UpdatedUtc)
            VALUES ($projectId, $schemaVersion, $name, $snapshotJson, $updatedUtc)
            ON CONFLICT(ProjectId) DO UPDATE SET
                SchemaVersion = excluded.SchemaVersion,
                Name = excluded.Name,
                SnapshotJson = excluded.SnapshotJson,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$projectId", current.ProjectId);
        command.Parameters.AddWithValue("$schemaVersion", current.SchemaVersion);
        command.Parameters.AddWithValue("$name", (object?)current.Name ?? DBNull.Value);
        command.Parameters.AddWithValue("$snapshotJson", json);
        command.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<ElectricalProject?> GetAsync(string projectId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ct.ThrowIfCancellationRequested();
        await InitializeAsync(ct);

        using var connection = _connectionFactory.Open(_databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SnapshotJson FROM ElectricalProjects WHERE ProjectId = $projectId LIMIT 1;";
        command.Parameters.AddWithValue("$projectId", projectId);
        var result = await command.ExecuteScalarAsync(ct);
        if (result is not string json || string.IsNullOrWhiteSpace(json)) return null;
        var project = JsonSerializer.Deserialize<ElectricalProject>(json, _jsonOptions);
        return project is null ? null : ElectricalProjectMigrator.Migrate(project);
    }
}
