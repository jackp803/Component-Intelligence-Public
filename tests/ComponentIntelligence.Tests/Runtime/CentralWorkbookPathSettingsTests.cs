using ComponentIntelligence.Runtime;

namespace ComponentIntelligence.Tests.Runtime;

public sealed class CentralWorkbookPathSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ci-workbook-settings-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(_root, "isolated", "project.db");
    private string Global => Path.Combine(_root, "shared", "central-workbook.txt");
    private string Seed => Path.Combine(_root, "isolated", "empty.xlsx");
    private string Chosen => Path.Combine(_root, "selected.xlsx");

    [Fact]
    public void ExplicitSelectionOverridesLauncherImmediatelyAndAfterReopen()
    {
        var settings = new CentralWorkbookPathSettings(Database, Global, Seed);
        Assert.Equal(Seed, settings.Load());
        settings.Save(Chosen);
        Assert.Equal(Chosen, settings.Load());
        Assert.Equal(Chosen, new CentralWorkbookPathSettings(Database, Global, Seed).Load());
    }

    [Fact]
    public void IsolatedSelectionDoesNotReadOrWriteSharedPreferences()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Global)!); File.WriteAllText(Global, "production.xlsx");
        var settings = new CentralWorkbookPathSettings(Database, Global, Seed);
        Assert.Equal(Seed, settings.Load());
        settings.Save(Chosen);
        Assert.Equal("production.xlsx", File.ReadAllText(Global));
        Assert.Equal(Chosen, settings.Load());
    }

    [Fact]
    public void SeparateTestDataDirectoriesDoNotShareSelections()
    {
        new CentralWorkbookPathSettings(Database, Global, Seed).Save(Chosen);
        var other = new CentralWorkbookPathSettings(Path.Combine(_root, "other", "project.db"), Global, Seed);
        Assert.Equal(Seed, other.Load());
    }

    [Fact]
    public void UnsetLauncherKeepsLegacySharedSettings()
    {
        var settings = new CentralWorkbookPathSettings(Database, Global, null);
        Assert.Null(settings.Load()); settings.Save(Chosen);
        Assert.Equal(Chosen, File.ReadAllText(Global));
        Assert.Equal(Chosen, settings.Load());
    }

    [Fact]
    public void MissingExplicitWorkbookDoesNotSilentlySwitchToSeed()
    {
        var settings = new CentralWorkbookPathSettings(Database, Global, Seed);
        settings.Save(Chosen);
        Assert.False(File.Exists(Chosen));
        Assert.Equal(Chosen, settings.Load());
    }

    [Fact]
    public void ChoosingPathOnlyWritesSettingsNotWorkbookOrDatabase()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Database)!);
        File.WriteAllText(Database, "database sentinel"); File.WriteAllText(Chosen, "workbook sentinel");
        var settings = new CentralWorkbookPathSettings(Database, Global, Seed); settings.Save("  " + Chosen + "  ");
        Assert.Equal(Chosen, settings.Load()); Assert.Equal("workbook sentinel", File.ReadAllText(Chosen));
        Assert.Equal("database sentinel", File.ReadAllText(Database));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
