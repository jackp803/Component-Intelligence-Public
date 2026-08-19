using ComponentIntelligence.Electrical.Topology;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class TopologyCanvasBoundsPolicyTests
{
    [Fact]
    public void Calculate_GrowsRightAndBottomWithoutMovingExistingContent()
    {
        var result = TopologyCanvasBoundsPolicy.Calculate(3200, 2000, 3000, 1800, 3190, 1990);

        Assert.Equal(0, result.ShiftX);
        Assert.Equal(0, result.ShiftY);
        Assert.Equal(4000, result.Width);
        Assert.Equal(2800, result.Height);
    }

    [Fact]
    public void Calculate_InsertsSpaceAtLeftAndTopAndKeepsTrailingExtent()
    {
        var result = TopologyCanvasBoundsPolicy.Calculate(3200, 2000, 20, -25, 180, 120);

        Assert.Equal(800, result.ShiftX);
        Assert.Equal(800, result.ShiftY);
        Assert.Equal(4000, result.Width);
        Assert.Equal(2800, result.Height);
    }

    [Fact]
    public void Calculate_UsesMultipleChunksForLargePointerJumpBeyondBoundary()
    {
        var result = TopologyCanvasBoundsPolicy.Calculate(3200, 2000, -1700, -900, -1500, -700);

        Assert.Equal(2400, result.ShiftX);
        Assert.Equal(1600, result.ShiftY);
        Assert.True(result.Width >= 5600);
        Assert.True(result.Height >= 3600);
    }

    [Fact]
    public void Calculate_DoesNothingWhenBoundsHaveSafeMargin()
    {
        var result = TopologyCanvasBoundsPolicy.Calculate(3200, 2000, 200, 200, 2500, 1500);

        Assert.Equal(new TopologyCanvasExpansion(0, 0, 3200, 2000), result);
    }
}
