using System.Text.RegularExpressions;

namespace ComponentIntelligence.Extraction;

public enum DiagramAnchorKind
{
    Junction,
    Circle,
    Rectangle,
    Line
}

public sealed record DiagramLabelMatch(
    int PageNumber,
    string Text,
    OcrTextBox TextBox,
    DiagramAnchorKind AnchorKind,
    DiagramPoint AnchorPoint,
    DiagramGeometrySource GeometrySource,
    double Distance,
    double Confidence);

/// <summary>
/// Deterministic OCR-label to geometry matching. It deliberately does not assign electrical meaning:
/// "1" near a connector circle is only a label/anchor candidate until Pin/Port evidence confirms it.
/// </summary>
public sealed class DiagramTextGeometryMatcher
{
    private static readonly Regex PinLike = new(
        @"^(?:pin\s*)?(?:\d{1,3}|[A-Z]\d{0,2}|A\+|B-|L\+|L-|C/Q|PE|FE|SG|GND|0V|24V)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string[] EngineeringTokens =
    [
        "m12", "m8", "rj45", "pin", "contact", "terminal", "port", "connector", "input", "output",
        "rs485", "rs-485", "io-link", "iolink", "ethernet", "ethercat", "profinet", "modbus",
        "24v", "0v", "gnd", "shield", "接頭", "接头", "腳位", "脚位", "端子", "接口"
    ];

    private readonly double _maximumDistance;

    public DiagramTextGeometryMatcher(double maximumDistance = 0.065)
    {
        _maximumDistance = Math.Clamp(maximumDistance, 0.015, 0.20);
    }

    public IReadOnlyList<DiagramLabelMatch> Match(
        int pageNumber,
        IEnumerable<OcrTextBox> boxes,
        DiagramGeometryPage geometry)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometry.PageNumber != pageNumber) return Array.Empty<DiagramLabelMatch>();

        var anchors = BuildAnchors(geometry);
        if (anchors.Count == 0) return Array.Empty<DiagramLabelMatch>();

        var matches = new List<DiagramLabelMatch>();
        foreach (var box in boxes.Where(LooksLikeDiagramLabel))
        {
            var center = box.Center;
            var ranked = anchors
                .Select(anchor => new { Anchor = anchor, Distance = Distance(center, anchor.Point) })
                .Where(candidate => candidate.Distance <= _maximumDistance)
                .OrderBy(candidate => candidate.Distance + AnchorPenalty(candidate.Anchor.Kind))
                .ThenByDescending(candidate => candidate.Anchor.Confidence)
                .FirstOrDefault();
            if (ranked is null) continue;

            var distanceConfidence = 1d - Math.Clamp(ranked.Distance / _maximumDistance, 0d, 1d);
            var ocrConfidence = Math.Clamp(box.Confidence / 100d, 0d, 1d);
            var confidence = Math.Clamp(
                0.40 * distanceConfidence +
                0.30 * ocrConfidence +
                0.30 * ranked.Anchor.Confidence,
                0d,
                0.96);

            matches.Add(new DiagramLabelMatch(
                pageNumber,
                box.Text,
                box,
                ranked.Anchor.Kind,
                ranked.Anchor.Point,
                ranked.Anchor.Source,
                ranked.Distance,
                confidence));
        }

        return matches
            .GroupBy(match => $"{match.Text}\u001f{Bucket(match.TextBox.Center.X)}\u001f{Bucket(match.TextBox.Center.Y)}")
            .Select(group => group.OrderByDescending(match => match.Confidence).First())
            .OrderBy(match => match.TextBox.Top)
            .ThenBy(match => match.TextBox.Left)
            .ToArray();
    }

    internal static bool LooksLikeDiagramLabel(OcrTextBox box)
    {
        var text = Regex.Replace(box.Text ?? string.Empty, @"\s+", " ").Trim();
        if (text.Length is < 1 or > 40) return false;
        if (box.Confidence >= 0 && box.Confidence < 20) return false;
        if (PinLike.IsMatch(text)) return true;
        if (EngineeringTokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase))) return true;

        // Short letter+digit or number+unit callouts are common beside connector/wiring diagrams.
        if (text.Length <= 12 && text.Any(char.IsLetter) && text.Any(char.IsDigit)) return true;
        if (text.Length <= 5 && text.All(character => char.IsLetterOrDigit(character) || "+-_/".Contains(character))) return true;
        return false;
    }

    private static IReadOnlyList<Anchor> BuildAnchors(DiagramGeometryPage page)
    {
        var anchors = new List<Anchor>();
        anchors.AddRange(page.Junctions.Select(junction =>
            new Anchor(DiagramAnchorKind.Junction, junction.Point, junction.Source, junction.Confidence)));
        anchors.AddRange(page.Circles.Select(circle =>
            new Anchor(DiagramAnchorKind.Circle, circle.Center, circle.Source, circle.Confidence)));
        foreach (var rectangle in page.Rectangles)
        {
            var center = new DiagramPoint((rectangle.Left + rectangle.Right) / 2d, (rectangle.Top + rectangle.Bottom) / 2d);
            anchors.Add(new Anchor(DiagramAnchorKind.Rectangle, center, rectangle.Source, rectangle.Confidence));
            anchors.Add(new Anchor(DiagramAnchorKind.Rectangle, new DiagramPoint(rectangle.Left, center.Y), rectangle.Source, rectangle.Confidence * 0.95));
            anchors.Add(new Anchor(DiagramAnchorKind.Rectangle, new DiagramPoint(rectangle.Right, center.Y), rectangle.Source, rectangle.Confidence * 0.95));
            anchors.Add(new Anchor(DiagramAnchorKind.Rectangle, new DiagramPoint(center.X, rectangle.Top), rectangle.Source, rectangle.Confidence * 0.95));
            anchors.Add(new Anchor(DiagramAnchorKind.Rectangle, new DiagramPoint(center.X, rectangle.Bottom), rectangle.Source, rectangle.Confidence * 0.95));
        }
        foreach (var line in page.Lines)
        {
            anchors.Add(new Anchor(DiagramAnchorKind.Line, line.From, line.Source, line.Confidence));
            anchors.Add(new Anchor(DiagramAnchorKind.Line, line.To, line.Source, line.Confidence));
            anchors.Add(new Anchor(
                DiagramAnchorKind.Line,
                new DiagramPoint((line.From.X + line.To.X) / 2d, (line.From.Y + line.To.Y) / 2d),
                line.Source,
                line.Confidence * 0.90));
        }
        return anchors;
    }

    private static double AnchorPenalty(DiagramAnchorKind kind) => kind switch
    {
        DiagramAnchorKind.Junction => 0.000,
        DiagramAnchorKind.Circle => 0.003,
        DiagramAnchorKind.Rectangle => 0.006,
        DiagramAnchorKind.Line => 0.009,
        _ => 0.012
    };

    private static double Distance(DiagramPoint first, DiagramPoint second) =>
        Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));

    private static long Bucket(double value) =>
        (long)Math.Round(value / 0.006, MidpointRounding.AwayFromZero);

    private sealed record Anchor(
        DiagramAnchorKind Kind,
        DiagramPoint Point,
        DiagramGeometrySource Source,
        double Confidence);
}
