namespace ComponentIntelligence.Repository;

public sealed class DatabaseBootstrap
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly string _databasePath;

    public DatabaseBootstrap(SqliteConnectionFactory connectionFactory, string databasePath)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = _connectionFactory.Open(_databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Components (
                Id TEXT NOT NULL PRIMARY KEY,
                Manufacturer TEXT NOT NULL,
                OfficialModel TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Metadata (
                Key TEXT NOT NULL PRIMARY KEY,
                Value TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(ct);
    }
}
