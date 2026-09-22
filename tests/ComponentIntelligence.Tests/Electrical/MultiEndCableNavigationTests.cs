using System.Xml.Linq;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class MultiEndCableNavigationTests
{
    [Fact]
    public void NormalTopologyRoutesUnifiedCableSettingsToAcceptedMultiEndEditor()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "ComponentIntelligence.Desktop"))) root = root.Parent;
        Assert.NotNull(root);
        var desktop = Path.Combine(root.FullName, "src", "ComponentIntelligence.Desktop");
        var xml = XDocument.Load(Path.Combine(desktop, "TopologyCanvasControl.xaml"));
        Assert.Single(xml.Descendants(), e => (string?)e.Attribute("Click") == "CableSettings_Click");
        var settings = File.ReadAllText(Path.Combine(desktop, "TopologyCanvasControl.CableSettings.cs"));
        Assert.Contains("ends.Count > 2 && dialog.ChoiceValue != CableSettingsChoice.OrdinaryWire", settings);
        Assert.Contains("var draft = _multiEndCableEditor.PrepareNew(_project, connectionIds)", settings);
        Assert.Contains("draft.ConstructionType = dialog.ChoiceValue", settings);
        Assert.Contains("OpenMultiEndCableEditor(draft)", settings);
        Assert.True(settings.IndexOf("if (dialog.ShowDialog() != true) return;", StringComparison.Ordinal) <
            settings.IndexOf("var draft = _multiEndCableEditor.PrepareNew", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(desktop, "ConnectorCableEditorDialog.cs")));
    }
}
