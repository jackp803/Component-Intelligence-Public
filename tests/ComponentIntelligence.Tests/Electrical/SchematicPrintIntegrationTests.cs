using System.Xml.Linq;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicPrintIntegrationTests
{
    [Fact]
    public void AnotherRepresentationHasSeparateNormalActionAndShortcut()
    {
        var xml = XDocument.Load(Path.Combine(ConsolidatedWorkspaceNavigationTests.Desktop(), "SchematicWorkspaceControl.xaml"));
        Assert.Single(xml.Descendants(), e => (string?)e.Attribute("Click") == "AnotherRepresentation_Click");
        Assert.Contains(SchematicShortcutCatalog.All, s => s.Command == "AnotherRepresentation" && s.Gesture == "Shift+P");
    }

    [Fact]
    public void DraftGapsAreAccessibleFromNormalEditor()
    {
        var xml = XDocument.Load(Path.Combine(ConsolidatedWorkspaceNavigationTests.Desktop(), "SchematicWorkspaceControl.xaml"));
        Assert.Single(xml.Descendants(), e => (string?)e.Attribute("Click") == "ReviewDraft_Click");
        Assert.Contains(SchematicShortcutCatalog.All, s => s.Command == "ReviewDraft" && s.Gesture == "F8");
    }

    [Fact]
    public void NormalEditorExposesPdfAndPrintWithDistinctShortcuts()
    {
        var xml = XDocument.Load(Path.Combine(ConsolidatedWorkspaceNavigationTests.Desktop(), "SchematicWorkspaceControl.xaml"));
        Assert.Single(xml.Descendants(), e => (string?)e.Attribute("Click") == "ExportPdf_Click");
        Assert.Single(xml.Descendants(), e => (string?)e.Attribute("Click") == "Print_Click");
        Assert.Contains(SchematicShortcutCatalog.All, s => s.Command == "ExportPdf" && s.Gesture == "Ctrl+Shift+E");
        Assert.Contains(SchematicShortcutCatalog.All, s => s.Command == "Print" && s.Gesture == "Ctrl+P");
    }

    [Fact]
    public void OutputWaitsForSavedSnapshotAndUsesTheSamePageRenderer()
    {
        var desktop = ConsolidatedWorkspaceNavigationTests.Desktop();
        var source = File.ReadAllText(Path.Combine(desktop, "SchematicWorkspaceControl.Print.cs"));
        Assert.Contains("await SaveOutputSnapshotAsync()", source);
        Assert.Contains("RenderPage(project, page)", source);
        Assert.DoesNotContain("TopologyProjection", source);
        Assert.DoesNotContain("DrawingPlanningInputBuilder", source);
    }
}
