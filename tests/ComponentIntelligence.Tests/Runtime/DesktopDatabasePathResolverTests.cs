using ComponentIntelligence.Runtime;

namespace ComponentIntelligence.Tests.Runtime;

public sealed class DesktopDatabasePathResolverTests
{
    private static readonly string LocalAppDataRoot = Path.Combine(
        Path.GetPathRoot(Environment.SystemDirectory)!,
        "Users",
        "operator",
        "AppData",
        "Local");

    [Fact]
    public void Resolve_UnsetOverride_ReturnsExistingProductionDefault()
    {
        var result = DesktopDatabasePathResolver.Resolve(null, LocalAppDataRoot);

        Assert.Equal(
            Path.Combine(LocalAppDataRoot, "ComponentIntelligence", "component-intelligence.db"),
            result);
    }

    [Fact]
    public void Resolve_WhitespaceOverride_ReturnsExistingProductionDefault()
    {
        var result = DesktopDatabasePathResolver.Resolve("  \t ", LocalAppDataRoot);

        Assert.Equal(
            Path.Combine(LocalAppDataRoot, "ComponentIntelligence", "component-intelligence.db"),
            result);
    }

    [Fact]
    public void Resolve_AbsoluteOverride_ReturnsNormalizedOverride()
    {
        var configured = Path.Combine(LocalAppDataRoot, "Smoke", "nested", "..", "disposable.db");

        var result = DesktopDatabasePathResolver.Resolve(configured, LocalAppDataRoot);

        Assert.Equal(Path.GetFullPath(configured), result);
    }

    [Fact]
    public void Resolve_RelativeOverride_FailsClosed()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DesktopDatabasePathResolver.Resolve(Path.Combine("smoke", "disposable.db"), LocalAppDataRoot));

        Assert.Contains(DesktopDatabasePathResolver.EnvironmentVariableName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("absolute", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_InvalidOverride_DoesNotReturnProductionDefault()
    {
        var productionDefault = Path.Combine(LocalAppDataRoot, "ComponentIntelligence", "component-intelligence.db");

        var exception = Record.Exception(() =>
            DesktopDatabasePathResolver.Resolve("not-an-absolute-path", LocalAppDataRoot));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.DoesNotContain(productionDefault, exception!.Message, StringComparison.OrdinalIgnoreCase);
    }
}
