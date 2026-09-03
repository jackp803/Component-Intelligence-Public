using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Electrical.Persistence;

public sealed record ProjectRevisionRow(string RevisionId, string ProjectId, DateTimeOffset CreatedUtc, string Trigger, string? Label, string SnapshotJson, string SummaryJson);

public sealed class ProjectRevisionRepository(SqliteConnectionFactory connectionFactory, string databasePath)
{
    private readonly SqliteConnectionFactory _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    private readonly string _databasePath = string.IsNullOrWhiteSpace(databasePath) ? throw new ArgumentException("databasePath is required", nameof(databasePath)) : databasePath;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.Open(_databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ElectricalProjectRevisions (
              RevisionId TEXT PRIMARY KEY,
              ProjectId TEXT NOT NULL,
              CreatedUtc TEXT NOT NULL,
              Trigger TEXT NOT NULL,
              Label TEXT NULL,
              SnapshotJson TEXT NOT NULL,
              SummaryJson TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ElectricalProjectRevisions_ProjectCreated
              ON ElectricalProjectRevisions(ProjectId, CreatedUtc DESC, RevisionId DESC);
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task InsertAsync(ProjectRevisionRow row, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        using var connection = _connectionFactory.Open(_databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO ElectricalProjectRevisions (RevisionId,ProjectId,CreatedUtc,Trigger,Label,SnapshotJson,SummaryJson) VALUES ($id,$project,$created,$trigger,$label,$snapshot,$summary);";
        command.Parameters.AddWithValue("$id", row.RevisionId); command.Parameters.AddWithValue("$project", row.ProjectId);
        command.Parameters.AddWithValue("$created", row.CreatedUtc.ToString("O")); command.Parameters.AddWithValue("$trigger", row.Trigger);
        command.Parameters.AddWithValue("$label", (object?)row.Label ?? DBNull.Value); command.Parameters.AddWithValue("$snapshot", row.SnapshotJson); command.Parameters.AddWithValue("$summary", row.SummaryJson);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<ProjectRevisionRow?> GetAsync(string revisionId, CancellationToken ct = default)
    {
        await InitializeAsync(ct); using var connection = _connectionFactory.Open(_databasePath); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RevisionId,ProjectId,CreatedUtc,Trigger,Label,SnapshotJson,SummaryJson FROM ElectricalProjectRevisions WHERE RevisionId=$id LIMIT 1;"; command.Parameters.AddWithValue("$id", revisionId);
        await using var reader = await command.ExecuteReaderAsync(ct); if (!await reader.ReadAsync(ct)) return null; return Read(reader);
    }

    public async Task<IReadOnlyList<ProjectRevisionRow>> ListAsync(string projectId, CancellationToken ct = default)
    {
        await InitializeAsync(ct); using var connection = _connectionFactory.Open(_databasePath); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RevisionId,ProjectId,CreatedUtc,Trigger,Label,SnapshotJson,SummaryJson FROM ElectricalProjectRevisions WHERE ProjectId=$project ORDER BY CreatedUtc DESC, RevisionId DESC;"; command.Parameters.AddWithValue("$project", projectId);
        var rows = new List<ProjectRevisionRow>(); await using var reader = await command.ExecuteReaderAsync(ct); while (await reader.ReadAsync(ct)) rows.Add(Read(reader)); return rows;
    }

    private static ProjectRevisionRow Read(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2)), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6));
}
