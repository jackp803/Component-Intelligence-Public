namespace ComponentIntelligence.Electrical.Schematic;

public static class SchematicTemplateGrid
{
    public static SchematicGridBounds? TryDetect(SchematicCadAsset asset, double pageWidth,
        double pageHeight, int columns, int rows)
    {
        if (columns is < 2 or > 9 || rows is < 2 or > 26) return null;
        var text = asset.Primitives.Where(p => p.Kind is "TEXT" or "MTEXT" &&
            p.Text is not null && double.IsFinite(p.Start.X) && double.IsFinite(p.Start.Y)).ToArray();
        SchematicCadPrimitive? Find(string label, bool minimum, bool horizontal) =>
            minimum
                ? text.Where(p => p.Text!.Trim() == label).MinBy(p => horizontal ? p.Start.Y : p.Start.X)
                : text.Where(p => p.Text!.Trim() == label).MaxBy(p => horizontal ? p.Start.Y : p.Start.X);

        var top = Enumerable.Range(1, columns).Select(i => Find(i.ToString(), true, true)).ToArray();
        var bottom = Enumerable.Range(1, columns).Select(i => Find(i.ToString(), false, true)).ToArray();
        var left = Enumerable.Range(0, rows).Select(i => Find(((char)('A' + i)).ToString(), true, false)).ToArray();
        var right = Enumerable.Range(0, rows).Select(i => Find(((char)('A' + i)).ToString(), false, false)).ToArray();
        if (top.Any(p => p is null) || bottom.Any(p => p is null) || left.Any(p => p is null) || right.Any(p => p is null)) return null;
        var x = top.Select(p => p!.Start.X).ToArray();
        var y = left.Select(p => p!.Start.Y).ToArray();
        var dx = (x[^1] - x[0]) / (columns - 1);
        var dy = (y[^1] - y[0]) / (rows - 1);
        if (dx <= 4 || dy <= 4) return null;
        var xtol = Math.Max(1, dx * .025);
        var ytol = Math.Max(1, dy * .025);
        for (var i = 0; i < columns; i++)
        {
            if (Math.Abs(x[i] - (x[0] + i * dx)) > xtol ||
                Math.Abs(bottom[i]!.Start.X - x[i]) > xtol ||
                Math.Abs(top[i]!.Start.Y - top[0]!.Start.Y) > ytol ||
                Math.Abs(bottom[i]!.Start.Y - bottom[0]!.Start.Y) > ytol) return null;
        }
        for (var i = 0; i < rows; i++)
        {
            if (Math.Abs(y[i] - (y[0] + i * dy)) > ytol ||
                Math.Abs(right[i]!.Start.Y - y[i]) > ytol ||
                Math.Abs(left[i]!.Start.X - left[0]!.Start.X) > xtol ||
                Math.Abs(right[i]!.Start.X - right[0]!.Start.X) > xtol) return null;
        }
        if (bottom[0]!.Start.Y - top[0]!.Start.Y < 2 * dy ||
            right[0]!.Start.X - left[0]!.Start.X < 2 * dx) return null;
        var bounds = new SchematicGridBounds(x[0] - dx / 2, y[0] - dy / 2, columns * dx, rows * dy);
        return bounds.X >= -xtol && bounds.Y >= -ytol &&
            bounds.X + bounds.Width <= pageWidth + xtol && bounds.Y + bounds.Height <= pageHeight + ytol
            ? bounds : null;
    }
}
