using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public static partial class CableWiringAppendixRenderer
{
    public static IReadOnlyList<IReadOnlyList<string>> MemberPages(CableWiringAppendixTile tile, SchematicPage page)
    {
        var count = (int)((MemberReturnBounds(page).Y - page.EffectiveGrid().Y - 24) / 5);
        if (count < 1) throw new InvalidOperationException("紙張太小，無法容納線材資料續頁。");
        return tile.RemainingMemberLines.Chunk(count).Select(lines => (IReadOnlyList<string>)lines).ToArray();
    }

    public static SchematicGridBounds MemberReturnBounds(SchematicPage page)
    {
        var grid = page.EffectiveGrid();
        return new(grid.X + 4, grid.Y + grid.Height - (page.TemplateGeometry is null ? 12 : 66), 60, 6);
    }

    public static BitmapSource RenderMembers(SchematicPage page, IReadOnlyList<string> lines, BitmapSource background)
    {
        var canvas = new Canvas { Width = page.Width * 4, Height = page.Height * 4, Background = Brushes.White };
        var grid = page.EffectiveGrid();
        canvas.Children.Add(new Image { Source = background, Width = canvas.Width, Height = canvas.Height });
        void Text(string value, double x, double y, bool link = false)
        {
            var text = new TextBlock { Text = value, FontFamily = new("Microsoft JhengHei"), FontSize = 12,
                Foreground = link ? Brushes.DarkCyan : Brushes.Black, Background = Brushes.White };
            Canvas.SetLeft(text, x * 4); Canvas.SetTop(text, y * 4); canvas.Children.Add(text);
        }
        Text("適用線材 / Reference、長度、備註", grid.X + 4, grid.Y + 3);
        for (var i = 0; i < lines.Count; i++) Text(lines[i], grid.X + 4, grid.Y + 18 + i * 5);
        var back = MemberReturnBounds(page); Text("返回接線圖", back.X, back.Y, true);
        return Rasterize(canvas, page);
    }
}
