using ComponentIntelligence.Repository;
using Xunit;

namespace ComponentIntelligence.Tests.Repository.TaskCoverage;

public sealed class T023Tests
{
    [Fact]
    public void PortAndPinTables_CreateRequiredColumnsAndStoreRelatedRows()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directoryPath, "component-intelligence.db");
        try
        {
            var factory = new SqliteConnectionFactory();
            using var connection = factory.Open(databasePath);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = SqliteSchema.ComponentsTable + SqliteSchema.ComponentPortsTable + SqliteSchema.ComponentPinsTable;
                command.ExecuteNonQuery();
            }
            AssertColumns(connection, "component_ports", "id", "component_id", "name", "port_type", "label", "created_at");
            AssertColumns(connection, "component_pins", "id", "port_id", "component_id", "pin_number", "name", "signal_type", "created_at");
            using (var component = connection.CreateCommand())
            {
                component.CommandText = "INSERT INTO components (id, manufacturer, official_model, created_at, updated_at) VALUES ('component-1', 'IFM', 'O5D100', '2026-08-13', '2026-08-13');";
                component.ExecuteNonQuery();
            }
            using (var port = connection.CreateCommand())
            {
                port.CommandText = "INSERT INTO component_ports (id, component_id, name, port_type, label, created_at) VALUES ('port-1', 'component-1', 'X1', 'M12', 'Sensor connector', '2026-08-13');";
                Assert.Equal(1, port.ExecuteNonQuery());
            }
            using var pin = connection.CreateCommand();
            pin.CommandText = "INSERT INTO component_pins (id, port_id, component_id, pin_number, name, signal_type, created_at) VALUES ('pin-1', 'port-1', 'component-1', '1', 'L+', 'POWER', '2026-08-13');";
            Assert.Equal(1, pin.ExecuteNonQuery());
        }
        finally { if (Directory.Exists(directoryPath)) Directory.Delete(directoryPath, recursive: true); }
    }
    private static void AssertColumns(Microsoft.Data.Sqlite.SqliteConnection connection, string table, params string[] expected)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        using var reader = command.ExecuteReader();
        while (reader.Read()) columns.Add(reader.GetString(reader.GetOrdinal("name")));
        Assert.All(expected, column => Assert.Contains(column, columns));
    }
}
