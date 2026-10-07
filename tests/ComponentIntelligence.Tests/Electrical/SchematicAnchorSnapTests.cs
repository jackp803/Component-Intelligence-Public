using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicAnchorSnapTests
{
    private static SchematicSymbol Symbol(string id, string page, double y) => new()
    {
        SymbolId = id, PageId = page, ComponentInstanceId = id, Position = new(10, y),
        Anchors = [new() { EndpointId = id + ":pin", Position = new(40, 5) }]
    };

    [Fact]
    public void NearClickUsesExactAnchorRatherThanGridOrFreeEnd()
    {
        var symbol = Symbol("one", "page", 10);
        var hit = SchematicAnchorSnap.Find([symbol], "page", new(51, 16), 2);
        Assert.NotNull(hit);
        Assert.Equal(new SchematicPoint(50, 15), hit.Position);
        Assert.Equal("one:pin", hit.EndpointId);
        Assert.False(symbol.Anchors[0].Confirmed);
    }

    [Fact]
    public void OtherPagesAndDistantPointsDoNotSnap()
    {
        Assert.Null(SchematicAnchorSnap.Find([Symbol("one", "other", 10)], "page", new(50, 15), 2));
        Assert.Null(SchematicAnchorSnap.Find([Symbol("one", "page", 10)], "page", new(53, 15), 2));
    }

    [Fact]
    public void EquidistantPinsAreAmbiguousInsteadOfChoosingByListOrder()
    {
        Assert.Throws<InvalidOperationException>(() => SchematicAnchorSnap.Find(
            [Symbol("one", "page", 10), Symbol("two", "page", 14)], "page", new(50, 17), 3));
    }

    [Fact]
    public void RotatedSymbolUsesItsTransformedAnchor()
    {
        var symbol = Symbol("one", "page", 10) with { Rotation = 90 };
        var expected = SchematicAuthoringService.AnchorPoint(symbol, "one:pin");
        var hit = SchematicAnchorSnap.Find([symbol], "page", expected with { X = expected.X + 1 }, 2);
        Assert.Equal(expected, hit!.Position);
    }
}
