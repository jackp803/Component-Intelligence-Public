using Microsoft.Data.Sqlite;

namespace ComponentIntelligence.Repository;

public enum ComponentSyncStatus
{
    LocalOnly,
    Pending,
    Synced,
    Conflict,
    Failed
}

public sealed record ComponentSyncState(
    string Manufacturer,
    string Model,
    string ComponentId,
    ComponentSyncStatus Status,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastSuccessfulSyncAt,
    string? Diagnostics);

/// <summary>
/// Persists Local ↔ Notion sync state in the same local SQLite database.
/// This table contains sync metadata only; Notion never becomes the runtime database.
/// </summary>
public sealed class ComponentSyncStateRepository
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS component_sync_state (
            manufacturer TEXT NOT NULL COLLATE NOCASE,
            model TEXT NOT NULL COLLATE NOCASE,
            component_id TEXT NOT NULL,
            status TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            last_successful_sync_at TEXT NULL,
            diagnostics TEXT NULL,
            PRIMARY KEY(manufacturer, model)
        );
        """;

    private readonly string _databasePath;
    private readonly SqliteConnectionFactory _factory;

    public ComponentSyncStateRepository(string databasePath, SqliteConnectionFactory? factory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
        _factory = factory ?? new SqliteConnectionFactory();
    }

    public async Task<ComponentSyncState?> FindAsync(
        string manufacturer,
        string model,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manufacturer);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        using var connection = _factory.Open(_databasePath);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT component_id, status, updated_at, last_successful_sync_at, diagnostics
            FROM component_sync_state
            WHERE manufacturer = $manufacturer AND model = $model
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$manufacturer", manufacturer);
        command.Parameters.AddWithValue("$model", model);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var statusText = reader.GetString(1);
        _ = Enum.TryParse<ComponentSyncStatus>(statusText, ignoreCase: true, out var status);
        var lastSuccess = reader.IsDBNull(3)
            ? (DateTimeOffset?)null
            : DateTimeOffset.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture);
        return new ComponentSyncState(
            manufacturer,
            model,
            reader.GetString(0),
            status,
            DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture),
            lastSuccess,
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    public async Task SaveAsync(ComponentSyncState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        using var connection = _factory.Open(_databasePath);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO component_sync_state (
                manufacturer, model, component_id, status, updated_at, last_successful_sync_at, diagnostics)
            VALUES ($manufacturer, $model, $componentId, $status, $updatedAt, $lastSuccess, $diagnostics)
            ON CONFLICT(manufacturer, model) DO UPDATE SET
                component_id = excluded.component_id,
                status = excluded.status,
                updated_at = excluded.updated_at,
                last_successful_sync_at = excluded.last_successful_sync_at,
                diagnostics = excluded.diagnostics;
            """;
        command.Parameters.AddWithValue("$manufacturer", state.Manufacturer);
        command.Parameters.AddWithValue("$model", state.Model);
        command.Parameters.AddWithValue("$componentId", state.ComponentId);
        command.Parameters.AddWithValue("$status", state.Status.ToString());
        command.Parameters.AddWithValue("$updatedAt", state.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$lastSuccess", state.LastSuccessfulSyncAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$diagnostics", state.Diagnostics ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Schema;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
