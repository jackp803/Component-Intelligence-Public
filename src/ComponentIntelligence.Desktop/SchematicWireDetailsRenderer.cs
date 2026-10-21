using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public sealed record SchematicWireDetailsRow(SchematicWireLegendEntry Entry, double Height);
public sealed record SchematicWireDetailsBlock(SchematicWireLegendSpecification Specification, IReadOnlyList<SchematicWireDetailsRow> Rows);
public sealed record SchematicWireDetailsTile(string SourcePageId, int SourcePageNumber,
    IReadOnlyList<SchematicWireDetailsBlock> Blocks, int Index, int Count);

public static class SchematicWireDetailsRenderer
{
    private const double Pixels = 4;
    private const double CellBorderMm = .25;
    private static readonly double[] Fractions = [.04, .18, .23, .30, .07, .18];
    private static readonly string[] Headers = ["索引", "完整線號", "接點", "完整功能", "線色", "核對備註"];

    private static string[] Cells(SchematicWireLegendEntry entry) =>
        [entry.Index, entry.Designation, entry.Contacts, entry.Functions, entry.Color, entry.Review];

    private static TextBlock Cell(string value, double width, double font = 12) => new()
    {
        Text = value, Width = width * Pixels, FontSize = font, FontFamily = new("Microsoft JhengHei"),
        TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black, Padding = new Thickness(4)
    };

    private static double RowHeight(IReadOnlyList<string> cells, double width) => Math.Max(8,
        cells.Select((value, index) =>
        {
            var cell = Cell(value, width * Fractions[index] - CellBorderMm);
            cell.Measure(new Size(cell.Width, double.PositiveInfinity));
            return cell.DesiredSize.Height / Pixels + CellBorderMm;
        }).Max());

    private static double HeaderHeight(double width) => RowHeight(Headers, width);

    public static IReadOnlyList<SchematicWireDetailsTile> Plan(IReadOnlyList<SchematicWireLegendEntry> entries,
        SchematicPage page, int sourcePageNumber)
    {
        if (entries.Count == 0) return [];
        var width = page.Width - 2 * page.Margin - 8;
        var top = page.Margin + 22;
        var bottom = page.Height - page.Margin - (page.TemplateGeometry is null ? 16 : 70);
        var available = bottom - top;
        if (width < 100 || available < 40) throw new InvalidOperationException("紙張太小，無法清楚輸出導線明細。");
        var headerHeight = HeaderHeight(width);
        var tiles = new List<List<SchematicWireDetailsBlock>>();
        var blocks = new List<SchematicWireDetailsBlock>();
        var used = 0d;
        foreach (var group in SchematicWireLegendPresentation.Groups(entries))
        {
            var title = Cell(group.Specification.Text, width, 13);
            title.FontWeight = FontWeights.SemiBold;
            title.Measure(new Size(title.Width, double.PositiveInfinity));
            var groupHeight = title.DesiredSize.Height / Pixels;
            var rows = new List<SchematicWireDetailsRow>();
            foreach (var entry in group.Entries)
            {
                var height = RowHeight(Cells(entry), width);
                if (height + groupHeight + headerHeight > available)
                    throw new InvalidOperationException("導線明細單列內容超過紙張，請使用較大紙張；未截斷文字。");
                var heading = rows.Count == 0 ? groupHeight + headerHeight : 0;
                if (used + heading + height > available)
                {
                    if (rows.Count > 0) blocks.Add(new(group.Specification, rows.ToArray()));
                    tiles.Add(blocks); blocks = []; rows.Clear(); used = 0;
                    heading = groupHeight + headerHeight;
                }
                used += heading + height;
                rows.Add(new(entry, height));
            }
            if (rows.Count > 0) blocks.Add(new(group.Specification, rows.ToArray()));
        }
        if (blocks.Count > 0) tiles.Add(blocks);
        if (tiles.Count > 1000) throw new InvalidOperationException("導線明細超過 1000 頁，未省略資料。");
        return tiles.Select((list, index) => new SchematicWireDetailsTile(page.PageId, sourcePageNumber, list, index + 1, tiles.Count)).ToArray();
    }

    public static BitmapSource Render(SchematicPage page, SchematicWireDetailsTile tile, BitmapSource background)
    {
        var canvas = new Canvas { Width = page.Width * Pixels, Height = page.Height * Pixels, Background = Brushes.White };
        canvas.Children.Add(new Image { Source = background, Width = canvas.Width, Height = canvas.Height });
        var left = page.Margin + 4;
        var width = page.Width - 2 * page.Margin - 8;
        void Add(TextBlock text, double x, double y)
        { Canvas.SetLeft(text, x * Pixels); Canvas.SetTop(text, y * Pixels); canvas.Children.Add(text); }
        Add(Cell($"導線明細 / 工程圖 {tile.SourcePageNumber} / {tile.Index}/{tile.Count}", width, 16), left, page.Margin + 2);
        var y = page.Margin + 22;
        foreach (var block in tile.Blocks)
        {
            var title = Cell(block.Specification.Text, width, 13);
            title.FontWeight = FontWeights.SemiBold;
            title.Measure(new Size(title.Width, double.PositiveInfinity));
            Add(title, left, y); y += title.DesiredSize.Height / Pixels;
            void Row(IReadOnlyList<string> cells, double height, bool header)
            {
                var x = left;
                for (var i = 0; i < cells.Count; i++)
                {
                    var cellWidth = width * Fractions[i];
                    var border = new Border { Width = cellWidth * Pixels, Height = height * Pixels,
                        BorderBrush = Brushes.Gray, BorderThickness = new Thickness(.5),
                        Background = header ? Brushes.WhiteSmoke : Brushes.White, Child = Cell(cells[i], cellWidth - CellBorderMm) };
                    Canvas.SetLeft(border, x * Pixels); Canvas.SetTop(border, y * Pixels); canvas.Children.Add(border);
                    x += cellWidth;
                }
                y += height;
            }
            Row(Headers, HeaderHeight(width), true);
            foreach (var row in block.Rows) Row(Cells(row.Entry), row.Height, false);
        }
        var footer = Cell("返回工程圖 " + tile.SourcePageNumber, 55, 12);
        footer.Foreground = Brushes.DarkCyan;
        Add(footer, left, page.Height - page.Margin - (page.TemplateGeometry is null ? 12 : 66));
        canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height)); canvas.UpdateLayout();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawRectangle(new VisualBrush(canvas) { Stretch = Stretch.Fill }, null,
            new Rect(0, 0, page.Width / 25.4 * 96, page.Height / 25.4 * 96));
        const double dpi = 300;
        var w = checked((int)Math.Ceiling(page.Width / 25.4 * dpi)); var h = checked((int)Math.Ceiling(page.Height / 25.4 * dpi));
        if ((long)w * h > 100000000) throw new InvalidOperationException("導線明細超過 300 DPI 大小上限。");
        var bitmap = new RenderTargetBitmap(w, h, dpi, dpi, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}
