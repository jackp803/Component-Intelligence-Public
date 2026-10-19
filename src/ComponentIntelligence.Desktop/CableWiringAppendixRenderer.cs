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
    public SchematicGridBounds? SketchBounds { get; init; }
    public IReadOnlyList<SchematicCadPrimitive> SketchPrimitives { get; init; } = [];
}
public sealed record CableWiringAppendixReturnLink(string PageId, int PageNumber,
    SchematicPoint Destination, SchematicGridBounds Bounds)
{
    public string? CableReference { get; init; }
}

public static partial class CableWiringAppendixRenderer
{
    private static IReadOnlyList<CableWiringAppendixTile> PlanWiring(CableInstance cable, SchematicPage page, int sourcePageCount,
        IReadOnlyList<CableInstance>? members, double reservedRight = 0, CableWiringDiagramPresentation? preparedDiagram = null)
    {
        var grid = page.EffectiveGrid();
        var width = grid.Width - 8;
        var diagram = preparedDiagram ?? CableWiringAppendixLayout.Build(cable, width);
        var memberLines = BuildMemberLines(members ?? new[] { cable }, width);
        var headerLimit = Math.Max(1, (int)((grid.Height - (page.TemplateGeometry is null ? 36 : 83)) * .22 / 5));
        var remaining = memberLines.Skip(headerLimit).ToArray();
        if (remaining.Length > 0)
        {
            memberLines = memberLines.Take(headerLimit).ToList();
            memberLines.Add("其餘適用線材／備註列於後續資料頁");
        }
        var footerColumns = Math.Max(1, (int)(width / 44));
        var footerHeight = Math.Max(8, Math.Ceiling((double)sourcePageCount / footerColumns) * 6 + 4);
        var footerY = grid.Y + grid.Height - footerHeight - (page.TemplateGeometry is null ? 8 : 55);
        double HeaderHeight(string value, double size)
        {
            var text = new TextBlock { Text = value, FontFamily = new("Microsoft JhengHei"), FontSize = size,
                Width = width * 4, TextWrapping = TextWrapping.Wrap };
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return text.DesiredSize.Height / 4;
        }
        var specificationY = grid.Y + 2 + Math.Max(8, HeaderHeight(Title(cable, members is { Count: > 1 }), 15) + 2);
        var memberY = specificationY + Math.Max(9, HeaderHeight(MemberHeading(1000, 1000), 12) + 2);
        var top = Math.Max(grid.Y + 27 + memberLines.Count * 5, memberY + Math.Max(1, memberLines.Count) * 5 + 5);
        var content = new SchematicGridBounds(grid.X + 4, top, width - reservedRight, footerY - top - 3);
        if (content.Width < 45 || content.Height < 30) throw new InvalidOperationException("紙張太小，無法清楚容納線材接線图與返回連結；請增加紙張尺寸。");
        var minimumTextHeight = 3.2;
        (SchematicCadPrimitive Primitive, Size Size)[] MeasureTexts(double scale) => diagram.Primitives.Where(p => p.Kind is "TEXT" or "MTEXT").Select(p =>
        {
            var text = CableWiringVisualRenderer.CreateText(p, 4, minimumTextHeight, scale);
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return (Primitive: p, Size: new Size(text.DesiredSize.Width / 4, text.DesiredSize.Height / 4));
        }).ToArray();
        var texts = MeasureTexts(1);
        SchematicGridBounds Bounds(double scale)
        {
            var right = Math.Max(diagram.Bounds.X + diagram.Bounds.Width,
                texts.Max(t => t.Primitive.Start.X + t.Size.Width / scale + 3));
            var bottom = Math.Max(diagram.Bounds.Y + diagram.Bounds.Height,
                texts.Max(t => t.Primitive.Start.Y + t.Size.Height / scale + 3));
            return diagram.Bounds with { Width = right - diagram.Bounds.X, Height = bottom - diagram.Bounds.Y };
        }
        bool LabelsFit(double scale)
        {
            foreach (var port in diagram.PortBounds.Values)
            {
                var labels = texts.Where(t => t.Primitive.Start.X >= port.X && t.Primitive.Start.X < port.X + port.Width &&
                    t.Primitive.Start.Y >= port.Y && t.Primitive.Start.Y < port.Y + port.Height)
                    .Select(t => new Rect(t.Primitive.Start.X * scale, t.Primitive.Start.Y * scale, t.Size.Width, t.Size.Height)).ToArray();
                if (labels.Any(r => r.Left < port.X * scale || r.Top < port.Y * scale ||
                    r.Right > (port.X + port.Width) * scale - 1 || r.Bottom > (port.Y + port.Height) * scale - 1)) return false;
                for (var i = 0; i < labels.Length; i++)
                for (var j = i + 1; j < labels.Length; j++)
                    if (labels[i].IntersectsWith(labels[j])) return false;
            }
            return true;
        }
        // Fit only the export viewport. Text keeps its paper size, and the saved
        // connector bounds, exact Pin identities, and wiring routes stay untouched.
        for (var step = 0; step <= 25; step++)
        {
            var scale = 1 - step / 100.0;
            // Wrapping follows the scaled connector width; paper font sizes do not shrink.
            texts = MeasureTexts(scale);
            var bounds = Bounds(scale);
            if (bounds.Width * scale > content.Width || bounds.Height * scale > content.Height || !LabelsFit(scale)) continue;
            var view = new SchematicGridBounds(bounds.X - (content.Width / scale - bounds.Width) / 2,
                bounds.Y, content.Width / scale, content.Height / scale);
            return [new(diagram, view, content, footerY, footerColumns, 1, 1) { GeometryScale = scale, MemberLines = memberLines,
                RemainingMemberLines = remaining, SharedWiring = members is { Count: > 1 }, SpecificationY = specificationY, MemberY = memberY, HeaderWidth = width }];
        }
        texts = MeasureTexts(1);
        if (!LabelsFit(1))
        {
            // A narrow saved connector may have no room for larger labels. Preserve
            // its original readable type rather than pushing text onto its Pins.
            minimumTextHeight = 0;
            texts = MeasureTexts(1);
        }
        var tiledBounds = Bounds(1);
        if (texts.Any(t => t.Size.Width + 2 >= content.Width || t.Size.Height + 2 >= content.Height))
            throw new InvalidOperationException("線材接線圖的文字長於可用版面，請縮短標記或使用較大紙張；未輸出裁切的文字。");
        // A label touching a seam must appear in full on at least one tile.
        var overlapX = Math.Max(8, texts.Max(t => t.Size.Width) + 2);
        var overlapY = Math.Max(8, texts.Max(t => t.Size.Height) + 2);
        var columns = Math.Max(1, (int)Math.Ceiling((tiledBounds.Width - overlapX) / (content.Width - overlapX)));
        var rows = Math.Max(1, (int)Math.Ceiling((tiledBounds.Height - overlapY) / (content.Height - overlapY)));
        if ((long)columns * rows > 1000) throw new InvalidOperationException("線材接線圖超過 1000 個分頁；請整理版面或使用較大紙張，未省略接點。");
        var tiles = new List<CableWiringAppendixTile>();
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
            tiles.Add(new(diagram, new(tiledBounds.X + column * (content.Width - overlapX),
                tiledBounds.Y + row * (content.Height - overlapY), content.Width, content.Height),
                content, footerY, footerColumns, tiles.Count + 1, rows * columns) { MinimumTextHeight = minimumTextHeight, MemberLines = memberLines,
                    RemainingMemberLines = tiles.Count == 0 ? remaining : [], SharedWiring = members is { Count: > 1 },
                    SpecificationY = specificationY, MemberY = memberY, HeaderWidth = width });
        return tiles;
    }

    public static IReadOnlyList<CableWiringAppendixReturnLink> ReturnLinks(CableWiringAppendixTile tile,
        IReadOnlyList<CableWiringAppendixSourceLink> sources, IReadOnlyDictionary<string, int> pageIndices, double margin)
    {
        return sources.DistinctBy(s => (s.SourcePageId, s.CableInstanceId)).Select((source, index) => new CableWiringAppendixReturnLink(
            source.SourcePageId, pageIndices[source.SourcePageId] + 1, source.ReturnPosition,
            new(tile.ContentBounds.X + index % tile.FooterColumns * 44, tile.FooterY + index / tile.FooterColumns * 6, 42, 5)) { CableReference = source.CableReference }).ToArray();
    }

    public static BitmapSource Render(CableInstance cable, SchematicPage page, CableWiringAppendixTile tile,
        IReadOnlyList<CableWiringAppendixReturnLink> returns, BitmapSource background)
    {
        const double pixelsPerMm = 4;
        var grid = page.EffectiveGrid();
        var left = tile.ContentBounds.X;
        var canvas = new Canvas { Width = page.Width * pixelsPerMm, Height = page.Height * pixelsPerMm, Background = Brushes.White };
        canvas.Children.Add(new Image { Source = background, Width = canvas.Width, Height = canvas.Height });
        void Text(string value, double x, double y, double size = 12, Brush? brush = null)
        {
            var text = new TextBlock { Text = value, FontFamily = new("Microsoft JhengHei"), FontSize = size,
                Foreground = brush ?? Brushes.Black, Background = Brushes.White, TextWrapping = TextWrapping.Wrap,
                Width = (left + tile.HeaderWidth - x) * pixelsPerMm };
            Canvas.SetLeft(text, x * pixelsPerMm); Canvas.SetTop(text, y * pixelsPerMm); canvas.Children.Add(text);
        }
        Text(Title(cable, tile.SharedWiring), left, grid.Y + 2, 15);
        Text(MemberHeading(tile.TileIndex, tile.TileCount), left, tile.SpecificationY);
        if (tile.MemberLines.Count == 0) Text(cable.ProvidedLengthMm is double length ? length.ToString("0.###") + " mm" : "長度待填", left, tile.MemberY, 11);
        for (var i = 0; i < tile.MemberLines.Count; i++) Text(tile.MemberLines[i], left, tile.MemberY + i * 5, 12);
        var clip = new Canvas { Width = tile.ContentBounds.Width * pixelsPerMm, Height = tile.ContentBounds.Height * pixelsPerMm,
            Background = Brushes.White, ClipToBounds = true };
        var geometryPixelsPerMm = pixelsPerMm * tile.GeometryScale;
        var diagram = new Canvas { RenderTransform = new TranslateTransform(-tile.ViewBounds.X * geometryPixelsPerMm, -tile.ViewBounds.Y * geometryPixelsPerMm) };
        CableWiringVisualRenderer.DrawDiagram(diagram, tile.Diagram, geometryPixelsPerMm,
            textPixelsPerMm: pixelsPerMm, minimumTextHeight: tile.MinimumTextHeight);
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
        return Rasterize(canvas, page);
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
