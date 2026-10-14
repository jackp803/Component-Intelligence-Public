using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    public ElectricalProject SetPortFanout(ElectricalProject project, string symbolId, string portId,
        bool enabled, double pitch = 10, double lead = 25) => Edit(ElectricalProjectMigrator.ForPortFanout(project), (draft, doc) =>
    {
        if (!double.IsFinite(pitch) || pitch <= 0 || !double.IsFinite(lead) || lead <= 0)
            throw new InvalidOperationException("Fan-out pitch and lead must be finite positive distances.");
        var index = doc.Symbols.FindIndex(s => s.SymbolId == symbolId);
        if (index < 0) throw new InvalidOperationException("Unknown representation.");
        var symbol = doc.Symbols[index];
        var owner = SchematicSymbolOwner.Resolve(draft, symbol);
        var port = owner.Ports.SingleOrDefault(p => p.PortId == portId)
            ?? throw new InvalidOperationException("Unknown Port.");
        if (port.Pins.Count == 0 || !SchematicPortPresentation.IsRepresented(symbol, port))
            throw new InvalidOperationException("Fan-out requires a complete represented Port with exact Pins.");
        if (symbol.Locked || doc.Wires.Any(w => w.Locked && (w.Start.SymbolId == symbolId || w.End.SymbolId == symbolId)))
            throw new InvalidOperationException("Unlock the representation and incident routes before changing fan-out.");
        var next = FanoutSymbol(symbol, port, enabled, pitch, lead);
        doc.Symbols[index] = next;
        ValidatePortFanout(draft, doc, next, owner);
        var affected = doc.Wires.Where(w => new[] { w.Start, w.End }.Any(a => a.SymbolId == symbolId &&
            SymbolAttachmentPoint(draft, symbol, a) != SymbolAttachmentPoint(draft, next, a)))
            .Select(w => w.WireId).ToHashSet(StringComparer.Ordinal);
        var reserved = doc.Wires.Where(w => !affected.Contains(w.WireId) || !CanRerouteSimpleWire(doc, w)).ToList();
        for (var i = 0; i < doc.Wires.Count; i++)
        {
            var wire = doc.Wires[i];
            if (!affected.Contains(wire.WireId)) continue;
            var points = wire.Points.ToList();
            if (wire.Start.SymbolId == symbolId)
                points = Reanchor(points, SymbolAttachmentPoint(draft, next, wire.Start), true);
            if (wire.End.SymbolId == symbolId)
                points = Reanchor(points, SymbolAttachmentPoint(draft, next, wire.End), false);
            if (CanRerouteSimpleWire(doc, wire)) points = RerouteSimpleWire(draft, doc, wire, reserved);
            doc.Wires[i] = wire with { Points = points };
            reserved.Add(doc.Wires[i]);
            ReanchorDependentJunctions(doc, wire.WireId);
        }
    });

    private static SchematicSymbol FanoutSymbol(SchematicSymbol symbol, ComponentPort port, bool enabled, double pitch, double lead)
    {
        var placements = symbol.EndpointDisplayPlacements.Where(p => p.PortId != port.PortId).ToList();
        if (enabled)
        foreach (var group in port.Pins.Select(pin => symbol.Anchors.Single(a => a.EndpointId == pin.PinId)).GroupBy(a => a.Direction, StringComparer.Ordinal))
        {
            var side = group.Key;
            if (side is not ("Left" or "Right" or "Top" or "Bottom")) throw new InvalidOperationException("Fan-out requires orthogonal native Pin directions.");
            var members = group.ToArray();
            var native = members.Select(a => SchematicSymbolPresentation.CadDisplayPoint(symbol, a.Position)).ToArray();
            var center = side is "Left" or "Right" ? native.Average(p => p.Y) : native.Average(p => p.X);
            for (var i = 0; i < members.Length; i++)
            {
                var coordinate = center + (i - (members.Length - 1) / 2d) * pitch;
                var point = side switch { "Left" => new SchematicPoint(-lead, coordinate),
                    "Right" => new SchematicPoint(symbol.Width + lead, coordinate),
                    "Top" => new SchematicPoint(coordinate, -lead), _ => new SchematicPoint(coordinate, symbol.Height + lead) };
                placements.Add(new() { EndpointId = members[i].EndpointId, PortId = port.PortId, Position = point });
            }
        }
        return symbol with { EndpointDisplayPlacements = placements,
            CollapsedPortIds = enabled ? symbol.CollapsedPortIds.Where(id => id != port.PortId).ToList() : symbol.CollapsedPortIds };
    }

    public static IReadOnlyList<SchematicPoint> PortFanoutLeadPoints(SchematicSymbol symbol, string endpointId)
    {
        var placement = symbol.EndpointDisplayPlacements.SingleOrDefault(p => p.EndpointId == endpointId);
        if (placement is null) return [];
        var anchor = symbol.Anchors.Single(a => a.EndpointId == endpointId);
        var members = symbol.EndpointDisplayPlacements.Where(p => p.PortId == placement.PortId &&
            symbol.Anchors.Single(a => a.EndpointId == p.EndpointId).Direction == anchor.Direction).ToArray();
        var horizontal = anchor.Direction is "Left" or "Right";
        var native = members.Select(p => SchematicSymbolPresentation.CadDisplayPoint(symbol,
            symbol.Anchors.Single(a => a.EndpointId == p.EndpointId).Position)).ToArray();
        var paths = members.Select((p, i) =>
        {
            var lane = (i + 1d) / (members.Length + 1d);
            return new SchematicWire { WireId = p.EndpointId, PageId = symbol.PageId, Points = Simplify(horizontal
                ? new[] { native[i], new(native[i].X + (p.Position.X - native[i].X) * lane, native[i].Y),
                    new SchematicPoint(native[i].X + (p.Position.X - native[i].X) * lane, p.Position.Y), p.Position }
                : new[] { native[i], new(native[i].X, native[i].Y + (p.Position.Y - native[i].Y) * lane),
                    new SchematicPoint(p.Position.X, native[i].Y + (p.Position.Y - native[i].Y) * lane), p.Position }) };
        }).ToArray();
        var conflict = SchematicCrossingService.Analyze(paths).Conflicts.Any(c => c.Code == "COLLINEAR_OVERLAP" &&
            native[Array.FindIndex(members, p => p.EndpointId == c.FirstWireId)] != native[Array.FindIndex(members, p => p.EndpointId == c.SecondWireId)]);
        if (conflict)
        {
            // Separate entry/exit lanes avoid a lead merging into another Pin's native row after alignment.
            var rows = native.Select(p => horizontal ? p.Y : p.X).Concat(members.Select(p => horizontal ? p.Position.Y : p.Position.X)).ToHashSet();
            var boundary = anchor.Direction switch { "Left" or "Top" => 0, "Right" => symbol.Width, _ => symbol.Height };
            for (var i = 0; i < members.Length; i++)
            {
                var display = members[i].Position;
                var delta = (horizontal ? display.X : display.Y) - boundary;
                var fraction = (i + 1d) / (members.Length + 1d);
                var entry = boundary + delta * (.1 + .25 * fraction);
                var exit = boundary + delta * (.65 + .25 * fraction);
                var row = ((horizontal ? native[i].Y : native[i].X) + (horizontal ? display.Y : display.X)) / 2;
                var center = row;
                for (var offset = 1; rows.Contains(row); offset++) row = center + (offset % 2 == 0 ? 1 : -1) * ((offset + 1) / 2) * .5;
                rows.Add(row);
                paths[i] = paths[i] with { Points = Simplify(horizontal
                    ? new[] { native[i], new(entry, native[i].Y), new(entry, row), new(exit, row), new(exit, display.Y), display }
                    : new[] { native[i], new(native[i].X, entry), new(row, entry), new(row, exit), new(display.X, exit), display }) };
            }
        }
        return paths.Single(p => p.WireId == endpointId).Points.Select(p => TransformAnchorDisplayPoint(symbol, p)).ToArray();
    }

    private static void ValidatePortFanout(ElectricalProject project, SchematicDocument doc, SchematicSymbol symbol,
        SchematicSymbolOwner owner)
    {
        var placements = symbol.EndpointDisplayPlacements;
        if (placements.Count == 0) return;
        if (project.SchemaVersion != ElectricalProjectMigrator.PortFanoutSchemaVersion)
            throw new InvalidOperationException("Pin display placement requires project schema 0.12.");
        if (placements.Select(p => p.EndpointId).Distinct(StringComparer.Ordinal).Count() != placements.Count ||
            placements.Select(p => p.Position).Distinct().Count() != placements.Count)
            throw new InvalidOperationException("Fan-out contacts require unique exact Pin identities and positions.");
        foreach (var group in placements.GroupBy(p => p.PortId, StringComparer.Ordinal))
        {
            var port = owner.Ports.SingleOrDefault(p => p.PortId == group.Key);
            if (port is null || port.Pins.Count == 0 || !SchematicPortPresentation.IsRepresented(symbol, port) ||
                !group.Select(p => p.EndpointId).ToHashSet(StringComparer.Ordinal).SetEquals(port.Pins.Select(p => p.PinId)))
                throw new InvalidOperationException("Fan-out display placement must retain every exact Pin of its represented Port.");
        }
        var page = doc.Pages.Single(p => p.PageId == symbol.PageId);
        bool OnSheet(SchematicPoint p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && p.X >= page.Margin &&
            p.Y >= page.Margin && p.X <= page.Width - page.Margin && p.Y <= page.Height - page.Margin;
        var bodies = doc.Symbols.Where(s => s.PageId == symbol.PageId && s.SymbolId != symbol.SymbolId)
            .Select(s => SchematicPortPresentation.GenericBodyBounds(doc, s with { EndpointDisplayPlacements = [] },
                SchematicSymbolOwner.Resolve(project, s))).ToArray();
        foreach (var placement in placements)
        {
            RequirePoint(placement.Position);
            var anchor = symbol.Anchors.Single(a => a.EndpointId == placement.EndpointId);
            var p = placement.Position;
            var outside = anchor.Direction switch
            {
                "Left" => p.X < 0, "Right" => p.X > symbol.Width,
                "Top" => p.Y < 0, "Bottom" => p.Y > symbol.Height, _ => false
            };
            if (!outside) throw new InvalidOperationException("Fan-out display contacts must lie outside the native body on their native side.");
            var leads = PortFanoutLeadPoints(symbol, placement.EndpointId);
            if (leads.Any(p => !OnSheet(p))) throw new InvalidOperationException("Fan-out leads must remain inside the sheet.");
            if (bodies.Any(body => leads.Zip(leads.Skip(1)).Any(s => FanoutSegmentTouchesBody(s.First, s.Second, body))))
                throw new InvalidOperationException("A fan-out contact or lead overlaps another representation body.");
        }
    }

    private static bool FanoutSegmentTouchesBody(SchematicPoint a, SchematicPoint b, SchematicGridBounds body) =>
        a.Y == b.Y && a.Y >= body.Y && a.Y <= body.Y + body.Height &&
            Math.Min(a.X, b.X) <= body.X + body.Width && Math.Max(a.X, b.X) >= body.X ||
        a.X == b.X && a.X >= body.X && a.X <= body.X + body.Width &&
            Math.Min(a.Y, b.Y) <= body.Y + body.Height && Math.Max(a.Y, b.Y) >= body.Y;
}
