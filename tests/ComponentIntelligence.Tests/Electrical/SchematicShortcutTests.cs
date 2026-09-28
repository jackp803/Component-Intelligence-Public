using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicShortcutTests
{
    [Fact]
    public void CommandsHaveUniqueDiscoverableGesturesAndKeepViewOrderAndTransferSeparate()
    {
        var shortcuts = SchematicShortcutCatalog.All;
        Assert.Equal(shortcuts.Count, shortcuts.Select(s => s.Gesture).Distinct().Count());
        Assert.Equal(shortcuts.Count, shortcuts.Select(s => s.Command).Distinct().Count());
        Assert.All(shortcuts, s => Assert.False(string.IsNullOrWhiteSpace(s.Label)));
        Assert.Contains(shortcuts, s => s.Command == "AllPages" && s.Gesture == "F6");
        Assert.Contains(shortcuts, s => s.Command == "Continuation" && s.Gesture == "X");
        Assert.Contains(shortcuts, s => s.Command == "MovePage");
        Assert.Contains(shortcuts, s => s.Command == "OrderPrevious");
        Assert.Contains(shortcuts, s => s.Command == "PagePrevious");
        Assert.Contains(shortcuts, s => s.Command == "Gauge");
    }
}
