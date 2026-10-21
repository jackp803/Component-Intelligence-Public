using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using Path = System.IO.Path;

internal static partial class Program
{
    private static void ColorsProof(string? root, bool pdf)
    {
        ExtendedPaletteProof();
        var template = new ArchivedCableTemplate { TemplateId = "synthetic-colors", TemplateRevision = "r1", AssetSha256 = new('a', 64),
            Ports = Enumerable.Range(1, 3).Select(i => new ComponentPort { PortId = "P" + i, Name = "P" + i,
                Connector = new() { ConnectorId = "connector" + i, Family = i == 1 ? "RJ45" : "M12" },
                Pins = (i == 3 ? new[] { 1, 2, 3, 4, 6, 7 } : Enumerable.Range(1, i == 1 ? 8 : 10)).Select(n =>
                    new ComponentPin { PinId = $"p{i}.{n}", PinNumber = n.ToString(), Function = n == 1 ? "GND" : n == 2 ? "24V" : "Signal " + n }).ToList() }).ToList() };
        var tables = new CableManufacturingEditorService(); var draft = tables.Prepare(template);
        var service = new CableWiringEditorService(); service.EnterVisualMode(draft);
        service.MovePort(draft, "P2", new(10, 100)); service.MovePort(draft, "P3", new(205, 10));
        service.MirrorPort(draft, "P2"); service.MirrorPort(draft, "P3");
        foreach (var (a, b) in new[] { ("p1.1", "p3.1"), ("p2.1", "p3.1"), ("p2.10", "p2.1"),
            ("p2.2", "p3.2"), ("p1.4", "p3.7"), ("p1.5", "p3.6"), ("p2.5", "p3.4") }) service.Connect(draft, a, b);
        var diagram = service.Build(draft);
        var before = JsonSerializer.Serialize(draft);
        var editor = new CableWiringDiagramEditor(draft);
        var canvas = (Canvas)typeof(CableWiringDiagramEditor).GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
        var colors = ReadColors(diagram, canvas);
        Require(colors.Count == 7 && colors.Values.Distinct().Count() == 7, "Seven connections must render in seven different non-black colors.");
        Require(diagram.EdgePrimitives.Values.SelectMany(p => p).Any(p => p.Kind == "ARC"), "Color fixture must exercise jump arcs.");
        editor.MovePort("P3", new(210, 14));
        var moved = ReadColors(service.Build(draft), canvas);
        Require(colors.All(p => moved[p.Key] == p.Value), "Moving connectors changed identification colors.");
        editor.Undo();
        Require(JsonSerializer.Serialize(draft) == before, "Color display or move undo altered wiring authority.");
        foreach (var key in colors.Keys)
        {
            editor.SelectEdge(key);
            ReadColors(service.Build(draft), canvas);
            Require(canvas.Children.OfType<Polyline>().All(p => p.Stroke == Brushes.Transparent), "Colored selection blocks jump gaps or edge hit surfaces.");
        }
        editor.SelectEdge("");
        var reopened = tables.Prepare(JsonSerializer.Deserialize<ArchivedCableTemplate>(JsonSerializer.Serialize(tables.Apply(template, draft)))!);
        var second = new CableWiringDiagramEditor(reopened);
        var secondCanvas = (Canvas)typeof(CableWiringDiagramEditor).GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(second)!;
        var reopenedColors = ReadColors(service.Build(reopened), secondCanvas);
        Require(colors.All(p => reopenedColors[p.Key] == p.Value), "Saving/reopening changed identification colors.");
        Require(before == JsonSerializer.Serialize(draft), "Reading colors changed source draft.");
        if (root is not null)
        {
            Directory.CreateDirectory(root);
            editor.Measure(new Size(1400, 950)); editor.Arrange(new Rect(0, 0, 1400, 950)); editor.FitToView(); editor.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1400, 950, 96, 96, PixelFormats.Pbgra32); bitmap.Render(editor);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(root, "colored-wiring-editor.png"))) encoder.Save(output);
            if (pdf) PdfSmoke(tables.Apply(template, draft), root);
        }
        Console.WriteLine("PASS seven distinct identification colors, same-color jump arcs, move/selection/undo, reopen and unchanged electrical authority.");
    }

    private static void ExtendedPaletteProof()
    {
        var parts = Enumerable.Range(0, 1000).Select(i => new SchematicCadPrimitive { Kind = "LINE",
            Start = new(0, i), End = new(10, i) }).ToArray();
        var diagram = new CableWiringDiagramPresentation(parts, new(0, 0, 10, 1000),
            new Dictionary<string, SchematicGridBounds>(), new Dictionary<string, SchematicPoint>(),
            new Dictionary<string, IReadOnlyList<SchematicPoint>>())
        { EdgePrimitives = parts.Select((part, index) => (part, index)).ToDictionary(p => p.index.ToString("D4"),
            p => (IReadOnlyList<SchematicCadPrimitive>)new[] { p.part }) };
        var canvas = new Canvas(); CableWiringVisualRenderer.DrawDiagram(canvas, diagram);
        var colors = canvas.Children.OfType<Line>().Select(p => ((SolidColorBrush)p.Stroke).Color).ToArray();
        Require(colors.Length == 1000 && colors.Distinct().Count() == 1000, "Identification colors repeated in a large harness.");
        diagram = diagram with { EdgePrimitives = diagram.EdgePrimitives.Reverse().ToDictionary(p => p.Key, p => p.Value) };
        var reordered = new Canvas(); CableWiringVisualRenderer.DrawDiagram(reordered, diagram);
        Require(colors.SequenceEqual(reordered.Children.OfType<Line>().Select(p => ((SolidColorBrush)p.Stroke).Color)),
            "Colors depended on dictionary enumeration order.");
        Console.WriteLine("PASS 1000 distinct display colors and dictionary-order independence.");
    }

    private static Dictionary<string, string> ReadColors(CableWiringDiagramPresentation diagram, Canvas canvas)
    {
        var source = diagram.Primitives.ToArray();
        var elements = canvas.Children.Cast<FrameworkElement>().Take(source.Length).ToArray();
        Require(elements.Length == source.Length, "Colored rendering omitted primitives.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (edge, primitives) in diagram.EdgePrimitives)
        {
            var shades = primitives.Select(p =>
            {
                var index = Array.FindIndex(source, x => ReferenceEquals(x, p));
                return ((SolidColorBrush)((Shape)elements[index]).Stroke).Color.ToString();
            }).Distinct().ToArray();
            Require(shades.Length == 1 && shades[0] != Colors.Black.ToString(), "A conductor segment or jump arc has a different/black identification color.");
            result.Add(edge, shades[0]);
        }
        var wireParts = diagram.EdgePrimitives.Values.SelectMany(p => p).ToHashSet(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < source.Length; index++)
            if (!wireParts.Contains(source[index]) && elements[index] is Shape shape)
                Require(((SolidColorBrush)shape.Stroke).Color == Colors.Black, "Connector borders or junction dots were recolored.");
        return result;
    }
}
