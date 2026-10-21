using System.Security.Cryptography;
using System.Text.Json;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Electrical.Schematic;

public static class SchematicCadSelectionService
{
    public static SchematicCadAsset Restore(SchematicCadAsset asset, CableCadSelection? selection)
    {
        if (selection is null) return asset;
        var restored = Select(asset, selection);
        if (!string.Equals(restored.SelectionSha256, selection.GeometrySha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("重新讀取的 CAD 區域與歸檔版本不一致；請重新歸檔，不會使用整張來源圖。");
        return restored;
    }

    public static SchematicCadAsset Select(SchematicCadAsset asset, CableCadSelection selection)
    {
        ArgumentNullException.ThrowIfNull(asset); ArgumentNullException.ThrowIfNull(selection);
        if ((selection.Bounds is null) == string.IsNullOrWhiteSpace(selection.BlockName))
            throw new InvalidDataException("Select one CAD region or named block.");
        var diagnostics = asset.Diagnostics.ToList();
        var primitives = new List<SchematicCadPrimitive>();
        var contacts = new List<SchematicCadContact>();
        SchematicGridBounds bounds;
        if (selection.Bounds is { } region)
        {
            if (!Valid(region) || region.X < 0 || region.Y < 0 ||
                region.X + region.Width > asset.Width + .001 || region.Y + region.Height > asset.Height + .001)
                throw new InvalidDataException("CAD selection is outside the imported drawing.");
            bounds = region;
            foreach (var primitive in asset.Primitives)
            {
                var box = PrimitiveBounds(primitive);
                if (Contains(region, box)) primitives.Add(primitive);
                else if (Intersects(region, box)) diagnostics.Add("CAD_SELECTION_CUTS_ENTITY");
            }
            contacts.AddRange(asset.ConnectionPoints.Where(c => Contains(region, new(c.Position.X, c.Position.Y, 0, 0))));
        }
        else
        {
            primitives.AddRange(asset.Primitives.Where(p => p.SourceBlocks.Contains(selection.BlockName!, StringComparer.Ordinal)));
            contacts.AddRange(asset.ConnectionPoints.Where(p => p.SourceBlocks?.Contains(selection.BlockName!, StringComparer.Ordinal) == true));
            if (primitives.Count == 0) throw new InvalidDataException("Selected CAD block has no supported visible geometry.");
            var boxes = primitives.Select(PrimitiveBounds).ToArray();
            var minX = boxes.Min(b => b.X); var minY = boxes.Min(b => b.Y);
            bounds = new(minX, minY, Math.Max(.1, boxes.Max(b => b.X + b.Width) - minX),
                Math.Max(.1, boxes.Max(b => b.Y + b.Height) - minY));
        }
        if (primitives.Count == 0) throw new InvalidDataException("Selected CAD region has no supported visible geometry.");
        SchematicPoint Map(SchematicPoint p) => new(p.X - bounds.X, p.Y - bounds.Y);
        var selected = asset with
        {
            Width = bounds.Width, Height = bounds.Height, SelectionSha256 = null,
            SelectionBounds = selection.Bounds, SelectionBlockName = selection.BlockName,
            Primitives = primitives.Select(p => p with { Start = Map(p.Start), End = p.End is null ? null : Map(p.End),
                Contours = p.Contours.Select(c => (IReadOnlyList<SchematicPoint>)c.Select(Map).ToArray()).ToArray() }).ToArray(),
            ConnectionPoints = contacts.Select(c => c with { Position = Map(c.Position) }).ToArray(),
            Diagnostics = diagnostics.Distinct(StringComparer.Ordinal).ToArray()
        };
        return selected with { SelectionSha256 = GeometryHash(selected) };
    }

    public static string GeometryHash(SchematicCadAsset asset) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(asset with { SelectionSha256 = null }))).ToLowerInvariant();

    private static bool Valid(SchematicGridBounds b) => double.IsFinite(b.X) && double.IsFinite(b.Y) &&
        double.IsFinite(b.Width) && double.IsFinite(b.Height) && b.Width > 0 && b.Height > 0;
    private static bool Contains(SchematicGridBounds a, SchematicGridBounds b) => b.X >= a.X - .001 && b.Y >= a.Y - .001 &&
        b.X + b.Width <= a.X + a.Width + .001 && b.Y + b.Height <= a.Y + a.Height + .001;
    private static bool Intersects(SchematicGridBounds a, SchematicGridBounds b) =>
        b.X <= a.X + a.Width && b.Y <= a.Y + a.Height && b.X + b.Width >= a.X && b.Y + b.Height >= a.Y;

    private static SchematicGridBounds PrimitiveBounds(SchematicCadPrimitive p)
    {
        if (p.Kind is "CIRCLE" or "ARC") return new(p.Start.X - p.Radius, p.Start.Y - p.Radius, p.Radius * 2, p.Radius * 2);
        var points = p.Contours.SelectMany(c => c).Concat(p.End is { } end ? [p.Start, end] : new[] { p.Start }).ToArray();
        if (p.Kind is "TEXT" or "MTEXT")
        {
            var width = p.TextWidth > 0 ? p.TextWidth : Math.Max(p.TextHeight, (p.DisplayText?.Length ?? 0) * p.TextHeight * .8 * p.TextWidthFactor);
            var height = Math.Max(.1, p.TextHeight);
            var offset = p.TextAnchorOffset(width, height, p.Kind == "TEXT" ? height : 0);
            var angle = p.Rotation * Math.PI / 180;
            points = new[] { new SchematicPoint(offset.X, offset.Y), new(offset.X + width, offset.Y),
                new(offset.X, offset.Y + height), new(offset.X + width, offset.Y + height) }
                .Select(v => new SchematicPoint(p.Start.X + v.X * Math.Cos(angle) - v.Y * Math.Sin(angle),
                    p.Start.Y + v.X * Math.Sin(angle) + v.Y * Math.Cos(angle))).ToArray();
        }
        var x = points.Min(v => v.X); var y = points.Min(v => v.Y);
        return new(x, y, points.Max(v => v.X) - x, points.Max(v => v.Y) - y);
    }
}
