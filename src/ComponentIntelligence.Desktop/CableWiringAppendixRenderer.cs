using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public sealed record CableWiringAppendixTile(CableWiringDiagramPresentation Diagram,
    SchematicGridBounds ViewBounds, SchematicGridBounds ContentBounds, double FooterY,
    int FooterColumns, int TileIndex, int TileCount);
public sealed record CableWiringAppendixReturnLink(string PageId, int PageNumber,
    SchematicPoint Destination, SchematicGridBounds Bounds);

public static class CableWiringAppendixRenderer
{
    public static IReadOnlyList<CableWiringAppendixTile> Plan(CableInstance cable, SchematicPage page, int sourcePageCount)
    {
        var draft = new CableManufacturingEditorService().Prepare(cable);
        var diagram = new CableWiringEditorService().Build(draft);
        var width = page.Width - 2 * page.Margin - 8;
        var footerColumns = Math.Max(1, (int)(width / 44));
        var footerHeight = Math.Max(8, Math.Ceiling((double)sourcePageCount / footerColumns) * 6 + 4);
        var footerY = page.Height - page.Margin - footerHeight - (page.TemplateGeometry is null ? 8 : 55);
        var content = new SchematicGridBounds(page.Margin + 4, page.Margin + 27, width, footerY - page.Margin - 30);
        if (content.Width < 45 || content.Height < 30) throw new InvalidOperationException("紙張太小，無法清楚容納線材接線图與返回連結；請增加紙張尺寸。");
        const double overlap = 8;
        var columns = Math.Max(1, (int)Math.Ceiling((diagram.Bounds.Width - overlap) / (content.Width - overlap)));
        var rows = Math.Max(1, (int)Math.Ceiling((diagram.Bounds.Height - overlap) / (content.Height - overlap)));
        if ((long)columns * rows > 1000) throw new InvalidOperationException("線材接線圖超過 1000 個分頁；請整理版面或使用較大紙張，未省略接點。");
        var tiles = new List<CableWiringAppendixTile>();
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
            tiles.Add(new(diagram, new(diagram.Bounds.X + column * (content.Width - overlap),
                diagram.Bounds.Y + row * (content.Height - overlap), content.Width, content.Height),
                content, footerY, footerColumns, tiles.Count + 1, rows * columns));
        return tiles;
    }

    public static IReadOnlyList<CableWiringAppendixReturnLink> ReturnLinks(CableWiringAppendixTile tile,
        IReadOnlyList<CableWiringAppendixSourceLink> sources, IReadOnlyDictionary<string, int> pageIndices, double margin)
    {
        return sources.DistinctBy(s => s.SourcePageId).Select((source, index) => new CableWiringAppendixReturnLink(
            source.SourcePageId, pageIndices[source.SourcePageId] + 1, source.ReturnPosition,
            new(margin + 4 + index % tile.FooterColumns * 44, tile.FooterY + index / tile.FooterColumns * 6, 42, 5))).ToArray();
    }

    public static BitmapSource Render(CableInstance cable, SchematicPage page, CableWiringAppendixTile tile,
        IReadOnlyList<CableWiringAppendixReturnLink> returns, BitmapSource background)
    {
        const double pixelsPerMm = 4;
        var canvas = new Canvas { Width = page.Width * pixelsPerMm, Height = page.Height * pixelsPerMm, Background = Brushes.White };
        canvas.Children.Add(new Image { Source = background, Width = canvas.Width, Height = canvas.Height });
        void Text(string value, double x, double y, double size = 12, Brush? brush = null)
        {
            var text = new TextBlock { Text = value, FontFamily = new("Microsoft JhengHei"), FontSize = size,
                Foreground = brush ?? Brushes.Black, Background = Brushes.White, TextWrapping = TextWrapping.Wrap,
                Width = (page.Width - page.Margin - x) * pixelsPerMm };
            Canvas.SetLeft(text, x * pixelsPerMm); Canvas.SetTop(text, y * pixelsPerMm); canvas.Children.Add(text);
        }
        Text((cable.ReferenceDesignator ?? "Reference 未設定") + " / " + (cable.DisplayName ?? cable.CableDefinitionId), page.Margin + 4, page.Margin + 2, 15);
        Text($"接線圖 {tile.TileIndex}/{tile.TileCount} / {cable.ProvidedLengthMm?.ToString() ?? "待確認"} mm / {cable.Specification ?? "規格待確認"}", page.Margin + 4, page.Margin + 10);
        Text((cable.ArchivedCable!.MappingConfirmed ? "接法已核對" : "草稿 / 接法未確認") + " / " + cable.CableInstanceId,
            page.Margin + 4, page.Margin + 17, 11, cable.ArchivedCable.MappingConfirmed ? Brushes.DarkGreen : Brushes.Firebrick);
        var clip = new Canvas { Width = tile.ContentBounds.Width * pixelsPerMm, Height = tile.ContentBounds.Height * pixelsPerMm,
            Background = Brushes.White, ClipToBounds = true };
        var diagram = new Canvas { RenderTransform = new TranslateTransform(-tile.ViewBounds.X * pixelsPerMm, -tile.ViewBounds.Y * pixelsPerMm) };
        CableWiringVisualRenderer.Draw(diagram, tile.Diagram.Primitives, pixelsPerMm);
        clip.Children.Add(diagram); Canvas.SetLeft(clip, tile.ContentBounds.X * pixelsPerMm); Canvas.SetTop(clip, tile.ContentBounds.Y * pixelsPerMm); canvas.Children.Add(clip);
        foreach (var link in returns) Text("返回工程圖 " + link.PageNumber, link.Bounds.X, link.Bounds.Y, 11, Brushes.DarkCyan);
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
