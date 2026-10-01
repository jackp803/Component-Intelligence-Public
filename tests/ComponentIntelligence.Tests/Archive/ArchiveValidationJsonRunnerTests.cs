using System.Text.Json;
using ComponentIntelligence.Archive;
using Xunit;

namespace ComponentIntelligence.Tests.Archive;

public sealed class ArchiveValidationJsonRunnerTests
{
    [Fact]
    public async Task ValidJson_ReturnsPassExitCode()
    {
        var result = await RunAsync(await WriteAsync(ArchiveChangeSetValidatorTests.ValidChangeSet()));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"status\": \"PASS\"", result.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewJson_ReturnsReviewExitCode()
    {
        var pin = ArchiveChangeSetValidatorTests.ValidPin("PIN-1", "PORT-1", "1") with { PinStatus = ArchivePinStatus.NC };
        var component = ArchiveChangeSetValidatorTests.ValidComponent("CMP-1") with
        {
            Ports = new[] { ArchiveChangeSetValidatorTests.ValidPort("PORT-1", "CMP-1", new[] { pin }) }
        };
        var input = ArchiveChangeSetValidatorTests.ValidChangeSet() with { Components = new[] { component } };

        var result = await RunAsync(await WriteAsync(input));
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("REVIEW_REQUIRED", result.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectedJson_ReturnsRejectExitCode()
    {
        var pins = new[]
        {
            ArchiveChangeSetValidatorTests.ValidPin("PIN-1", "PORT-1", "1")
        };
        var port = ArchiveChangeSetValidatorTests.ValidPort("PORT-1", "CMP-1", pins) with { PinCount = 2 };
        var component = ArchiveChangeSetValidatorTests.ValidComponent("CMP-1") with { Ports = new[] { port } };
        var input = ArchiveChangeSetValidatorTests.ValidChangeSet() with { Components = new[] { component } };

        var result = await RunAsync(await WriteAsync(input));
        Assert.Equal(3, result.ExitCode);
        Assert.Contains("\"status\": \"REJECT\"", result.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingFile_ReturnsUsageExitCode()
    {
        var result = await RunAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.json"));
        Assert.Equal(64, result.ExitCode);
        Assert.Contains("TOOL_FAILURE", result.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedJson_ReturnsUsageExitCode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"archive-bad-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, "{ not-json");
        try
        {
            var result = await RunAsync(path);
            Assert.Equal(64, result.ExitCode);
            Assert.Contains("TOOL_FAILURE", result.Json, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<string> WriteAsync(ArchiveChangeSet changeSet)
    {
        var path = Path.Combine(Path.GetTempPath(), $"archive-change-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(changeSet, ArchiveJson.CreateOptions(true)));
        return path;
    }

    private static async Task<(int ExitCode, string Json)> RunAsync(string path)
    {
        var type = typeof(ArchiveChangeSet).Assembly.GetType("ComponentIntelligence.Archive.ArchiveValidationJsonRunner");
        Assert.NotNull(type);
        dynamic instance = Activator.CreateInstance(type!)!;
        dynamic task = instance.ValidateFileAsync(path);
        var result = await task;
        return ((int)result.Item1, (string)result.Item2);
    }
}
