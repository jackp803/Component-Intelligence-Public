using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    public ElectricalProject MatePorts(ElectricalProject project, SchematicAttachment from, SchematicAttachment to,
        IReadOnlyList<ConnectorMatingPinPair> pairs, string evidence, string signal) => Edit(project, (draft, doc) =>
    {
        if (from.Kind != SchematicAttachmentKind.Port || to.Kind != SchematicAttachmentKind.Port)
            throw new InvalidOperationException("接頭對接需選擇兩個 Port；單一 Pin 請使用導線。");
        ArgumentException.ThrowIfNullOrWhiteSpace(signal);
        var first = doc.Symbols.Single(s => s.SymbolId == from.SymbolId);
        var second = doc.Symbols.Single(s => s.SymbolId == to.SymbolId);
        if (first.Locked || second.Locked) throw new InvalidOperationException("請先解除模塊鎖定。");
        var connection = ConnectorMatingService.Connect(draft, from.EndpointId!, to.EndpointId!, pairs, evidence);
        var wire = new SchematicWire { WireId = "wire-" + Guid.NewGuid().ToString("N"), PageId = first.PageId,
            Start = from, End = to, ConnectionId = connection.ConnectionId };
        doc.Wires.Add(wire);
        var other = wire with { WireId = "wire-" + Guid.NewGuid().ToString("N"), PageId = second.PageId };
        if (first.PageId != second.PageId) doc.Wires.Add(other);
        var a = SchematicPortPresentation.ConnectionPoint(draft, first, from.EndpointId!);
        var b = SchematicPortPresentation.ConnectionPoint(draft, second, to.EndpointId!);
        if (first.PageId == second.PageId)
        {
            var initial = wire with { Points = [a, b] };
            doc.Wires[doc.Wires.IndexOf(wire)] = initial with { Points = RerouteSimpleWire(draft, doc, initial) };
            return;
        }
        SchematicPoint Marker(SchematicSymbol symbol, SchematicAttachment attachment, SchematicPoint point)
        {
            var port = ConnectorMatingService.RequirePort(draft, attachment.EndpointId!);
            var side = SchematicPortPresentation.PortSide(symbol, port);
            var page = doc.Pages.Single(p => p.PageId == symbol.PageId);
            var room = side switch { "Left" => point.X - page.Margin, "Top" => point.Y - page.Margin,
                "Bottom" => page.Height - page.Margin - point.Y, _ => page.Width - page.Margin - point.X };
            var distance = Math.Min(15, room - 3);
            if (distance < 4)
                throw new InvalidOperationException("接頭旁沒有放箭頭的空間；請將模塊往圖紙內移動。");
            var result = side switch { "Left" => new SchematicPoint(point.X - distance, point.Y),
                "Top" => new(point.X, point.Y - distance), "Bottom" => new(point.X, point.Y + distance), _ => new(point.X + distance, point.Y) };
            return result;
        }
        var id = "xref-" + Guid.NewGuid().ToString("N");
        var source = new SchematicMarker { MarkerId = id + ":source", PageId = first.PageId, Position = Marker(first, from, a) };
        var destination = new SchematicMarker { MarkerId = id + ":destination", PageId = second.PageId, Position = Marker(second, to, b) };
        doc.Continuations.Add(new() { ContinuationId = id, Signal = signal.Trim(), Source = source, Destination = destination });
        doc.Wires[doc.Wires.IndexOf(wire)] = wire with { End = SchematicAttachment.Marker(source.MarkerId), Points = [a, source.Position] };
        doc.Wires[doc.Wires.IndexOf(other)] = other with { Start = SchematicAttachment.Marker(destination.MarkerId), Points = [destination.Position, b] };
    });
}
