using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ComponentIntelligence.Desktop;

public sealed class TopologyPdfExporter
{
    private readonly TopologyProjection _projection = new();

    public void ExportVisual(
        ElectricalProject project,
        string filePath,
        FrameworkElement topologySurface,
        ElectricalLayer? layerFilter = null,
        string? highlightedConnectionId = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(topologySurface);

        var originalTransform = topologySurface.LayoutTransform;
        try
        {
            // Export logical canvas coordinates, independent of the user's current Ctrl+wheel zoom.
            topologySurface.LayoutTransform = Transform.Identity;
            topologySurface.UpdateLayout();
            var visualBounds = CalculateVisualContentBounds(topologySurface);
            using var normalizedSurface = NormalizeSurfaceForExport(topologySurface, visualBounds);
            var bounds = normalizedSurface.Bounds;

            using var document = new PdfDocument();
            document.Info.Title = string.IsNullOrWhiteSpace(project.Name) ? "Electrical Topology" : project.Name;
            document.Info.Subject = "Component Intelligence Electrical Topology - exact canvas visual";
            document.Info.Creator = "Component Intelligence";
            document.Info.Keywords = string.IsNullOrWhiteSpace(highlightedConnectionId)
                ? $"Layer={layerFilter?.ToString() ?? "All"}"
                : $"Layer={layerFilter?.ToString() ?? "All"}; HighlightedConnection={highlightedConnectionId}";

            var page = document.AddPage();
            ConfigurePageForContent(page, bounds);
            using var gfx = XGraphics.FromPdfPage(page);

            const double margin = 16d;
            var availableWidth = page.Width.Point - margin * 2d;
            var availableHeight = page.Height.Point - margin * 2d;
            var scale = Math.Min(availableWidth / bounds.Width, availableHeight / bounds.Height);
            var width = bounds.Width * scale;
            var height = bounds.Height * scale;
            var destination = new XRect(
                (page.Width.Point - width) / 2d,
                (page.Height.Point - height) / 2d,
                width,
                height);
            gfx.DrawRectangle(XBrushes.White, destination);
            DrawVisualTiles(gfx, topologySurface, bounds, destination);
            document.Save(filePath);
        }
        finally
        {
            topologySurface.LayoutTransform = originalTransform;
            topologySurface.UpdateLayout();
        }
    }

    public void Export(ElectricalProject project, string filePath, ElectricalLayer? layerFilter = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _projection.EnsurePlacements(project);
        var graph = _projection.Build(project, layerFilter);
        if (graph.Nodes.Count == 0)
            throw new InvalidOperationException("Topology canvas has no visible nodes to export.");

        using var document = new PdfDocument();
        document.Info.Title = string.IsNullOrWhiteSpace(project.Name) ? "Electrical Topology" : project.Name;
        document.Info.Subject = "Component Intelligence Electrical Topology";
        document.Info.Creator = "Component Intelligence";

        var page = document.AddPage();
        page.Size = PageSize.A3;
        page.Orientation = PageOrientation.Landscape;
        using var gfx = XGraphics.FromPdfPage(page);

        var pageWidth = page.Width.Point;
        var pageHeight = page.Height.Point;
        const double margin = 34;
        const double headerHeight = 42;

        var minX = graph.Nodes.Min(node => node.Placement.X);
        var minY = graph.Nodes.Min(node => node.Placement.Y);
        var maxX = graph.Nodes.Max(node => node.Placement.X + node.Placement.Width);
        var maxY = graph.Nodes.Max(node => node.Placement.Y + node.Placement.Height);
        var contentWidth = Math.Max(1, maxX - minX);
        var contentHeight = Math.Max(1, maxY - minY);
        var availableWidth = pageWidth - margin * 2;
        var availableHeight = pageHeight - margin * 2 - headerHeight;
        var scale = Math.Min(availableWidth / contentWidth, availableHeight / contentHeight);
        scale = Math.Min(scale, 2.2);

        var font = CreateFont(9, XFontStyleEx.Regular);
        var bold = CreateFont(10, XFontStyleEx.Bold);
        var titleFont = CreateFont(15, XFontStyleEx.Bold);
        var small = CreateFont(7.5, XFontStyleEx.Regular);

        gfx.DrawString(
            string.IsNullOrWhiteSpace(project.Name) ? "Electrical Topology" : project.Name,
            titleFont,
            XBrushes.Black,
            new XRect(margin, margin - 8, availableWidth, 24),
            XStringFormats.TopLeft);
        gfx.DrawString(
            $"Project: {project.ProjectId}    Layer: {(layerFilter?.ToString() ?? "All")}    Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm}",
            small,
            XBrushes.DimGray,
            new XRect(margin, margin + 16, availableWidth, 18),
            XStringFormats.TopLeft);

        double Tx(double x) => margin + (x - minX) * scale;
        double Ty(double y) => margin + headerHeight + (y - minY) * scale;

        var nodeMap = graph.Nodes.ToDictionary(node => node.ObjectId, StringComparer.OrdinalIgnoreCase);
        foreach (var edge in graph.Edges)
        {
            if (!nodeMap.TryGetValue(edge.FromObjectId, out var from) || !nodeMap.TryGetValue(edge.ToObjectId, out var to)) continue;
            var x1 = Tx(from.Placement.X + from.Placement.Width / 2);
            var y1 = Ty(from.Placement.Y + from.Placement.Height / 2);
            var x2 = Tx(to.Placement.X + to.Placement.Width / 2);
            var y2 = Ty(to.Placement.Y + to.Placement.Height / 2);
            gfx.DrawLine(LayerPen(edge.Layer), x1, y1, x2, y2);
            var label = edge.NetLabel ?? edge.NetId;
            if (!string.IsNullOrWhiteSpace(label))
                gfx.DrawString(label, small, XBrushes.Black, (x1 + x2) / 2 + 3, (y1 + y2) / 2 - 3);
        }

        foreach (var node in graph.Nodes)
        {
            var x = Tx(node.Placement.X);
            var y = Ty(node.Placement.Y);
            var width = Math.Max(46, node.Placement.Width * scale);
            var height = Math.Max(28, node.Placement.Height * scale);
            var rect = new XRect(x, y, width, height);
            var pen = node.ObjectKind == "TERMINAL_BLOCK" ? new XPen(XColors.DarkSlateGray, 1.5) : new XPen(XColors.DimGray, 1.1);
            gfx.DrawRoundedRectangle(pen, XBrushes.WhiteSmoke, rect, new XSize(5, 5));
            gfx.DrawString(node.Label.Replace('\n', ' '), bold, XBrushes.Black, new XRect(x + 4, y + 5, width - 8, 14), XStringFormats.TopCenter);

            var component = project.Components.FirstOrDefault(item => string.Equals(item.ComponentInstanceId, node.ObjectId, StringComparison.OrdinalIgnoreCase));
            if (component is not null)
            {
                var ports = component.Ports.Take(8).ToArray();
                if (ports.Length > 0)
                {
                    var portText = string.Join("  ", ports.Select(port => $"{port.Name}:{port.Connector?.Family ?? port.Protocol ?? "?"}"));
                    gfx.DrawString(portText, small, XBrushes.DimGray, new XRect(x + 4, y + height - 13, width - 8, 10), XStringFormats.TopCenter);
                }
            }
        }

        document.Save(filePath);
    }

    private static XFont CreateFont(double size, XFontStyleEx style)
    {
        try { return new XFont("Segoe UI", size, style); }
        catch { return new XFont("Arial", size, style); }
    }

    private static XPen LayerPen(ElectricalLayer layer) => layer switch
    {
        ElectricalLayer.Power => new XPen(XColors.Firebrick, 1.6),
        ElectricalLayer.Analog => new XPen(XColors.DarkOrange, 1.6),
        ElectricalLayer.Digital => new XPen(XColors.ForestGreen, 1.6),
        ElectricalLayer.Communication => new XPen(XColors.RoyalBlue, 1.6),
        ElectricalLayer.Grounding => new XPen(XColors.SaddleBrown, 1.6),
        ElectricalLayer.Safety => new XPen(XColors.DarkViolet, 1.6),
        _ => new XPen(XColors.Gray, 1.3)
    };

    private static System.Windows.Rect CalculateVisualContentBounds(FrameworkElement surface)
    {
        var bounds = System.Windows.Rect.Empty;
        if (surface is System.Windows.Controls.Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                if (child.Visibility != Visibility.Visible || child.Opacity <= 0.001d ||
                    string.Equals(child.Uid, "CI-MARQUEE-SELECTION", StringComparison.Ordinal))
                    continue;
                var local = VisibleLocalBounds(child);
                if (local.IsEmpty) continue;
                try
                {
                    bounds.Union(child.TransformToAncestor(surface).TransformBounds(local));
                }
                catch (InvalidOperationException)
                {
                    // A visual being replaced by the final layout pass is simply omitted; the next
                    // stable visual contains the same engineering object.
                }
            }
        }
        if (bounds.IsEmpty)
            bounds = new System.Windows.Rect(0, 0, Math.Max(1d, surface.ActualWidth), Math.Max(1d, surface.ActualHeight));

        // Do not clamp to the Canvas Width/Height. Labels, selection effects and rotated
        // component images can legally extend outside the logical canvas rectangle.
        // Leave a real safety band around the ink. A fixed 48 px band almost disappears when a
        // large topology is reduced to one PDF page, which made the left/top content look cut.
        // The band scales with the drawing but stays bounded so the page remains compact.
        var padding = Math.Clamp(Math.Max(bounds.Width, bounds.Height) * 0.035d, 120d, 280d);
        bounds.Inflate(padding, padding);
        return new System.Windows.Rect(
            bounds.Left,
            bounds.Top,
            Math.Max(1d, bounds.Width),
            Math.Max(1d, bounds.Height));
    }

    private static System.Windows.Rect VisibleLocalBounds(UIElement child)
    {
        var bounds = VisualTreeHelper.GetDescendantBounds(child);
        if (child is FrameworkElement element && element.ActualWidth > 0d && element.ActualHeight > 0d)
            bounds.Union(new System.Windows.Rect(0d, 0d, element.ActualWidth, element.ActualHeight));

        // Line/Polyline/Path visuals frequently have no explicit Width/Height. Their engineering
        // coordinates live in RenderedGeometry, so descendant layout bounds alone can omit the
        // exact leftmost or topmost wire segment.
        if (child is Shape shape && shape.RenderedGeometry is { } geometry && !geometry.Bounds.IsEmpty)
        {
            var geometryBounds = geometry.Bounds;
            geometryBounds.Inflate(Math.Max(2d, shape.StrokeThickness / 2d + 2d),
                Math.Max(2d, shape.StrokeThickness / 2d + 2d));
            bounds.Union(geometryBounds);
        }
        return bounds;
    }

    private static void ConfigurePageForContent(PdfPage page, System.Windows.Rect bounds)
    {
        // Use the A3 long edge for printing quality, but crop the other page edge to the actual
        // topology aspect ratio. This removes unused original-canvas area while preserving a
        // single, proportional drawing with no stretch.
        const double a3LongEdgePoints = 1190.55d;
        const double a3ShortEdgePoints = 841.89d;
        const double minimumShortEdgePoints = 360d;
        var aspect = bounds.Width / Math.Max(1d, bounds.Height);
        if (aspect >= 1d)
        {
            page.Width = XUnit.FromPoint(a3LongEdgePoints);
            page.Height = XUnit.FromPoint(Math.Clamp(a3LongEdgePoints / aspect,
                minimumShortEdgePoints, a3ShortEdgePoints));
        }
        else
        {
            page.Height = XUnit.FromPoint(a3LongEdgePoints);
            page.Width = XUnit.FromPoint(Math.Clamp(a3LongEdgePoints * aspect,
                minimumShortEdgePoints, a3ShortEdgePoints));
        }
    }

    private static ExportSurfaceNormalization NormalizeSurfaceForExport(
        FrameworkElement surface,
        System.Windows.Rect bounds)
    {
        if (surface is not System.Windows.Controls.Canvas canvas)
            return new ExportSurfaceNormalization(surface, bounds);

        var positions = canvas.Children.Cast<UIElement>()
            .Select(child => new CanvasChildPosition(
                child,
                System.Windows.Controls.Canvas.GetLeft(child),
                System.Windows.Controls.Canvas.GetTop(child),
                System.Windows.Controls.Canvas.GetRight(child),
                System.Windows.Controls.Canvas.GetBottom(child)))
            .ToArray();
        var originalWidth = surface.Width;
        var originalHeight = surface.Height;
        var shiftX = -bounds.Left;
        var shiftY = -bounds.Top;
        foreach (var position in positions)
        {
            System.Windows.Controls.Canvas.SetLeft(
                position.Child,
                (double.IsNaN(position.Left) ? 0d : position.Left) + shiftX);
            System.Windows.Controls.Canvas.SetTop(
                position.Child,
                (double.IsNaN(position.Top) ? 0d : position.Top) + shiftY);
            System.Windows.Controls.Canvas.SetRight(position.Child, double.NaN);
            System.Windows.Controls.Canvas.SetBottom(position.Child, double.NaN);
        }
        surface.Width = bounds.Width;
        surface.Height = bounds.Height;
        surface.UpdateLayout();
        return new ExportSurfaceNormalization(
            surface,
            new System.Windows.Rect(0d, 0d, bounds.Width, bounds.Height),
            positions,
            originalWidth,
            originalHeight);
    }

    private static void DrawVisualTiles(
        XGraphics graphics,
        FrameworkElement surface,
        System.Windows.Rect bounds,
        XRect destination)
    {
        // Rendering one very large bitmap forces the whole topology below 96 dpi once the
        // 9000 px safety cap is reached. Independent 300 dpi tiles keep every label and wire
        // sharp while bounding peak memory, then are placed seamlessly on the same PDF page.
        const double tileExtent = 1400d;
        const double overlap = 2d;
        const double renderScale = 300d / 96d;
        var scaleX = destination.Width / bounds.Width;
        var scaleY = destination.Height / bounds.Height;

        for (var y = bounds.Top; y < bounds.Bottom; y += tileExtent)
        {
            var coreHeight = Math.Min(tileExtent, bounds.Bottom - y);
            for (var x = bounds.Left; x < bounds.Right; x += tileExtent)
            {
                var coreWidth = Math.Min(tileExtent, bounds.Right - x);
                var renderBounds = new System.Windows.Rect(
                    Math.Max(bounds.Left, x - overlap),
                    Math.Max(bounds.Top, y - overlap),
                    coreWidth + (x > bounds.Left ? overlap : 0d) +
                    (x + coreWidth < bounds.Right ? overlap : 0d),
                    coreHeight + (y > bounds.Top ? overlap : 0d) +
                    (y + coreHeight < bounds.Bottom ? overlap : 0d));
                var png = RenderVisualRegion(surface, renderBounds, renderScale);
                using var imageStream = new MemoryStream(png, writable: false);
                using var image = XImage.FromStream(imageStream);
                graphics.DrawImage(
                    image,
                    destination.X + (renderBounds.Left - bounds.Left) * scaleX,
                    destination.Y + (renderBounds.Top - bounds.Top) * scaleY,
                    renderBounds.Width * scaleX,
                    renderBounds.Height * scaleY);
            }
        }
    }

    private static byte[] RenderVisualRegion(
        FrameworkElement surface,
        System.Windows.Rect bounds,
        double renderScale)
    {

        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, bounds.Width, bounds.Height));
            var brush = new VisualBrush(surface)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = bounds,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new System.Windows.Rect(0, 0, bounds.Width, bounds.Height),
                Stretch = Stretch.Fill
            };
            context.DrawRectangle(brush, null, new System.Windows.Rect(0, 0, bounds.Width, bounds.Height));
        }

        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(bounds.Width * renderScale)),
            Math.Max(1, (int)Math.Ceiling(bounds.Height * renderScale)),
            96d * renderScale,
            96d * renderScale,
            PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private sealed class ExportSurfaceNormalization : IDisposable
    {
        private readonly FrameworkElement _surface;
        private readonly IReadOnlyList<CanvasChildPosition> _positions;
        private readonly double _originalWidth;
        private readonly double _originalHeight;

        public ExportSurfaceNormalization(FrameworkElement surface, System.Windows.Rect bounds)
            : this(surface, bounds, [], surface.Width, surface.Height)
        {
        }

        public ExportSurfaceNormalization(
            FrameworkElement surface,
            System.Windows.Rect bounds,
            IReadOnlyList<CanvasChildPosition> positions,
            double originalWidth,
            double originalHeight)
        {
            _surface = surface;
            Bounds = bounds;
            _positions = positions;
            _originalWidth = originalWidth;
            _originalHeight = originalHeight;
        }

        public System.Windows.Rect Bounds { get; }

        public void Dispose()
        {
            foreach (var position in _positions)
            {
                System.Windows.Controls.Canvas.SetLeft(position.Child, position.Left);
                System.Windows.Controls.Canvas.SetTop(position.Child, position.Top);
                System.Windows.Controls.Canvas.SetRight(position.Child, position.Right);
                System.Windows.Controls.Canvas.SetBottom(position.Child, position.Bottom);
            }
            _surface.Width = _originalWidth;
            _surface.Height = _originalHeight;
            _surface.UpdateLayout();
        }
    }

    private sealed record CanvasChildPosition(
        UIElement Child,
        double Left,
        double Top,
        double Right,
        double Bottom);
}
