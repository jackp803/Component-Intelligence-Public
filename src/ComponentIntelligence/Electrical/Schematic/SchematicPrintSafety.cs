namespace ComponentIntelligence.Electrical.Schematic;

public static class SchematicPrintSafety
{
    // Exclusive right/bottom bounds, in raster pixels. Pbgra32 is composited on white paper.
    public static bool HasInkOutside(byte[] pixels, int width, int height, int stride,
        int left, int top, int right, int bottom)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || stride < (long)width * 4 ||
            pixels.LongLength < (long)stride * height || left < 0 || top < 0 ||
            right <= left || bottom <= top)
            throw new ArgumentException("Invalid raster or printable area.");
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (x >= left && x < right && y >= top && y < bottom) continue;
            var index = y * stride + x * 4;
            var alpha = pixels[index + 3];
            if (pixels[index] < alpha || pixels[index + 1] < alpha || pixels[index + 2] < alpha)
                return true;
        }
        return false;
    }
}
