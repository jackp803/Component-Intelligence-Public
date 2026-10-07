namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingWorkspaceEventBoundaryTests
{
    [Fact]
    public void WorkspaceTabHandlerRejectsBubbledChildSelectionBeforeReload()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "ComponentIntelligence.Desktop"))) root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root.FullName, "src", "ComponentIntelligence.Desktop", "ElectricalWorkspaceWindow.WorkspaceTabs.cs"));
        var handler = source[source.IndexOf("WorkspaceTabs.SelectionChanged +=", StringComparison.Ordinal)..];
        var guard = handler.IndexOf("if (!ReferenceEquals(e.Source, WorkspaceTabs)) return;", StringComparison.Ordinal);
        Assert.True(guard >= 0, "Child ListBox/ComboBox selection must not reload DrawingPlan and clear selection/undo.");
        Assert.True(guard < handler.IndexOf("LoadPlan(", StringComparison.Ordinal));
    }
}
