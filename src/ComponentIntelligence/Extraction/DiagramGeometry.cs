namespace ComponentIntelligence.Extraction;

public enum DiagramGeometrySource
{
    PdfVector,
    RasterVision
}

/// <summary>
/// Normalized diagram coordinate. X/Y are in [0,1] with origin at the visual top-left of the page.
/// Keeping one coordinate convention lets native PDF vectors and any optional raster-vision result
/// be compared without leaking PDF bottom-left coordinates or pixel dimensions downstream.
/// </summary>
public readonly record struct DiagramPoint(double X, double Y);

public sealed record DiagramLineSegment(
    int PageNumber,
    DiagramPoint From,
    DiagramPoint To,
    double Thickness,
    DiagramGeometrySource Source,
    double Confidence)
{
    public double Length => Math.Sqrt(
        Math.Pow(To.X - From.X, 2) +
        Math.Pow(To.Y - From.Y, 2));
}

public sealed record DiagramRectangle(
    int PageNumber,
    double Left,
    double Top,
    double Right,
    double Bottom,
    DiagramGeometrySource Source,
    double Confidence)
{
    public double Width => Math.Max(0, Right - Left);
    public double Height => Math.Max(0, Bottom - Top);
}

public sealed record DiagramCircle(
    int PageNumber,
    DiagramPoint Center,
    double Radius,
    DiagramGeometrySource Source,
    double Confidence);

public sealed record DiagramJunction(
    int PageNumber,
    DiagramPoint Point,
    int Degree,
    DiagramGeometrySource Source,
    double Confidence);

public sealed record DiagramGeometryPage(
    int PageNumber,
    DiagramGeometrySource Source,
    IReadOnlyList<DiagramLineSegment> Lines,
    IReadOnlyList<DiagramRectangle> Rectangles,
    IReadOnlyList<DiagramCircle> Circles,
    IReadOnlyList<DiagramJunction> Junctions,
    IReadOnlyList<string> Diagnostics)
{
    public int PrimitiveCount => Lines.Count + Rectangles.Count + Circles.Count + Junctions.Count;
}

public sealed record DiagramGeometryResult(
    IReadOnlyList<DiagramGeometryPage> Pages,
    IReadOnlyList<string> Diagnostics)
{
    public bool HasGeometry => Pages.Any(page => page.PrimitiveCount > 0);

    public static DiagramGeometryResult Empty(params string[] diagnostics) =>
        new(Array.Empty<DiagramGeometryPage>(), diagnostics);
}

internal static class DiagramGeometryMath
{
    public static IReadOnlyList<DiagramJunction> InferJunctions(
        int pageNumber,
        IReadOnlyList<DiagramLineSegment> lines,
        DiagramGeometrySource source,
        double tolerance = 0.004)
    {
        if (lines.Count < 3) return Array.Empty<DiagramJunction>();

        var candidates = new List<DiagramPoint>();
        for (var i = 0; i < lines.Count; i++)
        {
            for (var j = i + 1; j < lines.Count; j++)
            {
                if (TryIntersect(lines[i], lines[j], tolerance, out var point))
                    candidates.Add(point);
            }
        }

        if (candidates.Count == 0) return Array.Empty<DiagramJunction>();

        var clusters = new List<List<DiagramPoint>>();
        foreach (var point in candidates)
        {
            var cluster = clusters.FirstOrDefault(existing => Distance(Centroid(existing), point) <= tolerance);
            if (cluster is null)
            {
                cluster = [];
                clusters.Add(cluster);
            }
            cluster.Add(point);
        }

        var junctions = new List<DiagramJunction>();
        foreach (var cluster in clusters)
        {
            var center = Centroid(cluster);
            var degree = lines.Count(line => DistanceToSegment(center, line.From, line.To) <= tolerance);
            if (degree < 3) continue;
            junctions.Add(new DiagramJunction(
                pageNumber,
                center,
                degree,
                source,
                Math.Clamp(0.55 + degree * 0.08, 0, 0.95)));
        }

        return junctions
            .OrderBy(junction => junction.Point.Y)
            .ThenBy(junction => junction.Point.X)
            .ToArray();
    }

    private static bool TryIntersect(
        DiagramLineSegment first,
        DiagramLineSegment second,
        double tolerance,
        out DiagramPoint point)
    {
        var x1 = first.From.X;
        var y1 = first.From.Y;
        var x2 = first.To.X;
        var y2 = first.To.Y;
        var x3 = second.From.X;
        var y3 = second.From.Y;
        var x4 = second.To.X;
        var y4 = second.To.Y;

        var denominator = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
        if (Math.Abs(denominator) <= 1e-9)
        {
            foreach (var candidate in new[] { first.From, first.To })
            {
                if (DistanceToSegment(candidate, second.From, second.To) <= tolerance)
                {
                    point = candidate;
                    return true;
                }
            }
            foreach (var candidate in new[] { second.From, second.To })
            {
                if (DistanceToSegment(candidate, first.From, first.To) <= tolerance)
                {
                    point = candidate;
                    return true;
                }
            }
            point = default;
            return false;
        }

        var determinant1 = x1 * y2 - y1 * x2;
        var determinant2 = x3 * y4 - y3 * x4;
        var x = (determinant1 * (x3 - x4) - (x1 - x2) * determinant2) / denominator;
        var y = (determinant1 * (y3 - y4) - (y1 - y2) * determinant2) / denominator;
        point = new DiagramPoint(x, y);
        return IsWithin(point, first.From, first.To, tolerance) &&
               IsWithin(point, second.From, second.To, tolerance);
    }

    private static bool IsWithin(DiagramPoint point, DiagramPoint from, DiagramPoint to, double tolerance) =>
        point.X >= Math.Min(from.X, to.X) - tolerance &&
        point.X <= Math.Max(from.X, to.X) + tolerance &&
        point.Y >= Math.Min(from.Y, to.Y) - tolerance &&
        point.Y <= Math.Max(from.Y, to.Y) + tolerance;

    private static double DistanceToSegment(DiagramPoint point, DiagramPoint from, DiagramPoint to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (Math.Abs(dx) < 1e-12 && Math.Abs(dy) < 1e-12) return Distance(point, from);
        var t = ((point.X - from.X) * dx + (point.Y - from.Y) * dy) / (dx * dx + dy * dy);
        t = Math.Clamp(t, 0, 1);
        return Distance(point, new DiagramPoint(from.X + t * dx, from.Y + t * dy));
    }

    private static DiagramPoint Centroid(IReadOnlyList<DiagramPoint> points) =>
        new(points.Average(point => point.X), points.Average(point => point.Y));

    private static double Distance(DiagramPoint a, DiagramPoint b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
