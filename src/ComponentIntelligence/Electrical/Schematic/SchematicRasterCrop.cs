namespace ComponentIntelligence.Electrical.Schematic;

public static class SchematicRasterCrop
{
    public static (int X, int Y, int Width, int Height) FindContentBounds(
        ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0 || stride < width * 4 || bgra.Length < stride * height)
            throw new ArgumentException("Invalid BGRA bitmap dimensions.");
        var left = width;
        var top = height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = y * stride + x * 4;
            if (bgra[offset + 3] < 16 ||
                bgra[offset] >= 245 && bgra[offset + 1] >= 245 && bgra[offset + 2] >= 245)
                continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
        if (right < left) return (0, 0, width, height);
        left = Math.Max(0, left - 2);
        top = Math.Max(0, top - 2);
        right = Math.Min(width - 1, right + 2);
        bottom = Math.Min(height - 1, bottom + 2);
        return (left, top, right - left + 1, bottom - top + 1);
    }
}
