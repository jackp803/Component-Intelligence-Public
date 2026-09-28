using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicSymbolPresentationTests
{
    [Theory]
    [InlineData(0, 152, 82.2, 0, false)]
    [InlineData(90, 127.8, 132, 90, false)]
    [InlineData(180, 98, 107.8, 0, true)]
    [InlineData(270, 102.2, 78, 90, true)]
    public void PinLabelFollowsRotationWithoutUpsideDownText(int rotation, double x, double y,
        int textRotation, bool oppositeCorner)
    {
        var anchor = new SchematicAnchor { EndpointId = "pin", Position = new(50, 6), Label = "1 L+" };
        var symbol = Symbol() with { Position = new(100, 80), Width = 50, Height = 30,
            Rotation = rotation, Anchors = [anchor] };
        var before = System.Text.Json.JsonSerializer.Serialize(symbol);
        var label = SchematicSymbolPresentation.AnchorLabel(symbol, anchor);
        Assert.Equal(x, label.Position.X, 8);
        Assert.Equal(y, label.Position.Y, 8);
        Assert.Equal(textRotation, label.Rotation);
        Assert.Equal(oppositeCorner, label.OppositeCorner);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(symbol));
    }

    [Fact]
    public void RotatedFourPinLabelsUseSeparateColumnsRatherThanOneHorizontalRow()
    {
        var anchors = Enumerable.Range(1, 4).Select(n => new SchematicAnchor
            { EndpointId = $"pin-{n}", Position = new(50, 6 * n), Label = $"{n} long function" }).ToList();
        var symbol = Symbol() with { Rotation = 90, Width = 50, Height = 30, Anchors = anchors };
        var labels = anchors.Select(a => SchematicSymbolPresentation.AnchorLabel(symbol, a)).ToArray();
        Assert.All(labels, l => Assert.Equal(90, l.Rotation));
        for (var i = 1; i < labels.Length; i++)
            Assert.Equal(6, labels[i - 1].Position.X - labels[i].Position.X, 8);
    }

    [Theory]
    [InlineData(0, false, 80)]
    [InlineData(90, false, 80)]
    [InlineData(0, true, 76)]
    [InlineData(90, true, 60)]
    public void LabelTopUsesMeasuredExtentForReferenceClearance(int rotation, bool oppositeCorner, double expected)
    {
        var pose = new SchematicAnchorLabel(new(100, 80), rotation, oppositeCorner);
        Assert.Equal(expected, pose.Top(20, 4));
    }

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
