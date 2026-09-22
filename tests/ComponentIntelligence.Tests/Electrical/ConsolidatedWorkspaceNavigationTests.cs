using System.Xml.Linq;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ConsolidatedWorkspaceNavigationTests
{
    internal static string Desktop()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "ComponentIntelligence.Desktop"))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "src", "ComponentIntelligence.Desktop");
    }

    [Fact]
    public void WorkspaceDefinesOnlyThreeAcceptedTabsWithoutRuntimeHiding()
    {
        var desktop = Desktop();
        var xml = XDocument.Load(Path.Combine(desktop, "ElectricalWorkspaceWindow.xaml"));
        var tabs = xml.Descendants().Where(e => e.Name.LocalName == "TabItem").ToArray();
        Assert.Equal(new[] { "拓樸 Topology", "Layout｜實體佈局", "Drawing Planning｜圖面規劃" }, tabs.Select(t => (string?)t.Attribute("Header")));
        Assert.False(File.Exists(Path.Combine(desktop, "ElectricalWorkspaceWindow.PrimaryTabs.cs")));
        Assert.DoesNotContain(xml.Descendants(), e => (string?)e.Attribute("Click") is "AutoArrangeTopology_Click" or "AutoCadReview_Click" or "ExportAutocadV2_Click" or "AddTerminalBlock_Click");
    }

    [Fact]
    public void MainHasOneElectricalEntryAndNoNotionProductSurface()
    {
        var desktop = Desktop();
        var xml = XDocument.Load(Path.Combine(desktop, "MainWindow.xaml"));
        Assert.Single(xml.Descendants(), e => (string?)e.Attribute("Click") == "OpenElectricalWorkspace_Click");
        Assert.DoesNotContain(xml.Descendants(), e => (string?)e.Attribute("Click") == "OpenTopology_Click");
        foreach (var name in new[] { "MainWindow.Notion.cs", "MainWindow.NotionOnly.cs", "NotionConnectionDialog.cs" })
            Assert.False(File.Exists(Path.Combine(desktop, name)));
    }

    [Fact]
    public void TopologyHasUnifiedCableEntryWithoutRetiredCreationCommands()
    {
        var xml = XDocument.Load(Path.Combine(Desktop(), "TopologyCanvasControl.xaml"));
        Assert.Single(xml.Descendants(), e => (string?)e.Attribute("Click") == "CableSettings_Click");
        Assert.DoesNotContain(xml.Descendants(), e => (string?)e.Attribute("Click") is
            "AddArchivedJumper_Click" or "AutoRouteConnections_Click" or "CreateCustomHarness_Click" or "CreateCableAssembly_Click");
    }
}
