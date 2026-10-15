using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public static class CableWiringVisualRenderer
{
    private static readonly Color[] IdentificationPalette =
    [
        Color.FromRgb(180, 35, 24), Color.FromRgb(23, 92, 211), Color.FromRgb(24, 121, 78),
        Color.FromRgb(158, 25, 134), Color.FromRgb(165, 79, 0), Color.FromRgb(0, 124, 145),
        Color.FromRgb(105, 65, 198), Color.FromRgb(101, 119, 0), Color.FromRgb(151, 47, 87),
        Color.FromRgb(63, 93, 119)
    ];

    public static void DrawDiagram(Canvas canvas, CableWiringDiagramPresentation diagram, double pixelsPerMm = 4,
        double? textPixelsPerMm = null, double minimumTextHeight = 0)
    {
        // Display-only identification colors: never physical insulation or electrical authority.
        var strokes = new Dictionary<SchematicCadPrimitive, Brush>(ReferenceEqualityComparer.Instance);
        var used = new HashSet<Color>();
        var index = 0;
        foreach (var (_, parts) in diagram.EdgePrimitives.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var color = IdentificationColor(index++);
            while (!used.Add(color))
            {
                var rgb = ((color.R << 16 | color.G << 8 | color.B) + 1) & 0xffffff;
                color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            }
            var brush = new SolidColorBrush(color); brush.Freeze();
            foreach (var part in parts) strokes.Add(part, brush);
        }
        // Preserve primitive order so shared junction dots stay above conductor segments.
        foreach (var primitive in diagram.Primitives)
            Draw(canvas, [primitive], pixelsPerMm, strokes.GetValueOrDefault(primitive), textPixelsPerMm, minimumTextHeight);
    }

    private static Color IdentificationColor(int index)
    {
        if (index < IdentificationPalette.Length) return IdentificationPalette[index];
        // Continue with separated dark hues rather than repeating a short palette for large harnesses.
        var hue = ((index - IdentificationPalette.Length) * 137.50776405003785 % 360) / 60;
        const double value = .62, saturation = .82;
        var chroma = value * saturation;
        var second = chroma * (1 - Math.Abs(hue % 2 - 1));
        var (r, g, b) = (int)hue switch
        {
            0 => (chroma, second, 0d), 1 => (second, chroma, 0d), 2 => (0d, chroma, second),
            3 => (0d, second, chroma), 4 => (second, 0d, chroma), _ => (chroma, 0d, second)
        };
        byte Channel(double c) => (byte)Math.Round((c + value - chroma) * 255);
        return Color.FromRgb(Channel(r), Channel(g), Channel(b));
    }

    public static void Draw(Canvas canvas, IEnumerable<SchematicCadPrimitive> primitives, double pixelsPerMm = 4, Brush? stroke = null,
        double? textPixelsPerMm = null, double minimumTextHeight = 0)
    {
        stroke ??= Brushes.Black;
        foreach (var p in primitives)
        {
            System.Windows.FrameworkElement? shape = p.Kind switch
            {
                "LINE" when p.End is not null => new Line { X1 = p.Start.X * pixelsPerMm, Y1 = p.Start.Y * pixelsPerMm,
                    X2 = p.End.X * pixelsPerMm, Y2 = p.End.Y * pixelsPerMm, Stroke = stroke, StrokeThickness = .3 * pixelsPerMm },
                "ARC" => Arc(p, pixelsPerMm, stroke),
                "CIRCLE" => new Ellipse { Width = p.Radius * 2 * pixelsPerMm, Height = p.Radius * 2 * pixelsPerMm,
                    Fill = p.Radius < .8 ? Brushes.Black : Brushes.White, Stroke = Brushes.Black, StrokeThickness = .3 * pixelsPerMm },
                "TEXT" or "MTEXT" => CreateText(p, textPixelsPerMm ?? pixelsPerMm, minimumTextHeight,
                    pixelsPerMm / (textPixelsPerMm ?? pixelsPerMm)),
                _ => null
            };
            if (shape is null) continue;
            if (p.Kind is not ("LINE" or "ARC"))
            {
                Canvas.SetLeft(shape, (p.Start.X - (p.Kind == "CIRCLE" ? p.Radius : 0)) * pixelsPerMm);
                Canvas.SetTop(shape, (p.Start.Y - (p.Kind == "CIRCLE" ? p.Radius : 0)) * pixelsPerMm);
            }
            shape.IsHitTestVisible = false; canvas.Children.Add(shape);
        }
    }

    internal static TextBlock CreateText(SchematicCadPrimitive primitive, double pixelsPerMm, double minimumTextHeight, double widthScale = 1) =>
        new() { Text = primitive.DisplayText ?? primitive.Text ?? "", FontSize = Math.Max(primitive.TextHeight, minimumTextHeight) * pixelsPerMm,
            Width = primitive.TextWidth > 0 ? primitive.TextWidth * pixelsPerMm * widthScale : double.NaN,
            TextWrapping = primitive.TextWidth > 0 ? System.Windows.TextWrapping.Wrap : System.Windows.TextWrapping.NoWrap,
            FontFamily = new("Microsoft JhengHei"), Foreground = Brushes.Black, Background = Brushes.White };

    private static Path Arc(SchematicCadPrimitive p, double scale, Brush stroke)
    {
        System.Windows.Point Point(double angle) => new((p.Start.X + Math.Cos(angle * Math.PI / 180) * p.Radius) * scale,
            (p.Start.Y - Math.Sin(angle * Math.PI / 180) * p.Radius) * scale);
        var sweep = (p.EndAngle - p.StartAngle + 360) % 360;
        var figure = new PathFigure { StartPoint = Point(p.StartAngle), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(Point(p.EndAngle), new(p.Radius * scale, p.Radius * scale), 0,
            sweep > 180, SweepDirection.Counterclockwise, true));
        return new Path { Data = new PathGeometry([figure]), Stroke = stroke, StrokeThickness = .3 * scale };
    }
}
