using System.Text.Json;
using ComponentIntelligence.Archive;
using Xunit;

namespace ComponentIntelligence.Tests.Archive;

public sealed class ArchiveEvalTests
{
    private static string RepoRoot => FindRepoRoot();

    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "OMRON_F03-20", "OMRON F03-20" };
        yield return new object[] { "OMRON_K7L-AT50DP", "OMRON K7L-AT50DP" };
        yield return new object[] { "IFM_AL1342", "IFM AL1342" };
        yield return new object[] { "IFM_AL5021", "IFM AL5021" };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ReferenceEvalCase_IsStructuredAndValidatorBacked(string directory, string expectedComponent)
    {
        using var document = Load(directory);
        var root = document.RootElement;

        Assert.Equal(expectedComponent, root.GetProperty("referenceComponent").GetString());
        Assert.NotEmpty(root.GetProperty("policyFocus").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("assertions").EnumerateArray());

        var expectedStatus = root.GetProperty("expectedValidatorStatus").GetString();
        Assert.False(string.IsNullOrWhiteSpace(expectedStatus));

        var changeSet = root.GetProperty("changeset").Deserialize<ArchiveChangeSet>(ArchiveJson.CreateOptions());
        Assert.NotNull(changeSet);

        var report = new ArchiveChangeSetValidator().Validate(changeSet!);
        Assert.Equal(expectedStatus, report.Status.ToString());
    }

    [Fact]
    public void F03_20_PreservesFunctionalRoleAndPassiveDirection()
    {
        using var document = Load("OMRON_F03-20");
        var ports = document.RootElement.GetProperty("changeset").GetProperty("components")[0].GetProperty("ports");

        Assert.Contains(ports.EnumerateArray(), port =>
            port.GetProperty("portRole").GetString() == "Input Port" &&
            port.GetProperty("direction").GetString() == "Passive");
        Assert.Contains(ports.EnumerateArray(), port =>
            port.GetProperty("portRole").GetString() == "Output Port" &&
            port.GetProperty("direction").GetString() == "Passive");
    }

    [Fact]
    public void K7L_Sensing_IsMixedPinsModeWithTerminalsTwoThreeFour()
    {
        using var document = Load("OMRON_K7L-AT50DP");
        var ports = document.RootElement.GetProperty("changeset").GetProperty("components")[0].GetProperty("ports");
        var sensing = ports.EnumerateArray().Single(port => port.GetProperty("portName").GetString() == "SENSING");

        Assert.Equal("Sensor Input", sensing.GetProperty("portRole").GetString());
        Assert.Equal("Mixed", sensing.GetProperty("direction").GetString());
        Assert.Equal("Pins", sensing.GetProperty("topologyEndpointMode").GetString());
        Assert.Equal(new[] { "2", "3", "4" }, sensing.GetProperty("pins").EnumerateArray().Select(pin => pin.GetProperty("pinNumber").GetString()).ToArray());
    }

    [Fact]
    public void AL1342_X01_RemainsMixedM12ACodedConnector()
    {
        using var document = Load("IFM_AL1342");
        var ports = document.RootElement.GetProperty("changeset").GetProperty("components")[0].GetProperty("ports");
        var x01 = ports.EnumerateArray().Single(port => port.GetProperty("portName").GetString() == "X01");

        Assert.Equal("IO-Link Port Class A", x01.GetProperty("portRole").GetString());
        Assert.Equal("Mixed", x01.GetProperty("direction").GetString());
        Assert.Equal("M12", x01.GetProperty("connector").GetString());
        Assert.Equal("A", x01.GetProperty("connectorCoding").GetString());
        Assert.Equal("Connector", x01.GetProperty("topologyEndpointMode").GetString());
    }

    [Fact]
    public void AL5021_FieldIo_ExposesAllEighteenConductors()
    {
        using var document = Load("IFM_AL5021");
        var ports = document.RootElement.GetProperty("changeset").GetProperty("components")[0].GetProperty("ports");
        var fieldIo = ports.EnumerateArray().Single(port => port.GetProperty("portName").GetString() == "FIELD_IO");
        var numbers = fieldIo.GetProperty("pins").EnumerateArray().Select(pin => pin.GetProperty("pinNumber").GetString()).ToArray();

        Assert.Equal("Pins", fieldIo.GetProperty("topologyEndpointMode").GetString());
        Assert.Equal(18, fieldIo.GetProperty("pinCount").GetInt32());
        Assert.Equal(18, numbers.Length);
        Assert.Contains("X1.0", numbers);
        Assert.Contains("X1.15", numbers);
        Assert.Contains("L-", numbers);
        Assert.Contains("L+", numbers);
    }

    [Fact]
    public void CentralArchiveManifest_IdentifiesDriveContractWithoutSecrets()
    {
        var path = Path.Combine(RepoRoot, "archive", "manifests", "CENTRAL_ARCHIVE.md");
        Assert.True(File.Exists(path), $"Missing {path}");
        var text = File.ReadAllText(path);

        foreach (var required in new[]
                 {
                     "Google Drive", "Component Intelligence", "Component_Intelligence_Database",
                     "Component_Intelligence_Database.xlsx", "Components", "Ports", "Pins",
                     "Documents/<Manufacturer>/<Model>"
                 })
            Assert.Contains(required, text, StringComparison.Ordinal);

        Assert.Contains("secret", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a second component database", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ArchiveService_ReadmeSeparatesImplementedValidationFromFutureWrites()
    {
        var path = Path.Combine(RepoRoot, "archive", "service", "README.md");
        Assert.True(File.Exists(path), $"Missing {path}");
        var text = File.ReadAllText(path);

        foreach (var operation in new[]
                 {
                     "lookup_component", "prepare_changeset", "validate_changeset",
                     "commit_changeset", "readback_changeset", "sync_archive"
                 })
            Assert.Contains(operation, text, StringComparison.Ordinal);

        Assert.Contains("implemented", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not implemented", text, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonDocument Load(string directory)
    {
        var path = Path.Combine(RepoRoot, "archive", "evals", directory, "case.json");
        Assert.True(File.Exists(path), $"Missing {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
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
