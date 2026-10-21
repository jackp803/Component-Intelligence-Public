using System.Windows;
using System.Windows.Controls;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public static partial class CableWiringAppendixRenderer
{
    public static IReadOnlyList<CableWiringAppendixTile> Plan(CableInstance cable, SchematicPage page, int sourcePageCount,
        IReadOnlyList<CableInstance>? members = null)
    {
        return PlanSinglePage(cable, page, sourcePageCount, members);
    }

    private sealed record SketchVariant(CableInstance Source, IReadOnlyList<string> References);

    private static IReadOnlyList<SketchVariant> SketchVariants(IReadOnlyList<CableInstance> members)
    {
        var sources = members.Where(c => c.ArchivedCable?.ManufacturingGeometry?.Primitives.Count > 0).Select(c => {
            var sketch = SchematicCableDetailService.BuildSketch(c, new(c.CableInstanceId, null) {
                SketchPosition = new(0, 0), SketchWidth = 170, SketchHeight = 70 }, includeInstanceText: false);
            // Compare rendered construction, not an asserted source hash or instance-specific CAD attributes.
            var rendered = sketch.Primitives.Select(p => p with { SourceBlocks = [] });
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rendered))));
            return (Cable: c, Key: key, HasInk: sketch.Primitives.Count > 0);
        });
        return sources.Where(s => s.HasInk).GroupBy(s => s.Key, StringComparer.Ordinal).Select(g => new SketchVariant(g.First().Cable,
            g.Select(s => s.Cable.ReferenceDesignator ?? "Reference 未設定").Distinct(StringComparer.Ordinal).ToArray())).ToArray();
    }

    private static CableWiringAppendixTile? PlaceSketch(IReadOnlyList<SketchVariant> variants, CableWiringAppendixTile tile, Rect region, bool shared, SchematicPage page, double captionScale = 1)
    {
        if (region.Width <= 2 || region.Height <= 2) return null;
        var obstacles = SketchObstacles(tile, page);
        var candidates = new List<(double Score, double Width, double Height, IReadOnlyList<SchematicCadPrimitive> Ink)>();
        for (var columns = 1; columns <= Math.Ceiling(Math.Sqrt(variants.Count)); columns++)
        {
            var cellWidth = Math.Min(170, (region.Width - (columns - 1) * 4) / columns);
            if (cellWidth <= 1) continue;
            var primitives = new List<SchematicCadPrimitive>(); var top = 0d;
            for (var row = 0; row < variants.Count; row += columns)
            {
                var rowHeight = 0d;
                for (var column = 0; column < Math.Min(columns, variants.Count - row); column++)
                {
                    var variant = variants[row + column]; var x = column * (cellWidth + 4);
                    var caption = variants.Count > 1 ? "示意 " + (row + column + 1) + " / " + string.Join(" / ", variant.References)
                        : shared ? "共用示意圖 / " + string.Join(" / ", variant.References) : "";
                    var captionHeight = caption.Length == 0 ? 0 : MeasureLabel(caption, cellWidth, 3 * captionScale) + 2;
                    if (caption.Length > 0) primitives.Add(new() { Kind = "MTEXT", Start = new(x, top), Text = caption,
                        TextHeight = 3 * captionScale, TextWidth = cellWidth, TextAttachment = "TopLeft" });
                    var sketch = SchematicCableDetailService.BuildSketch(variant.Source, new(variant.Source.CableInstanceId, null) {
                        TableVisible = false, SketchPosition = new(0, 0), SketchWidth = cellWidth, SketchHeight = 70 }, includeInstanceText: !shared);
                    primitives.AddRange(TranslateSketch(sketch.Primitives, x, top + captionHeight));
                    rowHeight = Math.Max(rowHeight, captionHeight + sketch.Bounds.Height);
                }
                top += rowHeight + (row + columns < variants.Count ? 4 : 0);
            }
            var width = variants.Count == 1 && !shared ? SchematicCableDetailService.BuildSketch(variants[0].Source,
                new(variants[0].Source.CableInstanceId, null) { TableVisible = false, SketchWidth = cellWidth, SketchHeight = 70 },
                includeInstanceText: !shared).Bounds.Width : columns * cellWidth + (columns - 1) * 4;
            var scale = Math.Min(1, Math.Min(region.Width / width, region.Height / top));
            candidates.Add((cellWidth * scale, width * scale, top * scale, TranslateSketch(primitives, 0, 0, scale)));
        }
        foreach (var candidate in candidates.OrderByDescending(c => c.Score))
        {
            var xs = obstacles.Select(o => o.Left - candidate.Width - 2).Append(region.Right - candidate.Width)
                .Where(x => x >= region.Left && x + candidate.Width <= region.Right + .000001).Distinct().OrderDescending();
            var ys = obstacles.Select(o => o.Top - candidate.Height - 2).Append(region.Bottom - candidate.Height)
                .Where(y => y >= region.Top - .000001 && y + candidate.Height <= region.Bottom + .000001).Distinct().OrderDescending();
            foreach (var y in ys)
            foreach (var x in xs)
            {
                var box = new Rect(x, y, candidate.Width, candidate.Height);
                if (obstacles.Any(o => o.IntersectsWith(box))) continue;
                return tile with { SketchBounds = new(x, y, candidate.Width, candidate.Height),
                    SketchPrimitives = TranslateSketch(candidate.Ink, x, y) };
            }
        }
        return null;
    }

    private static IReadOnlyList<SchematicCadPrimitive> TranslateSketch(IReadOnlyList<SchematicCadPrimitive> primitives, double x, double y, double scale = 1)
    {
        SchematicPoint Move(SchematicPoint p) => new(p.X * scale + x, p.Y * scale + y);
        return primitives.Select(p => p with { Start = Move(p.Start), End = p.End is null ? null : Move(p.End),
            Radius = p.Radius * scale, TextHeight = p.TextHeight * scale, TextWidth = p.TextWidth * scale,
            Contours = p.Contours.Select(c => (IReadOnlyList<SchematicPoint>)c.Select(Move).ToArray()).ToArray() }).ToArray();
    }

    private static IReadOnlyList<Rect> SketchObstacles(CableWiringAppendixTile tile, SchematicPage page)
    {
        var b = tile.ContentBounds; var scale = tile.GeometryScale;
        var clip = new Rect(b.X, b.Y, b.Width, b.Height);
        double X(double x) => b.X + (x - tile.ViewBounds.X) * scale;
        double Y(double y) => b.Y + (y - tile.ViewBounds.Y) * scale;
        var result = new List<Rect>();
        void Add(Rect box, bool clipToDiagram = true)
        {
            if (clipToDiagram) box.Intersect(clip);
            if (box.IsEmpty) return;
            box.Inflate(2, 2); result.Add(box);
        }
        var slots = page.TitleBlockSlots.Count > 0 ? page.TitleBlockSlots :
            page.TemplateGeometry is { } template ? SchematicTitleBlock.DetectSlots(template) : [];
        foreach (var slot in slots)
            Add(new Rect(slot.Bounds.X, slot.Bounds.Y - 5, slot.Bounds.Width, slot.Bounds.Height + 5), clipToDiagram: false);
        foreach (var port in tile.Diagram.PortBounds.Values) Add(new(X(port.X), Y(port.Y), port.Width * scale, port.Height * scale));
        foreach (var p in tile.Diagram.Primitives)
        {
            if (p.Kind is "TEXT" or "MTEXT")
            {
                var text = CableWiringVisualRenderer.CreateText(p, tile.ScaleTextWithGeometry ? 4 * scale : 4,
                    tile.MinimumTextHeight, tile.ScaleTextWithGeometry ? 1 : scale);
                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Add(new(X(p.Start.X), Y(p.Start.Y), text.DesiredSize.Width / 4, text.DesiredSize.Height / 4));
            }
            else if (p.Kind is "CIRCLE" or "ARC")
                Add(new(X(p.Start.X - p.Radius), Y(p.Start.Y - p.Radius), p.Radius * 2 * scale, p.Radius * 2 * scale));
            else if (p.End is { } end)
                Add(new(X(Math.Min(p.Start.X, end.X)) - .2, Y(Math.Min(p.Start.Y, end.Y)) - .2,
                    Math.Abs(p.Start.X - end.X) * scale + .4, Math.Abs(p.Start.Y - end.Y) * scale + .4));
        }
        return result;
    }
}
