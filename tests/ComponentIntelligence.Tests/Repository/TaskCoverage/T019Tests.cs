using ComponentIntelligence.Repository;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ComponentIntelligence.Tests.Repository.TaskCoverage;

public sealed class T019Tests
{
    [Fact]
    public void ComponentsTable_DefinesRequiredColumnsAndUniqueIdentity()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directoryPath, "component-intelligence.db");
        try
        {
            var factory = new SqliteConnectionFactory();
            using var connection = factory.Open(databasePath);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = SqliteSchema.ComponentsTable;
                command.ExecuteNonQuery();
            }
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(components);";
                using var reader = command.ExecuteReader();
                while (reader.Read()) columns.Add(reader.GetString(reader.GetOrdinal("name")));
            }
            var expected = new[] { "id", "manufacturer", "official_model", "mpn", "product_name", "category", "subcategory", "identity_status", "enrichment_status", "verification_status", "created_at", "updated_at", "last_verified_at" };
            Assert.All(expected, name => Assert.Contains(name, columns));
            Insert(connection, "1", "IFM", "O5D100");
            Assert.Throws<SqliteException>(() => Insert(connection, "2", "IFM", "O5D100"));
        }
        finally
        {
            if (Directory.Exists(directoryPath)) Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static void Insert(SqliteConnection connection, string id, string manufacturer, string officialModel)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO components (id, manufacturer, official_model, created_at, updated_at) VALUES ($id, $manufacturer, $model, '2026-08-13', '2026-08-13');";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$manufacturer", manufacturer);
        command.Parameters.AddWithValue("$model", officialModel);
        command.ExecuteNonQuery();
    }
}
