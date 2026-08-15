namespace ComponentIntelligence.Extraction;

/// <summary>
/// Merges native PDF-vector geometry with an optional raster-vision provider. Native PDF primitives
/// win when both channels describe the same shape. The core document pipeline currently uses only
/// native PDF vectors; RasterVision is reserved for optional local model providers.
/// </summary>
public sealed class DiagramGeometryReconciler
{
    public DiagramGeometryResult Reconcile(params DiagramGeometryResult[] inputs)
    {
        var results = inputs.Where(input => input is not null).ToArray();
        if (results.Length == 0) return DiagramGeometryResult.Empty("DIAGRAM_GEOMETRY_NO_INPUT");

        var pages = new List<DiagramGeometryPage>();
        foreach (var pageNumber in results.SelectMany(result => result.Pages).Select(page => page.PageNumber).Distinct().Order())
        {
            var candidates = results.SelectMany(result => result.Pages).Where(page => page.PageNumber == pageNumber).ToArray();
            var vectorPages = candidates.Where(page => page.Source == DiagramGeometrySource.PdfVector).ToArray();
            var rasterPages = candidates.Where(page => page.Source == DiagramGeometrySource.RasterVision).ToArray();

            var vectorLines = vectorPages.SelectMany(page => page.Lines).ToList();
            var lines = vectorLines
                .Concat(rasterPages.SelectMany(page => page.Lines).Where(line => !vectorLines.Any(native => SameLine(native, line))))
                .ToArray();

            var vectorRectangles = vectorPages.SelectMany(page => page.Rectangles).ToList();
            var rectangles = vectorRectangles
                .Concat(rasterPages.SelectMany(page => page.Rectangles).Where(rectangle => !vectorRectangles.Any(native => SameRectangle(native, rectangle))))
                .ToArray();

            var circles = rasterPages.SelectMany(page => page.Circles).ToArray();
            var junctions = candidates
                .SelectMany(page => page.Junctions)
                .GroupBy(junction => $"{Bucket(junction.Point.X, 0.008)}|{Bucket(junction.Point.Y, 0.008)}")
                .Select(group => group
                    .OrderByDescending(junction => junction.Source == DiagramGeometrySource.PdfVector)
                    .ThenByDescending(junction => junction.Degree)
                    .ThenByDescending(junction => junction.Confidence)
                    .First())
                .ToArray();

            var source = vectorPages.Length > 0 ? DiagramGeometrySource.PdfVector : DiagramGeometrySource.RasterVision;
            pages.Add(new DiagramGeometryPage(
                pageNumber,
                source,
                lines,
                rectangles,
                circles,
                junctions,
                candidates.SelectMany(page => page.Diagnostics).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
        }

        var diagnostics = results
            .SelectMany(result => result.Diagnostics)
            .Concat([
                $"DIAGRAM_GEOMETRY_RECONCILED_PAGES:{pages.Count}",
                $"DIAGRAM_GEOMETRY_RECONCILED_PRIMITIVES:{pages.Sum(page => page.PrimitiveCount)}"
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new DiagramGeometryResult(pages, diagnostics);
    }

    private static bool SameLine(DiagramLineSegment first, DiagramLineSegment second)
    {
        var firstCanonical = Canonicalize(first);
        var secondCanonical = Canonicalize(second);
        return Distance(firstCanonical.From, secondCanonical.From) <= 0.015 &&
               Distance(firstCanonical.To, secondCanonical.To) <= 0.015;
    }

    private static bool SameRectangle(DiagramRectangle first, DiagramRectangle second) =>
        Math.Abs(first.Left - second.Left) <= 0.015 &&
        Math.Abs(first.Top - second.Top) <= 0.015 &&
        Math.Abs(first.Right - second.Right) <= 0.015 &&
        Math.Abs(first.Bottom - second.Bottom) <= 0.015;

    private static DiagramLineSegment Canonicalize(DiagramLineSegment line)
    {
        var reverse = line.From.X > line.To.X ||
                      Math.Abs(line.From.X - line.To.X) < 1e-9 && line.From.Y > line.To.Y;
        return reverse ? line with { From = line.To, To = line.From } : line;
    }

    private static long Bucket(double value, double size) =>
        (long)Math.Round(value / size, MidpointRounding.AwayFromZero);

    private static double Distance(DiagramPoint first, DiagramPoint second) =>
        Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));
}
