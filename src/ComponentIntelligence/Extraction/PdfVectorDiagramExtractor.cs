using UglyToad.PdfPig;
using UglyToad.PdfPig.Geometry;

namespace ComponentIntelligence.Extraction;

/// <summary>
/// Non-AI vector diagram scanner. It reads the PDF content stream through PdfPig rather than
/// rasterizing first. Path bounding boxes are intentionally treated as geometry candidates only;
/// downstream code must not turn them directly into electrical facts without text/evidence support.
/// </summary>
public sealed class PdfVectorDiagramExtractor
{
    private readonly int _maxPages;

    public PdfVectorDiagramExtractor(int maxPages = 80)
    {
        _maxPages = Math.Clamp(maxPages, 1, 200);
    }

    public DiagramGeometryResult Extract(string pdfPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        var pages = new List<DiagramGeometryPage>();
        var diagnostics = new List<string>();

        try
        {
            using var document = PdfDocument.Open(pdfPath, new ParsingOptions { ClipPaths = true });
            foreach (var page in document.GetPages().Take(_maxPages))
            {
                var pageWidth = Math.Abs(page.Width);
                var pageHeight = Math.Abs(page.Height);
                if (pageWidth <= 1e-6 || pageHeight <= 1e-6)
                {
                    diagnostics.Add($"PDF_VECTOR_INVALID_PAGE_SIZE:{page.Number}");
                    continue;
                }

                var lines = new List<DiagramLineSegment>();
                var rectangles = new List<DiagramRectangle>();
                var paths = page.Paths;
                var acceptedBounds = 0;

                foreach (var path in paths)
                {
                    var optionalBounds = path.GetBoundingRectangle();
                    if (!optionalBounds.HasValue) continue;
                    var bounds = optionalBounds.Value;
                    var left = Math.Min(bounds.Left, bounds.Right);
                    var right = Math.Max(bounds.Left, bounds.Right);
                    var bottom = Math.Min(bounds.Bottom, bounds.Top);
                    var top = Math.Max(bounds.Bottom, bounds.Top);
                    var width = right - left;
                    var height = top - bottom;
                    if (width < 0 || height < 0) continue;

                    // Ignore page frames/background fills. They are layout decoration, not wiring geometry.
                    var areaRatio = (width * height) / (pageWidth * pageHeight);
                    if (areaRatio >= 0.85) continue;

                    acceptedBounds++;
                    var normalizedLeft = Clamp01(left / pageWidth);
                    var normalizedRight = Clamp01(right / pageWidth);
                    var normalizedTop = Clamp01(1d - top / pageHeight);
                    var normalizedBottom = Clamp01(1d - bottom / pageHeight);

                    var maxThinDimension = Math.Max(4d, Math.Min(pageWidth, pageHeight) * 0.006d);
                    var minLineLength = Math.Max(7d, Math.Min(pageWidth, pageHeight) * 0.012d);
                    var horizontal = height <= maxThinDimension && width >= minLineLength;
                    var vertical = width <= maxThinDimension && height >= minLineLength;

                    if (horizontal || vertical)
                    {
                        if (horizontal)
                        {
                            var y = (normalizedTop + normalizedBottom) / 2d;
                            lines.Add(new DiagramLineSegment(
                                page.Number,
                                new DiagramPoint(normalizedLeft, y),
                                new DiagramPoint(normalizedRight, y),
                                pageHeight <= 0 ? 0 : height / pageHeight,
                                DiagramGeometrySource.PdfVector,
                                height <= 1.5 ? 0.94 : 0.84));
                        }
                        if (vertical)
                        {
                            var x = (normalizedLeft + normalizedRight) / 2d;
                            lines.Add(new DiagramLineSegment(
                                page.Number,
                                new DiagramPoint(x, normalizedTop),
                                new DiagramPoint(x, normalizedBottom),
                                pageWidth <= 0 ? 0 : width / pageWidth,
                                DiagramGeometrySource.PdfVector,
                                width <= 1.5 ? 0.94 : 0.84));
                        }
                        continue;
                    }

                    // A non-thin path with a useful bounding box is retained only as a rectangle candidate.
                    // Curved PDF paths may also produce a box, so confidence deliberately stays below 0.7.
                    if (width >= minLineLength && height >= minLineLength && areaRatio <= 0.35)
                    {
                        var aspect = width / Math.Max(height, 1e-9);
                        if (aspect is >= 0.12 and <= 8.0)
                        {
                            rectangles.Add(new DiagramRectangle(
                                page.Number,
                                normalizedLeft,
                                normalizedTop,
                                normalizedRight,
                                normalizedBottom,
                                DiagramGeometrySource.PdfVector,
                                0.62));
                        }
                    }
                }

                lines = DeduplicateLines(lines).ToList();
                rectangles = DeduplicateRectangles(rectangles).ToList();
                var junctions = DiagramGeometryMath.InferJunctions(
                    page.Number,
                    lines,
                    DiagramGeometrySource.PdfVector);

                var pageDiagnostics = new[]
                {
                    $"PDF_VECTOR_PATHS:{paths.Count}",
                    $"PDF_VECTOR_BOUNDS_ACCEPTED:{acceptedBounds}",
                    $"PDF_VECTOR_LINES:{lines.Count}",
                    $"PDF_VECTOR_RECTANGLE_CANDIDATES:{rectangles.Count}",
                    $"PDF_VECTOR_JUNCTIONS:{junctions.Count}"
                };
                pages.Add(new DiagramGeometryPage(
                    page.Number,
                    DiagramGeometrySource.PdfVector,
                    lines,
                    rectangles,
                    Array.Empty<DiagramCircle>(),
                    junctions,
                    pageDiagnostics));
            }
        }
        catch (Exception exception)
        {
            diagnostics.Add($"PDF_VECTOR_EXTRACTION_FAILED:{exception.GetType().Name}:{exception.Message}");
        }

        diagnostics.Add($"PDF_VECTOR_PAGES:{pages.Count}");
        diagnostics.Add($"PDF_VECTOR_PRIMITIVES:{pages.Sum(page => page.PrimitiveCount)}");
        return new DiagramGeometryResult(pages, diagnostics);
    }

    private static IEnumerable<DiagramLineSegment> DeduplicateLines(IEnumerable<DiagramLineSegment> source)
    {
        const double bucket = 0.0025;
        return source
            .Where(line => line.Length >= 0.008)
            .GroupBy(line => string.Join('|',
                Quantize(line.From.X, bucket),
                Quantize(line.From.Y, bucket),
                Quantize(line.To.X, bucket),
                Quantize(line.To.Y, bucket)))
            .Select(group => group.OrderByDescending(line => line.Confidence).First());
    }

    private static IEnumerable<DiagramRectangle> DeduplicateRectangles(IEnumerable<DiagramRectangle> source)
    {
        const double bucket = 0.004;
        return source
            .GroupBy(rectangle => string.Join('|',
                Quantize(rectangle.Left, bucket),
                Quantize(rectangle.Top, bucket),
                Quantize(rectangle.Right, bucket),
                Quantize(rectangle.Bottom, bucket)))
            .Select(group => group.OrderByDescending(rectangle => rectangle.Confidence).First());
    }

    private static long Quantize(double value, double bucket) =>
        (long)Math.Round(value / bucket, MidpointRounding.AwayFromZero);

    private static double Clamp01(double value) => Math.Clamp(value, 0d, 1d);
}
