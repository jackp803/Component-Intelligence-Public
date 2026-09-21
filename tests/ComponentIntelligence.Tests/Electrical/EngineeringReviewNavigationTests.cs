using System.Xml.Linq;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class EngineeringReviewNavigationTests
{
    [Fact]
    public void EngineeringReview_IsInTheVisibleTopologyToolbar()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "ComponentIntelligence.Desktop"))) root = root.Parent;
        Assert.NotNull(root);
        var xml = XDocument.Load(Path.Combine(root.FullName, "src", "ComponentIntelligence.Desktop", "ElectricalWorkspaceWindow.xaml"));
        var button = Assert.Single(xml.Descendants().Where(e => e.Name.LocalName == "Button" && (string?)e.Attribute("Click") == "EngineeringReview_Click"));
        var tab = Assert.Single(button.Ancestors().Where(e => e.Name.LocalName == "TabItem"));
        Assert.Equal("拓樸 Topology", (string?)tab.Attribute("Header"));
        Assert.DoesNotContain(button.AncestorsAndSelf(), e => (string?)e.Attribute("Visibility") is "Collapsed" or "Hidden");
    }
}
