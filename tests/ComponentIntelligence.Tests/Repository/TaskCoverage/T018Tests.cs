using ComponentIntelligence.Repository;
using Xunit;

namespace ComponentIntelligence.Tests.Repository.TaskCoverage;

public sealed class T018Tests
{
    [Fact]
    public async Task InitializeAsync_CreatesSchemaIdempotently()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directoryPath, "component-intelligence.db");
        try
        {
            var factory = new SqliteConnectionFactory();
            var bootstrap = new DatabaseBootstrap(factory, databasePath);
            await bootstrap.InitializeAsync();
            await bootstrap.InitializeAsync();
            using var connection = factory.Open(databasePath);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Components', 'Metadata');";
            Assert.Equal(2, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }
        finally
        {
            if (Directory.Exists(directoryPath)) Directory.Delete(directoryPath, recursive: true);
        }
    }
}
