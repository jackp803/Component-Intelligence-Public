using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using PdfSharp.Pdf.IO;

internal static partial class Program
{
    private static int SinglePageCableProof(string root)
    {
        Directory.CreateDirectory(root);
        var page = new SchematicPage { PageId = "main", Title = "A3 complete cable assembly",
            CoordinateGrid = new(5, 5, 390, 275), TemplateGeometry = new() {
                SourceSha256 = new('b', 64), Width = 420, Height = 297, Primitives = [
                    new() { Kind = "LINE", Start = new(5, 5), End = new(395, 5) },
                    new() { Kind = "LINE", Start = new(395, 5), End = new(395, 280) },
                    new() { Kind = "LINE", Start = new(395, 280), End = new(5, 280) },
                    new() { Kind = "LINE", Start = new(5, 280), End = new(5, 5) },
                    new() { Kind = "TEXT", Start = new(235, 250), Text = "TITLE BLOCK", TextHeight = 3 }] } };
        var template = AppendixThreePortTemplate();
        var cables = Enumerable.Range(1, 2).Select(i => {
            var cable = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
            cable.ReferenceDesignator = "Cable" + i; cable.ProvidedLengthMm = 5000 + i;
            cable.Specification = "L1=200mm; L2=4800mm";
            cable.Notes = string.Join("\n", Enumerable.Range(1, 35).Select(n =>
                $"NOTE {n}: Independent assembly specification / test requirements for cable {i}."))
                + "\nEND-NOTE-" + i;
            cable.ArchivedCable = cable.ArchivedCable! with { ManufacturingGeometry = AppendixSketchCad() };
            return cable;
        }).ToArray();
        var before = JsonSerializer.Serialize(cables);
        foreach (var count in new[] { 1, 2, 20 })
        {
            var members = Enumerable.Range(0, count).Select(i => {
                var cable = JsonSerializer.Deserialize<CableInstance>(JsonSerializer.Serialize(cables[i % 2]))!;
                cable.ReferenceDesignator = "Cable" + (i + 1); return cable;
            }).ToArray();
            var tiles = CableWiringAppendixRenderer.Plan(members[0], page, count, members);
            Require(tiles.Count == 1, "The complete wiring diagram was split between sheets.");
            var tile = tiles[0];
            Require(tile.RemainingMemberLines.Count == 0 && CableWiringAppendixRenderer.MemberPages(tile, page).Count == 0,
                "Cable labels/notes were moved to a separate metadata sheet.");
            Require(tile.Diagram.PinPositions.Count == 24 && tile.Diagram.EdgePaths.Count == 7,
                "Single-page fitting removed a Pin or electrical path.");
            var inkCanvas = new Canvas();
            CableWiringVisualRenderer.DrawDiagram(inkCanvas, tile.Diagram, 4, 4, tile.MinimumTextHeight);
            inkCanvas.Measure(new(4000, 4000)); inkCanvas.Arrange(new Rect(0, 0, 4000, 4000)); inkCanvas.UpdateLayout();
            var ink = VisualTreeHelper.GetDescendantBounds(inkCanvas);
            Require(new Rect(tile.ViewBounds.X * 4, tile.ViewBounds.Y * 4, tile.ViewBounds.Width * 4, tile.ViewBounds.Height * 4).Contains(ink),
                "The fitted viewport clips wiring/text/stroke ink.");
            Require(tile.SketchBounds is not null && tile.SketchPrimitives.Count > 0, "Assembly sketch was omitted.");
            CheckAppendixSketchClearance(tile, tile.SketchBounds!, page);
            var metadata = tile.MetadataBounds!;
            var diagramBox = new Rect(tile.ContentBounds.X, tile.ContentBounds.Y, tile.ContentBounds.Width, tile.ContentBounds.Height);
            var sketchBox = new Rect(tile.SketchBounds!.X, tile.SketchBounds.Y, tile.SketchBounds.Width, tile.SketchBounds.Height);
            foreach (var label in tile.MemberLabels)
            {
                var box = new Rect(label.Bounds.X, label.Bounds.Y, label.Bounds.Width, label.Bounds.Height);
                Require(!box.IntersectsWith(diagramBox) && !box.IntersectsWith(sketchBox) &&
                    box.Bottom <= metadata.Y + metadata.Height, "A cable metadata label overlaps wiring/sketch or escapes its region.");
            }
            Require(tile.MemberLabels.Count == count * 3 &&
                tile.MemberLabels.Count(l => l.Text.Contains("END-NOTE-")) == count,
                "A member's complete notes/specification/length label was lost.");
            Require(JsonSerializer.Serialize(members[0].ArchivedCable!.Manufacturing!.WiringDiagram) ==
                JsonSerializer.Serialize(cables[0].ArchivedCable!.Manufacturing!.WiringDiagram), "Fitting changed saved electrical geometry.");
            if (count == 2)
            {
                var project = new ElectricalProject { ProjectId = "one-page-cable", Cables = members.ToList(), Schematic = new() {
                    Pages = [page], Symbols = members.Select((c, i) => new SchematicSymbol {
                        SymbolId = "cable-" + i, CableInstanceId = c.CableInstanceId, PageId = page.PageId,
                        Position = new(20, 40 + i * 60), Width = 80, Height = 20, Geometry = new() {
                            SourceSha256 = new('a', 64), Width = 80, Height = 20,
                            Primitives = [new() { Kind = "LINE", Start = new(0, 0), End = new(80, 20) }] } }).ToList() } };
                var workspace = new SchematicWorkspaceControl(() => project, (_, _) => { },
                    () => Task.FromResult<IReadOnlyList<ComponentIntelligence.Contracts.ComponentIR>>([]),
                    _ => Task.FromResult<Uri?>(null), () => { }, () => { });
                var method = typeof(SchematicWorkspaceControl).GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
                BitmapSource Background(ElectricalProject p, SchematicPage sheet) => (BitmapSource)method.Invoke(workspace, [p, sheet])!;
                var bitmap = CableWiringAppendixRenderer.Render(members[0], page, tile, [], Background(project, page with { PageId = "blank" }));
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(root, "single-page-cable.png"))) encoder.Save(stream);
                var countPages = SchematicPdfWriter.Write(project, Path.Combine(root, "single-page-cable.pdf"), Background);
                using var pdf = PdfReader.Open(Path.Combine(root, "single-page-cable.pdf"), PdfDocumentOpenMode.Import);
                Require(countPages == 2 && pdf.PageCount == 2, "Shared cable assembly needs exactly one A3 wiring/labels/sketch page.");
                Require(Math.Abs(pdf.Pages[1].Width.Millimeter - 420) < .1 && Math.Abs(pdf.Pages[1].Height.Millimeter - 297) < .1,
                    "Single-page export changed A3 paper size.");
                var operators = PdfSharp.Pdf.Content.ContentReader.ReadContent(pdf.Pages[1])
                    .OfType<PdfSharp.Pdf.Content.Objects.COperator>().Select(op => op.Name).ToArray();
                Require(operators.Contains("m") && operators.Contains("l") && operators.Any(op => op is "f" or "f*"),
                    "Cable labels, wiring and sketch were rasterized instead of retained as zoomable PDF vectors.");
            }
        }
        Require(before == JsonSerializer.Serialize(cables), "Export mutated independent instance metadata or saved wiring.");
        var extra = new ComponentPort { PortId = "P4", Name = "P4", Pins = Enumerable.Range(1, 3).Select(i =>
            new ComponentPin { PinId = "p4." + i, PinNumber = i.ToString(), Function = "Signal " + i }).ToList() };
        var four = ArchivedCableInstanceFactory.Create(template with { TemplateId = "four-port",
            Ports = template.Ports.Append(extra).ToList(), Mapping = template.Mapping.Append(new("p3.1", "p4.1")).ToList(),
            Manufacturing = null }, CableConstructionType.Custom);
        four.ArchivedCable = four.ArchivedCable! with { ManufacturingGeometry = AppendixSketchCad() };
        var complete = CableWiringAppendixRenderer.Plan(four, page, 1);
        Require(complete.Count == 1 && complete[0].Diagram.PinPositions.Count == 27 &&
            complete[0].SketchPrimitives.Count > 0, "Four-port wiring/appearance was split or lost contacts.");
        var compact = ArchivedCableInstanceFactory.Create(template with { Ports = [
            new ComponentPort { PortId = "ONE", Name = "ONE", Pins = [new ComponentPin { PinId = "one-pin", PinNumber = "1" }] }],
            Mapping = [], Manufacturing = null }, CableConstructionType.Custom);
        var compactTile = CableWiringAppendixRenderer.Plan(compact, page, 8).Single();
        var links = CableWiringAppendixRenderer.ReturnLinks(compactTile, Enumerable.Range(1, 8).Select(i =>
            new CableWiringAppendixSourceLink(page.PageId, new(10, 10), 10, 10, new(10, 10)) {
                CableInstanceId = "C" + i, CableReference = "C" + i }).ToArray(), new Dictionary<string, int> { [page.PageId] = 0 }, page.Margin);
        Require(links.All(link => link.Bounds.X >= page.EffectiveGrid().X &&
            link.Bounds.X + link.Bounds.Width <= page.EffectiveGrid().X + page.EffectiveGrid().Width),
            "Centered compact wiring moved return links outside the A3 grid.");
        var variants = Enumerable.Range(1, 8).Select(i => {
            var c = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
            c.ReferenceDesignator = "Variant" + i;
            c.ArchivedCable = c.ArchivedCable! with { ManufacturingGeometry = new() { SourceSha256 = new('a', 64),
                Width = 100, Height = 30, Primitives = [
                    new() { Kind = "LINE", Start = new(0, 0), End = new(100, 0) },
                    new() { Kind = "CIRCLE", Start = new(20 + i, 10), Radius = i }] } };
            return c;
        }).ToArray();
        var manySketches = CableWiringAppendixRenderer.Plan(variants[0], page, 8, variants).Single();
        Require(manySketches.SketchPrimitives.Count(p => p.Kind == "CIRCLE") == 8,
            "Proportional fitting lost a distinct manufacturing appearance.");
        File.WriteAllText(Path.Combine(root, "single-page-pass.txt"), "PASS all labels, complete wiring and sketch remain on one A3 page.");
        Console.WriteLine("SINGLE_PAGE_CABLE_PASS"); return 0;
    }
}
