using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicSymbolPresentationTests
{
    [Fact]
    public void ConfirmedCadContactsDoNotReceiveDuplicatePinLabels()
    {
        var symbol = Symbol();
        var anchor = new SchematicAnchor { EndpointId = "pin", Confirmed = true };
        Assert.False(SchematicSymbolPresentation.ShowAnchorLabel(symbol, anchor));
        Assert.True(SchematicSymbolPresentation.ShowAnchorLabel(symbol, anchor with { Confirmed = false }));
        Assert.True(SchematicSymbolPresentation.ShowAnchorLabel(symbol with { Geometry = null }, anchor));
    }

    [Fact]
    public void OnlyExplicitTagAttributeUsesInstanceReferenceWithoutMutatingSourceGeometry()
    {
        var symbol = Symbol();
        var view = SchematicSymbolPresentation.Geometry(symbol, "K1")!;
        Assert.Equal("K1", view.Primitives[0].Text);
        Assert.Equal("LSA", view.Primitives[1].Text);
        Assert.Equal("LSA", symbol.Geometry!.Primitives[0].Text);
        Assert.Equal(symbol.Geometry.SourceSha256, view.SourceSha256);
        Assert.Equal("?", SchematicSymbolPresentation.Geometry(symbol, null)!.Primitives[0].Text);
    }

    private static SchematicSymbol Symbol() => new()
    {
        SymbolId = "s", ComponentInstanceId = "c", PageId = "p",
        Geometry = new() { SourceSha256 = new string('A', 64), Width = 30, Height = 20,
            Primitives = [new() { Kind = "TEXT", Start = new(1, 1), Text = "LSA", AttributeTag = "TAG1" },
                new() { Kind = "TEXT", Start = new(1, 5), Text = "LSA" }] }
    };
}
