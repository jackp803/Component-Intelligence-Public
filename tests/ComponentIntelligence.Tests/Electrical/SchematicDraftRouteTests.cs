using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicDraftRouteTests
{
    [Fact]
    public void OffGridAnchorAndSnappedBendDoNotLeaveRetracedSpur()
    {
        SchematicPoint[] start = [new(116.75, 74)];
        var bend = SchematicDraftRoute.Append(start, new(90, 72.5));
        var final = SchematicDraftRoute.Append(bend, new(90, 107.5));
        Assert.Equal(new SchematicPoint[] { new(116.75, 74), new(90, 74), new(90, 107.5) }, final);
        Assert.Single(start);
        Assert.Equal(new SchematicPoint(90, 72.5), bend[^1]);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(-1, -1)]
    public void RetracedHorizontalTailIsRemovedInEveryDirection(int x, int y)
    {
        SchematicPoint[] source = [new(0, 0), new(0, y * 10), new(x * 2, y * 10)];
        Assert.Equal(new SchematicPoint[] { new(0, 0), new(0, y * 10), new(-x * 10, y * 10) },
            SchematicDraftRoute.Append(source, new(-x * 10, y * 10)));
    }

    [Fact]
    public void RealDetourAndExactEndAreRetainedWithoutChangingInput()
    {
        SchematicPoint[] source = [new(0, 0), new(20, 0), new(20, 10), new(30, 10)];
        var route = SchematicDraftRoute.Append(source, new(30, 20.75));
        Assert.Equal(source.Append(new SchematicPoint(30, 20.75)), route);
        Assert.Equal(route, SchematicDraftRoute.Append(route, route[^1]));
        Assert.Equal(4, source.Length);
    }
}
