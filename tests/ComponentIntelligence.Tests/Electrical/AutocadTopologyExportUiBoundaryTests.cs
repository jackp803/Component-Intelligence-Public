using System.Security.Cryptography;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class AutocadTopologyExportUiBoundaryTests
{
    private const string AcceptedV1HandlerSha256 =
        "2CBABB109812834A088DA5BB3A00536C2B60A34155687ACA410DDD4343DB5F09";

    [Fact]
    public void ExistingV1AutocadReviewHandler_RemainsByteIdenticalToAcceptedCp2A()
    {
        var source = RepositoryFile(
            "src",
            "ComponentIntelligence.Desktop",
            "ElectricalWorkspaceWindow.AutocadReview.cs");

        Assert.Equal(
            AcceptedV1HandlerSha256,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))));
    }

    [Fact]
    public void TopologyEditor_ExposesSeparateV2ContractExportAction()
    {
        var xaml = File.ReadAllText(RepositoryFile(
            "src",
            "ComponentIntelligence.Desktop",
            "ElectricalWorkspaceWindow.xaml"));

        Assert.Contains("x:Name=\"ExportAutocadV2Button\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"ExportAutocadV2_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AutoCadReviewButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"AutoCadReview_Click\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void V2UiHandler_UsesOnlyNonLaunchCoordinatorAndSurfacesTakeoverIdentity()
    {
        var source = File.ReadAllText(RepositoryFile(
            "src",
            "ComponentIntelligence.Desktop",
            "ElectricalWorkspaceWindow.AutocadV2Export.cs"));

        Assert.Contains("AutocadStagingGraphV2ExportCoordinator", source, StringComparison.Ordinal);
        Assert.Contains("result.SchemaVersion", source, StringComparison.Ordinal);
        Assert.Contains("result.ProjectId", source, StringComparison.Ordinal);
        Assert.Contains("result.GraphPath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AutocadStagingReviewRunner", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessStartInfo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("powershell", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accoreconsole", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SymbolAcceptanceRegistry", source, StringComparison.Ordinal);
    }

    private static string RepositoryFile(params string[] pathParts)
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(pathParts)}");
    }
}
