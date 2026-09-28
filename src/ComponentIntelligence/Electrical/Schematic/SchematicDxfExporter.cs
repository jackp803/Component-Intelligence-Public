using ComponentIntelligence.Electrical.Domain;
using netDxf;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Units;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicDxfSheet(string PageId, string Title, byte[] Dxf, IReadOnlyList<string> Diagnostics);

// Draft geometry exchange, not an ACADE project or electrical verification report.
public sealed class SchematicDxfExporter
{
    public IReadOnlyList<SchematicDxfSheet> Create(ElectricalProject project)
    {
        SchematicAuthoringService.Validate(project);
        var source = project.Schematic ?? throw new InvalidOperationException("No schematic pages.");
        return source.Pages.Select((page, index) => CreatePage(project, source, page, index + 1)).ToArray();
    }

    private static SchematicDxfSheet CreatePage(ElectricalProject project, SchematicDocument source, SchematicPage page, int number)
    {
        var dxf = new DxfDocument();
        dxf.DrawingVariables.InsUnits = DrawingUnits.Millimeters;
        var diagnostics = new List<string>();
        Vector2 Point(SchematicPoint p) => new(p.X, page.Height - p.Y);
        Layer Layer(string name) => dxf.Layers.Contains(name) ? dxf.Layers[name] : dxf.Layers.Add(new Layer(name));
        void Add(EntityObject entity, string layer, double width = .25)
        {
            entity.Layer = Layer(layer);
            entity.Lineweight = width switch { <= .25 => Lineweight.W25, <= .35 => Lineweight.W35, <= .5 => Lineweight.W50, _ => Lineweight.W70 };
            if (layer == "SCHEMATIC_DRAFT_WIRE") entity.Linetype = Linetype.Dashed;
            dxf.Entities.Add(entity);
        }
        void Line(SchematicPoint a, SchematicPoint b, string layer, double weight = .25) => Add(new Line(Point(a), Point(b)), layer, weight);
        void Label(string? text, SchematicPoint p, string layer, double height = 3, double rotation = 0)
        {
            if (!string.IsNullOrWhiteSpace(text)) Add(new Text(text, Point(p), height) { Alignment = TextAlignment.TopLeft, Rotation = -rotation }, layer);
        }
        void Primitive(SchematicCadPrimitive primitive, string layer, double weight,
            Func<SchematicPoint, SchematicPoint>? transform = null, int rotation = 0)
        {
            var p = transform?.Invoke(primitive.Start) ?? primitive.Start;
            switch (primitive.Kind)
            {
                case "LINE" when primitive.End is not null:
                    Line(p, transform?.Invoke(primitive.End) ?? primitive.End, layer, weight); break;
                case "CIRCLE": Add(new Circle(Point(p), primitive.Radius), layer, weight); break;
                case "ARC": Add(new Arc(Point(p), primitive.Radius, primitive.StartAngle - rotation, primitive.EndAngle - rotation), layer, weight); break;
                case "TEXT": case "MTEXT":
                    Label(primitive.Text, p, layer, Math.Max(.1, primitive.TextHeight), primitive.Rotation + rotation);
                    if (primitive.Kind == "MTEXT") diagnostics.Add("MTEXT_FORMAT_REVIEW");
                    break;
                default: throw new InvalidOperationException($"Unsupported schematic primitive: {primitive.Kind}");
            }
        }
        if (page.TemplateGeometry is { } template)
        {
            foreach (var p in template.Primitives) Primitive(p, "SCHEMATIC_TEMPLATE", .25);
            diagnostics.AddRange(template.Diagnostics.Select(d => "TEMPLATE: " + d));
        }
        else
        {
            var m = page.Margin;
            SchematicPoint[] frame = [new(m, m), new(page.Width - m, m), new(page.Width - m, page.Height - m), new(m, page.Height - m), new(m, m)];
            for (var i = 1; i < frame.Length; i++) Line(frame[i - 1], frame[i], "SCHEMATIC_FRAME");
            diagnostics.Add("COMPANY_TEMPLATE_NOT_SET");
        }
        for (var col = 0; col < page.GridColumns; col++)
            Label((col + 1).ToString(), new(page.Margin + (col + .5) * (page.Width - 2 * page.Margin) / page.GridColumns, 3), "SCHEMATIC_FRAME", 10d / 3);
        for (var row = 0; row < page.GridRows; row++)
            Label(((char)('A' + row)).ToString(), new(3, page.Margin + (row + .5) * (page.Height - 2 * page.Margin) / page.GridRows), "SCHEMATIC_FRAME", 10d / 3);
        Label($"{project.Name}   |   {page.Title}   |   {number}", new(page.Margin + 2, page.Height - page.Margin - 8), "SCHEMATIC_FRAME", 4);

        var wires = source.Wires.Where(w => w.PageId == page.PageId).ToArray();
        var crossings = SchematicCrossingService.Analyze(wires);
        foreach (var wire in wires)
        {
            var weight = SchematicWirePresentation.StrokeWidthMm(wire.Awg);
            var layer = wire.ConnectionId is null ? "SCHEMATIC_DRAFT_WIRE" : "SCHEMATIC_WIRE";
            foreach (var p in SchematicCrossingService.RoutePrimitives(wire, crossings)) Primitive(p, layer, weight);
            if (wire.ConnectionId is null) diagnostics.Add("INCOMPLETE_WIRE: " + wire.WireId);
        }
        foreach (var p in crossings.Junctions)
            Add(new Hatch(HatchPattern.Solid, [new HatchBoundaryPath(new EntityObject[] { new Circle(Point(p), 5d / 6) })], false), "SCHEMATIC_JUNCTION");
        diagnostics.AddRange(crossings.Conflicts.Select(c => c.Code));

        foreach (var symbol in source.Symbols.Where(s => s.PageId == page.PageId))
        {
            var component = project.Components.Single(c => c.ComponentInstanceId == symbol.ComponentInstanceId);
            SchematicPoint Map(SchematicPoint p)
            {
                var local = symbol.Rotation switch
                {
                    90 => new SchematicPoint(symbol.Height - p.Y, p.X),
                    180 => new(symbol.Width - p.X, symbol.Height - p.Y),
                    270 => new(p.Y, symbol.Width - p.X),
                    _ => p
                };
                return new(symbol.Position.X + local.X, symbol.Position.Y + local.Y);
            }
            if (symbol.Geometry is { } geometry)
            {
                foreach (var p in geometry.Primitives) Primitive(p, "SCHEMATIC_SYMBOL", .25, Map, symbol.Rotation);
                diagnostics.AddRange(geometry.Diagnostics.Select(d => symbol.SymbolId + ": " + d));
            }
            else
            {
                SchematicPoint[] box = [new(0, 0), new(symbol.Width, 0), new(symbol.Width, symbol.Height), new(0, symbol.Height), new(0, 0)];
                for (var i = 1; i < box.Length; i++) Line(Map(box[i - 1]), Map(box[i]), "SCHEMATIC_SYMBOL");
                Label(component.DisplayName ?? component.ComponentDefinitionId, Map(new(2, symbol.Height / 2)), "SCHEMATIC_SYMBOL", 11d / 3, symbol.Rotation);
                diagnostics.Add("CATALOG_RASTER_IMAGE_NOT_EMBEDDED: " + symbol.SymbolId);
            }
            Label(component.ReferenceDesignator ?? "Reference 未設定", new(symbol.Position.X, symbol.Position.Y - 6), "SCHEMATIC_LABEL", 11d / 3);
            foreach (var anchor in symbol.Anchors)
            {
                var point = SchematicAuthoringService.AnchorPoint(symbol, anchor.EndpointId);
                Add(new Circle(Point(point), 7d / 6), "SCHEMATIC_PIN");
                Label(anchor.Label ?? anchor.EndpointId, new(point.X + 2, point.Y - 3.8), "SCHEMATIC_LABEL", 8.5 / 3);
                if (!anchor.Confirmed) diagnostics.Add("UNCONFIRMED_ANCHOR: " + anchor.EndpointId);
            }
        }
        var service = new SchematicAuthoringService();
        foreach (var pair in source.Continuations)
        foreach (var marker in new[] { pair.Source, pair.Destination }.Where(m => m.PageId == page.PageId))
        {
            var p = marker.Position; var a = new SchematicPoint(p.X - 3, p.Y - 5d / 3); var b = new SchematicPoint(p.X - 3, p.Y + 5d / 3);
            Line(p, a, "SCHEMATIC_CONTINUATION"); Line(a, b, "SCHEMATIC_CONTINUATION"); Line(b, p, "SCHEMATIC_CONTINUATION");
            Label(service.ReferenceFor(project, marker.MarkerId), new(p.X + 2, p.Y - 5), "SCHEMATIC_LABEL", 10d / 3);
        }
        using var stream = new MemoryStream();
        if (!dxf.Save(stream)) throw new IOException("DXF serialization failed.");
        return new(page.PageId, page.Title, stream.ToArray(), diagnostics.Distinct(StringComparer.Ordinal).ToArray());
    }
}
