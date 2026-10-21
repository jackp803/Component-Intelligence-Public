using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    public ElectricalProject AlignPortFanout(ElectricalProject project, string symbolId, string portId) =>
        Edit(ElectricalProjectMigrator.ForPortFanout(project), (draft, doc) =>
    {
        var originals = doc.Symbols.ToDictionary(s => s.SymbolId, StringComparer.Ordinal);
        var source = originals.GetValueOrDefault(symbolId) ?? throw new InvalidOperationException("Unknown representation.");
        var port = SchematicSymbolOwner.Resolve(draft, source).Ports.SingleOrDefault(p => p.PortId == portId)
            ?? throw new InvalidOperationException("Unknown Port.");
        var ids = port.Pins.Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        bool Selected(SchematicAttachment a) => a.Kind == SchematicAttachmentKind.Pin && a.SymbolId == symbolId && ids.Contains(a.EndpointId!);
        var links = doc.Wires.Where(w => Selected(w.Start) || Selected(w.End)).Select(w =>
            (Wire: w, From: Selected(w.Start) ? w.Start : w.End, To: Selected(w.Start) ? w.End : w.Start)).ToArray();
        if (links.Length == 0) throw new InvalidOperationException("此 Port 尚未有 Pin 對 Pin 接線，無法依接線對齊。");
        if (links.Any(l => l.To.Kind != SchematicAttachmentKind.Pin || l.To.SymbolId == symbolId) ||
            links.GroupBy(l => l.From.EndpointId).Any(g => g.Count() != 1) ||
            links.GroupBy(l => (l.To.SymbolId, l.To.EndpointId)).Any(g => g.Count() != 1) ||
            links.Any(l => doc.Wires.Any(w => w.Start.WireId == l.Wire.WireId || w.End.WireId == l.Wire.WireId)))
            throw new InvalidOperationException("此組接線包含分支或非 Pin 接點；保留接法與轉角標示，不自動合併接點。");

        SchematicSymbol Expand(string id, ComponentPort value)
        {
            var symbol = doc.Symbols.Single(s => s.SymbolId == id);
            if (symbol.Locked || doc.Wires.Any(w => w.Locked && (w.Start.SymbolId == id || w.End.SymbolId == id)))
                throw new InvalidOperationException("請先解鎖兩端模塊及相連導線，再對齊接點。");
            if (!SchematicPortPresentation.IsRepresented(symbol, value) || value.Pins.Count == 0)
                throw new InvalidOperationException("對齊需要完整的 Port 接點表示。");
            if (!symbol.EndpointDisplayPlacements.Any(p => p.PortId == value.PortId))
            {
                symbol = FanoutSymbol(symbol, value, true, DefaultFanoutPitch, DefaultFanoutLead);
                doc.Symbols[doc.Symbols.FindIndex(s => s.SymbolId == id)] = symbol;
            }
            return symbol;
        }
        source = Expand(symbolId, port);
        var peers = links.GroupBy(l =>
        {
            var target = originals[l.To.SymbolId!];
            var targetPort = SchematicSymbolOwner.Resolve(draft, target).Ports.Single(p => p.Pins.Any(pin => pin.PinId == l.To.EndpointId));
            return (SymbolId: target.SymbolId, PortId: targetPort.PortId);
        });
        foreach (var group in peers)
        {
            var target = doc.Symbols.Single(s => s.SymbolId == group.Key.SymbolId);
            if (target.PageId != source.PageId) throw new InvalidOperationException("跨頁接線保留各頁接點位置，不自動對齊。");
            var targetPort = SchematicSymbolOwner.Resolve(draft, target).Ports.Single(p => p.PortId == group.Key.PortId);
            target = Expand(target.SymbolId, targetPort);
            var positions = target.EndpointDisplayPlacements.ToDictionary(p => p.EndpointId, StringComparer.Ordinal);
            bool? horizontal = null;
            foreach (var link in group)
            {
                var a = AnchorPoint(source, link.From.EndpointId!);
                var b = AnchorPoint(target, link.To.EndpointId!);
                var sideA = SchematicPortPresentation.RotatedSide(source.Anchors.Single(p => p.EndpointId == link.From.EndpointId).Direction, source.Rotation);
                var sideB = SchematicPortPresentation.RotatedSide(target.Anchors.Single(p => p.EndpointId == link.To.EndpointId).Direction, target.Rotation);
                var alongX = sideA == "Right" && sideB == "Left" && a.X < b.X || sideA == "Left" && sideB == "Right" && a.X > b.X;
                var alongY = sideA == "Bottom" && sideB == "Top" && a.Y < b.Y || sideA == "Top" && sideB == "Bottom" && a.Y > b.Y;
                if (!alongX && !alongY || horizontal is not null && horizontal != alongX)
                    throw new InvalidOperationException("兩端接點方向未相對，請先調整模塊方向；轉角線仍可標示。");
                horizontal = alongX;
                var aligned = alongX ? b with { Y = a.Y } : b with { X = a.X };
                positions[link.To.EndpointId!] = positions[link.To.EndpointId!] with { Position = LocalDisplayPoint(target, aligned) };
            }
            // Unwired Pins remain separate; move a colliding unused slot, never merge it into a connected Pin.
            var matched = group.Select(l => l.To.EndpointId!).ToHashSet(StringComparer.Ordinal);
            var used = matched.Select(id => positions[id].Position).Select(p => TransformAnchorDisplayPoint(target, p))
                .Select(p => horizontal == true ? p.Y : p.X).ToList();
            var ordered = target.EndpointDisplayPlacements.Where(p => p.PortId == targetPort.PortId).ToArray();
            foreach (var placement in ordered.Where(p => !matched.Contains(p.EndpointId)))
            {
                var global = TransformAnchorDisplayPoint(target, placement.Position);
                var coordinate = horizontal == true ? global.Y : global.X;
                if (used.Any(c => Math.Abs(c - coordinate) < DefaultFanoutPitch - .001))
                {
                    coordinate = used.Max() + 10;
                    global = horizontal == true ? global with { Y = coordinate } : global with { X = coordinate };
                    positions[placement.EndpointId] = placement with { Position = LocalDisplayPoint(target, global) };
                }
                used.Add(coordinate);
            }
            target = target with { EndpointDisplayPlacements = target.EndpointDisplayPlacements.Select(p => positions[p.EndpointId]).ToList() };
            doc.Symbols[doc.Symbols.FindIndex(s => s.SymbolId == target.SymbolId)] = target;
        }
        foreach (var symbol in doc.Symbols.Where(s => s != originals[s.SymbolId]))
            ValidatePortFanout(draft, doc, symbol, SchematicSymbolOwner.Resolve(draft, symbol));
        var affected = doc.Wires.Where(w => new[] { w.Start, w.End }.Any(a => a.SymbolId is not null &&
            SymbolAttachmentPoint(draft, originals[a.SymbolId], a) != SymbolAttachmentPoint(draft, doc.Symbols.Single(s => s.SymbolId == a.SymbolId), a)))
            .Select(w => w.WireId).ToHashSet(StringComparer.Ordinal);
        affected.UnionWith(links.Where(l => !l.Wire.ManualRoute).Select(l => l.Wire.WireId));
        if (doc.Wires.Any(w => w.Locked && (affected.Contains(w.WireId) || affected.Contains(w.Start.WireId ?? "") || affected.Contains(w.End.WireId ?? ""))))
            throw new InvalidOperationException("相連導線已鎖定，接點對齊已取消。");
        if (doc.Wires.Any(w => affected.Contains(w.Start.WireId ?? "") || affected.Contains(w.End.WireId ?? "")))
            throw new InvalidOperationException("受影響的接點含分支導線；保留原接法與轉角，不自動拉直接點。");
        // Reanchor every fixed/manual route before reserving geometry for automatic routes.
        for (var i = 0; i < doc.Wires.Count; i++)
        {
            var wire = doc.Wires[i];
            if (!affected.Contains(wire.WireId)) continue;
            var points = wire.Points.ToList();
            var start = wire.Start.SymbolId is string sourceId
                ? SymbolAttachmentPoint(draft, doc.Symbols.Single(s => s.SymbolId == sourceId), wire.Start) : points[0];
            var end = wire.End.SymbolId is string targetId
                ? SymbolAttachmentPoint(draft, doc.Symbols.Single(s => s.SymbolId == targetId), wire.End) : points[^1];
            if (wire.ManualRoute && wire.Points.Count == 2)
                points = Simplify(start.X == end.X || start.Y == end.Y ? [start, end] :
                    [start, wire.Points[0].Y == wire.Points[1].Y ? new(end.X, start.Y) : new(start.X, end.Y), end]);
            else
            {
                points = Reanchor(points, start, true);
                points = Reanchor(points, end, false);
            }
            if (wire.ManualRoute && RouteRetraces(points)) throw new InvalidOperationException("對齊會讓手動導線回折重疊，操作已取消；請先調整該導線。");
            doc.Wires[i] = wire with { Points = points };
        }
        var leads = new List<SchematicWire>();
        var leadOwners = new Dictionary<string, (SchematicSymbol Symbol, SchematicEndpointDisplayPlacement Placement)>(StringComparer.Ordinal);
        var prefix = "fanout-display-lead:";
        while (doc.Wires.Any(w => w.WireId.StartsWith(prefix, StringComparison.Ordinal))) prefix = "_" + prefix;
        foreach (var symbol in doc.Symbols)
        foreach (var placement in symbol.EndpointDisplayPlacements)
        {
            var id = prefix + leads.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            leadOwners[id] = (symbol, placement);
            leads.Add(new() { WireId = id, PageId = symbol.PageId,
                Start = SchematicAttachment.Pin(symbol.SymbolId, placement.EndpointId), End = SchematicAttachment.Pin(symbol.SymbolId, placement.EndpointId),
                Points = PortFanoutLeadPoints(symbol, placement.EndpointId).ToList() });
        }
        bool OwnedRoute(string leadId, SchematicWire wire)
        {
            var owner = leadOwners[leadId];
            return new[] { wire.Start, wire.End }.Any(a => a.SymbolId == owner.Symbol.SymbolId &&
                (a.Kind == SchematicAttachmentKind.Pin && a.EndpointId == owner.Placement.EndpointId ||
                 a.Kind == SchematicAttachmentKind.Port && a.EndpointId == owner.Placement.PortId));
        }
        void CheckLeads(IReadOnlyList<SchematicWire> routes)
        {
            var all = leads.Concat(routes).ToArray();
            var byId = all.ToDictionary(w => w.WireId, StringComparer.Ordinal);
            foreach (var conflict in SchematicCrossingService.Analyze(all).Conflicts.Where(c =>
                c.Code is "COLLINEAR_OVERLAP" or "UNCONNECTED_CONTACT" &&
                (leadOwners.ContainsKey(c.FirstWireId) || leadOwners.ContainsKey(c.SecondWireId))))
            {
                if (leadOwners.TryGetValue(conflict.FirstWireId, out var first) && leadOwners.TryGetValue(conflict.SecondWireId, out var second))
                {
                    if (first.Symbol.SymbolId == second.Symbol.SymbolId && first.Placement.PortId == second.Placement.PortId &&
                        NativeAnchorPoint(first.Symbol, first.Placement.EndpointId) == NativeAnchorPoint(second.Symbol, second.Placement.EndpointId)) continue;
                }
                else
                {
                    var leadId = leadOwners.ContainsKey(conflict.FirstWireId) ? conflict.FirstWireId : conflict.SecondWireId;
                    var routeId = leadId == conflict.FirstWireId ? conflict.SecondWireId : conflict.FirstWireId;
                    if (OwnedRoute(leadId, byId[routeId])) continue;
                }
                throw new InvalidOperationException("展開引線會碰到其他接點或導線，對齊已取消；請調整引線長度或模塊位置。");
            }
            foreach (var lead in leads)
            foreach (var symbol in doc.Symbols.Where(s => s.PageId == lead.PageId))
            {
                var owner = SchematicSymbolOwner.Resolve(draft, symbol);
                foreach (var anchor in symbol.Anchors.Where(a => !SchematicPortPresentation.IsCollapsedPin(doc, symbol, owner, a)))
                {
                    var endpoint = leadOwners[lead.WireId];
                    if (symbol.SymbolId == endpoint.Symbol.SymbolId && anchor.EndpointId == endpoint.Placement.EndpointId) continue;
                    var point = AnchorPoint(symbol, anchor.EndpointId);
                    var sharedNative = symbol.SymbolId == endpoint.Symbol.SymbolId && point == lead.Points[0] &&
                        owner.Ports.Any(p => p.PortId == endpoint.Placement.PortId && p.Pins.Any(pin => pin.PinId == anchor.EndpointId));
                    if (!sharedNative && lead.Points.Zip(lead.Points.Skip(1)).Any(e =>
                        e.First.Y == e.Second.Y && point.Y == e.First.Y && point.X >= Math.Min(e.First.X, e.Second.X) && point.X <= Math.Max(e.First.X, e.Second.X) ||
                        e.First.X == e.Second.X && point.X == e.First.X && point.Y >= Math.Min(e.First.Y, e.Second.Y) && point.Y <= Math.Max(e.First.Y, e.Second.Y)))
                        throw new InvalidOperationException("展開引線會碰到另一個 Pin，對齊已取消；請調整引線長度或模塊位置。");
                }
            }
        }
        var fixedRoutes = doc.Wires.Where(w => !affected.Contains(w.WireId) || !CanRerouteSimpleWire(doc, w)).ToList();
        CheckLeads(fixedRoutes);
        var reserved = fixedRoutes.Concat(leads).ToList();
        IReadOnlyList<SchematicWire> ReservationsFor(SchematicWire wire) => reserved.Where(route =>
            !leadOwners.ContainsKey(route.WireId) || !OwnedRoute(route.WireId, wire)).ToArray();
        foreach (var wire in doc.Wires.Where(w => affected.Contains(w.WireId)))
            EnsureAutomaticEndpointsClear(doc, wire, ReservationsFor(wire));
        for (var i = 0; i < doc.Wires.Count; i++)
        {
            var wire = doc.Wires[i];
            if (!affected.Contains(wire.WireId) || !CanRerouteSimpleWire(doc, wire)) continue;
            doc.Wires[i] = wire with { Points = RerouteSimpleWire(draft, doc, wire, ReservationsFor(wire)) };
            reserved.Add(doc.Wires[i]);
        }
        var finalConflicts = SchematicCrossingService.Analyze(doc.Wires).Conflicts.Where(c => c.Code is "COLLINEAR_OVERLAP" or "UNCONNECTED_CONTACT" &&
            (affected.Contains(c.FirstWireId) || affected.Contains(c.SecondWireId)));
        foreach (var conflict in finalConflicts)
        {
            var first = doc.Wires.Single(w => w.WireId == conflict.FirstWireId);
            var second = doc.Wires.Single(w => w.WireId == conflict.SecondWireId);
            var sharedPin = new[] { first.Start, first.End }.Any(a => a.Kind is SchematicAttachmentKind.Pin or SchematicAttachmentKind.Port &&
                new[] { second.Start, second.End }.Any(b => b == a));
            if (!sharedPin) throw new InvalidOperationException("對齊後導線會碰到未相連接點或重疊，操作已取消；請先調整手動導線。");
        }
        CheckLeads(doc.Wires);
    });

    private static SchematicPoint LocalDisplayPoint(SchematicSymbol symbol, SchematicPoint global)
    {
        var x = global.X - symbol.Position.X; var y = global.Y - symbol.Position.Y;
        return symbol.Rotation switch { 0 => new(x, y), 90 => new(y, symbol.Height - x),
            180 => new(symbol.Width - x, symbol.Height - y), 270 => new(symbol.Width - y, x),
            _ => throw new InvalidOperationException("Only orthogonal rotations are supported.") };
    }
}
