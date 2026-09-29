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
        if (!symbol.Anchors.Any(a => port.Pins.Any(p => p.PinId == a.EndpointId)))
            throw new InvalidOperationException("This representation has no pins for the selected port.");
        var collapsed = symbol.CollapsedPortIds.ToList();
        if (!collapsed.Remove(portId)) collapsed.Add(portId);
        doc.Symbols[index] = symbol with { CollapsedPortIds = collapsed };
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
        if (anchors.Length == 0) throw new InvalidOperationException("This representation has no pins for the selected port.");
        const double margin = 3;
        const double pitch = 5;
        var span = (anchors.Length - 1) * pitch;
        var extent = side is "Left" or "Right" ? symbol.Height : symbol.Width;
        if (symbol.Geometry is null) extent = Math.Max(extent, span + 2 * margin);
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
        var next = symbol with { Anchors = symbol.Anchors.Select(a => positions.TryGetValue(a.EndpointId, out var point)
            ? a with { Position = point, Direction = side, Confirmed = false, CadContactId = null } : a).ToList(),
            AssetRevision = null };
        if (symbol.Geometry is null)
            next = FitGenericPortEdges(next, owner.Ports, symbol.Width, symbol.Height);
        foreach (var wire in doc.Wires.Where(w => w.Locked))
            if (new[] { wire.Start, wire.End }.Where(a => a.SymbolId == symbolId)
                .Any(a => AnchorPoint(symbol, a.EndpointId!) != AnchorPoint(next, a.EndpointId!)))
                throw new InvalidOperationException("Unlock attached wires before moving or resizing this port.");
        doc.Symbols[index] = next;
        for (var i = 0; i < doc.Wires.Count; i++)
        {
            var wire = doc.Wires[i];
            if (wire.Start.SymbolId != symbolId && wire.End.SymbolId != symbolId) continue;
            var points = wire.Points.ToList();
            if (wire.Start.SymbolId == symbolId && AnchorPoint(symbol, wire.Start.EndpointId!) != AnchorPoint(next, wire.Start.EndpointId!))
                points = ReanchorSymbol(points, next, wire.Start.EndpointId!, true);
            if (wire.End.SymbolId == symbolId && AnchorPoint(symbol, wire.End.EndpointId!) != AnchorPoint(next, wire.End.EndpointId!))
                points = ReanchorSymbol(points, next, wire.End.EndpointId!, false);
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
        foreach (var (side, groups) in bySide)
        {
            if (groups.Count < 2) continue;
            var horizontal = side is "Top" or "Bottom";
            var length = horizontal ? width : height;
            var ordered = groups.OrderBy(g => g.Average(a => horizontal ? a.Position.X : a.Position.Y)).ToArray();
            var widths = ordered.Select(g => Math.Max(16, (g.Count - 1) * 5 + 5)).ToArray();
            var gap = (length - 6 - widths.Sum()) / (ordered.Length + 1);
            var cursor = 3 + gap;
            for (var groupIndex = 0; groupIndex < ordered.Length; groupIndex++)
            {
                var group = ordered[groupIndex].OrderBy(a => horizontal ? a.Position.X : a.Position.Y).ToArray();
                var start = cursor + (widths[groupIndex] - (group.Length - 1) * 5) / 2;
                for (var pin = 0; pin < group.Length; pin++)
                {
                    var old = anchorsById[group[pin].EndpointId];
                    var position = horizontal ? new SchematicPoint(start + pin * 5, side == "Top" ? 0 : height)
                        : new SchematicPoint(side == "Left" ? 0 : width, start + pin * 5);
                    anchorsById[old.EndpointId] = old with { Position = position, Confirmed = false, CadContactId = null };
                }
                cursor += widths[groupIndex] + gap;
            }
        }
        return symbol with { Width = width, Height = height,
            Anchors = symbol.Anchors.Select(a => anchorsById[a.EndpointId]).ToList() };
    }
}
