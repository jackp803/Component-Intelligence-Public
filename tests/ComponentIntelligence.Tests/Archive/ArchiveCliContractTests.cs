using System.Text.Json;
using ComponentIntelligence.Archive;
using ComponentIntelligence.Cli;
using Xunit;

namespace ComponentIntelligence.Tests.Archive;

public sealed class ArchiveCliContractTests
{
    [Fact]
    public async Task ArchiveValidate_ValidChangeset_ReturnsZero()
    {
        var path = Path.Combine(Path.GetTempPath(), $"archive-cli-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(
            ArchiveChangeSetValidatorTests.ValidChangeSet(),
            ArchiveJson.CreateOptions(true)));

        try
        {
            var exitCode = await Program.Main(new[] { "archive-validate", path });
            Assert.Equal(0, exitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ArchiveValidate_WithoutPath_ReturnsUsageError()
    {
        var exitCode = await Program.Main(new[] { "archive-validate" });
        Assert.Equal(64, exitCode);
    }
}
