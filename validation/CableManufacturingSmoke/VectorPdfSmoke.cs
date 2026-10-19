using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Extraction;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;

internal static partial class Program
{
    private static int VectorPdfProof(string root)
    {
        var renderer = typeof(SchematicPdfWriter).Assembly.GetType("ComponentIntelligence.Desktop.WpfPdfVectorRenderer");
        Require(renderer is not null, "WpfPdfVectorRenderer is missing: arranged WPF content cannot yet be exported as vectors.");
        var draw = renderer!.GetMethod("Draw", BindingFlags.Public | BindingFlags.Static, [typeof(XGraphics), typeof(Visual), typeof(double)]);
        Require(draw is not null, "Vector renderer must expose Draw(XGraphics, Visual, double).");
        void Draw(XGraphics graphics, Visual visual, double scale)
        {
            try { draw!.Invoke(null, [graphics, visual, scale]); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }
        }

        Directory.CreateDirectory(root);
        var pdf = System.IO.Path.Combine(root, "vector-proof.pdf");
        Require(!File.Exists(pdf), "Use a fresh vector-proof output directory.");
        const double width = 1680, height = 1188, pointsPerUnit = 72d / 25.4 / 4;
        var canvas = new Canvas { Width = width, Height = height, Background = Brushes.White };
        var text = new TextBlock { Text = "Cable A1 / \u7dda\u6750\u63a5\u7dda", FontFamily = new FontFamily("Microsoft JhengHei"),
            FontSize = 36, Foreground = Brushes.Black };
        Canvas.SetLeft(text, 72); Canvas.SetTop(text, 64); canvas.Children.Add(text);
        var nested = new Canvas { Width = 180, Height = 130, Clip = new RectangleGeometry(new Rect(0, 0, 120, 100)),
            RenderTransform = new TransformGroup { Children = { new ScaleTransform(1.5, .75), new TranslateTransform(30, 15) } } };
        Canvas.SetLeft(nested, 120); Canvas.SetTop(nested, 220); canvas.Children.Add(nested);
        var clipped = new DrawingGroup { Transform = new TranslateTransform(5, 7),
            ClipGeometry = new RectangleGeometry(new Rect(0, 0, 50, 40)) { Transform = new TranslateTransform(10, 10) } };
        clipped.Children.Add(new GeometryDrawing(Brushes.Magenta, null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
        var host = new VectorProofDrawing(clipped) { Width = 150, Height = 120 };
        Canvas.SetLeft(host, 10); Canvas.SetTop(host, 20); nested.Children.Add(host);

        var geometry = new DrawingGroup();
        var arc = new PathGeometry([new PathFigure(new Point(0, 70), [
            new ArcSegment(new Point(180, 70), new Size(90, 55), 20, false, SweepDirection.Clockwise, true),
            new QuadraticBezierSegment(new Point(230, 160), new Point(290, 70), true)], false)]);
        geometry.Children.Add(new GeometryDrawing(null, new Pen(Brushes.Red, 7) { StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Triangle, DashCap = PenLineCap.Round, LineJoin = PenLineJoin.Bevel,
            DashStyle = new DashStyle([4, 2, 1, 2], .75) }, arc));
        var ring = new GeometryGroup { FillRule = FillRule.EvenOdd, Transform = new ScaleTransform(1.1, .9) };
        ring.Children.Add(new RectangleGeometry(new Rect(0, 150, 180, 90)));
        ring.Children.Add(new EllipseGeometry(new Point(90, 195), 35, 25));
        geometry.Children.Add(new GeometryDrawing(Brushes.Blue, new Pen(Brushes.Blue, 2), ring));
        geometry.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(160, 0, 160, 0)) { Opacity = .8 },
            null, new EllipseGeometry(new Point(290, 200), 55, 35)));
        var shapes = new VectorProofDrawing(geometry) { Width = 360, Height = 260,
            RenderTransform = new RotateTransform(13, 0, 0) };
        Canvas.SetLeft(shapes, 700); Canvas.SetTop(shapes, 350); canvas.Children.Add(shapes);
        var line = new Line { X1 = 80, Y1 = 780, X2 = 900, Y2 = 900, Stroke = Brushes.Red, StrokeThickness = 5,
            StrokeDashArray = [3, 2], StrokeStartLineCap = PenLineCap.Square, StrokeEndLineCap = PenLineCap.Round };
        canvas.Children.Add(line);
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Pbgra32, null,
            new byte[] { 0, 0, 128, 128, 0, 128, 0, 128, 128, 0, 0, 128, 0, 0, 0, 255 }, 8);
        var raster = new VectorProofDrawing(new ImageDrawing(bitmap, new Rect(0, 0, 24, 24))) { Width = 24, Height = 24 };
        Canvas.SetLeft(raster, 1450); Canvas.SetTop(raster, 1000); canvas.Children.Add(raster);
        var vectorImageSource = new DrawingGroup { Transform = new TranslateTransform(5, 7) };
        vectorImageSource.Children.Add(new GeometryDrawing(Brushes.Orange, new Pen(Brushes.Black, 2),
            new RectangleGeometry(new Rect(-10, -20, 40, 30))));
        var vectorImage = new VectorProofDrawing(new ImageDrawing(new DrawingImage(vectorImageSource), new Rect(0, 0, 120, 90)))
            { Width = 120, Height = 90 };
        Canvas.SetLeft(vectorImage, 1100); Canvas.SetTop(vectorImage, 1000); canvas.Children.Add(vectorImage);
        canvas.Children.Add(new VectorProofDrawing(new ImageDrawing(new DrawingImage(), new Rect(0, 0, 20, 20)))
            { Width = 20, Height = 20 });
        var emptyClip = new VectorProofDrawing(new GeometryDrawing(Brushes.Cyan, null, new RectangleGeometry(new Rect(1200, 400, 50, 50))))
            { Width = width, Height = height, Clip = Geometry.Empty };
        canvas.Children.Add(emptyClip);
        canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height)); canvas.UpdateLayout();
        var state = VectorProofState(canvas);
        var before = VectorProofBitmap(canvas, (int)width, (int)height);
        Save(before, System.IO.Path.Combine(root, "vector-proof-wpf.png"));
        using (var document = new PdfDocument())
        {
            var page = document.AddPage(); page.Width = XUnit.FromMillimeter(420); page.Height = XUnit.FromMillimeter(297);
            using (var graphics = XGraphics.FromPdfPage(page))
            {
                var initial = graphics.Transform;
                Draw(graphics, canvas, pointsPerUnit);
                Require(graphics.Transform == initial, "Vector export leaked its graphics transform.");
            }
            document.Save(pdf);
        }
        Require(state == VectorProofState(canvas), "Vector export changed arranged visual state or drawing data.");
        Require(VectorProofPixels(before).SequenceEqual(VectorProofPixels(VectorProofBitmap(canvas, (int)width, (int)height))),
            "Vector export changed the source WPF appearance.");
        using (var document = PdfReader.Open(pdf, PdfDocumentOpenMode.Import))
        {
            Require(document.PageCount == 1 && Math.Abs(document.Pages[0].Width.Point - 420 * 72 / 25.4) < .01,
                "Expected one A3 landscape page at the requested unit scale.");
            var page = document.Pages[0];
            var operators = ContentReader.ReadContent(page).OfType<COperator>().ToArray();
            Require(operators.Count(op => op.Name == "m") > 50 && operators.Any(op => op.Name == "c") &&
                operators.Any(op => op.Name is "f" or "f*" or "B" or "B*"), "Decoded PDF lacks filled vector paths and curves, including glyph outlines.");
            Require(operators.Any(op => op.Name is "W" or "W*"), "Nested clipping was omitted from decoded PDF content.");
            Require(!operators.Any(op => op.Name is "Tj" or "TJ" or "Tf"), "Text was substituted with PDF fonts instead of glyph outlines.");
            var resources = page.Elements.GetDictionary("/Resources")!;
            Require(resources.Elements.GetDictionary("/Font")?.Elements.Count is null or 0, "Outlined text must not depend on PDF font resources.");
            var objects = resources.Elements.GetDictionary("/XObject")!;
            var images = objects.Elements.Keys.Select(key => objects.Elements.GetDictionary(key)!).ToArray();
            Require(images.Length == 1 && images[0].Elements.GetName("/Subtype") == "/Image" &&
                images[0].Elements.GetInteger("/Width") == 2 && images[0].Elements.GetInteger("/Height") == 2,
                "Only the original tiny bitmap may be rasterized; a page-sized image is forbidden.");
            Console.WriteLine($"PASS decoded PDF: {operators.Count(op => op.Name == "m")} vector subpaths; curves, fills and clips; no font substitution; one 2x2 image.");
        }
        var rendered = new PdfFullPageRasterizer(150, 1).Extract(pdf).Single();
        using var source = new MemoryStream(rendered.Bytes);
        var preview = BitmapDecoder.Create(source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        Save(preview, System.IO.Path.Combine(root, "vector-proof-pdf.png"));
        var wpfMagenta = VectorProofInk(before, (r, g, b) => r > 220 && g < 30 && b > 220, new Rect(0, 0, width, height), 1);
        var pdfScale = rendered.WidthPixels / width;
        foreach (var (point, color) in new (Point, Color)[] {
            (new(1453, 1003), Color.FromRgb(255, 127, 127)),
            (new(1470, 1003), Color.FromRgb(127, 255, 127)),
            (new(1453, 1020), Color.FromRgb(127, 127, 255)),
            (new(1470, 1020), Colors.Black) })
        {
            var wpfColor = VectorProofPixel(before, point, 1);
            var pdfColor = VectorProofPixel(preview, point, pdfScale);
            Require(wpfColor == color, $"Pbgra32 fixture pixel at {point}: expected {color}, WPF produced {wpfColor}.");
            Require(Math.Abs(pdfColor.R - wpfColor.R) <= 3 && Math.Abs(pdfColor.G - wpfColor.G) <= 3 &&
                Math.Abs(pdfColor.B - wpfColor.B) <= 3 && pdfColor.A == wpfColor.A,
                $"Premultiplied Pbgra32 bitmap pixel at {point}: WPF {wpfColor}, PDFium {pdfColor}.");
        }
        Console.WriteLine("PASS semitransparent Pbgra32 bitmap pixels match WPF after PDFium rendering.");
        Require(VectorProofInk(preview, (r, g, b) => r < 30 && g > 220 && b > 220, new Rect(1150, 350, 150, 150), pdfScale).Area == 0,
            "An empty WPF clip must suppress all of its drawing.");
        var pdfMagenta = VectorProofInk(preview, (r, g, b) => r > 220 && g < 30 && b > 220, new Rect(0, 0, width, height), pdfScale);
        var expected = new Rect(187.5, 262.75, 75, 30);
        VectorProofNear(wpfMagenta.Bounds, expected, 1.5, "WPF fixture transform/clip bounds");
        VectorProofNear(pdfMagenta.Bounds, expected, 1.5, "PDF nested layout/transform/clip bounds");
        foreach (var (name, predicate, region) in new (string, Func<byte, byte, byte, bool>, Rect)[] {
            ("Latin/CJK outlines", (r,g,b) => r < 100 && g < 100 && b < 100, new Rect(60, 50, 550, 80)),
            ("dashed arcs and lines", (r,g,b) => r > 200 && g < 70 && b < 70, new Rect(50, 250, 1100, 700)),
            ("even-odd filled shape", (r,g,b) => r < 70 && g < 70 && b > 200, new Rect(500, 450, 650, 350)),
            ("alpha solid brush", (r,g,b) => g > r + 40 && g > b + 40, new Rect(800, 450, 400, 350)),
            ("nested DrawingImage", (r,g,b) => r > 230 && g > 130 && g < 190 && b < 30, new Rect(1050, 950, 250, 200)) })
        {
            var a = VectorProofInk(before, predicate, region, 1);
            var b = VectorProofInk(preview, predicate, region, pdfScale);
            Require(a.Area > 50, $"Fixture has no {name} ink.");
            VectorProofNear(b.Bounds, a.Bounds, 2, name);
            Require(Math.Abs(b.Area / a.Area - 1) < .18, $"PDF {name} ink area differs from WPF: {a.Area:F1} vs {b.Area:F1}.");
        }
        using (var document = new PdfDocument())
        using (var graphics = XGraphics.FromPdfPage(document.AddPage()))
        {
            foreach (var invalidScale in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
            {
                try { Draw(graphics, canvas, invalidScale); throw new Exception("Invalid unit scale was accepted."); }
                catch (ArgumentOutOfRangeException) { }
            }
            foreach (var unsupported in new Drawing[] {
                new GeometryDrawing(new LinearGradientBrush(Colors.Red, Colors.Blue, 0), null, new RectangleGeometry(new Rect(0, 0, 10, 10))),
                new DrawingGroup { Opacity = .5, Children = { new GeometryDrawing(Brushes.Red, null, new RectangleGeometry(new Rect(0, 0, 10, 10))) } },
                new ImageDrawing(new DrawingImage(new GeometryDrawing(new LinearGradientBrush(Colors.Red, Colors.Blue, 0),
                    null, new RectangleGeometry(new Rect(0, 0, 20, 20)))), new Rect(0, 0, 20, 20)) })
            {
                var visual = new DrawingVisual(); using (var context = visual.RenderOpen()) context.DrawDrawing(unsupported);
                var initial = graphics.Transform;
                try { Draw(graphics, visual, 1); throw new Exception("Unsupported drawing was silently omitted or approximated."); }
                catch (NotSupportedException error) { Require(error.Message.Length > 20, "Unsupported content needs a meaningful diagnostic."); }
                Require(graphics.Transform == initial, "Failed export leaked its graphics state.");
            }
        }
        Console.WriteLine("PASS A3 scale, unchanged WPF input, restored graphics state, nested bounds, Latin/CJK ink, dashed curves/lines, even-odd fill, alpha brush, explicit unsupported-content errors.");
        Console.WriteLine(pdf);
        return 0;
    }

    private static string VectorProofState(Visual visual)
    {
        var drawing = VisualTreeHelper.GetDrawing(visual);
        var state = $"{VisualTreeHelper.GetOffset(visual)}|{VisualTreeHelper.GetTransform(visual)?.Value}|{VisualTreeHelper.GetClip(visual)}|" +
            $"{VisualTreeHelper.GetOpacity(visual)}|{VisualTreeHelper.GetDescendantBounds(visual)}|{(drawing is null ? "" : XamlWriter.Save(drawing))}";
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++) state += VectorProofState((Visual)VisualTreeHelper.GetChild(visual, i));
        return state;
    }

    private static RenderTargetBitmap VectorProofBitmap(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); return bitmap;
    }

    private static byte[] VectorProofPixels(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(bytes, converted.PixelWidth * 4, 0); return bytes;
    }

    private static Color VectorProofPixel(BitmapSource bitmap, Point point, double scale)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new Int32Rect((int)(point.X * scale), (int)(point.Y * scale), 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static (Rect Bounds, double Area) VectorProofInk(BitmapSource bitmap, Func<byte, byte, byte, bool> predicate, Rect region, double scale)
    {
        var pixels = VectorProofPixels(bitmap); var bounds = Rect.Empty; var count = 0;
        for (var y = Math.Max(0, (int)(region.Top * scale)); y < Math.Min(bitmap.PixelHeight, (int)(region.Bottom * scale)); y++)
        for (var x = Math.Max(0, (int)(region.Left * scale)); x < Math.Min(bitmap.PixelWidth, (int)(region.Right * scale)); x++)
        {
            var i = (y * bitmap.PixelWidth + x) * 4;
            if (!predicate(pixels[i + 2], pixels[i + 1], pixels[i])) continue;
            bounds.Union(new Rect(x / scale, y / scale, 1 / scale, 1 / scale)); count++;
        }
        return (bounds, count / (scale * scale));
    }

    private static void VectorProofNear(Rect actual, Rect expected, double tolerance, string name) =>
        Require(!actual.IsEmpty && Math.Abs(actual.Left - expected.Left) < tolerance && Math.Abs(actual.Top - expected.Top) < tolerance &&
            Math.Abs(actual.Right - expected.Right) < tolerance && Math.Abs(actual.Bottom - expected.Bottom) < tolerance,
            $"{name}: expected {expected}, found {actual}.");

    private sealed class VectorProofDrawing(Drawing drawing) : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawingContext) => drawingContext.DrawDrawing(drawing);
    }

#if VECTOR_PDF_PROOF
    internal static int RunVectorPdfProof(string root) => VectorPdfProof(root);
#endif
}

#if VECTOR_PDF_PROOF
internal static class VectorPdfProofRunner
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { return Program.RunVectorPdfProof(System.IO.Path.GetFullPath(args[0])); }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
#endif
