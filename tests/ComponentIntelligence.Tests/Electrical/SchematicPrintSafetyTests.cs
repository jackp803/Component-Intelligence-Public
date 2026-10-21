using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicPrintSafetyTests
{
    [Fact]
    public void WhitePaperMarginsDoNotPreventActualSizePrinting()
    {
        var pixels = Enumerable.Repeat((byte)255, 10 * 10 * 4).ToArray();
        pixels[4 * (5 * 10 + 5)] = 0;
        Assert.False(SchematicPrintSafety.HasInkOutside(pixels, 10, 10, 40, 2, 2, 8, 8));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(8, 5)]
    [InlineData(5, 1)]
    [InlineData(5, 8)]
    public void ContentInAnyUnprintableMarginBlocksRatherThanClips(int x, int y)
    {
        var pixels = Enumerable.Repeat((byte)255, 10 * 10 * 4).ToArray();
        pixels[4 * (y * 10 + x)] = 254;
        Assert.True(SchematicPrintSafety.HasInkOutside(pixels, 10, 10, 40, 2, 2, 8, 8));
    }

    [Fact]
    public void TransparentPixelsAreWhiteOnPaperAndStridePaddingIsNotContent()
    {
        var pixels = new byte[48 * 10];
        Assert.False(SchematicPrintSafety.HasInkOutside(pixels, 10, 10, 48, 2, 2, 8, 8));
    }

    [Fact]
    public void MissingOrInvalidPrintableAreaFailsClosed()
    {
        Assert.Throws<ArgumentException>(() => SchematicPrintSafety.HasInkOutside(new byte[4], 1, 1, 4, 0, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => SchematicPrintSafety.HasInkOutside(new byte[3], 1, 1, 4, 0, 0, 1, 1));
    }
}
