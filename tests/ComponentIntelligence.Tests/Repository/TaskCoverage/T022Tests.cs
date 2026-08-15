using ComponentIntelligence.Repository;
using Xunit;

namespace ComponentIntelligence.Tests.Repository.TaskCoverage;

public sealed class T022Tests
{
    [Fact]
    public void ComponentNormalizedSpecsTable_CreatesRequiredColumnsAndStoresNormalizedSpec()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directoryPath, "component-intelligence.db");
        try
        {
            var factory = new SqliteConnectionFactory();
            using var connection = factory.Open(databasePath);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = SqliteSchema.ComponentsTable + SqliteSchema.ComponentNormalizedSpecsTable;
                command.ExecuteNonQuery();
            }
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(component_normalized_specs);";
                using var reader = command.ExecuteReader();
                while (reader.Read()) columns.Add(reader.GetString(reader.GetOrdinal("name")));
            }
            Assert.All(new[] { "id", "component_id", "name", "text_value", "numeric_value", "unit", "status", "source_id", "created_at" }, column => Assert.Contains(column, columns));
            using (var component = connection.CreateCommand())
            {
                component.CommandText = "INSERT INTO components (id, manufacturer, official_model, created_at, updated_at) VALUES ('component-1', 'IFM', 'O5D100', '2026-08-13T00:00:00+00:00', '2026-08-13T00:00:00+00:00');";
                component.ExecuteNonQuery();
            }
            using var normalizedSpec = connection.CreateCommand();
            normalizedSpec.CommandText = "INSERT INTO component_normalized_specs (id, component_id, name, text_value, numeric_value, unit, status, created_at) VALUES ('spec-1', 'component-1', 'Rated Voltage', '24 V DC', 24.0, 'V', 'VERIFIED', '2026-08-13T00:00:00+00:00');";
            Assert.Equal(1, normalizedSpec.ExecuteNonQuery());
        }
        finally
        {
            if (Directory.Exists(directoryPath)) Directory.Delete(directoryPath, recursive: true);
        }
    }
}
