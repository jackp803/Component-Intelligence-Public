using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private static readonly ConditionalWeakTable<SchematicCadPrimitive, Geometry> CadVectorGeometry = new();

    private static void AddCadVectorBatch(Canvas canvas, IReadOnlyList<SchematicCadPrimitive> primitives, double thickness)
    {
        var group = new GeometryGroup();
        foreach (var primitive in primitives)
            group.Children.Add(CadVectorGeometry.GetValue(primitive, CreateCadVector));
        group.Freeze();
        var pen = new Pen(Brushes.Black, thickness); pen.Freeze();
        var drawing = new GeometryDrawing(null, pen, group); drawing.Freeze();
        var bounds = drawing.Bounds;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;
        var source = new DrawingImage(drawing); source.Freeze();
        var image = new CadVectorImage(group, pen, bounds.TopLeft) { Source = source, Width = bounds.Width, Height = bounds.Height,
            Stretch = Stretch.Fill };
        Canvas.SetLeft(image, bounds.Left); Canvas.SetTop(image, bounds.Top); canvas.Children.Add(image);
    }

    private sealed class CadVectorImage(Geometry geometry, Pen pen, Point offset) : Image
    {
        protected override HitTestResult? HitTestCore(PointHitTestParameters parameters) =>
            geometry.StrokeContains(pen, new Point(parameters.HitPoint.X + offset.X, parameters.HitPoint.Y + offset.Y))
                ? base.HitTestCore(parameters) : null;
    }

    private static Geometry CreateCadVector(SchematicCadPrimitive primitive)
    {
        Geometry geometry;
        if (primitive.Kind == "LINE")
            geometry = primitive.End is { } end ? new LineGeometry(new(primitive.Start.X * 3, primitive.Start.Y * 3),
                new(end.X * 3, end.Y * 3)) : Geometry.Empty;
        else if (primitive.Kind == "CIRCLE")
            geometry = new EllipseGeometry(new Point(primitive.Start.X * 3, primitive.Start.Y * 3),
                Math.Max(0, primitive.Radius * 3 - .5), Math.Max(0, primitive.Radius * 3 - .5));
        else
        {
            Point At(double angle) => new((primitive.Start.X + primitive.Radius * Math.Cos(angle * Math.PI / 180)) * 3,
                (primitive.Start.Y - primitive.Radius * Math.Sin(angle * Math.PI / 180)) * 3);
            var sweep = (primitive.EndAngle - primitive.StartAngle + 360) % 360;
            var figure = new PathFigure { StartPoint = At(primitive.StartAngle) };
            figure.Segments.Add(new ArcSegment(At(primitive.EndAngle), new(primitive.Radius * 3, primitive.Radius * 3),
                0, sweep > 180, SweepDirection.Counterclockwise, true));
            geometry = new PathGeometry([figure]);
        }
        if (!geometry.IsFrozen) geometry.Freeze();
        return geometry;
    }
}
