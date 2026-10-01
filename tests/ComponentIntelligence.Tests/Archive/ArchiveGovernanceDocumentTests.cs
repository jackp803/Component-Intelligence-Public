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

    [Fact]
    public void SkillDocument_IsThinPortableProcedure()
    {
        var skillPath = Path.Combine(RepoRoot, "skills", "component-intelligence-archive", "SKILL.md");
        Assert.True(File.Exists(skillPath), $"Missing {skillPath}");

        var text = File.ReadAllText(skillPath);
        Assert.StartsWith("---", text);
        Assert.Contains("name: component-intelligence-archive", text, StringComparison.Ordinal);
        Assert.Contains("description: Use when", text, StringComparison.Ordinal);
        Assert.Contains("docs/archive/AUTHORITY.md", text, StringComparison.Ordinal);
        Assert.Contains("CREATE", text, StringComparison.Ordinal);
        Assert.Contains("UPDATE", text, StringComparison.Ordinal);
        Assert.Contains("REVIEW", text, StringComparison.Ordinal);
        Assert.Contains("VALIDATED_NOT_WRITTEN", text, StringComparison.Ordinal);
        Assert.Contains("readback", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("## Source Priority", text, StringComparison.Ordinal);
        Assert.True(text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < 700, "Skill should stay thin and route to authority rather than duplicate policy.");
    }

    [Fact]
    public void ToolContract_DeclaresProviderNeutralCapabilities()
    {
        var path = Path.Combine(RepoRoot, "skills", "component-intelligence-archive", "references", "TOOL_CONTRACT.md");
        Assert.True(File.Exists(path), $"Missing {path}");

        var text = File.ReadAllText(path);
        foreach (var capability in new[]
                 {
                     "AuthorityRead", "ArchiveLookup", "EvidenceSearch", "EvidenceRead", "ChangesetBuild",
                     "Validate", "ArchiveWrite", "Readback", "ArchiveSync"
                 })
            Assert.Contains(capability, text, StringComparison.Ordinal);
    }

    [Fact]
    public void PortabilityAndContinuityDocs_BootstrapWithoutChatHistory()
    {
        var portabilityPath = Path.Combine(RepoRoot, "docs", "archive", "AI_PORTABILITY.md");
        var continuityPath = Path.Combine(RepoRoot, "docs", "archive", "DATA_CONTINUITY.md");
        Assert.True(File.Exists(portabilityPath), $"Missing {portabilityPath}");
        Assert.True(File.Exists(continuityPath), $"Missing {continuityPath}");

        var portability = File.ReadAllText(portabilityPath);
        Assert.Contains("AUTHORITY.md", portability, StringComparison.Ordinal);
        Assert.Contains("SKILL.md", portability, StringComparison.Ordinal);
        Assert.Contains("TOOL_CONTRACT.md", portability, StringComparison.Ordinal);
        Assert.Contains("chat history", portability, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("changeset", portability, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("read back", portability, StringComparison.OrdinalIgnoreCase);

        var continuity = File.ReadAllText(continuityPath);
        Assert.Contains("Google Drive", continuity, StringComparison.Ordinal);
        Assert.Contains("GitHub", continuity, StringComparison.Ordinal);
        Assert.Contains("stable", continuity, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("readback", continuity, StringComparison.OrdinalIgnoreCase);
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
