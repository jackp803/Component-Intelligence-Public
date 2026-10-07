using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicSymbolPresentationTests
{
    [Fact]
    public void CableContactCoverageIncludesMissingPinsWithoutCountingDuplicateOrForeignAnchors()
    {
        var cable = new CableInstance { CableInstanceId = "W", CableDefinitionId = "c", ArchivedCable = new() {
            Template = new() { TemplateId = "c", TemplateRevision = "r1", AssetSha256 = new string('A', 64) },
            Ports = [new() { PortId = "port", Name = "end", Pins = Enumerable.Range(1, 3)
                .Select(i => new ComponentPin { PinId = $"pin-{i}", PinNumber = i.ToString() }).ToList() }] } };
        var owner = new SchematicSymbolOwner(null, cable);
        var symbol = Symbol() with { ComponentInstanceId = "", CableInstanceId = "W", Anchors = [
            new() { EndpointId = "pin-1", Confirmed = true }, new() { EndpointId = "pin-2", Confirmed = true }] };
        Assert.Equal((2, 3), SchematicSymbolPresentation.ContactCoverage(symbol, owner));
        symbol.Anchors.Add(new() { EndpointId = "pin-1", Confirmed = true });
        symbol.Anchors.Add(new() { EndpointId = "foreign-pin", Confirmed = true });
        Assert.Equal((2, 3), SchematicSymbolPresentation.ContactCoverage(symbol, owner));
        symbol.Anchors.Add(new() { EndpointId = "pin-3", Confirmed = false });
        Assert.Equal((2, 3), SchematicSymbolPresentation.ContactCoverage(symbol, owner));
    }

    [Theory]
    [InlineData("TEXT", "TAG1", false)]
    [InlineData("MTEXT", "TAG1", false)]
    [InlineData("LINE", "TAG1", true)]
    [InlineData("TEXT", "TAG2", true)]
    [InlineData("TEXT", null, true)]
    public void ExternalReferenceIsOmittedOnlyWhenCadRendersExplicitReference(string kind, string? tag, bool expected)
    {
        var symbol = Symbol();
        symbol = symbol with { Geometry = symbol.Geometry! with
            { Primitives = [new() { Kind = kind, Start = new(1, 1), AttributeTag = tag, Text = "K1" }] } };
        Assert.Equal(expected, SchematicSymbolPresentation.ShowReferenceLabel(symbol));
        Assert.True(SchematicSymbolPresentation.ShowReferenceLabel(symbol with { Geometry = null }));
    }

    [Theory]
    [InlineData(0, 152, 86, SchematicLabelSide.Right)]
    [InlineData(90, 124, 132, SchematicLabelSide.Bottom)]
    [InlineData(180, 98, 104, SchematicLabelSide.Left)]
    [InlineData(270, 106, 78, SchematicLabelSide.Top)]
    public void PinLabelStaysHorizontalAndOutsideRotatedBody(int rotation, double x, double y,
        SchematicLabelSide side)
    {
        var anchor = new SchematicAnchor { EndpointId = "pin", Position = new(50, 6), Label = "1 L+" };
        var symbol = Symbol() with { Position = new(100, 80), Width = 50, Height = 30,
            Rotation = rotation, Anchors = [anchor] };
        var before = System.Text.Json.JsonSerializer.Serialize(symbol);
        var label = SchematicSymbolPresentation.AnchorLabel(symbol, anchor);
        Assert.Equal(x, label.Position.X, 8);
        Assert.Equal(y, label.Position.Y, 8);
        Assert.Equal(side, label.Side);
        var box = label.Bounds(18, 3);
        Assert.True(side switch
        {
            SchematicLabelSide.Left => box.Right < 100,
            SchematicLabelSide.Right => box.Left > 150,
            SchematicLabelSide.Top => box.Bottom < 80,
            SchematicLabelSide.Bottom => box.Top > 130,
            _ => false
        });
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(symbol));
    }

    [Fact]
    public void RotatedFourPinLabelsRemainOnSeparateOutsidePositions()
    {
        var anchors = Enumerable.Range(1, 4).Select(n => new SchematicAnchor
            { EndpointId = $"pin-{n}", Position = new(50, 6 * n), Label = $"{n} long function" }).ToList();
        var symbol = Symbol() with { Rotation = 90, Width = 50, Height = 30, Anchors = anchors };
        var labels = anchors.Select(a => SchematicSymbolPresentation.AnchorLabel(symbol, a)).ToArray();
        Assert.All(labels, l => Assert.Equal(SchematicLabelSide.Bottom, l.Side));
        for (var i = 1; i < labels.Length; i++)
            Assert.Equal(6, labels[i - 1].Position.X - labels[i].Position.X, 8);
        Assert.All(labels, label => Assert.Equal(90, label.TextRotation));
        var boxes = labels.Select(label => label.Bounds(35, 3)).ToArray();
        for (var i = 0; i < boxes.Length; i++) Assert.True(boxes[i].Right < labels[i].Position.X);
        for (var i = 1; i < boxes.Length; i++)
            Assert.False(boxes[i - 1].Left < boxes[i].Right && boxes[i - 1].Right > boxes[i].Left);
    }

    [Fact]
    public void ImportedCadTextUsesWorldPositionWithoutRotatingSourceText()
    {
        var symbol = Symbol() with { Position = new(100, 80), Width = 30, Height = 20, Rotation = 90 };
        var view = SchematicSymbolPresentation.UprightCadText(symbol, SchematicSymbolPresentation.Geometry(symbol, "K1")!);
        Assert.Equal(2, view.Primitives.Count);
        Assert.Equal(new SchematicPoint(119, 81), view.Primitives[0].Start);
        Assert.All(view.Primitives, primitive => Assert.Equal(0, primitive.Rotation));
        Assert.Equal("K1", view.Primitives[0].Text);
        Assert.Equal("LSA", symbol.Geometry!.Primitives[0].Text);
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

    [Fact]
    public void CableTextUsesOnlyExplicitFieldBindingsAndKeepsUnknownValuesAsDraft()
    {
        var template = new ArchivedCableTemplate { TemplateId = "c", TemplateRevision = "r1", AssetSha256 = new string('A', 64),
            TextBindings = [new("LEN", CableTextField.LengthMm), new("SPEC", CableTextField.Specification)] };
        var cable = new CableInstance { CableInstanceId = "W", CableDefinitionId = "c", ProvidedLengthMm = 1200,
            Specification = "AWG 22", ArchivedCable = new() { Template = template } };
        var symbol = Symbol() with { ComponentInstanceId = "", CableInstanceId = "W", Geometry = new() {
            SourceSha256 = template.AssetSha256, Width = 50, Height = 20,
            Primitives = [new() { Kind = "TEXT", Start = new(1, 1), AttributeTag = "LEN", Text = "old" },
                new() { Kind = "TEXT", Start = new(1, 5), AttributeTag = "SPEC", Text = "old" },
                new() { Kind = "TEXT", Start = new(1, 9), AttributeTag = "UNBOUND", Text = "keep" }] } };
        var owner = new SchematicSymbolOwner(null, cable);
        var view = SchematicSymbolPresentation.GeometryForOwner(symbol, owner)!;
        Assert.Equal("1200 mm", view.Primitives[0].Text); Assert.Equal("AWG 22", view.Primitives[1].Text);
        Assert.Equal("keep", view.Primitives[2].Text); Assert.Equal("old", symbol.Geometry!.Primitives[0].Text);
        Assert.Equal("", SchematicSymbolPresentation.CableCaption(symbol, owner));
        cable.ProvidedLengthMm = null;
        Assert.Equal("長度待填", SchematicSymbolPresentation.GeometryForOwner(symbol, owner)!.Primitives[0].Text);
        var withoutFields = symbol with { Geometry = symbol.Geometry with { Primitives = [] } };
        Assert.Contains("長度待填", SchematicSymbolPresentation.CableCaption(withoutFields, owner));
        Assert.Contains("AWG 22", SchematicSymbolPresentation.CableCaption(withoutFields, owner));
    }

    private static SchematicSymbol Symbol() => new()
    {
        SymbolId = "s", ComponentInstanceId = "c", PageId = "p",
        Geometry = new() { SourceSha256 = new string('A', 64), Width = 30, Height = 20,
            Primitives = [new() { Kind = "TEXT", Start = new(1, 1), Text = "LSA", AttributeTag = "TAG1" },
                new() { Kind = "TEXT", Start = new(1, 5), Text = "LSA" }] }
    };
}
