using Microsoft.Data.Sqlite;

namespace ComponentIntelligence.Repository;

public sealed class SqliteConnectionFactory
{
    static SqliteConnectionFactory()
    {
        // Explicit initialization makes desktop/published deployments deterministic and
        // avoids relying on the first Microsoft.Data.Sqlite type initializer to locate e_sqlite3.
        SQLitePCL.Batteries_V2.Init();
    }

    public string BuildConnectionString(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var normalizedPath = Path.GetFullPath(databasePath);
        return new SqliteConnectionStringBuilder
        {
            DataSource = normalizedPath,
            Pooling = false,
            ForeignKeys = true
        }.ToString();
    }

    public SqliteConnection Open(string databasePath)
    {
        var normalizedPath = Path.GetFullPath(databasePath);
        var directoryPath = Path.GetDirectoryName(normalizedPath);
        if (!string.IsNullOrEmpty(directoryPath)) Directory.CreateDirectory(directoryPath);
        var connection = new SqliteConnection(BuildConnectionString(normalizedPath));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
        return connection;
    }
}
