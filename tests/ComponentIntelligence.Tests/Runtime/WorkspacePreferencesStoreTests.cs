using ComponentIntelligence.Runtime;

namespace ComponentIntelligence.Tests.Runtime;

public sealed class WorkspacePreferencesStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ci-workspace-preferences-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_root, "preferences.json");

    [Fact]
    public void MissingFile_ReturnsDefaultsWithoutWritingAnything()
    {
        AssertDefaults(new WorkspacePreferencesStore(FilePath).Load());
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"zoom\":\"invalid\"}")]
    public void MalformedJson_ReturnsDefaults(string json)
    {
        WriteJson(json);
        AssertDefaults(new WorkspacePreferencesStore(FilePath).Load());
        Assert.Equal(json, File.ReadAllText(FilePath));
    }

    [Fact]
    public void PartialJson_PreservesDefaultsForMissingFields()
    {
        WriteJson("{\"leftCollapsed\":true,\"importDirectory\":\"imports\"}");
        var preferences = new WorkspacePreferencesStore(FilePath).Load();
        Assert.True(preferences.LeftCollapsed);
        Assert.Equal("imports", preferences.ImportDirectory);
        Assert.Equal(240, preferences.LeftWidth);
        Assert.Equal(260, preferences.InspectorWidth);
        Assert.Equal(.65, preferences.Zoom);
        Assert.False(preferences.AllPages);
    }

    [Theory]
    [InlineData(50, 500, .1, 180, 400, .25)]
    [InlineData(500, 50, 5, 400, 220, 2)]
    public void Load_ClampsFiniteWidthsAndZoom(double left, double inspector, double zoom, double expectedLeft, double expectedInspector, double expectedZoom)
    {
        WriteJson(FormattableString.Invariant($"{{\"leftWidth\":{left},\"inspectorWidth\":{inspector},\"zoom\":{zoom}}}"));
        var preferences = new WorkspacePreferencesStore(FilePath).Load();
        Assert.Equal(expectedLeft, preferences.LeftWidth);
        Assert.Equal(expectedInspector, preferences.InspectorWidth);
        Assert.Equal(expectedZoom, preferences.Zoom);
    }

    [Theory]
    [InlineData("\"NaN\"")]
    [InlineData("\"Infinity\"")]
    [InlineData("\"-Infinity\"")]
    [InlineData("1e999")]
    public void Load_NonFiniteNumbersFallBackToFiniteDefaults(string number)
    {
        WriteJson($"{{\"leftWidth\":{number},\"inspectorWidth\":{number},\"zoom\":{number},\"allPages\":true}}");
        var preferences = new WorkspacePreferencesStore(FilePath).Load();
        Assert.Equal(240, preferences.LeftWidth);
        Assert.Equal(260, preferences.InspectorWidth);
        Assert.Equal(.65, preferences.Zoom);
        Assert.True(preferences.AllPages);
    }

    [Fact]
    public void Save_RoundTripsAndAtomicallyReplacesExistingFileWithoutTempFiles()
    {
        var store = new WorkspacePreferencesStore(FilePath);
        store.Save(new WorkspacePreferences());
        var expected = new WorkspacePreferences
        {
            LeftWidth = 315, InspectorWidth = 330, LeftCollapsed = true,
            Zoom = 1.25, AllPages = true, ImportDirectory = "imports", ExportDirectory = "exports"
        };
        store.Save(expected);

        Assert.Equal(expected, new WorkspacePreferencesStore(FilePath).Load());
        Assert.Equal([FilePath], Directory.GetFiles(_root));
    }

    [Fact]
    public void Save_NormalizesNonFiniteAndOutOfRangeValues()
    {
        var store = new WorkspacePreferencesStore(FilePath);
        store.Save(new WorkspacePreferences { LeftWidth = double.NaN, InspectorWidth = double.PositiveInfinity, Zoom = 3 });
        var preferences = store.Load();
        Assert.Equal(240, preferences.LeftWidth);
        Assert.Equal(260, preferences.InspectorWidth);
        Assert.Equal(2, preferences.Zoom);
    }

    [Fact]
    public void SaveFailure_IsVisibleToCallerAndPreservesPreviousFile()
    {
        var store = new WorkspacePreferencesStore(FilePath);
        store.Save(new WorkspacePreferences { Zoom = 1 });
        using (var locked = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<IOException>(() => store.Save(new WorkspacePreferences { Zoom = 2 }));
        }
        Assert.Equal(1, store.Load().Zoom);
        Assert.Equal([FilePath], Directory.GetFiles(_root));
    }

    private void WriteJson(string json)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(FilePath, json);
    }

    private static void AssertDefaults(WorkspacePreferences preferences)
    {
        Assert.Equal(240, preferences.LeftWidth);
        Assert.Equal(260, preferences.InspectorWidth);
        Assert.False(preferences.LeftCollapsed);
        Assert.Equal(.65, preferences.Zoom);
        Assert.False(preferences.AllPages);
        Assert.Null(preferences.ImportDirectory);
        Assert.Null(preferences.ExportDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
