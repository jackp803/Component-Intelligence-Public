using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicViewportMathTests
{
    [Theory]
    [InlineData(100, 60, 0.5, 1, 260)]
    [InlineData(260, 60, 1, 0.5, 100)]
    public void ZoomKeepsThePointUnderThePointer(double oldOffset, double pointer,
        double oldZoom, double newZoom, double expected)
    {
        Assert.Equal(expected, SchematicViewportMath.OffsetAfterZoom(oldOffset, pointer, oldZoom, newZoom));
    }

    [Fact]
    public void MiddlePanChangesViewOnly()
    {
        Assert.Equal(70, SchematicViewportMath.OffsetAfterPan(100, 40, 70));
    }
}
