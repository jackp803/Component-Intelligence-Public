using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    public ElectricalProject GroupWagoSymbols(ElectricalProject project, IReadOnlyCollection<string> symbolIds) => Edit(project, (draft, doc) =>
    {
        var ids = symbolIds.ToHashSet(StringComparer.Ordinal);
        var rows = doc.Symbols.Where(s => ids.Contains(s.SymbolId)).OrderBy(s => s.Position.Y).ThenBy(s => s.Position.X).ToArray();
        if (ids.Count < 2 || ids.Count != symbolIds.Count || rows.Length != ids.Count || rows.Any(s => !SchematicWagoGroups.IsEligible(draft, s) || s.WagoVisualGroupId is not null) ||
            rows.Select(s => s.PageId).Distinct().Count() != 1 || rows.Select(s => s.ComponentInstanceId).Distinct().Count() != rows.Length)
            throw new InvalidOperationException("請選擇同頁至少兩顆尚未合併、接點完整且各自獨立的 WAGO 221-2411 一般圖塊。");
        RequireWagoUnlocked(doc, rows);
        var page = doc.Pages.Single(p => p.PageId == rows[0].PageId);
        var x = rows.Min(s => s.Position.X); var y = Math.Max(page.Margin + SchematicWagoGroups.HeaderHeight, rows.Min(s => s.Position.Y));
        var groupId = "wago-group-" + Guid.NewGuid().ToString("N");
        for (var n = 0; n < rows.Length; n++) {
            var source = rows[n] with { Rotation = 0, EndpointDisplayPlacements = [], CollapsedPortIds = [] };
            source = source with { Anchors = source.Anchors.Select(a => a.Direction switch {
                "Top" => a with { Direction = "Left", Position = new(0, source.Height / 2) },
                "Bottom" => a with { Direction = "Right", Position = new(source.Width, source.Height / 2) },
                _ => a }).ToList() };
            var isolated = new ElectricalProject { ProjectId = "wago-layout", SchemaVersion = draft.SchemaVersion,
                Components = [draft.Components.Single(c => c.ComponentInstanceId == source.ComponentInstanceId)],
                Schematic = new() { Pages = [page], Symbols = [source] } };
            var fitted = ResizeGenericSymbol(isolated, source.SymbolId, SchematicWagoGroups.Width, SchematicWagoGroups.RowHeight).Schematic!.Symbols.Single();
            doc.Symbols[doc.Symbols.FindIndex(s => s.SymbolId == source.SymbolId)] = fitted with {
                Position = new(x, y + n * SchematicWagoGroups.RowHeight), Rotation = 0, WagoVisualGroupId = groupId };
        }
        ReanchorWagoRoutes(draft, doc, rows);
    });

    public ElectricalProject DissolveWagoGroup(ElectricalProject project, string symbolId) => Edit(project, (_, doc) =>
    {
        var id = doc.Symbols.Single(s => s.SymbolId == symbolId).WagoVisualGroupId
            ?? throw new InvalidOperationException("請先選擇已合併的 WAGO。");
        var rows = doc.Symbols.Where(s => s.WagoVisualGroupId == id).ToArray();
        if (rows.Any(s => s.Locked)) throw new InvalidOperationException("請先解鎖合併顯示的 WAGO。");
        for (var i = 0; i < doc.Symbols.Count; i++)
            if (doc.Symbols[i].WagoVisualGroupId == id) doc.Symbols[i] = doc.Symbols[i] with { WagoVisualGroupId = null };
    });

    private static void RequireWagoUnlocked(SchematicDocument doc, IReadOnlyList<SchematicSymbol> rows)
    {
        var ids = rows.Select(s => s.SymbolId).ToHashSet(StringComparer.Ordinal);
        if (rows.Any(s => s.Locked) || doc.Wires.Any(w => w.Locked && (ids.Contains(w.Start.SymbolId ?? "") || ids.Contains(w.End.SymbolId ?? ""))))
            throw new InvalidOperationException("請先解鎖選取的 WAGO 及其連接導線。");
    }

    private static void RequireWagoUngrouped(SchematicSymbol symbol)
    {
        if (symbol.WagoVisualGroupId is not null) throw new InvalidOperationException("請先拆開 WAGO 合併顯示，再修改單顆外觀、接點、刪除或搬頁。");
    }

    private static void TranslateWagoGroup(ElectricalProject draft, SchematicDocument doc, SchematicSymbol selected, SchematicPoint position, int rotation)
    {
        if (rotation != selected.Rotation) throw new InvalidOperationException("合併 WAGO 請先拆開後再旋轉單顆。");
        var rows = doc.Symbols.Where(s => s.WagoVisualGroupId == selected.WagoVisualGroupId).ToArray(); RequireWagoUnlocked(doc, rows);
        var dx = position.X - selected.Position.X; var dy = position.Y - selected.Position.Y;
        foreach (var row in rows) doc.Symbols[doc.Symbols.IndexOf(row)] = row with { Position = new(row.Position.X + dx, row.Position.Y + dy) };
        ReanchorWagoRoutes(draft, doc, rows);
    }

    private static void ReanchorWagoRoutes(ElectricalProject draft, SchematicDocument doc, IReadOnlyList<SchematicSymbol> old)
    {
        var ids = old.Select(s => s.SymbolId).ToHashSet(StringComparer.Ordinal);
        var affected = doc.Wires.Where(w => ids.Contains(w.Start.SymbolId ?? "") || ids.Contains(w.End.SymbolId ?? "")).Select(w => w.WireId).ToHashSet(StringComparer.Ordinal);
        var reserved = doc.Wires.Where(w => !affected.Contains(w.WireId)).ToList();
        for (var i = 0; i < doc.Wires.Count; i++) {
            var wire = doc.Wires[i]; if (!affected.Contains(wire.WireId)) continue;
            var points = wire.Points.ToList();
            var moveStart = ids.Contains(wire.Start.SymbolId ?? ""); var moveEnd = ids.Contains(wire.End.SymbolId ?? "");
            SchematicSymbol Owner(SchematicAttachment a) => doc.Symbols.Single(s => s.SymbolId == a.SymbolId);
            var start = moveStart ? SymbolAttachmentPoint(draft, Owner(wire.Start), wire.Start) : points[0];
            var end = moveEnd ? SymbolAttachmentPoint(draft, Owner(wire.End), wire.End) : points[^1];
            var dx = start.X - points[0].X; var dy = start.Y - points[0].Y;
            if (moveStart && moveEnd && Math.Abs(end.X - points[^1].X - dx) < .000001 && Math.Abs(end.Y - points[^1].Y - dy) < .000001)
                points = points.Select(p => new SchematicPoint(p.X + dx, p.Y + dy)).ToList();
            else if (moveStart && moveEnd && wire.ManualRoute)
            {
                // Update both ends atomically; sequential edits may collapse a two-vertex route.
                points = [start];
                foreach (var p in wire.Points.Skip(1).SkipLast(1).Append(end)) {
                    if (points[^1].X != p.X && points[^1].Y != p.Y) points.Add(new(p.X, points[^1].Y));
                    if (points[^1] != p) points.Add(p);
                }
            }
            else
            {
                if (moveStart) points = wire.ManualRoute ? Reanchor(points, start, true) : ReanchorSymbol(points, draft, Owner(wire.Start), wire.Start, true);
                if (moveEnd) points = wire.ManualRoute ? Reanchor(points, end, false) : ReanchorSymbol(points, draft, Owner(wire.End), wire.End, false);
            }
            if (points.Count < 2) throw new InvalidOperationException("WAGO 移動後導線長度為零，請調整位置。");
            if (CanRerouteSimpleWire(doc, wire)) points = RerouteSimpleWire(draft, doc, wire, reserved);
            doc.Wires[i] = wire with { Points = points }; reserved.Add(doc.Wires[i]); ReanchorDependentJunctions(doc, wire.WireId);
        }
    }

    internal static void EnsureWagoGroupClear(ElectricalProject project, SchematicDocument doc, string id)
    {
        var group = SchematicWagoGroups.Build(doc).Single(g => g.Id == id); var ids = group.Members.Select(s => s.SymbolId).ToHashSet(StringComparer.Ordinal);
        bool Overlap(SchematicGridBounds a, SchematicGridBounds b) => a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
        var interior = new SchematicGridBounds(group.Bounds.X + .001, group.Bounds.Y + .001, group.Bounds.Width - .002, group.Bounds.Height - .002);
        var neighbors = doc.Symbols.Where(s => s.PageId == group.Members[0].PageId && !ids.Contains(s.SymbolId)).ToArray();
        var related = doc.Wires.Where(w => ids.Contains(w.Start.SymbolId ?? "") || ids.Contains(w.End.SymbolId ?? "")).Select(w => w.WireId).ToHashSet(StringComparer.Ordinal);
        var children = doc.Wires.SelectMany(w => new[] { w.Start, w.End }.Where(a => a.Kind == SchematicAttachmentKind.WireJunction)
            .Select(a => (Parent: a.WireId!, Child: w.WireId))).ToLookup(e => e.Parent, e => e.Child, StringComparer.Ordinal);
        var pending = new Queue<string>(related);
        while (pending.TryDequeue(out var parent))
            foreach (var child in children[parent]) if (related.Add(child)) pending.Enqueue(child);
        if (neighbors
            .Any(s => Overlap(group.Bounds, SchematicPortPresentation.GenericBodyBounds(doc, s, SchematicSymbolOwner.Resolve(project, s)))) ||
            SchematicWagoGroups.Build(doc, group.Members[0].PageId).Any(g => g.Id != id && Overlap(group.Bounds, g.Bounds)) ||
            doc.Wires.Where(w => w.PageId == group.Members[0].PageId)
                .Any(w => w.Points.Zip(w.Points.Skip(1)).Any(p => FanoutSegmentTouchesBody(p.First, p.Second, related.Contains(w.WireId) ? interior : group.Bounds))) ||
            neighbors.Any(s => s.EndpointDisplayPlacements.Any(p => {
                var lead = PortFanoutLeadPoints(s, p.EndpointId);
                return lead.Zip(lead.Skip(1)).Any(e => FanoutSegmentTouchesBody(e.First, e.Second, group.Bounds)); })))
            throw new InvalidOperationException("WAGO 合併外框會遮住其他圖塊、導線或引線，請調整位置或走線。");
    }
}
