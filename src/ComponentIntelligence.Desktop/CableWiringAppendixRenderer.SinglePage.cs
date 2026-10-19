using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public sealed record CableWiringAppendixLabel(string Text, SchematicGridBounds Bounds, double FontHeight);

public static partial class CableWiringAppendixRenderer
{
    private static IReadOnlyList<CableWiringAppendixTile> PlanSinglePage(CableInstance cable, SchematicPage page,
        int sourcePageCount, IReadOnlyList<CableInstance>? members)
    {
        // Fit the complete assembly on one sheet; never tile routes or send instance labels to an appendix.
        members ??= [cable];
        var grid = page.EffectiveGrid(); var width = grid.Width - 8;
        var columns = Math.Max(1, (int)(width / 44));
        var footerHeight = Math.Max(8, Math.Ceiling((double)sourcePageCount / columns) * 6 + 4);
        var bottom = grid.Y + grid.Height - (page.TemplateGeometry is null ? 8 : 55);
        var slots = page.TitleBlockSlots.Count > 0 ? page.TitleBlockSlots :
            page.TemplateGeometry is { } geometry ? SchematicTitleBlock.DetectSlots(geometry) : [];
        if (slots.Count > 0) bottom = Math.Min(bottom, slots.Min(s => s.Bounds.Y) - 3);
        var footerY = bottom - footerHeight;
        double Height(string text, double w, double font) => MeasureLabel(text, w, font);
        var titleFont = 3.75;
        while (Height(Title(cable, members.Count > 1), width, titleFont) > (footerY - grid.Y) * .16) titleFont *= .9;
        var specificationY = grid.Y + 2 + Math.Max(8, Height(Title(cable, members.Count > 1), width, titleFont) + 2);
        var top = specificationY + 9;
        var available = new SchematicGridBounds(grid.X + 4, top, width, footerY - top - 3);
        if (available.Width < 45 || available.Height < 30)
            throw new InvalidOperationException("紙張的有效區域不足，無法完整放入接線圖、標籤與示意圖。");
        var leftWidth = (width - 7) * .58;
        var right = new SchematicGridBounds(available.X + leftWidth + 7, top, width - leftWidth - 7, available.Height);
        var diagram = CableWiringAppendixLayout.Build(cable, width);
        var view = new SchematicGridBounds(diagram.Bounds.X - 2, diagram.Bounds.Y - 2,
            diagram.Bounds.Width + 4, diagram.Bounds.Height + 4);
        var scale = Math.Min(1, Math.Min((leftWidth - 4) / view.Width, (available.Height - 4) / view.Height));
        var content = new SchematicGridBounds(available.X + (leftWidth - view.Width * scale) / 2,
            top + (available.Height - view.Height * scale) / 2, view.Width * scale, view.Height * scale);
        var variants = SketchVariants(members);
        var sketchHeight = variants.Count == 0 ? 0 : Math.Min(75, right.Height * .36);
        var metadata = right with { Height = right.Height - sketchHeight - (variants.Count == 0 ? 0 : 7) };
        var values = MemberFields(members).Select(field => field.Prefix + field.Value).ToArray();
        var font = 3d;
        double TotalHeight() => values.Sum(value => Height(value, metadata.Width, font) + font * .3);
        while (TotalHeight() > metadata.Height)
        {
            font *= .9;
            if (font < .025) throw new InvalidOperationException("線材文字過多，無法在單頁內完整呈現；未裁切或省略內容。");
        }
        var labels = new List<CableWiringAppendixLabel>(); var y = metadata.Y;
        foreach (var value in values)
        {
            var h = Height(value, metadata.Width, font);
            labels.Add(new(value, new(metadata.X, y, metadata.Width, h), font)); y += h + font * .3;
        }
        var tile = new CableWiringAppendixTile(diagram, view, content, footerY, columns, 1, 1) {
            GeometryScale = scale, MinimumTextHeight = 3.2, ScaleTextWithGeometry = true,
            SharedWiring = members.Count > 1, HeaderWidth = width, FooterX = grid.X + 4, TitleFontHeight = titleFont,
            SpecificationY = specificationY, MemberY = metadata.Y, MemberLabels = labels,
            MemberLines = BuildMemberLines(members, width), MetadataBounds = metadata
        };
        if (variants.Count == 0) return [tile];
        var region = new Rect(right.X, right.Y + right.Height - sketchHeight, right.Width, sketchHeight);
        var fitted = PlaceSketch(variants, tile, region, members.Count > 1, page, Math.Min(1, font / 3));
        return [fitted ?? throw new InvalidOperationException("無法在同頁安全放入完整示意圖；未裁切圖面。")];
    }

    private static double MeasureLabel(string value, double width, double font)
    {
        var text = new TextBlock { Text = value, Width = width * 4, FontSize = font * 4,
            FontFamily = new("Microsoft JhengHei"), TextWrapping = TextWrapping.Wrap };
        text.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        return text.DesiredSize.Height / 4;
    }
}
