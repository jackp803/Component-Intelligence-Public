using System.Text.Json;
using System.Text.Json.Serialization;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Repository;

/// <summary>
/// MVP snapshot repository for the normalized Component IR.
/// Raw/evidence table persistence remains a follow-up item; see docs/handoff/CODEX_MVP_HANDOFF.md.
/// </summary>
public sealed class SqliteComponentIrRepository : IComponentRepository
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS component_ir_snapshots (
            id TEXT NOT NULL PRIMARY KEY,
            manufacturer TEXT NOT NULL COLLATE NOCASE,
            model TEXT NOT NULL COLLATE NOCASE,
            json TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            UNIQUE(manufacturer, model)
        );
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _databasePath;
    private readonly SqliteConnectionFactory _factory;

    public SqliteComponentIrRepository(string databasePath, SqliteConnectionFactory? factory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
        _factory = factory ?? new SqliteConnectionFactory();
    }

    public async Task<ComponentIR?> FindByIdentityAsync(
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
            SELECT json
            FROM component_ir_snapshots
            WHERE manufacturer = $manufacturer AND model = $model
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$manufacturer", manufacturer);
        command.Parameters.AddWithValue("$model", model);
        var json = (string?)await command.ExecuteScalarAsync(cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<ComponentIR>(json, JsonOptions);
    }

    public async Task<ComponentIR?> GetByIdAsync(
        string componentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);

        using var connection = _factory.Open(_databasePath);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM component_ir_snapshots WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", componentId);
        var json = (string?)await command.ExecuteScalarAsync(cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<ComponentIR>(json, JsonOptions);
    }

    public async Task SaveAsync(ComponentIR component, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);

        using var connection = _factory.Open(_databasePath);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO component_ir_snapshots (id, manufacturer, model, json, updated_at)
            VALUES ($id, $manufacturer, $model, $json, $updated)
            ON CONFLICT(manufacturer, model) DO UPDATE SET
                id = excluded.id,
                json = excluded.json,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", component.Identity.ComponentId);
        command.Parameters.AddWithValue("$manufacturer", component.Identity.Manufacturer);
        command.Parameters.AddWithValue("$model", component.Identity.Model);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(component, JsonOptions));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureSchemaAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Schema;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
