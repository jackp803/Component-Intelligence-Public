using System.Xml.Linq;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class MultiEndCableNavigationTests
{
    [Fact]
    public void NormalTopologyHasSeparateMultiEndActionAndNoOneBranchAtATimeGuidance()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "ComponentIntelligence.Desktop"))) root = root.Parent;
        Assert.NotNull(root);
        var desktop = Path.Combine(root.FullName, "src", "ComponentIntelligence.Desktop");
        var xml = XDocument.Load(Path.Combine(desktop, "TopologyCanvasControl.xaml"));
        Assert.Single(xml.Descendants().Where(e => (string?)e.Attribute("Click") == "CreateMultiEndCable_Click"));
        Assert.DoesNotContain("一次選一個分支設定", File.ReadAllText(Path.Combine(desktop, "ConnectorCableEditorDialog.cs")));
    }
}
