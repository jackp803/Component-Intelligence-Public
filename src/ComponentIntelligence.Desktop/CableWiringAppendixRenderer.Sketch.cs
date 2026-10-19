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
        var tiles = PlanWiring(cable, page, sourcePageCount, members);
        var variants = SketchVariants(members ?? new[] { cable });
        if (variants.Count == 0) return tiles;
        var shared = members is { Count: > 1 };
        var placed = new List<CableWiringAppendixTile>();
        foreach (var tile in tiles)
        {
            var b = tile.ContentBounds;
            var region = new Rect(b.X + b.Width / 2, b.Y + b.Height * .45, b.Width / 2, b.Height * .55);
            if (PlaceSketch(variants, tile, region, shared, page) is not { } result) break;
            placed.Add(result);
        }
        if (placed.Count == tiles.Count) return placed;

        // Keep the saved electrical layout and full-width header; reserve a separate
        // right column only when the diagram has no safe lower-right opening.
        var full = tiles[0].ContentBounds;
        var reserved = Math.Min(180, full.Width * .45);
        var partitioned = PlanWiring(cable, page, sourcePageCount, members, reserved, tiles[0].Diagram);
        return partitioned.Select(tile => PlaceSketch(variants, tile,
            new Rect(full.X + full.Width - reserved + 6, full.Y, reserved - 6, full.Height), shared, page)
            ?? throw new InvalidOperationException("接線圖與製作示意 CAD 無法安全放在同一頁；請使用較大紙張，未輸出重疊或裁切的圖面。")).ToArray();
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

    private static CableWiringAppendixTile? PlaceSketch(IReadOnlyList<SketchVariant> variants, CableWiringAppendixTile tile, Rect region, bool shared, SchematicPage page)
    {
        if (region.Width <= 2 || region.Height <= 2) return null;
        var obstacles = SketchObstacles(tile, page);
        for (var step = 0; step <= 4; step++)
        {
            var factor = 1 - step * .05;
            var width = Math.Min(170, region.Width) * factor;
            var captions = variants.Select((v, i) => variants.Count > 1 ? "示意 " + (i + 1) + " / " + string.Join(" / ", v.References) : "").ToArray();
            var captionHeights = captions.Select(value => {
                if (value.Length == 0) return 0d;
                var text = new TextBlock { Text = value, FontSize = 12, Width = width * 4, TextWrapping = TextWrapping.Wrap };
                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return text.DesiredSize.Height / 4 + 2;
            }).ToArray();
            var availableHeight = region.Height - captionHeights.Sum() - (variants.Count - 1) * 4;
            if (availableHeight <= variants.Count * 2) continue;
            var primitives = new List<SchematicCadPrimitive>(); var w = 0d; var h = 0d;
            for (var i = 0; i < variants.Count; i++)
            {
                var source = variants[i].Source;
                if (captions[i].Length > 0) primitives.Add(new() { Kind = "MTEXT", Start = new(0, h), Text = captions[i],
                    TextHeight = 3, TextWidth = width, TextAttachment = "TopLeft" });
                h += captionHeights[i];
                var sketch = SchematicCableDetailService.BuildSketch(source, new(source.CableInstanceId, null) {
                    TableVisible = false, SketchPosition = new(0, 0), SketchWidth = width,
                    SketchHeight = Math.Min(70, availableHeight / variants.Count) * factor }, includeInstanceText: !shared);
                primitives.AddRange(TranslateSketch(sketch.Primitives, 0, h));
                w = Math.Max(w, captions[i].Length > 0 ? width : sketch.Bounds.Width);
                h += sketch.Bounds.Height + (i + 1 < variants.Count ? 4 : 0);
            }
            var xs = obstacles.Select(o => o.Left - w - 2).Append(region.Right - w).Where(x => x >= region.Left && x + w <= region.Right).Distinct().OrderDescending();
            var ys = obstacles.Select(o => o.Top - h - 2).Append(region.Bottom - h).Where(y => y >= region.Top && y + h <= region.Bottom).Distinct().OrderDescending();
            foreach (var y in ys)
            foreach (var x in xs)
            {
                var box = new Rect(x, y, w, h);
                if (obstacles.Any(o => o.IntersectsWith(box))) continue;
                return tile with { SketchBounds = new(x, y, w, h), SketchPrimitives = TranslateSketch(primitives, x, y) };
            }
        }
        return null;
    }

    private static IReadOnlyList<SchematicCadPrimitive> TranslateSketch(IReadOnlyList<SchematicCadPrimitive> primitives, double x, double y)
    {
        SchematicPoint Move(SchematicPoint p) => new(p.X + x, p.Y + y);
        return primitives.Select(p => p with { Start = Move(p.Start), End = p.End is null ? null : Move(p.End),
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
                var text = CableWiringVisualRenderer.CreateText(p, 4, tile.MinimumTextHeight, scale);
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
