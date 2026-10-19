using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shape = System.Windows.Shapes.Shape;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using netDxf;
using netDxf.Blocks;
using netDxf.Tables;
using PdfSharp.Pdf.IO;

internal static partial class Program
{
    private static int NetDimensionsProof(string root, string? only)
    {
        Directory.CreateDirectory(root);
        var template = AppendixThreePortTemplate();
        template = template with { Ports = template.Ports.Append(new ComponentIntelligence.Electrical.Domain.ComponentPort {
            PortId = "P4", Name = "P4", Pins = Enumerable.Range(1, 3).Select(i => new ComponentPin {
                PinId = "p4." + i, PinNumber = i.ToString(), Function = i == 1 ? "GND" : i == 2 ? "24V" : "Sensor" }).ToList() }).ToList(),
            Mapping = template.Mapping.Concat(new[] { new CablePinMapping("p3.1", "p4.1"), new("p3.2", "p4.2"), new("p3.3", "p4.3") }).ToList(), Manufacturing = null };
        var cable = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
        cable.ReferenceDesignator = "C1";
        var draft = new CableManufacturingEditorService().Prepare(cable);
        var service = new CableWiringEditorService(); service.EnterVisualMode(draft);
        var before = JsonSerializer.Serialize(draft);
        var diagram = service.Build(draft);
        Dictionary<string, Color> Colors(CableWiringDiagramPresentation view)
        {
            var canvas = new Canvas(); CableWiringVisualRenderer.DrawDiagram(canvas, view);
            Require(canvas.Children.Count == view.Primitives.Count, "Wiring render omitted a primitive.");
            var rendered = new Dictionary<SchematicCadPrimitive, UIElement>(ReferenceEqualityComparer.Instance);
            for (var i = 0; i < view.Primitives.Count; i++) rendered.Add(view.Primitives[i], canvas.Children[i]);
            return view.EdgePrimitives.ToDictionary(e => e.Key, e => {
                var strokes = e.Value.Select(p => (Shape)rendered[p]).Select(s => ((SolidColorBrush)s.Stroke).Color).Distinct().ToArray();
                Require(strokes.Length == 1, "Segments or crossover arcs on the same edge have inconsistent colors."); return strokes[0]; });
        }
        if (only is null or "colors")
        {
            var colors = Colors(diagram);
            var ground = new[] { ("p1.1", "p3.1"), ("p2.1", "p3.1"), ("p2.1", "p2.10"), ("p3.1", "p4.1") };
            Require(ground.Select(e => colors[CableWiringEditorService.EdgeKey(e.Item1, e.Item2)]).Distinct().Count() == 1,
                "Electrically continuous GND branches still have different identification colors.");
            Require(colors[CableWiringEditorService.EdgeKey("p2.2", "p3.2")] == colors[CableWiringEditorService.EdgeKey("p3.2", "p4.2")],
                "Transitively connected 24V branches have different colors.");
            Require(colors.Values.Distinct().Count() == 6, "Unconnected networks were merged by geometry or text labels.");
            draft.ExplicitMapping!.Reverse();
            var reordered = Colors(service.Build(draft));
            Require(colors.All(p => reordered[p.Key] == p.Value), "Reordering stored mappings changed network colors.");
            draft.ExplicitMapping.Reverse();
            service.Connect(draft, "p1.1", "p2.1");
            var cycle = Colors(service.Build(draft));
            Require(ground.Select(e => cycle[CableWiringEditorService.EdgeKey(e.Item1, e.Item2)])
                .Append(cycle[CableWiringEditorService.EdgeKey("p1.1", "p2.1")]).Distinct().Count() == 1, "A cycle split one electrical network into different colors.");
            service.Disconnect(draft, "p1.1", "p2.1");
            Require(JsonSerializer.Serialize(draft) == before, "Display colors modified exact mappings, functions or physical wire data.");
            var canvas = new Canvas { Width = diagram.Bounds.Width * 4 + 30, Height = diagram.Bounds.Height * 4 + 30, Background = Brushes.White };
            CableWiringVisualRenderer.DrawDiagram(canvas, diagram);
            Render(canvas, canvas.Width, canvas.Height, Path.Combine(root, "continuous-net-colors.png"));
            var many = new CableManufacturingEditorService().Prepare(AppendixThreePortTemplate() with { Mapping = [], Manufacturing = null });
            service.EnterVisualMode(many);
            for (var i = 1; i <= 8; i++) service.Connect(many, "p1." + i, "p2." + i);
            for (var i = 1; i <= 2; i++) service.Connect(many, "p3." + i, "p2." + (i + 8));
            for (var i = 3; i < 6; i += 2) service.Connect(many, "p3." + i, "p3." + (i + 1));
            Require(Colors(service.Build(many)).Values.Distinct().Count() == 12, "Identification colors repeat after the first palette.");
        }
        if (only is null or "dimensions")
        {
            var source = Path.Combine(root, "synthetic-dimensioned-cable.dxf");
            var body = new Block("CABLE_APPEARANCE");
            foreach (var p in AppendixSketchCad().Primitives.Where(p => p.AttributeTag is null))
            {
                var start = new Vector2(p.Start.X, -p.Start.Y);
                if (p.Kind == "LINE" && p.End is { } end) body.Entities.Add(new netDxf.Entities.Line(start, new(end.X, -end.Y)));
                if (p.Kind == "CIRCLE") body.Entities.Add(new netDxf.Entities.Circle(start, p.Radius));
                if (p.Kind == "ARC") body.Entities.Add(new netDxf.Entities.Arc(start, p.Radius, p.StartAngle, p.EndAngle));
                if (p.Kind == "TEXT") body.Entities.Add(new netDxf.Entities.Text(p.Text, start, p.TextHeight));
            }
            var style = new DimensionStyle("CABLE_MARKS") { TextHeight = 3.5, ArrowSize = 2 };
            body.Entities.Add(new netDxf.Entities.LinearDimension(new Vector2(1060, -514), new(1100, -514), 16, 0, style) { UserText = "L1" });
            body.Entities.Add(new netDxf.Entities.LinearDimension(new Vector2(1120, -524), new(1190, -524), 26, 0, style) { UserText = "L2" });
            var doc = new DxfDocument { BuildDimensionBlocks = true };
            doc.Entities.Add(new netDxf.Entities.Insert(body) { Scale = new(1.25, 1.25, 1.25) }); doc.Save(source);
            var sourceBytes = File.ReadAllBytes(source);
            var asset = new SchematicCadFileLoader(stagingRoot: Path.Combine(root, "staging")).ReadAsync(source, 1).GetAwaiter().GetResult();
            Require(asset.Primitives.Count(p => p.Text is "L1" or "L2") == 2 && asset.Primitives.Count(p => p.Kind == "SOLID_HATCH") >= 4,
                "CAD import lost dimension labels or filled arrowheads.");
            Require(asset.ConnectionPoints.Count == 0 && cable.ProvidedLengthMm is null, "Visual dimension labels became engineering data.");
            var previewType = typeof(SchematicCadFileLoader).Assembly.GetType("ComponentIntelligence.Desktop.CableCadRoleEditor")!;
            var preview = (FrameworkElement)Activator.CreateInstance(previewType)!;
            previewType.GetMethod("Restore")!.Invoke(preview, [source, asset, null, 1d]);
            Render(preview, 1200, 650, Path.Combine(root, "import-preview-dimensions.png"));
            cable.ArchivedCable = cable.ArchivedCable! with { ManufacturingGeometry = asset,
                Template = cable.ArchivedCable.Template with { AssetSha256 = asset.SourceSha256 } };
            var page = new SchematicPage { PageId = "main", Title = "Dimensioned cable", CoordinateGrid = new(10, 10, 390, 275) };
            var project = new ElectricalProject { ProjectId = "net-dimensions", Cables = [cable], Schematic = new() { Pages = [page], Symbols = [
                new() { SymbolId = "cable", CableInstanceId = cable.CableInstanceId, PageId = page.PageId,
                    Position = new(30, 40), Width = 300, Height = asset.Height / asset.Width * 300, Geometry = asset }] } };
            var snapshot = JsonSerializer.Serialize(project);
            var tile = CableWiringAppendixRenderer.Plan(cable, page, 1).Single();
            Require(tile.SketchPrimitives.Count(p => p.Text is "L1" or "L2") == 2 &&
                tile.SketchPrimitives.Count(p => p.Kind == "SOLID_HATCH") >= 4, "Appendix layout omitted dimensions from the cable appearance.");
            CheckAppendixSketchClearance(tile, tile.SketchBounds!, page);
            var palette = Colors(tile.Diagram);
            Require(palette[CableWiringEditorService.EdgeKey("p1.1", "p3.1")] == palette[CableWiringEditorService.EdgeKey("p3.1", "p4.1")],
                "PDF planning uses edge colors instead of continuous network colors.");
            var workspace = new SchematicWorkspaceControl(() => project, (_, _) => { }, () => Task.FromResult<IReadOnlyList<ComponentIntelligence.Contracts.ComponentIR>>([]),
                _ => Task.FromResult<Uri?>(null), () => { }, () => { });
            var render = typeof(SchematicWorkspaceControl).GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
            BitmapSource Background(ElectricalProject p, SchematicPage sheet) => (BitmapSource)render.Invoke(workspace, [p, sheet])!;
            var bitmap = CableWiringAppendixRenderer.Render(cable, page, tile, [], Background(project, page with { PageId = "empty" }));
            Save(bitmap, Path.Combine(root, "net-colors-and-dimensions.png"));
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var counts = palette.Values.Distinct().ToDictionary(c => c, _ => 0);
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var r = (double)pixels[i + 2]; var g = (double)pixels[i + 1]; var b = (double)pixels[i];
                if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) < 30) continue;
                // Subpixel strokes blend with white in the 300-DPI page transform.
                foreach (var color in counts.Keys)
                {
                    var cr = 255d - color.R; var cg = 255d - color.G; var cb = 255d - color.B;
                    var coverage = ((255 - r) * cr + (255 - g) * cg + (255 - b) * cb) / (cr * cr + cg * cg + cb * cb);
                    if (coverage is < .35 or > 1.02) continue;
                    if (Math.Abs(255 - cr * coverage - r) < 8 && Math.Abs(255 - cg * coverage - g) < 8 && Math.Abs(255 - cb * coverage - b) < 8)
                        counts[color]++;
                }
            }
            Require(counts.Values.All(count => count > 10), "An identification color is missing from the actual appendix bitmap: " + string.Join(", ", counts));
            var pdfPath = Path.Combine(root, "net-colors-and-dimensions.pdf");
            Require(SchematicPdfWriter.Write(project, pdfPath, Background) == 2, "PDF did not contain main appearance and wiring appendix.");
            using var pdf = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
            Require(pdf.Pages[1].Annotations.Count > 0, "PDF lost its return link.");
            Require(JsonSerializer.Serialize(project) == snapshot && cable.ProvidedLengthMm is null && sourceBytes.SequenceEqual(File.ReadAllBytes(source)),
                "Preview or export changed actual cable length, physical data or source CAD.");
            File.WriteAllText(Path.Combine(root, "dimensioned-cad.json"), JsonSerializer.Serialize(asset, new JsonSerializerOptions { WriteIndented = true }));
        }
        File.WriteAllText(Path.Combine(root, "net-dimensions-pass.txt"), "PASS " + (only ?? "all"));
        Console.WriteLine("NET_DIMENSIONS_PASS"); return 0;
    }
}
