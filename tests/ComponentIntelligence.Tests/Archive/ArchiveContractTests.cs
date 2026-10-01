using System.Text.Json;
using System.Text.Json.Serialization;
using ComponentIntelligence.Contracts;
using Xunit;

namespace ComponentIntelligence.Tests.Archive;

public sealed class ArchiveContractTests
{
    private static string RepoRoot => FindRepoRoot();

    [Theory]
    [InlineData("archive/contracts/archive-changeset.schema.json")]
    [InlineData("archive/contracts/validation-result.schema.json")]
    [InlineData("archive/contracts/archive-readback.schema.json")]
    public void ArchiveSchemas_ArePresentAndValidJson(string relativePath)
    {
        var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Missing {relativePath}");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("object", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void ArchiveChangeSetSchema_RequiresIdentityAndOperation()
    {
        var path = Path.Combine(RepoRoot, "archive", "contracts", "archive-changeset.schema.json");
        Assert.True(File.Exists(path), $"Missing {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var required = document.RootElement.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("jobId", required);
        Assert.Contains("operation", required);
        Assert.Contains("manufacturer", required);
        Assert.Contains("model", required);
        Assert.Contains("components", required);
    }

    [Fact]
    public void ArchiveChangeSet_CanDeserializeMinimalCreatePayload()
    {
        var assembly = typeof(ComponentIdentity).Assembly;
        var type = assembly.GetType("ComponentIntelligence.Archive.ArchiveChangeSet");
        Assert.NotNull(type);

        const string json = """
        {
          "jobId": "JOB-1",
          "policyVersion": "v2",
          "policyRevision": "test",
          "operation": "CREATE",
          "manufacturer": "IFM",
          "model": "O5D100",
          "components": [
            {
              "componentId": "IFM_O5D100",
              "manufacturer": "IFM",
              "model": "O5D100",
              "topologyStatus": "Review",
              "layoutStatus": "NeedsData",
              "ports": []
            }
          ],
          "documentPaths": [],
          "unresolvedUnknowns": [],
          "conflicts": []
        }
        """;

        var value = JsonSerializer.Deserialize(json, type!, JsonOptions());
        Assert.NotNull(value);
        Assert.Equal("IFM", type!.GetProperty("Manufacturer")!.GetValue(value));
        Assert.Equal("O5D100", type.GetProperty("Model")!.GetValue(value));
        Assert.Equal("CREATE", type.GetProperty("Operation")!.GetValue(value)!.ToString());
    }

    [Theory]
    [InlineData("ComponentIntelligence.Archive.ArchiveValidationStatus", "PASS")]
    [InlineData("ComponentIntelligence.Archive.ArchiveValidationStatus", "REVIEW_REQUIRED")]
    [InlineData("ComponentIntelligence.Archive.ArchiveJobStatus", "VALIDATED_NOT_WRITTEN")]
    [InlineData("ComponentIntelligence.Archive.ArchiveReadbackStatus", "MISMATCH")]
    public void ArchiveStatusEnums_RoundTripAsExactStrings(string typeName, string member)
    {
        var type = typeof(ComponentIdentity).Assembly.GetType(typeName);
        Assert.NotNull(type);

        var value = Enum.Parse(type!, member);
        var json = JsonSerializer.Serialize(value, type!, JsonOptions());
        Assert.Equal($"\"{member}\"", json);
    }

    private static JsonSerializerOptions JsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
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
