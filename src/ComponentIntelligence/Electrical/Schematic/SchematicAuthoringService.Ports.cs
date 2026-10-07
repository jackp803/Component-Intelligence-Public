using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    public ElectricalProject TogglePortCollapsed(ElectricalProject project, string symbolId, string portId) => Edit(project, (draft, doc) =>
    {
        var index = doc.Symbols.FindIndex(s => s.SymbolId == symbolId);
        if (index < 0) throw new InvalidOperationException("Select a component representation.");
        var symbol = doc.Symbols[index];
        var owner = SchematicSymbolOwner.Resolve(draft, symbol);
        var port = owner.Ports.SingleOrDefault(p => p.PortId == portId)
            ?? throw new InvalidOperationException("Unknown port.");
        if (!SchematicPortPresentation.IsRepresented(symbol, port))
            throw new InvalidOperationException("This section contains only part of the Port; connect its exact Pins.");
        if (!symbol.Anchors.Any(a => port.Pins.Any(p => p.PinId == a.EndpointId)))
            throw new InvalidOperationException("This representation has no pins for the selected port.");
        var collapsed = symbol.CollapsedPortIds.ToList();
        if (!collapsed.Remove(portId)) collapsed.Add(portId);
        var next = symbol with { CollapsedPortIds = collapsed };
        if (symbol.ManualSize)
            EnsureMovedContactsClear(draft, doc, next,
                collapsed.Contains(portId) ? ["port:" + portId] : port.Pins.Select(p => "pin:" + p.PinId).ToArray());
        foreach (var wire in doc.Wires.Where(w => w.Locked))
            if (new[] { wire.Start, wire.End }.Where(a => a.Kind == SchematicAttachmentKind.Port && a.SymbolId == symbolId)
                .Any(a => SymbolAttachmentPoint(draft, symbol, a) != SymbolAttachmentPoint(draft, next, a)))
                throw new InvalidOperationException("Unlock attached Port wires before changing this display.");
        doc.Symbols[index] = next;
        for (var i = 0; i < doc.Wires.Count; i++)
        {
            var wire = doc.Wires[i];
            var moveStart = wire.Start.Kind == SchematicAttachmentKind.Port && wire.Start.SymbolId == symbolId &&
                SymbolAttachmentPoint(draft, symbol, wire.Start) != SymbolAttachmentPoint(draft, next, wire.Start);
            var moveEnd = wire.End.Kind == SchematicAttachmentKind.Port && wire.End.SymbolId == symbolId &&
                SymbolAttachmentPoint(draft, symbol, wire.End) != SymbolAttachmentPoint(draft, next, wire.End);
            if (!moveStart && !moveEnd) continue;
            var points = wire.Points.ToList();
            if (moveStart)
                points = Reanchor(points, SymbolAttachmentPoint(draft, next, wire.Start), true);
            if (moveEnd)
                points = Reanchor(points, SymbolAttachmentPoint(draft, next, wire.End), false);
            doc.Wires[i] = wire with { Points = points };
            ReanchorDependentJunctions(doc, wire.WireId);
        }
    });

    public ElectricalProject MovePortToEdge(ElectricalProject project, string symbolId, string portId,
        string side, double coordinate) => Edit(project, (draft, doc) =>
    {
        var index = doc.Symbols.FindIndex(s => s.SymbolId == symbolId);
        if (index < 0) throw new InvalidOperationException("Select a component representation.");
        var symbol = doc.Symbols[index];
        if (symbol.Locked) throw new InvalidOperationException("Unlock the representation before moving its port.");
        if (!double.IsFinite(coordinate) || side is not ("Left" or "Right" or "Top" or "Bottom"))
            throw new InvalidOperationException("Choose a finite position on one of the four module edges.");
        var owner = SchematicSymbolOwner.Resolve(draft, symbol);
        var port = owner.Ports.SingleOrDefault(p => p.PortId == portId)
            ?? throw new InvalidOperationException("Unknown port.");
        var pins = port.Pins.Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        var anchors = symbol.Anchors.Where(a => pins.Contains(a.EndpointId)).ToArray();
        if (anchors.Length == 0 && port.Pins.Count > 0)
            throw new InvalidOperationException("This representation does not contain the selected port pins.");
        SchematicSymbol next;
        if (anchors.Length == 0)
        {
            var extent = side is "Left" or "Right" ? symbol.Height : symbol.Width;
            if (extent < 6) throw new InvalidOperationException("This module edge is too short for a Port contact.");
            var placement = new SchematicPortPlacement { PortId = portId, Side = side,
                Coordinate = Math.Clamp(coordinate, 3, extent - 3) };
            next = symbol with { PortPlacements = symbol.PortPlacements.Where(p => p.PortId != portId)
                .Append(placement).ToList(), AssetRevision = null };
        }
        else
        {
            const double margin = 3;
            const double pitch = 5;
            var span = (anchors.Length - 1) * pitch;
            var extent = side is "Left" or "Right" ? symbol.Height : symbol.Width;
            if (symbol.Geometry is null && !symbol.ManualSize) extent = Math.Max(extent, span + 2 * margin);
            else if (span > extent - 2 * margin)
                throw new InvalidOperationException("This edge is too short for all pins in the port.");
            var first = Math.Clamp(coordinate - span / 2, margin, extent - margin - span);
            var positions = anchors.Select((anchor, i) => (anchor.EndpointId, Position: side switch
            {
                "Left" => new SchematicPoint(0, first + i * pitch),
                "Right" => new SchematicPoint(symbol.Width, first + i * pitch),
                "Top" => new SchematicPoint(first + i * pitch, 0),
                _ => new SchematicPoint(first + i * pitch, symbol.Height)
            })).ToDictionary(x => x.EndpointId, x => x.Position, StringComparer.Ordinal);
            next = symbol with { Anchors = symbol.Anchors.Select(a => positions.TryGetValue(a.EndpointId, out var point)
                ? a with { Position = point, Direction = side, Confirmed = false, CadContactId = null } : a).ToList(),
                AssetRevision = null };
            if (symbol.Geometry is null && !symbol.ManualSize)
                next = FitGenericPortEdges(next, owner.Ports, symbol.Width, symbol.Height);
        }
        next = next with { CadPortBindings = next.CadPortBindings.Where(b => b.PortId != portId).ToList() };
        if (symbol.ManualSize)
        {
            var extent = side is "Left" or "Right" ? symbol.Height : symbol.Width;
            var placement = new SchematicPortPlacement { PortId = portId, Side = side,
                Coordinate = Math.Clamp(coordinate, 3, extent - 3) };
            next = next with { PortPlacements = symbol.PortPlacements.Where(p => p.PortId != portId)
                .Append(placement).ToList() };
            EnsureMovedContactsClear(draft, doc, next,
                new[] { "port:" + portId }.Concat(anchors.Select(a => "pin:" + a.EndpointId)).ToArray());
        }
        foreach (var wire in doc.Wires.Where(w => w.Locked))
            if (new[] { wire.Start, wire.End }.Where(a => a.SymbolId == symbolId)
                .Any(a => SymbolAttachmentPoint(draft, symbol, a) != SymbolAttachmentPoint(draft, next, a)))
                throw new InvalidOperationException("Unlock attached wires before moving or resizing this port.");
        doc.Symbols[index] = next;
        for (var i = 0; i < doc.Wires.Count; i++)
        {
            var wire = doc.Wires[i];
            if (wire.Start.SymbolId != symbolId && wire.End.SymbolId != symbolId) continue;
            var points = wire.Points.ToList();
            if (wire.Start.SymbolId == symbolId && SymbolAttachmentPoint(draft, symbol, wire.Start) != SymbolAttachmentPoint(draft, next, wire.Start))
                points = ReanchorSymbol(points, draft, next, wire.Start, true);
            if (wire.End.SymbolId == symbolId && SymbolAttachmentPoint(draft, symbol, wire.End) != SymbolAttachmentPoint(draft, next, wire.End))
                points = ReanchorSymbol(points, draft, next, wire.End, false);
            if (CanReroutePortWire(doc, wire)) points = ReroutePortWire(draft, doc, wire);
            else if (CanRerouteSimpleWire(doc, wire)) points = RerouteSimpleWire(draft, doc, wire);
            doc.Wires[i] = wire with { Points = points };
            ReanchorDependentJunctions(doc, wire.WireId);
        }
    });

    private static SchematicSymbol FitGenericPortEdges(SchematicSymbol symbol, IReadOnlyList<ComponentPort> ports,
        double oldWidth, double oldHeight)
    {
        var bySide = new Dictionary<string, List<List<SchematicAnchor>>>(StringComparer.Ordinal)
        {
            ["Top"] = [], ["Bottom"] = [], ["Left"] = [], ["Right"] = []
        };
        foreach (var port in ports)
        {
            var ids = port.Pins.Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
            var anchors = symbol.Anchors.Where(a => ids.Contains(a.EndpointId)).ToList();
            if (anchors.Count > 0 && bySide.TryGetValue(anchors[0].Direction, out var sideGroups)) sideGroups.Add(anchors);
        }
        static double Required(List<List<SchematicAnchor>> groups) =>
            groups.Count == 0 ? 0 : 6 + groups.Sum(g => Math.Max(16, (g.Count - 1) * 5 + 5)) + 2 * (groups.Count - 1);
        var width = Math.Max(oldWidth, Math.Max(Required(bySide["Top"]), Required(bySide["Bottom"])));
        var height = Math.Max(oldHeight, Math.Max(Required(bySide["Left"]), Required(bySide["Right"])));
        var anchorsById = symbol.Anchors.ToDictionary(a => a.EndpointId, StringComparer.Ordinal);
        if (width > oldWidth || height > oldHeight)
            foreach (var anchor in symbol.Anchors)
            {
                var p = anchor.Position;
                if (anchor.Direction == "Right" && Math.Abs(p.X - oldWidth) < .001) p = p with { X = width };
                if (anchor.Direction == "Bottom" && Math.Abs(p.Y - oldHeight) < .001) p = p with { Y = height };
                anchorsById[anchor.EndpointId] = anchor with { Position = p };
            }
        return symbol with { Width = width, Height = height,
            Anchors = symbol.Anchors.Select(a => anchorsById[a.EndpointId]).ToList() };
    }
}
