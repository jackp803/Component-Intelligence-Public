using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public sealed record CableWiringAppendixTile(CableWiringDiagramPresentation Diagram,
    SchematicGridBounds ViewBounds, SchematicGridBounds ContentBounds, double FooterY,
    int FooterColumns, int TileIndex, int TileCount)
{
    public double GeometryScale { get; init; } = 1;
    public double MinimumTextHeight { get; init; } = 3.2;
    public IReadOnlyList<string> MemberLines { get; init; } = [];
    public bool SharedWiring { get; init; }
    public IReadOnlyList<string> RemainingMemberLines { get; init; } = [];
    public double SpecificationY { get; init; }
    public double MemberY { get; init; }
    public double HeaderWidth { get; init; }
    public double FooterX { get; init; }
    public SchematicGridBounds? SketchBounds { get; init; }
    public IReadOnlyList<SchematicCadPrimitive> SketchPrimitives { get; init; } = [];
    public bool ScaleTextWithGeometry { get; init; }
    public double TitleFontHeight { get; init; } = 3.75;
    public SchematicGridBounds? MetadataBounds { get; init; }
    public IReadOnlyList<CableWiringAppendixLabel> MemberLabels { get; init; } = [];
}
public sealed record CableWiringAppendixReturnLink(string PageId, int PageNumber,
    SchematicPoint Destination, SchematicGridBounds Bounds)
{
    public string? CableReference { get; init; }
}

public static partial class CableWiringAppendixRenderer
{

    public static IReadOnlyList<CableWiringAppendixReturnLink> ReturnLinks(CableWiringAppendixTile tile,
        IReadOnlyList<CableWiringAppendixSourceLink> sources, IReadOnlyDictionary<string, int> pageIndices, double margin)
    {
        return sources.DistinctBy(s => (s.SourcePageId, s.CableInstanceId)).Select((source, index) => new CableWiringAppendixReturnLink(
            source.SourcePageId, pageIndices[source.SourcePageId] + 1, source.ReturnPosition,
            new(tile.FooterX + index % tile.FooterColumns * 44, tile.FooterY + index / tile.FooterColumns * 6, 42, 5)) { CableReference = source.CableReference }).ToArray();
    }

    public static BitmapSource Render(CableInstance cable, SchematicPage page, CableWiringAppendixTile tile,
        IReadOnlyList<CableWiringAppendixReturnLink> returns, BitmapSource background)
        => Rasterize(CreateCanvas(cable, page, tile, returns, background), page);

    public static Canvas CreateCanvas(CableInstance cable, SchematicPage page, CableWiringAppendixTile tile,
        IReadOnlyList<CableWiringAppendixReturnLink> returns, BitmapSource background)
    {
        const double pixelsPerMm = 4;
        var grid = page.EffectiveGrid();
        var left = grid.X + 4;
        var canvas = new Canvas { Width = page.Width * pixelsPerMm, Height = page.Height * pixelsPerMm, Background = Brushes.White };
        canvas.Children.Add(new Image { Source = background, Width = canvas.Width, Height = canvas.Height });
        void Text(string value, double x, double y, double size = 12, Brush? brush = null)
        {
            var text = new TextBlock { Text = value, FontFamily = new("Microsoft JhengHei"), FontSize = size,
                Foreground = brush ?? Brushes.Black, Background = Brushes.White, TextWrapping = TextWrapping.Wrap,
                Width = (left + tile.HeaderWidth - x) * pixelsPerMm };
            Canvas.SetLeft(text, x * pixelsPerMm); Canvas.SetTop(text, y * pixelsPerMm); canvas.Children.Add(text);
        }
        Text(Title(cable, tile.SharedWiring), left, grid.Y + 2, tile.TitleFontHeight * 4);
        Text(MemberHeading(tile.TileIndex, tile.TileCount), left, tile.SpecificationY);
        foreach (var label in tile.MemberLabels)
        {
            var text = new TextBlock { Text = label.Text, Width = label.Bounds.Width * 4, FontSize = label.FontHeight * 4,
                FontFamily = new("Microsoft JhengHei"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black };
            Canvas.SetLeft(text, label.Bounds.X * 4); Canvas.SetTop(text, label.Bounds.Y * 4); canvas.Children.Add(text);
        }
        var clip = new Canvas { Width = tile.ContentBounds.Width * pixelsPerMm, Height = tile.ContentBounds.Height * pixelsPerMm,
            Background = Brushes.White, ClipToBounds = true };
        var geometryPixelsPerMm = pixelsPerMm * tile.GeometryScale;
        var diagram = new Canvas { RenderTransform = new TranslateTransform(-tile.ViewBounds.X * geometryPixelsPerMm, -tile.ViewBounds.Y * geometryPixelsPerMm) };
        CableWiringVisualRenderer.DrawDiagram(diagram, tile.Diagram, geometryPixelsPerMm,
            textPixelsPerMm: tile.ScaleTextWithGeometry ? geometryPixelsPerMm : pixelsPerMm, minimumTextHeight: tile.MinimumTextHeight);
        clip.Children.Add(diagram); Canvas.SetLeft(clip, tile.ContentBounds.X * pixelsPerMm); Canvas.SetTop(clip, tile.ContentBounds.Y * pixelsPerMm); canvas.Children.Add(clip);
        if (tile.SketchPrimitives.Count > 0)
        {
            var sketch = SchematicWorkspaceControl.CadCanvas(new SchematicCadAsset { SourceSha256 = "", Width = page.Width,
                Height = page.Height, Primitives = tile.SketchPrimitives });
            sketch.RenderTransform = new ScaleTransform(pixelsPerMm / 3, pixelsPerMm / 3);
            canvas.Children.Add(sketch);
        }
        foreach (var link in returns)
        {
            var text = new TextBlock { Text = string.IsNullOrWhiteSpace(link.CableReference) ? "返回工程圖 " + link.PageNumber : "返回 " + link.CableReference + " / " + link.PageNumber,
                Width = link.Bounds.Width * 4, FontFamily = new("Microsoft JhengHei"), FontSize = 11, Foreground = Brushes.DarkCyan,
                TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, Background = Brushes.White };
            Canvas.SetLeft(text, link.Bounds.X * 4); Canvas.SetTop(text, link.Bounds.Y * 4); canvas.Children.Add(text);
        }
        canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height)); canvas.UpdateLayout();
        return canvas;
    }

    private static string Title(CableInstance cable, bool shared) =>
        (shared ? "共用接法" : cable.ReferenceDesignator ?? "Reference 未設定") + " / " + (cable.DisplayName ?? cable.CableDefinitionId);

    private static BitmapSource Rasterize(Canvas canvas, SchematicPage page)
    {
        canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height)); canvas.UpdateLayout();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawRectangle(new VisualBrush(canvas) { Stretch = Stretch.Fill }, null,
            new Rect(0, 0, page.Width / 25.4 * 96, page.Height / 25.4 * 96));
        const double dpi = 300;
        var width = checked((int)Math.Ceiling(page.Width / 25.4 * dpi)); var height = checked((int)Math.Ceiling(page.Height / 25.4 * dpi));
        if ((long)width * height > 100000000) throw new InvalidOperationException("接線圖超過 300 DPI 輸出大小上限，未縮小或裁切。");
        var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}
