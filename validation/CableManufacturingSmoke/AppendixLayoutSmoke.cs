using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

internal static partial class Program
{
    private static int AppendixLayoutProof(string root, string? only)
    {
        Directory.CreateDirectory(root);
        var template = AppendixThreePortTemplate();
        var cables = Enumerable.Range(1, 2).Select(i => {
            var cable = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
            cable.ReferenceDesignator = "Valve" + i + "_cable"; cable.ProvidedLengthMm = i * 500; cable.Notes = "Sensor " + i;
            return cable;
        }).ToArray();
        var page = new SchematicPage { PageId = "main", Title = "Three-port regression", CoordinateGrid = new(5, 5, 390, 275),
            TemplateGeometry = new() { SourceSha256 = new('b', 64), Width = 420, Height = 297, Primitives = [
                new() { Kind = "LINE", Start = new(5, 5), End = new(395, 5) },
                new() { Kind = "LINE", Start = new(395, 5), End = new(395, 280) },
                new() { Kind = "LINE", Start = new(395, 280), End = new(5, 280) },
                new() { Kind = "LINE", Start = new(5, 280), End = new(5, 5) }] } };
        var before = JsonSerializer.Serialize(cables);
        var tiles = CableWiringAppendixRenderer.Plan(cables[0], page, 2, cables);
        if (only is null or "fit")
        {
            Require(tiles.Count == 1, "A readable three-port diagram was split, leaving the bottom of P2 on a nearly empty page.");
            var tile = tiles.Single();
            Require(tile.GeometryScale < 1, "The regression fixture must exercise geometry shrink-to-fit.");
            var original = new CableWiringEditorService().Build(new CableManufacturingEditorService().Prepare(cables[0]));
            Require(JsonSerializer.Serialize(tile.Diagram.PinPositions) == JsonSerializer.Serialize(original.PinPositions), "PDF fitting moved exact Pin positions.");
            Require(JsonSerializer.Serialize(tile.Diagram.EdgePaths) == JsonSerializer.Serialize(original.EdgePaths), "PDF fitting changed the internal wiring routes.");
            Require(tile.Diagram.PinPositions.Count == 24 && tile.Diagram.EdgePaths.Count == 7, "A Pin or electrical edge was omitted.");
            foreach (var port in tile.Diagram.PortBounds.Values)
                Require(port.X >= tile.ViewBounds.X && port.Y >= tile.ViewBounds.Y &&
                    port.X + port.Width <= tile.ViewBounds.X + tile.ViewBounds.Width && port.Y + port.Height <= tile.ViewBounds.Y + tile.ViewBounds.Height,
                    "A connector body is clipped by the fitted viewport.");
            var canvas = new Canvas();
            CableWiringVisualRenderer.DrawDiagram(canvas, tile.Diagram, 4 * tile.GeometryScale, 4, tile.MinimumTextHeight);
            foreach (var text in canvas.Children.OfType<TextBlock>())
            {
                text.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
                Require(text.FontSize >= 3.2 * 4, "Fitting made Pin labels too small.");
                var box = new Rect(Canvas.GetLeft(text), Canvas.GetTop(text), text.DesiredSize.Width, text.DesiredSize.Height);
                var owner = tile.Diagram.PortBounds.Values.Single(p => box.Left >= p.X * 4 * tile.GeometryScale &&
                    box.Left < (p.X + p.Width) * 4 * tile.GeometryScale && box.Top >= p.Y * 4 * tile.GeometryScale && box.Top < (p.Y + p.Height) * 4 * tile.GeometryScale);
                Require(box.Right <= (owner.X + owner.Width) * 4 * tile.GeometryScale && box.Bottom <= (owner.Y + owner.Height) * 4 * tile.GeometryScale,
                    "Rendered text extends outside the scaled connector.");
            }
        }
        if (only is null or "width")
        {
            var canvas = new Canvas();
            CableWiringVisualRenderer.Draw(canvas, [new() { Kind = "TEXT", Start = new(0, 0), Text = "P2 / 10 / GND", TextWidth = 54, TextHeight = 3.2 }],
                pixelsPerMm: 3.2, textPixelsPerMm: 4, minimumTextHeight: 3.2);
            var text = canvas.Children.OfType<TextBlock>().Single();
            Require(Math.Abs(text.Width - 54 * 3.2) < .001 && Math.Abs(text.FontSize - 12.8) < .001,
                "Text wrapping width does not follow geometry scale while the paper font size stays fixed.");
        }
        if (only is null or "bottom")
        foreach (var length in Enumerable.Range(10, 35))
        {
            var longLabel = AppendixThreePortTemplate();
            longLabel.Ports[1].Pins[^1].Function = string.Join(" ", Enumerable.Repeat("M", length));
            var longer = ArchivedCableInstanceFactory.Create(longLabel, CableConstructionType.Custom);
            var fitted = CableWiringAppendixRenderer.Plan(longer, page, 2, [longer, cables[1]])[0];
            var canvas = new Canvas();
            CableWiringVisualRenderer.DrawDiagram(canvas, fitted.Diagram, 4 * fitted.GeometryScale, 4, fitted.MinimumTextHeight);
            foreach (var text in canvas.Children.OfType<TextBlock>())
            {
                text.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
                var scale = 4 * fitted.GeometryScale;
                var owner = fitted.Diagram.PortBounds.Values.Single(p => Canvas.GetLeft(text) >= p.X * scale &&
                    Canvas.GetLeft(text) < (p.X + p.Width) * scale && Canvas.GetTop(text) >= p.Y * scale && Canvas.GetTop(text) < (p.Y + p.Height) * scale);
                Require(Canvas.GetTop(text) + text.DesiredSize.Height <= (owner.Y + owner.Height) * scale,
                    "A final wrapped Pin label erases the scaled connector's bottom border (length " + length + ").");
            }
        }
        if (only is null or "header")
        {
            var longTitle = ArchivedCableInstanceFactory.Create(AppendixThreePortTemplate() with {
                DisplayName = string.Join(" ", Enumerable.Repeat("CONTROL", 60)) }, CableConstructionType.Custom);
            var narrow = page with { CoordinateGrid = new(5, 5, 180, 275) };
            var tile = CableWiringAppendixRenderer.Plan(longTitle, narrow, 1)[0];
            var heading = new TextBlock { Text = "Reference 未設定 / " + longTitle.DisplayName, Width = tile.ContentBounds.Width * 4,
                FontSize = 15, FontFamily = new("Microsoft JhengHei"), TextWrapping = TextWrapping.Wrap };
            heading.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            Require(tile.SpecificationY >= narrow.EffectiveGrid().Y + 2 + heading.DesiredSize.Height / 4 + 2 && tile.MemberY >= tile.SpecificationY + 9,
                "Header fields do not use the measured title/specification positions.");
            Require(tile.ContentBounds.Y >= narrow.EffectiveGrid().Y + 2 + heading.DesiredSize.Height / 4 + 2 + 4 + 2 + 4 + 5,
                "Wrapped appendix title/specification overlap subsequent header or diagram content.");
        }
        if (only is null or "frame")
        {
            var tile = tiles[0]; var grid = page.EffectiveGrid();
            Require(tile.ContentBounds.X >= grid.X + 2 && tile.ContentBounds.X + tile.ContentBounds.Width <= grid.X + grid.Width - 2,
                "Appendix content overwrites the imported drawing frame instead of respecting its effective grid.");
            var continuation = CableWiringAppendixRenderer.MemberReturnBounds(page);
            Require(continuation.X == grid.X + 4 && continuation.Y == grid.Y + grid.Height - 66,
                "Shared-cable metadata navigation uses a different coordinate origin from its drawing frame.");
        }
        if (only is null or "export")
        {
            var project = new ElectricalProject { ProjectId = "appendix-regression", Cables = cables.ToList(), Schematic = new() { Pages = [page],
                Symbols = cables.Select((c, i) => new SchematicSymbol { SymbolId = "body-" + i, CableInstanceId = c.CableInstanceId,
                    PageId = page.PageId, Position = new(20, 40 + i * 60), Width = 80, Height = 20, Geometry = new() {
                        SourceSha256 = template.AssetSha256, Width = 80, Height = 20,
                        Primitives = [new() { Kind = "LINE", Start = new(0, 0), End = new(80, 20) }] } }).ToList() } };
            var workspace = new SchematicWorkspaceControl(() => project, (_, _) => { }, () => Task.FromResult<IReadOnlyList<ComponentIntelligence.Contracts.ComponentIR>>([]),
                _ => Task.FromResult<Uri?>(null), () => { }, () => { });
            var render = typeof(SchematicWorkspaceControl).GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
            BitmapSource Background(ElectricalProject p, SchematicPage sheet) => (BitmapSource)render.Invoke(workspace, [p, sheet])!;
            var tile = tiles[0];
            var bitmap = CableWiringAppendixRenderer.Render(cables[0], page, tile, [], Background(project, page with { PageId = "empty" }));
            Save(bitmap, Path.Combine(root, "three-port-appendix.png"));
            // Check the actual border through both the header and the diagram viewport.
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var borderX = (int)Math.Round(395 / page.Width * bitmap.PixelWidth);
            foreach (var y in new[] { 9d, 17d, 24d, 29d, 60d, 120d, 190d })
            {
                var row = (int)Math.Round(y / page.Height * bitmap.PixelHeight);
                Require(Enumerable.Range(borderX - 3, 7).Any(x => pixels[(row * bitmap.PixelWidth + x) * 4] < 120),
                    "Appendix white text or content background erased the imported border at " + y + " mm.");
            }
            var count = SchematicPdfWriter.Write(project, Path.Combine(root, "three-port-grouped.pdf"), Background);
            Require(count == 2, "Two equivalent three-port cables should export one main sheet and one complete shared wiring sheet.");
        }
        Require(JsonSerializer.Serialize(cables) == before, "Export changed saved cable layouts or independent instance data.");
        File.WriteAllText(Path.Combine(root, "appendix-layout-pass.txt"), "PASS " + (only ?? "all"));
        Console.WriteLine("APPENDIX_LAYOUT_PASS"); return 0;
    }

    private static ArchivedCableTemplate AppendixThreePortTemplate()
    {
        var functions = new[] { new[] { "GND", "N/A", "N/A", "D+", "D-", "N/A", "N/A", "N/A" },
            new[] { "GND", "24V", "N/A", "N/A", "Feedback(0-10V)", "N/A", "N/A", "N/A", "N/A", "GND" },
            new[] { "GND", "24V", "Sensor", "Feedback(0-10V)", "D-", "D+" } };
        var ports = functions.Select((values, index) => new ComponentIntelligence.Electrical.Domain.ComponentPort {
            PortId = "P" + (index + 1), Name = "P" + (index + 1),
            Connector = new() { ConnectorId = "c" + index, Family = index == 0 ? "RJ45" : "M12", Coding = index == 0 ? "" : "A",
                Gender = index == 2 ? ConnectorGender.Female : ConnectorGender.Male, PinCount = values.Length },
            Pins = values.Select((f, i) => new ComponentIntelligence.Electrical.Domain.ComponentPin {
                PinId = "p" + (index + 1) + "." + (i + 1), PinNumber = (index == 2 && i > 3 ? i + 2 : i + 1).ToString(), Function = f }).ToList() }).ToList();
        var layout = new CableWiringDiagram { PortBounds = new() {
            ["P1"] = new(10, 10, 60, 80), ["P2"] = new(10, 95, 60, 94), ["P3"] = new(185, 23, 60, 66) } };
        foreach (var port in ports)
        for (var i = 0; i < port.Pins.Count; i++)
        {
            var bounds = layout.PortBounds[port.PortId];
            layout.PinPositions[port.Pins[i].PinId] = new(port.PortId == "P3" ? bounds.X : bounds.X + bounds.Width, bounds.Y + 22 + i * 7);
        }
        return new() { TemplateId = "three-port", TemplateRevision = "r1", AssetSha256 = new('a', 64), DisplayName = "Three-port cable", Ports = ports,
            Mapping = [new("p1.1", "p3.1"), new("p1.4", "p3.6"), new("p1.5", "p3.5"), new("p2.1", "p3.1"),
                new("p2.2", "p3.2"), new("p2.5", "p3.4"), new("p2.1", "p2.10")],
            Manufacturing = new() { InventoryOnly = true, WiringDiagram = layout } };
    }
}
