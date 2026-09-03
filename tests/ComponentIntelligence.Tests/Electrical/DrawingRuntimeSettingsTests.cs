using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingRuntimeSettingsTests
{
    [Fact]
    public void Validate_RequiresActualPythonAndAutomationRootWithPipelineScript()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp3b-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "tools"));
        var python = Path.Combine(root, OperatingSystem.IsWindows() ? "python.exe" : "python");
        File.WriteAllText(python, "stub");
        File.WriteAllText(Path.Combine(root, "tools", "electrical_drawing_pipeline.py"), "# stub");
        try
        {
            var valid = DrawingRuntimeSettingsValidator.Validate(new DrawingRuntimeSettings { PythonExecutable = python, AutomationRoot = root });
            Assert.True(valid.IsValid);
            Assert.Empty(valid.Issues);

            var invalid = DrawingRuntimeSettingsValidator.Validate(new DrawingRuntimeSettings { PythonExecutable = Path.Combine(root, "missing-python"), AutomationRoot = root });
            Assert.False(invalid.IsValid);
            Assert.Contains(invalid.Issues, issue => issue.Code == "DRAWING_RUNTIME_PYTHON_MISSING");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void UserLocalStore_RoundTripsOnlyValidatedPublicFields()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp3b-runtime-store-{Guid.NewGuid():N}");
        var automationRoot = Path.Combine(root, "automation");
        Directory.CreateDirectory(Path.Combine(automationRoot, "tools"));
        var python = Path.Combine(root, OperatingSystem.IsWindows() ? "python.exe" : "python");
        File.WriteAllText(python, "stub");
        File.WriteAllText(Path.Combine(automationRoot, "tools", "electrical_drawing_pipeline.py"), "# stub");
        var path = Path.Combine(root, "drawing-runtime.json");
        try
        {
            var store = new DrawingRuntimeSettingsStore(path);
            var value = new DrawingRuntimeSettings { PythonExecutable = python, AutomationRoot = automationRoot };
            store.Save(value);
            var json = File.ReadAllText(path);
            Assert.Contains("\"pythonExecutable\"", json, StringComparison.Ordinal);
            Assert.Contains("\"automationRoot\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("placeholder", json, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(value, store.Load());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Save_InvalidSettings_FailsClosedWithoutWritingFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp3b-runtime-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "drawing-runtime.json");
        try
        {
            var store = new DrawingRuntimeSettingsStore(path);
            var invalid = new DrawingRuntimeSettings
            {
                PythonExecutable = Path.Combine(root, "missing-python"),
                AutomationRoot = Path.Combine(root, "missing-automation")
            };
            Assert.Throws<InvalidOperationException>(() => store.Save(invalid));
            Assert.False(File.Exists(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
