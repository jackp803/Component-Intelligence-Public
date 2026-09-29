using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    public ElectricalProject TogglePortCollapsed(ElectricalProject project, string symbolId, string portId) => Edit(project, (draft, doc) =>
    {
        var index = doc.Symbols.FindIndex(s => s.SymbolId == symbolId);
        if (index < 0) throw new InvalidOperationException("Select a component representation.");
        var symbol = doc.Symbols[index];
        var port = SchematicSymbolOwner.Resolve(draft, symbol).Ports.SingleOrDefault(p => p.PortId == portId)
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
        var port = SchematicSymbolOwner.Resolve(draft, symbol).Ports.SingleOrDefault(p => p.PortId == portId)
            ?? throw new InvalidOperationException("Unknown port.");
        var pins = port.Pins.Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        var anchors = symbol.Anchors.Where(a => pins.Contains(a.EndpointId)).ToArray();
        if (anchors.Length == 0) throw new InvalidOperationException("This representation has no pins for the selected port.");
        if (doc.Wires.Any(w => w.Locked && (w.Start.SymbolId == symbolId && pins.Contains(w.Start.EndpointId!) ||
            w.End.SymbolId == symbolId && pins.Contains(w.End.EndpointId!))))
            throw new InvalidOperationException("Unlock attached wires before moving this port.");
        const double margin = 3;
        const double pitch = 5;
        var extent = side is "Left" or "Right" ? symbol.Height : symbol.Width;
        var span = (anchors.Length - 1) * pitch;
        if (span > extent - 2 * margin)
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
        doc.Symbols[index] = next;
        for (var i = 0; i < doc.Wires.Count; i++)
        {
            var wire = doc.Wires[i];
            if (wire.Start.SymbolId != symbolId && wire.End.SymbolId != symbolId) continue;
            var points = wire.Points.ToList();
            if (wire.Start.SymbolId == symbolId && positions.ContainsKey(wire.Start.EndpointId!))
                points = ReanchorSymbol(points, next, wire.Start.EndpointId!, true);
            if (wire.End.SymbolId == symbolId && positions.ContainsKey(wire.End.EndpointId!))
                points = ReanchorSymbol(points, next, wire.End.EndpointId!, false);
            doc.Wires[i] = wire with { Points = points };
        }
    });
}
