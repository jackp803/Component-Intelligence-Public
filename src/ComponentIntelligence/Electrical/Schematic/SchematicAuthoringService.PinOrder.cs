using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    public static IReadOnlyList<SchematicAnchor> PortPinsInVisualOrder(SchematicSymbol symbol, ComponentPort port)
    {
        var ids = port.Pins.Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        var pins = symbol.Anchors.Where(a => ids.Contains(a.EndpointId)).ToArray();
        if (pins.Length == 0) return pins;
        var points = pins.ToDictionary(a => a.EndpointId, a => AnchorPoint(symbol, a.EndpointId));
        var horizontal = points.Values.Max(p => p.X) - points.Values.Min(p => p.X) >
            points.Values.Max(p => p.Y) - points.Values.Min(p => p.Y);
        return pins.OrderBy(a => horizontal ? points[a.EndpointId].X : points[a.EndpointId].Y)
            .ThenBy(a => horizontal ? points[a.EndpointId].Y : points[a.EndpointId].X).ToArray();
    }

    public ElectricalProject ReorderPortPins(ElectricalProject project, string symbolId, string portId,
        IReadOnlyList<string> orderedPinIds) => Edit(project, (draft, doc) =>
    {
        var index = doc.Symbols.FindIndex(s => s.SymbolId == symbolId);
        if (index < 0) throw new InvalidOperationException("Select a component representation.");
        var symbol = doc.Symbols[index];
        if (symbol.Locked) throw new InvalidOperationException("Unlock the representation before reordering Pins.");
        var port = SchematicSymbolOwner.Resolve(draft, symbol).Ports.SingleOrDefault(p => p.PortId == portId)
            ?? throw new InvalidOperationException("Unknown Port.");
        var slots = PortPinsInVisualOrder(symbol, port);
        if (orderedPinIds.Count != slots.Count || orderedPinIds.Distinct(StringComparer.Ordinal).Count() != slots.Count ||
            !orderedPinIds.ToHashSet(StringComparer.Ordinal).SetEquals(slots.Select(a => a.EndpointId)))
            throw new InvalidOperationException("Choose each represented Pin exactly once.");
        var display = symbol.EndpointDisplayPlacements.Where(p => p.PortId == portId).ToDictionary(p => p.EndpointId, StringComparer.Ordinal);
        if (display.Count > 0)
        {
            var displayPositions = orderedPinIds.Select((id, i) => (id, point: display[slots[i].EndpointId].Position)).ToDictionary(p => p.id, p => p.point, StringComparer.Ordinal);
            var reordered = symbol with { EndpointDisplayPlacements = symbol.EndpointDisplayPlacements.Select(p => displayPositions.TryGetValue(p.EndpointId, out var point) ? p with { Position = point } : p).ToList() };
            ReanchorChangedSymbol(draft, doc, index, symbol, reordered);
            return;
        }
        if (slots.Any(a => a.CadContactId is not null))
            throw new InvalidOperationException("Physical CAD Pin bindings must be changed in contact binding; only virtual Pins can be reordered.");
        var positions = orderedPinIds.Select((id, i) => (id, slot: slots[i])).ToDictionary(p => p.id, p => p.slot, StringComparer.Ordinal);
        var next = symbol with { Anchors = symbol.Anchors.Select(a => positions.TryGetValue(a.EndpointId, out var slot)
            ? a with { Position = slot.Position, Direction = slot.Direction, Confirmed = false } : a).ToList(), AssetRevision = null };
        ReanchorChangedSymbol(draft, doc, index, symbol, next);
    });
}
