using Xunit;

namespace ComponentIntelligence.Tests.Archive;

public sealed class ArchiveGovernanceDocumentTests
{
    private static string RepoRoot => FindRepoRoot();

    [Fact]
    public void AuthorityDocument_SelectsMainAndArchiveSpecV2()
    {
        var authorityPath = Path.Combine(RepoRoot, "docs", "archive", "AUTHORITY.md");
        Assert.True(File.Exists(authorityPath), $"Missing {authorityPath}");

        var text = File.ReadAllText(authorityPath);
        Assert.Contains("main", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("docs/COMPONENT_ARCHIVE_SPEC_V2.md", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Notion-only", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("docs/COMPONENT_ARCHIVE_SPEC_V2.md")]
    [InlineData("docs/CENTRAL_WORKBOOK_KNOWLEDGE_V1.md")]
    [InlineData("docs/TOPOLOGY_ENDPOINT_ROUTING_V2.md")]
    [InlineData("docs/VENDOR-PART-INTAKE-V1.md")]
    public void SupportingAuthorityDocuments_ArePresent(string relativePath)
    {
        Assert.True(File.Exists(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))), $"Missing {relativePath}");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ComponentIntelligence.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate ComponentIntelligence.sln from test output directory.");
    }
}
