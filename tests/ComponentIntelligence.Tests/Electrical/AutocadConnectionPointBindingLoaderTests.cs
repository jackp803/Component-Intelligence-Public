using ComponentIntelligence.Desktop;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class AutocadConnectionPointBindingLoaderTests : IDisposable
{
    private readonly List<string> _temporaryPaths = [];

    [Fact]
    public void MissingSidecar_ReturnsErrorAndBlocksReviewWithoutCreatingAFile()
    {
        var path = NewPath();

        var result = AutocadConnectionPointBindingLoader.Load(path);

        Assert.False(result.Succeeded);
        Assert.False(File.Exists(path));
        var issue = Assert.Single(result.Issues);
        Assert.Equal("Error", issue.Severity);
        Assert.Equal("AuditedBindingsSidecarMissing", issue.Code);
    }

    [Fact]
    public void MalformedSidecar_ReturnsError()
    {
        var path = Write("{ not-json }");

        var result = AutocadConnectionPointBindingLoader.Load(path);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == "AuditedBindingsSidecarInvalid");
    }

    [Fact]
    public void DuplicateEndpointBinding_ReturnsError()
    {
        var path = Write("""
            {"schemaVersion":"ci-acade-connection-points.v1","bindings":[
              {"endpointId":"P1","symbolKey":"SYM1","connectionPointId":"X1"},
              {"endpointId":"p1","symbolKey":"SYM2","connectionPointId":"X2"}
            ]}
            """);

        var result = AutocadConnectionPointBindingLoader.Load(path);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == "AuditedBindingsDuplicate");
    }

    [Fact]
    public void ValidSidecar_ReturnsAuditedBindings()
    {
        var path = Write("""
            {"schemaVersion":"ci-acade-connection-points.v1","bindings":[
              {"endpointId":"P1","symbolKey":"SYM1","connectionPointId":"X1"}
            ]}
            """);

        var result = AutocadConnectionPointBindingLoader.Load(path);

        Assert.True(result.Succeeded);
        var binding = Assert.Single(result.Bindings);
        Assert.Equal("P1", binding.EndpointId);
        Assert.Equal("SYM1", binding.SymbolKey);
        Assert.Equal("X1", binding.ConnectionPointId);
    }

    public void Dispose()
    {
        foreach (var path in _temporaryPaths.Where(File.Exists)) File.Delete(path);
    }

    private string NewPath()
    {
        var path = Path.Combine(
        Path.GetTempPath(),
        $"component-intelligence-acade-test-{Guid.NewGuid():N}.json");
        _temporaryPaths.Add(path);
        return path;
    }

    private string Write(string contents)
    {
        var path = NewPath();
        File.WriteAllText(path, contents);
        return path;
    }
}
