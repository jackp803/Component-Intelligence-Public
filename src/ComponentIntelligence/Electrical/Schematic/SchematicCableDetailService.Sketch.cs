namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicCableDetailService
{
    private static IReadOnlyList<SchematicCadPrimitive> FitSketch(IReadOnlyList<SchematicCadPrimitive> source,
        SchematicCableDetailBinding binding)
    {
        if (source.Count == 0) return [];
        var origin = source[0].Start;
        SchematicPoint Relative(SchematicPoint p) => new(p.X - origin.X, p.Y - origin.Y);
        // Compute local extrema before adding radii/glyph sizes; large world coordinates can erase small offsets.
        var local = source.Select(p => p with { Start = Relative(p.Start), End = p.End is null ? null : Relative(p.End),
            Contours = p.Contours.Select(c => (IReadOnlyList<SchematicPoint>)c.Select(Relative).ToArray()).ToArray() }).ToArray();
        var points = local.SelectMany(SketchBoundsPoints).ToArray();
        if (points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) throw InvalidSketch();
        var x = points.Min(p => p.X); var y = points.Min(p => p.Y);
        var width = Math.Max(.1, points.Max(p => p.X) - x);
        var height = Math.Max(.1, points.Max(p => p.Y) - y);
        // Fit rendered content, not legacy insertion-point bounds. Leave room for the fixed preview stroke.
        var margin = Math.Min(.5, Math.Min(binding.SketchWidth, binding.SketchHeight) / 4);
        var scale = Math.Min((binding.SketchWidth - 2 * margin) / width, (binding.SketchHeight - 2 * margin) / height);
        if (!double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(scale) || scale <= 0) throw InvalidSketch();
        SchematicPoint Point(SchematicPoint p) => new(binding.SketchPosition.X + margin + (p.X - x) * scale,
            binding.SketchPosition.Y + margin + (p.Y - y) * scale);
        return local.Select(p => p with
        {
            Start = Point(p.Start),
            End = p.End is null ? null : Point(p.End),
            Radius = p.Radius * scale,
            TextHeight = p.TextHeight * scale,
            TextWidth = p.TextWidth * scale,
            Contours = p.Contours.Select(c => (IReadOnlyList<SchematicPoint>)c.Select(Point).ToArray()).ToArray()
        }).ToArray();
    }

    private static IEnumerable<SchematicPoint> SketchBoundsPoints(SchematicCadPrimitive p)
    {
        yield return p.Start;
        if (p.End is not null) yield return p.End;
        foreach (var point in p.Contours.SelectMany(c => c)) yield return point;
        if (p.Kind is "CIRCLE" or "ARC")
        {
            if (!double.IsFinite(p.Radius) || p.Radius < 0 || !double.IsFinite(p.StartAngle) || !double.IsFinite(p.EndAngle))
                throw InvalidSketch();
            // Match the importer's conservative curved-extrema envelope.
            yield return new(p.Start.X - p.Radius, p.Start.Y - p.Radius);
            yield return new(p.Start.X + p.Radius, p.Start.Y + p.Radius);
        }
        if (p.Kind is not ("TEXT" or "MTEXT")) yield break;
        if (!double.IsFinite(p.TextHeight) || p.TextHeight < 0 || !double.IsFinite(p.TextWidthFactor) || p.TextWidthFactor <= 0 ||
            !double.IsFinite(p.TextWidth) || p.TextWidth < 0 || !double.IsFinite(p.Rotation)) throw InvalidSketch();
        if (string.IsNullOrEmpty(p.Text)) yield break;
        if (p.TextHeight == 0) throw InvalidSketch();

        // A conservative font-independent envelope retains complete labels; WPF smoke tests check actual ink.
        var lines = (p.DisplayText ?? "").Replace("\r", "").Split('\n');
        var glyphWidth = p.TextHeight * (p.Kind == "TEXT" ? p.TextWidthFactor : 1);
        if (!double.IsFinite(glyphWidth) || glyphWidth <= 0) throw InvalidSketch();
        var textWidth = lines.Max(l => l.Length) * glyphWidth;
        var anchorWidth = textWidth;
        double lineCount = lines.Length;
        if (p.Kind == "MTEXT" && p.TextWidth > 0)
        {
            anchorWidth = p.TextWidth;
            // WPF may draw one unbreakable glyph beyond an exceptionally narrow wrap box.
            textWidth = Math.Max(p.TextWidth, glyphWidth);
            var capacity = Math.Max(1, Math.Floor(p.TextWidth / glyphWidth));
            lineCount = lines.Sum(l => SketchWrappedLines(l, capacity));
        }
        var textHeight = p.TextHeight * 1.5 * lineCount;
        var text = p.Kind == "MTEXT" && p.TextAttachment is null ? p with { TextAttachment = "TopLeft" } : p;
        // WPF's multiline TEXT baseline is on the last line, not the first.
        var baseline = p.Kind == "TEXT" ? textHeight - .5 * p.TextHeight : 0;
        var offset = text.TextAnchorOffset(anchorWidth, textHeight, baseline);
        var angle = p.Rotation * Math.PI / 180;
        foreach (var corner in new[] { new SchematicPoint(offset.X, offset.Y), new(offset.X + textWidth, offset.Y),
            new(offset.X, offset.Y + textHeight), new(offset.X + textWidth, offset.Y + textHeight) })
            yield return new(p.Start.X + corner.X * Math.Cos(angle) - corner.Y * Math.Sin(angle),
                p.Start.Y + corner.X * Math.Sin(angle) + corner.Y * Math.Cos(angle));
    }

    private static double SketchWrappedLines(string line, double capacity)
    {
        double count = 1, used = 0;
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(line, @"\S+|\s+"))
        {
            var token = match.Value.Replace("\t", "    ");
            if (string.IsNullOrWhiteSpace(token)) { used += token.Length; continue; }
            // Preserve word boundaries, then conservatively break overlong words by glyph capacity.
            if (used > 0 && used + token.Length > capacity) { count++; used = 0; }
            var lines = Math.Max(1, Math.Ceiling(token.Length / capacity));
            count += lines - 1;
            used += token.Length - (lines - 1) * capacity;
        }
        return count;
    }

    private static InvalidOperationException InvalidSketch() => new("製作示意 CAD 含無效座標或文字尺寸，請檢查來源圖塊。");
}
