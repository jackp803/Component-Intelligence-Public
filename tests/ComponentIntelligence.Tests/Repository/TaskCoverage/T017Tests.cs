using ComponentIntelligence.Repository;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ComponentIntelligence.Tests.Repository.TaskCoverage;

public sealed class T017Tests
{
    [Fact]
    public void Open_CreatesDatabaseAndReturnsOpenConnectionWithNormalizedDataSource()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directoryPath, "component-intelligence.db");
        try
        {
            var factory = new SqliteConnectionFactory();
            using var connection = factory.Open(databasePath);
            Assert.Equal(System.Data.ConnectionState.Open, connection.State);
            Assert.True(File.Exists(databasePath));
            var parsed = new SqliteConnectionStringBuilder(connection.ConnectionString);
            Assert.Equal(Path.GetFullPath(databasePath), parsed.DataSource);
            Assert.Equal(connection.ConnectionString, factory.BuildConnectionString(databasePath));
        }
        finally
        {
            if (Directory.Exists(directoryPath)) Directory.Delete(directoryPath, recursive: true);
        }
    }
}
