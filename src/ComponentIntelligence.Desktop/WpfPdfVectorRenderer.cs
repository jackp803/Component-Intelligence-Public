using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;

namespace ComponentIntelligence.Desktop;

/// <summary>Copies an already arranged WPF visual to PDF paths, retaining only existing bitmap images as rasters.</summary>
public static class WpfPdfVectorRenderer
{
    // WPF uses this absolute tolerance when converting strokes/arcs to vector outlines, never page pixels.
    private const double OutlineTolerance = .001;

    /// <summary>
    /// Draws in the caller's current graphics coordinates. Layout is read without measuring or arranging it.
    /// Non-solid brushes, group opacity/masks, effects, video and non-bitmap image sources are unsupported.
    /// </summary>
    public static void Draw(XGraphics graphics, Visual visual, double pointsPerUnit)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(visual);
        if (!double.IsFinite(pointsPerUnit) || pointsPerUnit <= 0)
            throw new ArgumentOutOfRangeException(nameof(pointsPerUnit), "PDF points per WPF unit must be finite and greater than zero.");
        visual.VerifyAccess();
        var state = graphics.Save();
        try
        {
            graphics.ScaleTransform(pointsPerUnit);
            DrawVisual(graphics, visual);
        }
        finally { graphics.Restore(state); }
    }

    private static void DrawVisual(XGraphics graphics, Visual visual)
    {
        if (visual is UIElement { Visibility: not Visibility.Visible } || VisualTreeHelper.GetOpacity(visual) == 0) return;
        var description = $"visual {visual.GetType().Name}";
        CheckCompositing(VisualTreeHelper.GetOpacity(visual), VisualTreeHelper.GetOpacityMask(visual), description);
        if (VisualTreeHelper.GetEffect(visual) is { } effect)
            throw Unsupported(description, $"effect {effect.GetType().Name}");
        var state = graphics.Save();
        try
        {
            // VisualOffset is applied after the WPF render/layout transform, in parent coordinates.
            var matrix = VisualTreeHelper.GetTransform(visual)?.Value ?? Matrix.Identity;
            var offset = VisualTreeHelper.GetOffset(visual);
            matrix.Translate(offset.X, offset.Y);
            if (!ApplyTransform(graphics, matrix, description)) return;
            if (!ApplyClip(graphics, VisualTreeHelper.GetClip(visual))) return;
            if (VisualTreeHelper.GetDrawing(visual) is { } drawing) DrawDrawing(graphics, drawing);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            {
                var child = VisualTreeHelper.GetChild(visual, i);
                if (child is not Visual childVisual) throw Unsupported(description, $"non-2D child {child.GetType().Name}");
                DrawVisual(graphics, childVisual);
            }
        }
        finally { graphics.Restore(state); }
    }

    private static void DrawDrawing(XGraphics graphics, Drawing drawing)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                if (group.Opacity == 0) return;
                CheckCompositing(group.Opacity, group.OpacityMask, nameof(DrawingGroup));
                var state = graphics.Save();
                try
                {
                    if (!ApplyTransform(graphics, group.Transform?.Value ?? Matrix.Identity, nameof(DrawingGroup))) return;
                    if (!ApplyClip(graphics, group.ClipGeometry)) return;
                    foreach (var child in group.Children) DrawDrawing(graphics, child);
                }
                finally { graphics.Restore(state); }
                break;
            case GeometryDrawing shape:
                if (shape.Geometry is not { } geometry) return;
                if (shape.Brush is { } fill) FillGeometry(graphics, geometry, fill, nameof(GeometryDrawing));
                if (shape.Pen is { Brush: { } stroke, Thickness: > 0 } pen)
                {
                    var brush = SolidBrush(stroke, "GeometryDrawing pen");
                    // WPF widening preserves individual segment flags, asymmetric caps, joins and dash caps/offsets.
                    FillPath(graphics, geometry.GetWidenedPathGeometry(pen, OutlineTolerance, ToleranceType.Absolute), brush);
                }
                break;
            case GlyphRunDrawing glyph:
                if (glyph.GlyphRun is { } run && glyph.ForegroundBrush is { } foreground)
                    FillGeometry(graphics, run.BuildGeometry(), foreground, nameof(GlyphRunDrawing));
                break;
            case ImageDrawing image:
                if (image.ImageSource is null || image.Rect.IsEmpty || image.Rect.Width == 0 || image.Rect.Height == 0) return;
                if (image.ImageSource is not BitmapSource bitmap)
                    throw Unsupported(nameof(ImageDrawing), $"image source {image.ImageSource.GetType().Name}; only existing BitmapSource rasters are supported");
                // PDFsharp expects straight alpha; WPF render targets use premultiplied BGRA.
                if (bitmap.Format == PixelFormats.Pbgra32)
                    bitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                using (var pdfImage = XImage.FromBitmapSource(bitmap))
                    graphics.DrawImage(pdfImage, image.Rect.X, image.Rect.Y, image.Rect.Width, image.Rect.Height);
                break;
            default:
                throw Unsupported("drawing", drawing.GetType().Name);
        }
    }

    private static void FillGeometry(XGraphics graphics, Geometry geometry, Brush brush, string description) =>
        FillPath(graphics, geometry.GetOutlinedPathGeometry(OutlineTolerance, ToleranceType.Absolute), SolidBrush(brush, description));

    private static void FillPath(XGraphics graphics, PathGeometry geometry, XSolidBrush brush)
    {
        if (geometry.IsEmpty()) return;
        graphics.DrawPath(brush, PdfPath(geometry));
    }

    private static bool ApplyClip(XGraphics graphics, Geometry? geometry)
    {
        if (geometry is null) return true;
        var outline = geometry.GetOutlinedPathGeometry(OutlineTolerance, ToleranceType.Absolute);
        // PDFsharp ignores an empty clipping path; WPF makes the entire subtree invisible instead.
        if (outline.IsEmpty()) return false;
        graphics.IntersectClip(PdfPath(outline));
        return true;
    }

    private static XGraphicsPath PdfPath(PathGeometry geometry)
    {
        // PDFsharp's WPF constructor clones the geometry. Bake its transform into the clone's points so that
        // path transforms (including clips) cannot be lost by a PDF renderer or applied to the pen twice.
        var path = geometry.CloneCurrentValue();
        var matrix = path.Transform?.Value ?? Matrix.Identity;
        path.Transform = Transform.Identity;
        if (!matrix.IsIdentity)
        {
            foreach (var figure in path.Figures)
            {
                figure.StartPoint = matrix.Transform(figure.StartPoint);
                foreach (var segment in figure.Segments)
                {
                    switch (segment)
                    {
                        case LineSegment line: line.Point = matrix.Transform(line.Point); break;
                        case PolyLineSegment lines: TransformPoints(lines.Points, matrix); break;
                        case BezierSegment curve:
                            curve.Point1 = matrix.Transform(curve.Point1); curve.Point2 = matrix.Transform(curve.Point2);
                            curve.Point3 = matrix.Transform(curve.Point3); break;
                        case PolyBezierSegment curves: TransformPoints(curves.Points, matrix); break;
                        default: throw Unsupported("normalized vector outline", segment.GetType().Name);
                    }
                }
            }
        }
        return new XGraphicsPath(path) { FillMode = path.FillRule == FillRule.EvenOdd ? XFillMode.Alternate : XFillMode.Winding };
    }

    private static void TransformPoints(PointCollection points, Matrix matrix)
    {
        for (var i = 0; i < points.Count; i++) points[i] = matrix.Transform(points[i]);
    }

    private static XSolidBrush SolidBrush(Brush brush, string description)
    {
        if (brush is not SolidColorBrush solid) throw Unsupported(description, $"brush {brush.GetType().Name}; a SolidColorBrush is required");
        var color = solid.Color;
        return new XSolidBrush(XColor.FromArgb((int)Math.Round(color.A * solid.Opacity), color.R, color.G, color.B));
    }

    private static bool ApplyTransform(XGraphics graphics, Matrix matrix, string description)
    {
        if (!double.IsFinite(matrix.M11) || !double.IsFinite(matrix.M12) || !double.IsFinite(matrix.M21) ||
            !double.IsFinite(matrix.M22) || !double.IsFinite(matrix.OffsetX) || !double.IsFinite(matrix.OffsetY))
            throw Unsupported(description, "non-finite transform");
        if (!matrix.HasInverse) return false;
        if (!matrix.IsIdentity)
            graphics.MultiplyTransform(new XMatrix(matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.OffsetX, matrix.OffsetY), XMatrixOrder.Prepend);
        return true;
    }

    private static void CheckCompositing(double opacity, Brush? mask, string description)
    {
        if (opacity != 1) throw Unsupported(description, $"group opacity {opacity}; transparent groups need isolated PDF compositing");
        if (mask is not null) throw Unsupported(description, $"opacity mask {mask.GetType().Name}");
    }

    private static NotSupportedException Unsupported(string description, string feature) =>
        new($"WPF vector PDF export does not support {feature} in {description}.");
}
