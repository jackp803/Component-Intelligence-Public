using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicCrossingTests
{
    [Fact]
    public void UnconnectedCrossingHasCrossoverAndRecomputesAfterMovement()
    {
        var a = Wire("A", new(0, 20), new(60, 20)); var b = Wire("B", new(30, 0), new(30, 40));
        var result = SchematicCrossingService.Analyze([a, b]);
        var crossing = Assert.Single(result.Crossovers);
        Assert.Equal(new SchematicPoint(30, 20), crossing.Position);
        Assert.Equal("A", crossing.HorizontalWireId);
        Assert.Empty(result.Junctions);
        Assert.Empty(SchematicCrossingService.Analyze([a, b with { Points = [new(70, 0), new(70, 40)] }]).Crossovers);
    }

    [Fact]
    public void CoincidentUnrelatedEndsAreNotJunctionsAndOverlapIsReported()
    {
        var a = Wire("A", new(0, 20), new(60, 20)); var b = Wire("B", new(60, 20), new(60, 40));
        Assert.Empty(SchematicCrossingService.Analyze([a, b]).Junctions);
        Assert.NotEmpty(SchematicCrossingService.Analyze([a, b]).Conflicts);
        b = b with { Points = [new(20, 20), new(80, 20)] };
        Assert.Contains(SchematicCrossingService.Analyze([a, b]).Conflicts, c => c.Code == "COLLINEAR_OVERLAP");
    }

    [Fact]
    public void SharedExplicitPinCanHaveContactDotButSameTextCannot()
    {
        var a = Wire("A", new(0, 20), new(60, 20)) with { Start = SchematicAttachment.Pin("S", "P") };
        var b = Wire("B", new(0, 20), new(0, 40)) with { Start = SchematicAttachment.Pin("S", "P") };
        Assert.Single(SchematicCrossingService.Analyze([a, b]).Junctions);
        Assert.Empty(SchematicCrossingService.Analyze([a, b with { Start = SchematicAttachment.Pin("OTHER", "OTHER-P") }]).Junctions);
    }

    private static SchematicWire Wire(string id, SchematicPoint a, SchematicPoint b) => new() { WireId = id, PageId = "PAGE", Points = [a, b] };
}
