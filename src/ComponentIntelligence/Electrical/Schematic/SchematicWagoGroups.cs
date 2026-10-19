using System.Text.RegularExpressions;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicWagoGroup(string Id, IReadOnlyList<SchematicSymbol> Members, SchematicGridBounds Bounds);

public static class SchematicWagoGroups
{
    public const double Width = 24, RowHeight = 18, HeaderHeight = 10;

    public static string PinLabel(ElectricalProject project, SchematicSymbol symbol, SchematicAnchor anchor) =>
        SchematicSymbolOwner.Resolve(project, symbol).Ports.SelectMany(p => p.Pins).Single(p => p.PinId == anchor.EndpointId).PinNumber ?? anchor.Label ?? "";

    public static bool IsEligible(ElectricalProject project, SchematicSymbol symbol)
    {
        if (symbol.CableInstanceId is not null || symbol.Geometry is not null || symbol.SectionCount != 1) return false;
        var component = project.Components.SingleOrDefault(c => c.ComponentInstanceId == symbol.ComponentInstanceId);
        if (component is null || component.Ports.Count != 1 || component.Ports[0].Pins.Count != 2 ||
            !component.Ports[0].Pins.Select(p => p.PinId).ToHashSet(StringComparer.Ordinal).SetEquals(symbol.Anchors.Select(a => a.EndpointId))) return false;
        var rows = project.BomItems.Where(b => b.ComponentInstanceIds.Contains(component.ComponentInstanceId) || b.ComponentDefinitionId == component.ComponentDefinitionId).ToArray();
        var identities = rows.Where(b => !string.IsNullOrWhiteSpace(b.Row.Manufacturer) && !string.IsNullOrWhiteSpace(b.Row.ModelOrPartNumber)).ToArray();
        if (identities.Length > 0) return identities.All(b =>
            string.Equals(b.Row.Manufacturer?.Trim(), "WAGO", StringComparison.OrdinalIgnoreCase) && b.Row.ModelOrPartNumber?.Trim() == "221-2411");
        return Regex.IsMatch(component.DisplayName?.Trim() ?? "", @"^WAGO\s+221-2411(?:\s*·\s*#\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static IReadOnlyList<SchematicWagoGroup> Build(SchematicDocument doc, string? pageId = null) => doc.Symbols
        .Where(s => s.WagoVisualGroupId is not null && (pageId is null || s.PageId == pageId))
        .GroupBy(s => s.WagoVisualGroupId!, StringComparer.Ordinal).Select(g => {
            var rows = g.OrderBy(s => s.Position.Y).ThenBy(s => s.SymbolId, StringComparer.Ordinal).ToArray();
            var first = rows[0];
            return new SchematicWagoGroup(g.Key, rows, new(first.Position.X, first.Position.Y - HeaderHeight,
                first.Width, rows.Max(s => s.Position.Y + s.Height) - first.Position.Y + HeaderHeight));
        }).ToArray();

    public static IReadOnlyList<SchematicCadPrimitive> Primitives(ElectricalProject project, SchematicWagoGroup group)
    {
        var b = group.Bounds; var result = new List<SchematicCadPrimitive>();
        void Line(double x1, double y1, double x2, double y2) => result.Add(new() { Kind = "LINE", Start = new(x1, y1), End = new(x2, y2) });
        Line(b.X, b.Y, b.X + b.Width, b.Y); Line(b.X, b.Y, b.X, b.Y + b.Height);
        Line(b.X + b.Width, b.Y, b.X + b.Width, b.Y + b.Height); Line(b.X, b.Y + b.Height, b.X + b.Width, b.Y + b.Height);
        result.Add(new() { Kind = "MTEXT", Text = "WAGO 221-2411", Start = new(b.X + 2, b.Y + 1), TextHeight = 3, TextWidth = b.Width - 4, TextAttachment = "TopLeft" });
        for (var i = 0; i < group.Members.Count; i++) {
            var row = group.Members[i];
            var reference = SchematicSymbolOwner.Resolve(project, row).Reference ?? "未設定 " + (i + 1);
            // Fit long References inside their own row without truncating their identity.
            var units = reference.Sum(c => c > 255 ? 1d : .7d);
            var height = 3d;
            while (Math.Ceiling(units * height / (b.Width - 4)) * height * 1.5 > RowHeight - 5) height *= .9;
            Line(b.X, row.Position.Y, b.X + b.Width, row.Position.Y);
            result.Add(new() { Kind = "MTEXT", Text = reference,
                Start = new(b.X + 2, row.Position.Y + 2), TextHeight = height, TextWidth = b.Width - 4, TextAttachment = "TopLeft" });
        }
        return result;
    }

    public static void Validate(ElectricalProject project)
    {
        if (project.Schematic is not { } doc) return;
        foreach (var group in Build(doc)) {
            var rows = group.Members; var first = rows[0]; var b = group.Bounds;
            if (string.IsNullOrWhiteSpace(group.Id) || rows.Count < 2 || rows.Select(s => s.PageId).Distinct().Count() != 1 ||
                rows.Select(s => s.ComponentInstanceId).Distinct().Count() != rows.Count ||
                rows.Any(s => !IsEligible(project, s) || s.Rotation != 0 || !s.ManualSize || s.Width != Width || s.Height != RowHeight ||
                    s.Position.X != first.Position.X || s.EndpointDisplayPlacements.Count != 0 || s.CollapsedPortIds.Count != 0 ||
                    s.Anchors.Any(a => a.Direction is not ("Left" or "Right"))) ||
                rows.Zip(rows.Skip(1)).Any(p => p.Second.Position.Y < p.First.Position.Y + p.First.Height - .001))
                throw new InvalidOperationException("WAGO 合併顯示必須保留同頁各自獨立的 221-2411；請先拆開後再修改單顆外觀、接點或搬頁。");
            var page = doc.Pages.Single(p => p.PageId == first.PageId);
            if (b.X < page.Margin || b.Y < page.Margin || b.X + b.Width > page.Width - page.Margin || b.Y + b.Height > page.Height - page.Margin)
                throw new InvalidOperationException("WAGO 合併外框超出圖面；請減少顆數或調整位置。");
            SchematicAuthoringService.EnsureWagoGroupClear(project, doc, group.Id);
        }
    }
}
