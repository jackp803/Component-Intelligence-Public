using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public static class CableWiringVisualRenderer
{
    public static void Draw(Canvas canvas, IEnumerable<SchematicCadPrimitive> primitives, double pixelsPerMm = 4)
    {
        foreach (var p in primitives)
        {
            System.Windows.FrameworkElement? shape = p.Kind switch
            {
                "LINE" when p.End is not null => new Line { X1 = p.Start.X * pixelsPerMm, Y1 = p.Start.Y * pixelsPerMm,
                    X2 = p.End.X * pixelsPerMm, Y2 = p.End.Y * pixelsPerMm, Stroke = Brushes.Black, StrokeThickness = .3 * pixelsPerMm },
                "CIRCLE" => new Ellipse { Width = p.Radius * 2 * pixelsPerMm, Height = p.Radius * 2 * pixelsPerMm,
                    Fill = p.Radius < .8 ? Brushes.Black : Brushes.White, Stroke = Brushes.Black, StrokeThickness = .3 * pixelsPerMm },
                "TEXT" or "MTEXT" => new TextBlock { Text = p.DisplayText ?? p.Text ?? "", FontSize = p.TextHeight * pixelsPerMm,
                    FontFamily = new("Microsoft JhengHei"), Foreground = Brushes.Black, Background = Brushes.White },
                _ => null
            };
            if (shape is null) continue;
            if (p.Kind != "LINE")
            {
                Canvas.SetLeft(shape, (p.Start.X - (p.Kind == "CIRCLE" ? p.Radius : 0)) * pixelsPerMm);
                Canvas.SetTop(shape, (p.Start.Y - (p.Kind == "CIRCLE" ? p.Radius : 0)) * pixelsPerMm);
            }
            shape.IsHitTestVisible = false; canvas.Children.Add(shape);
        }
    }
}
