using System.Text.RegularExpressions;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicWagoGroup(string Id, IReadOnlyList<SchematicSymbol> Members, SchematicGridBounds Bounds)
{
    public double HeaderHeight { get; init; } = SchematicWagoGroups.HeaderHeight;
}

public static class SchematicWagoGroups
{
    public const double Width = 24, RowHeight = 18, HeaderHeight = 10;
    public const double ImageHeaderHeight = 24;

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
            var header = first.WagoHeaderHeight ?? HeaderHeight;
            return new SchematicWagoGroup(g.Key, rows, new(first.Position.X, first.Position.Y - header,
                first.Width, rows.Max(s => s.Position.Y + s.Height) - first.Position.Y + header)) { HeaderHeight = header };
        }).ToArray();

    public static SchematicGridBounds ImageBounds(SchematicWagoGroup group, double pixelWidth, double pixelHeight)
    {
        if (!double.IsFinite(pixelWidth) || !double.IsFinite(pixelHeight) || pixelWidth <= 0 || pixelHeight <= 0)
            throw new InvalidOperationException("元件圖片尺寸必須大於零且為有效數值。");
        var b = group.Bounds;
        var box = group.HeaderHeight >= 20 ? new SchematicGridBounds(b.X + 2, b.Y + 9, b.Width - 4, group.HeaderHeight - 11) :
            new SchematicGridBounds(b.X + 1, b.Y + 1, b.Width * .4 - 1, group.HeaderHeight - 2);
        var fit = Math.Min(box.Width / pixelWidth, box.Height / pixelHeight);
        return new(box.X + (box.Width - pixelWidth * fit) / 2, box.Y + (box.Height - pixelHeight * fit) / 2, pixelWidth * fit, pixelHeight * fit);
    }

    public static IReadOnlyList<SchematicCadPrimitive> Primitives(ElectricalProject project, SchematicWagoGroup group)
    {
        var b = group.Bounds; var result = new List<SchematicCadPrimitive>();
        void Line(double x1, double y1, double x2, double y2) => result.Add(new() { Kind = "LINE", Start = new(x1, y1), End = new(x2, y2) });
        Line(b.X, b.Y, b.X + b.Width, b.Y); Line(b.X, b.Y, b.X, b.Y + b.Height);
        Line(b.X + b.Width, b.Y, b.X + b.Width, b.Y + b.Height); Line(b.X, b.Y + b.Height, b.X + b.Width, b.Y + b.Height);
        var legacyHeader = group.HeaderHeight < 20;
        result.Add(new() { Kind = "MTEXT", Text = "WAGO 221-2411", Start = new(b.X + (legacyHeader ? b.Width * .42 : 2), b.Y + 1),
            TextHeight = legacyHeader ? 2.2 : 2.5, TextWidth = legacyHeader ? b.Width * .58 - 1 : b.Width - 4, TextAttachment = "TopLeft" });
        for (var i = 0; i < group.Members.Count; i++) {
            var row = group.Members[i];
            var reference = SchematicSymbolOwner.Resolve(project, row).Reference ?? "未設定 " + (i + 1);
            // Fit long References inside their own row without truncating their identity.
            var units = reference.Sum(c => c > 255 ? 1d : .7d);
            var height = 3d;
            while (Math.Ceiling(units * height / (b.Width - 4)) * height * 1.5 > row.Height - 5) height *= .9;
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
                !double.IsFinite(group.HeaderHeight) || group.HeaderHeight < HeaderHeight ||
                rows.Any(s => !IsEligible(project, s) || s.Rotation != 0 || !s.ManualSize || s.Width < 20 || s.Height < RowHeight ||
                    Math.Abs(s.Width - first.Width) > .000001 || Math.Abs(s.Height - first.Height) > .000001 ||
                    Math.Abs((s.WagoHeaderHeight ?? HeaderHeight) - group.HeaderHeight) > .000001 ||
                    s.Position.X != first.Position.X || s.CollapsedPortIds.Count != 0 ||
                    s.Anchors.Any(a => a.Direction is not ("Left" or "Right"))) ||
                rows.Zip(rows.Skip(1)).Any(p => p.Second.Position.Y < p.First.Position.Y + p.First.Height - .001))
                throw new InvalidOperationException("WAGO 合併顯示須保留同頁獨立的 221-2411，且各列尺寸一致；旋轉、刪除或搬頁前請先拆開。");
            var pins = rows.SelectMany(s => s.Anchors.Select(a => (Symbol: s.SymbolId, Point: SchematicAuthoringService.AnchorPoint(s, a.EndpointId)))).ToArray();
            if (pins.SelectMany((p, i) => pins.Skip(i + 1).Select(q => (p, q))).Any(pair =>
                pair.p.Symbol != pair.q.Symbol && Math.Pow(pair.p.Point.X - pair.q.Point.X, 2) + Math.Pow(pair.p.Point.Y - pair.q.Point.Y, 2) < 25))
                throw new InvalidOperationException("不同 WAGO 的展開接點太接近；請增加列高或調整引線間距。");
            var leads = rows.SelectMany(s => s.EndpointDisplayPlacements.Select(p => new SchematicWire {
                WireId = s.SymbolId + ":" + p.EndpointId, PageId = s.PageId,
                Start = SchematicAttachment.Pin(s.SymbolId, p.EndpointId), End = SchematicAttachment.Pin(s.SymbolId, p.EndpointId),
                Points = SchematicAuthoringService.PortFanoutLeadPoints(s, p.EndpointId).ToList()
            })).ToArray();
            var leadById = leads.ToDictionary(l => l.WireId);
            bool Unsafe(SchematicRouteConflict c) => c.Code is "COLLINEAR_OVERLAP" or "UNCONNECTED_CONTACT";
            if (SchematicCrossingService.Analyze(leads).Conflicts.Any(c => Unsafe(c) &&
                    leadById[c.FirstWireId].Start.SymbolId != leadById[c.SecondWireId].Start.SymbolId) ||
                leads.Any(lead => doc.Wires.Where(w => w.PageId == first.PageId && w.Start != lead.Start && w.End != lead.Start)
                    .Any(w => SchematicCrossingService.Analyze([lead, w]).Conflicts.Any(Unsafe))))
                throw new InvalidOperationException("不同 WAGO 的引線重疊；請增加列高或縮小引線間距。");
            var page = doc.Pages.Single(p => p.PageId == first.PageId);
            if (b.X < page.Margin || b.Y < page.Margin || b.X + b.Width > page.Width - page.Margin || b.Y + b.Height > page.Height - page.Margin)
                throw new InvalidOperationException("WAGO 合併外框超出圖面；請減少顆數或調整位置。");
            SchematicAuthoringService.EnsureWagoGroupClear(project, doc, group.Id);
        }
    }
}
