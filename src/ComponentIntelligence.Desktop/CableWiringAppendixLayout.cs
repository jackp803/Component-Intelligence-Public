using System.Windows;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

internal static class CableWiringAppendixLayout
{
    public static CableWiringDiagramPresentation Build(CableInstance cable, double availableWidth)
    {
        var draft = new CableManufacturingEditorService().Prepare(cable);
        var service = new CableWiringEditorService();
        var original = service.Build(draft);
        var wrapped = Wrap(original, draft.Ends);
        if (Fits(wrapped, draft.Ends) && wrapped.Bounds.Width <= availableWidth) return wrapped;
        var groups = TextGroups(original, draft.Ends);

        // Reflow only the export copy. Exact Pin IDs and electrical edges are retained.
        var width = Math.Min(100, Math.Max(45, (availableWidth - 70) / 2));
        var gap = Math.Min(80, Math.Max(30, availableWidth - 2 * width - 20));
        draft.WiringDiagram = new();
        var top = 10d;
        for (var row = 0; row < draft.Ends.Count; row += 2)
        {
            var rowHeight = 0d;
            for (var column = 0; column < Math.Min(2, draft.Ends.Count - row); column++)
            {
                var port = draft.Ends[row + column];
                var labels = groups[port.PortId];
                double Height(SchematicCadPrimitive p) => Measure(p with { TextWidth = width - 6 }).Height;
                var header = labels[1];
                var descriptionY = Math.Max(10, 3 + Height(labels[0]) + 3);
                var y = Math.Max(22, descriptionY + Height(header) + 6);
                var x = 10 + column * (width + gap);
                foreach (var pin in port.Pins.OrderBy(p => original.PinPositions[p.PinId].Y))
                {
                    var label = labels[2 + port.Pins.IndexOf(pin)];
                    draft.WiringDiagram.PinPositions[pin.PinId] = new(column == 0 ? x + width : x, top + y);
                    y += Math.Max(7, Height(label) + 4);
                }
                draft.WiringDiagram.PortBounds[port.PortId] = new(x, top, width, y + 3);
                rowHeight = Math.Max(rowHeight, y + 3);
            }
            top += rowHeight + 18;
        }
        var result = Wrap(service.Build(draft), draft.Ends);
        if (!Fits(result, draft.Ends)) throw new InvalidOperationException("接線圖文字無法在接頭框內安全排版，未輸出重疊標示。");
        return result;
    }

    private static Dictionary<string, SchematicCadPrimitive[]> TextGroups(CableWiringDiagramPresentation diagram, IReadOnlyList<ComponentPort> ports)
    {
        // Build emits the two headings and each Port's Pin labels together, including labels outside a saved box.
        var labels = diagram.Primitives.Where(p => p.Kind is "TEXT" or "MTEXT").ToArray();
        var result = new Dictionary<string, SchematicCadPrimitive[]>(StringComparer.Ordinal); var index = 0;
        foreach (var port in ports) { result.Add(port.PortId, labels.Skip(index).Take(2 + port.Pins.Count).ToArray()); index += 2 + port.Pins.Count; }
        if (index != labels.Length) throw new InvalidOperationException("接線圖文字與 Port／Pin 清單不一致，未輸出省略標示的圖面。");
        return result;
    }

    private static Size Measure(SchematicCadPrimitive p)
    {
        var text = CableWiringVisualRenderer.CreateText(p, 4, 3.2);
        text.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        return new(text.DesiredSize.Width / 4, text.DesiredSize.Height / 4);
    }

    private static CableWiringDiagramPresentation Wrap(CableWiringDiagramPresentation diagram, IReadOnlyList<ComponentPort> ports)
    {
        var owners = new Dictionary<SchematicCadPrimitive, SchematicGridBounds>(ReferenceEqualityComparer.Instance);
        foreach (var (portId, labels) in TextGroups(diagram, ports))
            foreach (var label in labels) owners.Add(label, diagram.PortBounds[portId]);
        var descriptions = new Dictionary<SchematicCadPrimitive, double>(ReferenceEqualityComparer.Instance);
        foreach (var labels in TextGroups(diagram, ports).Values)
        {
            var name = labels[0] with { TextWidth = Math.Max(1, owners[labels[0]].Width - 6), TextHeight = Math.Max(3.2, labels[0].TextHeight) };
            descriptions.Add(labels[1], owners[labels[0]].Y + Math.Max(10, 3 + Measure(name).Height + 3));
        }
        var primitives = diagram.Primitives.Select(p => {
            if (p.Kind is not ("TEXT" or "MTEXT")) return p;
            var port = owners[p];
            return p with { Start = descriptions.TryGetValue(p, out var y) ? p.Start with { Y = y } : p.Start,
                TextWidth = Math.Max(1, port.X + port.Width - p.Start.X - 3), TextHeight = Math.Max(3.2, p.TextHeight) };
        }).ToArray();
        var right = primitives.Max(p => p.Kind is "TEXT" or "MTEXT" ? p.Start.X + Measure(p).Width : Math.Max(p.Start.X, p.End?.X ?? p.Start.X) + p.Radius);
        var bottom = primitives.Max(p => p.Kind is "TEXT" or "MTEXT" ? p.Start.Y + Measure(p).Height : Math.Max(p.Start.Y, p.End?.Y ?? p.Start.Y) + p.Radius);
        return diagram with { Primitives = primitives, Bounds = diagram.Bounds with { Width = right - diagram.Bounds.X + 3, Height = bottom - diagram.Bounds.Y + 3 } };
    }

    private static bool Fits(CableWiringDiagramPresentation diagram, IReadOnlyList<ComponentPort> ports)
    {
        var groups = TextGroups(diagram, ports);
        foreach (var (portId, port) in diagram.PortBounds)
        {
            var labels = groups[portId].Select(p => new Rect(new(p.Start.X, p.Start.Y), Measure(p))).ToArray();
            if (labels.Any(r => r.Bottom > port.Y + port.Height - 2 || r.Top < port.Y || r.Left < port.X || r.Right > port.X + port.Width)) return false;
            for (var i = 0; i < labels.Length; i++)
            for (var j = i + 1; j < labels.Length; j++)
                if (labels[i].IntersectsWith(labels[j])) return false;
        }
        return true;
    }
}
