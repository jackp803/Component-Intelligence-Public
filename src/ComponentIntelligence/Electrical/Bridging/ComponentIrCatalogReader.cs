using System.Text.Json;
using System.Text.Json.Serialization;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Electrical.Bridging;

public sealed class ComponentIrCatalogReader
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

    private readonly string _databasePath;
    private readonly SqliteConnectionFactory _factory;
    private readonly JsonSerializerOptions _options;

    public ComponentIrCatalogReader(string databasePath, SqliteConnectionFactory? factory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
        _factory = factory ?? new SqliteConnectionFactory();
        _options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
        _options.Converters.Add(new JsonStringEnumConverter());
    }

    public async Task<ComponentIR?> GetByIdAsync(string componentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        using var connection = _factory.Open(_databasePath);
        await EnsureSchemaAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM component_ir_snapshots WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", componentId.Trim());
        var value = await command.ExecuteScalarAsync(ct);
        return value is string json ? JsonSerializer.Deserialize<ComponentIR>(json, _options) : null;
    }

    public async Task<ComponentIR?> FindByIdentityAsync(string manufacturer, string model, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manufacturer);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        using var connection = _factory.Open(_databasePath);
        await EnsureSchemaAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM component_ir_snapshots WHERE manufacturer = $manufacturer COLLATE NOCASE AND model = $model COLLATE NOCASE LIMIT 1;";
        command.Parameters.AddWithValue("$manufacturer", manufacturer.Trim());
        command.Parameters.AddWithValue("$model", model.Trim());
        var value = await command.ExecuteScalarAsync(ct);
        return value is string json ? JsonSerializer.Deserialize<ComponentIR>(json, _options) : null;
    }

    public async Task<IReadOnlyList<ComponentIR>> ListAsync(string? search = null, int limit = 500, CancellationToken ct = default)
    {
        if (limit is <= 0 or > 5000) throw new ArgumentOutOfRangeException(nameof(limit));
        using var connection = _factory.Open(_databasePath);
        await EnsureSchemaAsync(connection, ct);

        await using var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(search))
        {
            command.CommandText = "SELECT json FROM component_ir_snapshots ORDER BY manufacturer, model LIMIT $limit;";
        }
        else
        {
            command.CommandText = """
                SELECT json
                FROM component_ir_snapshots
                WHERE manufacturer LIKE $search OR model LIKE $search OR id LIKE $search
                ORDER BY manufacturer, model
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$search", $"%{search.Trim()}%");
        }
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<ComponentIR>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var component = JsonSerializer.Deserialize<ComponentIR>(reader.GetString(0), _options);
            if (component is not null) results.Add(component);
        }
        return results;
    }

    private static async Task EnsureSchemaAsync(Microsoft.Data.Sqlite.SqliteConnection connection, CancellationToken ct)
    {
        await using var schema = connection.CreateCommand();
        schema.CommandText = Schema;
        await schema.ExecuteNonQueryAsync(ct);
    }
}
