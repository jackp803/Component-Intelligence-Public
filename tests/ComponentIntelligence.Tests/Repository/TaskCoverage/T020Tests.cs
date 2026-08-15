using ComponentIntelligence.Repository;
using Xunit;

namespace ComponentIntelligence.Tests.Repository.TaskCoverage;

public class T020Tests
{
    [Fact]
    public void ComponentSourcesTable_Constant_NotNull()
    {
        Assert.NotNull(SqliteSchema.ComponentSourcesTable);
        Assert.NotEmpty(SqliteSchema.ComponentSourcesTable);
    }

    [Fact]
    public void ComponentDocumentsTable_Constant_NotNull()
    {
        Assert.NotNull(SqliteSchema.ComponentDocumentsTable);
        Assert.NotEmpty(SqliteSchema.ComponentDocumentsTable);
    }

    [Fact]
    public void ComponentSourcesTable_ContainsAuthorityColumn()
    {
        Assert.Contains("source_authority", SqliteSchema.ComponentSourcesTable);
    }

    [Fact]
    public void ComponentSourcesTable_ContainsUrlColumn()
    {
        Assert.Contains("url", SqliteSchema.ComponentSourcesTable);
    }

    [Fact]
    public void ComponentDocumentsTable_ContainsHashColumn()
    {
        Assert.Contains("hash", SqliteSchema.ComponentDocumentsTable);
    }

    [Fact]
    public void ComponentDocumentsTable_ContainsTimestampColumn()
    {
        Assert.Contains("timestamp", SqliteSchema.ComponentDocumentsTable);
    }

    [Fact]
    public void ComponentSourcesTable_CreatesCorrectTableName()
    {
        Assert.Contains("component_sources", SqliteSchema.ComponentSourcesTable.ToLowerInvariant());
    }

    [Fact]
    public void ComponentDocumentsTable_CreatesCorrectTableName()
    {
        Assert.Contains("component_documents", SqliteSchema.ComponentDocumentsTable.ToLowerInvariant());
    }
}
