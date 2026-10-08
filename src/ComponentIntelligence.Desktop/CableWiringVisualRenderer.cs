using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public static class CableWiringVisualRenderer
{
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
                "TEXT" or "MTEXT" => CreateText(p, textPixelsPerMm ?? pixelsPerMm, minimumTextHeight),
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

    internal static TextBlock CreateText(SchematicCadPrimitive primitive, double pixelsPerMm, double minimumTextHeight) =>
        new() { Text = primitive.DisplayText ?? primitive.Text ?? "", FontSize = Math.Max(primitive.TextHeight, minimumTextHeight) * pixelsPerMm,
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
