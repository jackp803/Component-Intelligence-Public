using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public static partial class CableWiringAppendixRenderer
{
    private static string MemberHeading(int index, int count) => $"接線圖 {index}/{count} / Reference、實際線長、規格、備註";

    private static List<string> BuildMemberLines(IReadOnlyList<CableInstance> members, double width)
    {
        var lines = new List<string>();
        var limit = Math.Max(1, (int)(width / 3.2));
        void Field(string prefix, string value)
        {
            // Repeat ownership on wrapped rows, including rows on continuation pages.
            foreach (var paragraph in value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var text = System.Globalization.StringInfo.ParseCombiningCharacters(paragraph);
                var prefixLength = prefix.Length < limit ? prefix.Length : 0;
                if (prefixLength == 0)
                {
                    for (var i = 0; i < prefix.Length; i += limit) lines.Add(prefix.Substring(i, Math.Min(limit, prefix.Length - i)));
                }
                var length = Math.Max(1, limit - prefixLength);
                if (text.Length == 0) lines.Add(prefixLength > 0 ? prefix : "");
                for (var i = 0; i < text.Length; i += length)
                {
                    var end = i + length < text.Length ? text[i + length] : paragraph.Length;
                    lines.Add((prefixLength > 0 ? prefix : "") + paragraph.Substring(text[i], end - text[i]));
                }
            }
        }
        foreach (var member in members)
        {
            var reference = member.ReferenceDesignator ?? "Reference 未設定";
            var length = member.ProvidedLengthMm?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                (member.ProvidedLengthMm.HasValue ? " mm" : "長度待填");
            Field(reference + " / ", length);
            Field(reference + " / 規格：", string.IsNullOrWhiteSpace(member.Specification) ? "待確認" : member.Specification);
            Field(reference + " / 備註：", string.IsNullOrWhiteSpace(member.Notes) ? "未填" : member.Notes);
        }
        return lines;
    }

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
        Text("適用線材 / Reference、實際線長、規格、備註", grid.X + 4, grid.Y + 3);
        for (var i = 0; i < lines.Count; i++) Text(lines[i], grid.X + 4, grid.Y + 18 + i * 5);
        var back = MemberReturnBounds(page); Text("返回接線圖", back.X, back.Y, true);
        return Rasterize(canvas, page);
    }
}
