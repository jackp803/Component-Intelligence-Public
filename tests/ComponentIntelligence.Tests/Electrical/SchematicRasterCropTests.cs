using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicRasterCropTests
{
    [Fact]
    public void CropsWhiteMarginsWithoutChangingTheSourcePixels()
    {
        const int width = 20, height = 20, stride = width * 4;
        var pixels = new byte[stride * height];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 255;
        }
        for (var y = 4; y <= 15; y++)
        for (var x = 8; x <= 11; x++) pixels[y * stride + x * 4] = 0;
        var before = pixels.ToArray();

        Assert.Equal((6, 2, 8, 16), SchematicRasterCrop.FindContentBounds(pixels, width, height, stride));
        Assert.Equal(before, pixels);
    }

    [Fact]
    public void PureWhiteOrTransparentImageKeepsItsFullBounds()
    {
        var white = Enumerable.Repeat((byte)255, 10 * 10 * 4).ToArray();
        var transparent = new byte[white.Length];
        Assert.Equal((0, 0, 10, 10), SchematicRasterCrop.FindContentBounds(white, 10, 10, 40));
        Assert.Equal((0, 0, 10, 10), SchematicRasterCrop.FindContentBounds(transparent, 10, 10, 40));
    }
}
