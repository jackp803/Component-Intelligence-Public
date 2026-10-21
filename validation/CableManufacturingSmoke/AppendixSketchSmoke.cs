using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using PdfSharp.Pdf.IO;

internal static partial class Program
{
    private static int AppendixSketchProof(string root, string? only)
    {
        Directory.CreateDirectory(root);
        var page = new SchematicPage { PageId = "main", Title = "Cable wiring and appearance", CoordinateGrid = new(5, 5, 390, 275),
            TemplateGeometry = new() { SourceSha256 = new('b', 64), Width = 420, Height = 297, Primitives = [
                new() { Kind = "LINE", Start = new(5, 5), End = new(395, 5) }, new() { Kind = "LINE", Start = new(395, 5), End = new(395, 280) },
                new() { Kind = "LINE", Start = new(395, 280), End = new(5, 280) }, new() { Kind = "LINE", Start = new(5, 280), End = new(5, 5) },
                new() { Kind = "LINE", Start = new(230, 235), End = new(395, 235) }, new() { Kind = "LINE", Start = new(230, 235), End = new(230, 280) },
                new() { Kind = "TEXT", Start = new(235, 250), Text = "TITLE BLOCK MUST STAY CLEAR", TextHeight = 3 }] } };
        var template = AppendixThreePortTemplate() with { TextBindings = [new("REF", CableTextField.Reference), new("LEN", CableTextField.LengthMm)] };
        CableInstance Cable(ArchivedCableTemplate source, int index, bool sketch = true)
        {
            var result = ArchivedCableInstanceFactory.Create(source, CableConstructionType.Custom);
            result.ReferenceDesignator = "C" + index; result.ProvidedLengthMm = index * 500; result.Notes = "Sensor " + index;
            if (sketch) result.ArchivedCable = result.ArchivedCable! with { ManufacturingGeometry = AppendixSketchCad() };
            return result;
        }
        var cables = new[] { Cable(template, 1), Cable(template, 2) };
        var before = JsonSerializer.Serialize(cables);
        var baseline = CableWiringAppendixRenderer.Plan(Cable(template, 1, false), page, 2);
        var tiles = CableWiringAppendixRenderer.Plan(cables[0], page, 2, cables);
        var boundsProperty = typeof(CableWiringAppendixTile).GetProperty("SketchBounds");
        var primitivesProperty = typeof(CableWiringAppendixTile).GetProperty("SketchPrimitives");
        (SchematicGridBounds Bounds, IReadOnlyList<SchematicCadPrimitive> Primitives) Sketch(CableWiringAppendixTile tile)
        {
            Require(boundsProperty?.GetValue(tile) is SchematicGridBounds && primitivesProperty?.GetValue(tile) is IReadOnlyList<SchematicCadPrimitive>,
                "The wiring appendix has no planned imported cable sketch on the same sheet.");
            return ((SchematicGridBounds)boundsProperty!.GetValue(tile)!, (IReadOnlyList<SchematicCadPrimitive>)primitivesProperty!.GetValue(tile)!);
        }
        if (only is null or "three-port")
        {
            Require(tiles.Count == 1, "Adding a sketch unnecessarily split a normal three-port wiring diagram.");
            CheckAppendixSketchClearance(tiles.Single(), Sketch(tiles.Single()).Bounds, page);
            var sketch = Sketch(tiles[0]);
            Require(sketch.Bounds.X > page.EffectiveGrid().X + page.EffectiveGrid().Width / 2 && sketch.Bounds.Y > 120,
                "Cable appearance is not in the lower-right sheet region.");
            Require(sketch.Bounds.Width > 100 && sketch.Bounds.Height < 75 && sketch.Bounds.Height > 15, "Source whitespace was fitted instead of the actual CAD content.");
            Require(sketch.Primitives.Any(p => p.Kind == "SOLID_HATCH") && sketch.Primitives.Any(p => p.Kind == "ARC") &&
                sketch.Primitives.Any(p => p.Text == "P1") && sketch.Primitives.Any(p => p.Text == "P3"), "Cable CAD contours or Port labels were lost.");
            Require(sketch.Primitives.All(p => p.AttributeTag is null) && sketch.Primitives.All(p => p.Text is not "C1" and not "500 mm"),
                "Shared appearance incorrectly uses one physical cable's Reference or length for the whole group.");
            Require(JsonSerializer.Serialize(tiles[0].Diagram.PinPositions) == JsonSerializer.Serialize(baseline[0].Diagram.PinPositions) &&
                JsonSerializer.Serialize(tiles[0].Diagram.EdgePaths) == JsonSerializer.Serialize(baseline[0].Diagram.EdgePaths),
                "Adding the appearance changed exact Pin identities or internal wiring.");
        }
        if (only is null or "two-port")
        {
            var two = template with { TemplateId = "two-port", Ports = [template.Ports[0], template.Ports[2]],
                Mapping = [new("p1.1", "p3.1"), new("p1.4", "p3.6"), new("p1.5", "p3.5")], Manufacturing = null };
            var cable = Cable(two, 3);
            var fitted = CableWiringAppendixRenderer.Plan(cable, page, 1);
            Require(fitted.Count == 1, "A two-ended cable with appearance should still fit on one wiring sheet.");
            CheckAppendixSketchClearance(fitted[0], Sketch(fitted[0]).Bounds, page);
            Require(Sketch(fitted[0]).Primitives.Any(p => p.Text == "C3"), "Single-cable CAD text fields are not resolved to their physical instance.");
            SaveAppendixSketchPreview(root, "two-port.png", page, cable, fitted[0]);
        }
        if (only is null or "missing")
        {
            var missing = Cable(template, 4, false);
            var tile = CableWiringAppendixRenderer.Plan(missing, page, 1)[0];
            Require(boundsProperty!.GetValue(tile) is null && ((IReadOnlyList<SchematicCadPrimitive>)primitivesProperty!.GetValue(tile)!).Count == 0,
                "A missing manufacturing CAD was fabricated from the connection drawing.");
        }
        if (only is null or "crowded")
        {
            var crowded = Cable(template, 5);
            var saved = crowded.ArchivedCable!.Manufacturing!.WiringDiagram!;
            var old = saved.PortBounds["P3"]; saved.PortBounds["P3"] = old with { Y = 115 };
            foreach (var pin in template.Ports[2].Pins) saved.PinPositions[pin.PinId] = saved.PinPositions[pin.PinId] with { Y = saved.PinPositions[pin.PinId].Y + 92 };
            var fitted = CableWiringAppendixRenderer.Plan(crowded, page, 1);
            Require(fitted.Any(t => t.HeaderWidth > t.ContentBounds.Width), "The crowded fixture did not exercise safe side-by-side partitioning.");
            foreach (var tile in fitted) CheckAppendixSketchClearance(tile, Sketch(tile).Bounds, page);
            SaveAppendixSketchPreview(root, "partitioned.png", page, crowded, fitted[0]);
        }
        if (only is null or "titleblock")
        {
            var tallTitle = page with { TitleBlockSlots = [new("SheetContent", new(230, 130, 165, 150))] };
            var tile = CableWiringAppendixRenderer.Plan(cables[0], tallTitle, 2, cables)[0];
            var sketch = Sketch(tile);
            Require(sketch.Bounds.Y + sketch.Bounds.Height <= 128, "An imported/custom taller title block is covered by the cable appearance.");
            SaveAppendixSketchPreview(root, "tall-titleblock.png", tallTitle, cables[0], tile);
        }
        if (only is null or "variants")
        {
            var variant = Cable(template, 7);
            var cad = variant.ArchivedCable!.ManufacturingGeometry!;
            variant.ArchivedCable = variant.ArchivedCable with { ManufacturingGeometry = cad with {
                Primitives = cad.Primitives.Append(new SchematicCadPrimitive { Kind = "CIRCLE", Start = new(1210, 524), Radius = 4 }).ToArray() } };
            var fitted = CableWiringAppendixRenderer.Plan(cables[0], page, 2, [cables[0], variant]);
            Require(fitted.Count == 1, "Different appearance duplicated identical internal wiring.");
            var sketch = Sketch(fitted[0]);
            Require(sketch.Primitives.Count(p => p.Kind == "SOLID_HATCH") == 2 &&
                sketch.Primitives.Any(p => p.Kind == "MTEXT" && p.Text!.Contains("C1")) &&
                sketch.Primitives.Any(p => p.Kind == "MTEXT" && p.Text!.Contains("C7")),
                "Different manufacturing CAD in one topology group was silently replaced by the first cable's appearance.");
            CheckAppendixSketchClearance(fitted[0], sketch.Bounds, page);
            SaveAppendixSketchPreview(root, "appearance-variants.png", page, cables[0], fitted[0]);
        }
        if (only is null or "same-appearance")
        {
            var same = Cable(template, 8);
            var cad = same.ArchivedCable!.ManufacturingGeometry!;
            same.ArchivedCable = same.ArchivedCable with { ManufacturingGeometry = cad with {
                Primitives = cad.Primitives.Select(p => p with { SourceBlocks = ["OTHER_IMPORT_BLOCK"],
                    Text = p.AttributeTag == "REF" ? "OTHER SOURCE REFERENCE" : p.Text }).ToArray() } };
            var tile = CableWiringAppendixRenderer.Plan(cables[0], page, 2, [cables[0], same])[0];
            Require(Sketch(tile).Primitives.Count(p => p.Kind == "SOLID_HATCH") == 1,
                "Identical rendered appearance was duplicated because CAD block metadata or bound Reference literals differed.");
        }
        if (only is null or "shared-source")
        {
            var missing = Cable(template, 6, false);
            var tile = CableWiringAppendixRenderer.Plan(missing, page, 2, [missing, cables[1]])[0];
            Require(Sketch(tile).Primitives.Count > 0, "A shared group lost the available appearance because its first member has no CAD snapshot.");
        }
        if (only is null or "export")
        {
            var variant = Cable(template with { Mapping = template.Mapping.Skip(1).Prepend(new CablePinMapping("p1.1", "p3.2")).ToList() }, 3);
            var connector = Cable(template with { LibraryItemKind = CableLibraryItemKind.Connector }, 4);
            var all = cables.Concat([variant, connector]).ToList();
            var project = new ElectricalProject { ProjectId = "appendix-sketch", Cables = all, Schematic = new() { Pages = [page],
                Symbols = all.Select((c, i) => new SchematicSymbol { SymbolId = "body-" + i, CableInstanceId = c.CableInstanceId,
                    PageId = page.PageId, Position = new(20 + i % 2 * 180, 40 + i / 2 * 75), Width = 80, Height = 20,
                    Geometry = new() { SourceSha256 = template.AssetSha256, Width = 80, Height = 20,
                        Primitives = [new() { Kind = "LINE", Start = new(0, 0), End = new(80, 20) }] } }).ToList() } };
            var snapshot = JsonSerializer.Serialize(project);
            var workspace = new SchematicWorkspaceControl(() => project, (_, _) => { }, () => Task.FromResult<IReadOnlyList<ComponentIntelligence.Contracts.ComponentIR>>([]),
                _ => Task.FromResult<Uri?>(null), () => { }, () => { });
            var render = typeof(SchematicWorkspaceControl).GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
            BitmapSource Background(ElectricalProject p, SchematicPage sheet) => (BitmapSource)render.Invoke(workspace, [p, sheet])!;
            var bitmap = CableWiringAppendixRenderer.Render(cables[0], page, tiles[0], [], Background(project, page with { PageId = "empty" }));
            Save(bitmap, Path.Combine(root, "appearance-on-wiring.png"));
            var sketch = Sketch(tiles[0]);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var dark = 0;
            for (var y = (int)(sketch.Bounds.Y / page.Height * bitmap.PixelHeight); y < (sketch.Bounds.Y + sketch.Bounds.Height) / page.Height * bitmap.PixelHeight; y++)
            for (var x = (int)(sketch.Bounds.X / page.Width * bitmap.PixelWidth); x < (sketch.Bounds.X + sketch.Bounds.Width) / page.Width * bitmap.PixelWidth; x++)
                if (pixels[(y * bitmap.PixelWidth + x) * 4] < 120) dark++;
            Require(dark > 2000, "The planned imported cable appearance is blank in the rendered PDF image.");
            var path = Path.Combine(root, "wiring-with-appearance.pdf");
            Require(SchematicPdfWriter.Write(project, path, Background) == 3,
                "Shared wiring/sketch should export once, different mappings separately, and connector-only appendices not at all.");
            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            Require(pdf.Pages[1].Annotations.Count >= 2, "Adding the sketch lost the separate same-sheet cable return links.");
            Require(JsonSerializer.Serialize(project) == snapshot, "PDF export modified physical cable details, CAD or saved wiring.");
        }
        Require(JsonSerializer.Serialize(cables) == before, "Sketch layout synchronized independent physical cable data.");
        File.WriteAllText(Path.Combine(root, "appendix-sketch-pass.txt"), "PASS " + (only ?? "all"));
        Console.WriteLine("APPENDIX_SKETCH_PASS"); return 0;
    }

    private static void SaveAppendixSketchPreview(string root, string name, SchematicPage page, CableInstance cable, CableWiringAppendixTile tile)
    {
        var project = new ElectricalProject { ProjectId = "sketch-preview", Cables = [cable], Schematic = new() { Pages = [page] } };
        var workspace = new SchematicWorkspaceControl(() => project, (_, _) => { }, () => Task.FromResult<IReadOnlyList<ComponentIntelligence.Contracts.ComponentIR>>([]),
            _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var render = typeof(SchematicWorkspaceControl).GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var background = (BitmapSource)render.Invoke(workspace, [project, page])!;
        Save(CableWiringAppendixRenderer.Render(cable, page, tile, [], background), Path.Combine(root, name));
    }

    private static void CheckAppendixSketchClearance(CableWiringAppendixTile tile, SchematicGridBounds bounds, SchematicPage page)
    {
        var box = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var grid = page.EffectiveGrid();
        Require(box.Left >= grid.X + 3 && box.Right <= grid.X + grid.Width - 3 && box.Top >= tile.ContentBounds.Y && box.Bottom <= tile.FooterY - 2,
            "Cable appearance overlaps the header, border, title block or navigation footer.");
        foreach (var port in tile.Diagram.PortBounds.Values)
        {
            var body = new Rect(tile.ContentBounds.X + (port.X - tile.ViewBounds.X) * tile.GeometryScale,
                tile.ContentBounds.Y + (port.Y - tile.ViewBounds.Y) * tile.GeometryScale, port.Width * tile.GeometryScale, port.Height * tile.GeometryScale);
            body.Intersect(new(tile.ContentBounds.X, tile.ContentBounds.Y, tile.ContentBounds.Width, tile.ContentBounds.Height));
            Require(body.IsEmpty || !body.IntersectsWith(box), "Cable appearance covers a connector or its Pin labels.");
        }
        foreach (var route in tile.Diagram.EdgePaths.Values)
        foreach (var (a, b) in route.Zip(route.Skip(1)))
        {
            var wire = new Rect(tile.ContentBounds.X + (Math.Min(a.X, b.X) - tile.ViewBounds.X) * tile.GeometryScale - .6,
                tile.ContentBounds.Y + (Math.Min(a.Y, b.Y) - tile.ViewBounds.Y) * tile.GeometryScale - .6,
                Math.Abs(a.X - b.X) * tile.GeometryScale + 1.2, Math.Abs(a.Y - b.Y) * tile.GeometryScale + 1.2);
            wire.Intersect(new(tile.ContentBounds.X, tile.ContentBounds.Y, tile.ContentBounds.Width, tile.ContentBounds.Height));
            Require(wire.IsEmpty || !wire.IntersectsWith(box), "Cable appearance covers an internal conductor.");
        }
    }

    private static SchematicCadAsset AppendixSketchCad() => new() { SourceSha256 = new('c', 64), MillimetresPerUnit = 1, Width = 2000, Height = 1000,
        Primitives = [
            new() { Kind = "CIRCLE", Start = new(1010, 520), Radius = 6 },
            new() { Kind = "LINE", Start = new(1020, 509), End = new(1060, 509) }, new() { Kind = "LINE", Start = new(1060, 509), End = new(1060, 518) },
            new() { Kind = "LINE", Start = new(1060, 518), End = new(1020, 518) }, new() { Kind = "LINE", Start = new(1020, 518), End = new(1020, 509) },
            new() { Kind = "LINE", Start = new(1020, 530), End = new(1060, 530) }, new() { Kind = "LINE", Start = new(1060, 530), End = new(1060, 539) },
            new() { Kind = "LINE", Start = new(1060, 539), End = new(1020, 539) }, new() { Kind = "LINE", Start = new(1020, 539), End = new(1020, 530) },
            new() { Kind = "LINE", Start = new(1060, 514), End = new(1100, 514) }, new() { Kind = "LINE", Start = new(1060, 534), End = new(1100, 534) },
            new() { Kind = "ARC", Start = new(1100, 524), Radius = 20, StartAngle = 270, EndAngle = 90 },
            new() { Kind = "LINE", Start = new(1120, 524), End = new(1190, 524) },
            new() { Kind = "LINE", Start = new(1190, 517), End = new(1220, 517) }, new() { Kind = "LINE", Start = new(1220, 517), End = new(1220, 531) },
            new() { Kind = "LINE", Start = new(1220, 531), End = new(1190, 531) }, new() { Kind = "LINE", Start = new(1190, 531), End = new(1190, 517) },
            new() { Kind = "SOLID_HATCH", Start = new(1193, 519), Contours = [[new(1193, 519), new(1196, 519), new(1196, 529), new(1193, 529)]] },
            new() { Kind = "TEXT", Start = new(1030, 506), Text = "P1", TextHeight = 4 },
            new() { Kind = "TEXT", Start = new(1030, 528), Text = "P2", TextHeight = 4 },
            new() { Kind = "TEXT", Start = new(1200, 514), Text = "P3", TextHeight = 4 },
            new() { Kind = "TEXT", Start = new(1020, 551), Text = "SOURCE_REF", AttributeTag = "REF", TextHeight = 3 },
            new() { Kind = "TEXT", Start = new(1100, 551), Text = "SOURCE_LENGTH", AttributeTag = "LEN", TextHeight = 3 }] };
}
