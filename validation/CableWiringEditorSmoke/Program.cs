using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using PdfSharp.Pdf.IO;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var template = new ArchivedCableTemplate { TemplateId = "synthetic", TemplateRevision = "r1", AssetSha256 = new('a', 64),
                Ports = Enumerable.Range(1, 3).Select(i => new ComponentPort { PortId = "P" + i, Name = "P" + i,
                    Connector = new() { ConnectorId = "connector" + i, Family = i == 1 ? "RJ45" : "M12", Coding = i == 1 ? null : "A" },
                    Pins = Enumerable.Range(1, i == 1 ? 8 : 6).Select(n => new ComponentPin { PinId = $"p{i}.{n}", PinNumber = n.ToString(), Function = n == 1 ? "GND" : n == 2 ? "+24V" : "Signal " + n }).ToList() }).ToList() };
            var tables = new CableManufacturingEditorService(); var draft = tables.Prepare(template);
            new CableWiringEditorService().EnterVisualMode(draft);
            var editor = new CableWiringDiagramEditor(draft);
            editor.Measure(new Size(1100, 700)); editor.Arrange(new Rect(0, 0, 1100, 700)); editor.UpdateLayout();
            editor.ConnectPins("p1.1", "p2.1"); editor.ConnectPins("p2.1", "p3.1"); editor.ConnectPins("p1.4", "p3.6");
            Require(draft.ExplicitMapping!.Count == 3, "Connect commands did not update the shared staged mapping.");
            editor.Undo(); Require(draft.ExplicitMapping.Count == 2, "Undo failed.");
            editor.Redo(); Require(draft.ExplicitMapping.Count == 3, "Redo failed.");
            editor.MovePort("P3", new(10, 150)); editor.MovePin("p3.1", new(100, 172));
            var saved = tables.Apply(template, draft);
            var reopened = tables.Prepare(JsonSerializer.Deserialize<ArchivedCableTemplate>(JsonSerializer.Serialize(saved))!);
            Require(reopened.InventoryOnly && reopened.ExplicitMapping!.Count == 3 && reopened.WiringDiagram!.PinPositions["p3.1"] == new SchematicPoint(70, 172), "Save/reopen lost mapping or border-attached GND layout.");
            editor.SelectEdge(CableWiringEditorService.EdgeKey("p1.4", "p3.6")); editor.DeleteSelection();
            Require(draft.ExplicitMapping.Count == 2, "Delete did not remove the selected conductor.");
            editor.SetPinUsage("p1.8", CablePinUsage.Nc);
            var pick = typeof(CableWiringDiagramEditor).GetMethod("PickPin", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            pick.Invoke(editor, ["p1.8"]); pick.Invoke(editor, ["p2.2"]);
            var status = (System.Windows.Controls.TextBlock)typeof(CableWiringDiagramEditor).GetField("_status", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!;
            Require(status.Text.Contains("NC"), "Rejected NC wiring lost its visible error feedback.");
            editor.SelectEdge(CableWiringEditorService.EdgeKey("p1.1", "p2.1"));
            editor.ZoomBy(1.25, new(500, 300)); Require(editor.Zoom > 1, "Zoom command failed.");
            editor.PanBy(new(30, 20)); Require(editor.PanOffset != new Vector(0, 0), "Pan command failed.");
            editor.FitToView(); editor.UpdateLayout();
            InteractionProof(draft);
            var pixels = Render(editor, 1100, 700);
            Require(pixels.Count(b => b < 200) > 1000, "Editor is blank.");
            if (args.Length > 0)
            {
                Directory.CreateDirectory(args[0]);
                var bitmap = new RenderTargetBitmap(1100, 700, 96, 96, PixelFormats.Pbgra32); bitmap.Render(editor);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(args[0], "cable-wiring-editor.png")); png.Save(stream);
            }
            if (args.Skip(1).Contains("pdf")) PdfSmoke(tables.Apply(template, draft), args[0]);
            Console.WriteLine("PASS wiring commands, undo/redo, exact inventory, shared draft, save/reopen, zoom/pan and nonblank rendering");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static byte[] Render(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var bytes = new byte[width * height * 4]; bitmap.CopyPixels(bytes, width * 4, 0); return bytes;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static void InteractionProof(CableManufacturingDraft source)
    {
        var before = JsonSerializer.Serialize(source);
        var staged = JsonSerializer.Deserialize<CableManufacturingDraft>(before)!;
        var editor = new CableWiringDiagramEditor(staged);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(CableWiringDiagramEditor).GetMethod("PickPin", flags)!.Invoke(editor, ["p3.2"]);
        typeof(CableWiringDiagramEditor).GetMethod("Branch", flags)!.Invoke(editor,
            [CableWiringEditorService.EdgeKey("p1.1", "p2.1"), new SchematicPoint(120, 32)]);
        Require(staged.ExplicitMapping!.Any(p => p.FromSourcePinId == "p3.2" && p.ToSourcePinId == "p1.1"), "Visual wire-branch did not retain an exact shared Pin.");
        Require(new CableWiringEditorService().Build(staged).Primitives.Any(p => p.Kind == "CIRCLE" && p.Radius < 1), "Explicit branch junction is not visible.");
        editor.Undo(); Require(staged.ExplicitMapping.Count == source.ExplicitMapping!.Count, "Branch undo failed.");
        Require(JsonSerializer.Serialize(source) == before, "Discarding a staged editor modified its source.");
        var table = new CableManufacturingTableEditor(staged, [], false);
        var grid = (System.Windows.Controls.DataGrid)typeof(CableManufacturingTableEditor).GetField("_grid", flags)!.GetValue(table)!;
        Require(!grid.CanUserSortColumns && grid.Columns.OfType<System.Windows.Controls.DataGridComboBoxColumn>().All(c => c.IsReadOnly), "Pin inventory allows accidental row mapping changes.");
        typeof(CableManufacturingTableEditor).GetMethod("AddRow", flags)!.Invoke(table, null);
        Require(table.Commit() && staged.Rows.Count == source.Rows.Count, "Inventory added an accidental mapping row.");
        var archive = new ComponentIntelligence.SymbolArchive.SymbolArchiveRepository(Path.Combine(Path.GetTempPath(), "ci-confirmation-" + Guid.NewGuid().ToString("N")));
        var dialog = new CableArchiveEditorDialog(archive, []);
        var checkbox = (System.Windows.Controls.CheckBox)typeof(CableArchiveEditorDialog).GetField("_confirmed", flags)!.GetValue(dialog)!;
        var archiveTable = (CableManufacturingTableEditor)typeof(CableArchiveEditorDialog).GetField("_table", flags)!.GetValue(dialog)!;
        checkbox.IsChecked = true;
        Require(archiveTable.Draft.ConfirmMapping, "Confirmation checkbox is disconnected from the staged draft.");
        var service = new CableWiringEditorService(); service.EnterVisualMode(archiveTable.Draft);
        service.Connect(archiveTable.Draft, archiveTable.Draft.Ends[0].Pins[0].PinId, archiveTable.Draft.Ends[1].Pins[0].PinId);
        typeof(CableArchiveEditorDialog).GetMethod("RefreshBindings", flags)!.Invoke(dialog, null);
        Require(checkbox.IsChecked == false && !archiveTable.Draft.ConfirmMapping, "Rewiring restored a stale approval checkbox.");
        checkbox.IsChecked = true; Require(archiveTable.Draft.ConfirmMapping, "Explicit reconfirmation is not accepted.");
        dialog.Close();
    }

    private static void PdfSmoke(ArchivedCableTemplate template, string root)
    {
        var project = new ElectricalProject { ProjectId = "wiring-pdf", Name = "Synthetic Cable Project", Schematic = new()
            { Pages = [new() { PageId = "main1", Title = "Engineering 1" }, new() { PageId = "main2", Title = "Engineering 2" }] } };
        var geometry = new SchematicCadAsset { SourceSha256 = template.AssetSha256, Width = 60, Height = 40,
            Primitives = [new() { Kind = "LINE", Start = new(0, 0), End = new(60, 0) }],
            ConnectionPoints = template.Ports.Select((p, i) => new SchematicCadContact(p.PortId, p.Name, new(i == 0 ? 0 : 60, 10 + i * 8), Direction: i == 0 ? "Left" : "Right")).ToArray() };
        var service = new SchematicAuthoringService();
        var bindings = template.Ports.ToDictionary(p => p.PortId, p => p.PortId);
        project = service.AddArchivedCable(project, template, CableConstructionType.Custom, geometry, bindings, "main1", new(30, 40));
        project = service.AddArchivedCable(project, template, CableConstructionType.Custom, geometry, bindings, "main1", new(180, 40));
        var body = project.Schematic!.Symbols[0];
        project.Schematic.Symbols.Add(body with { SymbolId = "repeated-body", PageId = "main2", Position = new(40, 60) });
        var before = JsonSerializer.Serialize(project);
        BitmapSource Page(ElectricalProject p, SchematicPage sheet)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, sheet.Width * 4, sheet.Height * 4));
                foreach (var symbol in p.Schematic!.Symbols.Where(s => s.PageId == sheet.PageId))
                    dc.DrawRectangle(null, new Pen(Brushes.Black, 2), new Rect(symbol.Position.X * 4, symbol.Position.Y * 4, symbol.Width * 4, symbol.Height * 4));
            }
            var bitmap = new RenderTargetBitmap((int)(sheet.Width * 4), (int)(sheet.Height * 4), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); bitmap.Freeze(); return bitmap;
        }
        var file = Path.Combine(root, "synthetic-linked-cable-appendix.pdf");
        if (File.Exists(file)) File.Delete(file);
        Require(SchematicPdfWriter.Write(project, file, Page) == 4, "Expected two main sheets followed by two unique physical-cable diagrams.");
        using var saved = PdfReader.Open(file, PdfDocumentOpenMode.Import);
        Require(saved.Pages[0].Annotations.Count == 2 && saved.Pages[1].Annotations.Count == 1, "Cable-body links missing or duplicate appendix sections.");
        Require(saved.Pages[2].Annotations.Count == 2 && saved.Pages[3].Annotations.Count == 1, "Return links must point to all source engineering pages.");
        Require(JsonSerializer.Serialize(project) == before, "PDF export mutated the project.");
        var protectedBytes = File.ReadAllBytes(file);
        try { SchematicPdfWriter.Write(project, file, Page); throw new Exception("Existing PDF was overwritten."); }
        catch (IOException) { }
        Require(File.ReadAllBytes(file).SequenceEqual(protectedBytes), "Existing PDF changed after refused overwrite.");
        var reordered = JsonSerializer.Deserialize<ElectricalProject>(before)!;
        reordered.Schematic!.Pages.Insert(0, new() { PageId = "old-detail", Title = "Legacy Detail", CableDetail = new(project.Cables[0].CableInstanceId, null) { DetailId = "detail" } });
        reordered.Schematic.Continuations.Add(new() { ContinuationId = "xref", Signal = "synthetic", Source = new() { MarkerId = "from", PageId = "main1", Position = new(110, 30) }, Destination = new() { MarkerId = "to", PageId = "old-detail", Position = new(100, 60) } });
        var movedFile = Path.Combine(root, "synthetic-relocated-continuation.pdf");
        if (File.Exists(movedFile)) File.Delete(movedFile);
        Require(SchematicPdfWriter.Write(reordered, movedFile, Page) == 5, "Dedicated detail did not relocate ahead of its diagram.");
        var inline = JsonSerializer.Deserialize<ElectricalProject>(before)!;
        inline.Cables.RemoveAt(1); inline.Schematic!.Symbols.Clear();
        inline.Schematic.Pages[0].InlineCableDetails.Add(new(inline.Cables[0].CableInstanceId, null) { DetailId = "inline", SketchVisible = false, TableWidth = 250 });
        var inlineFile = Path.Combine(root, "synthetic-inline-linked-appendix.pdf");
        if (File.Exists(inlineFile)) File.Delete(inlineFile);
        Require(SchematicPdfWriter.Write(inline, inlineFile, Page) == 3, "Inline-only representation has no appendix.");
        using var inlinePdf = PdfReader.Open(inlineFile, PdfDocumentOpenMode.Import);
        Require(inlinePdf.Pages[0].Annotations.Count == 1 && inlinePdf.Pages[2].Annotations.Count == 1, "Inline table must link forward and back.");
        var many = template with { Ports = template.Ports.Select(p => new ComponentPort { PortId = p.PortId, Name = p.Name, Connector = p.Connector, Pins = Enumerable.Range(1, 100).Select(n => new ComponentPin { PinId = p.PortId + "." + n, PinNumber = n.ToString(), Function = "Synthetic function " + n }).ToList() }).ToList(), Manufacturing = new() { InventoryOnly = true }, Mapping = [] };
        var tiles = CableWiringAppendixRenderer.Plan(ArchivedCableInstanceFactory.Create(many, CableConstructionType.Custom), new SchematicPage { PageId = "large", Title = "Large Inventory" }, 2);
        Require(tiles.Count > 1 && tiles.All(t => t.ContentBounds.Width == t.ViewBounds.Width && t.ContentBounds.Height == t.ViewBounds.Height), "Large inventories must tile at readable 1:1 scale.");
        Require(tiles[0].Diagram.PinPositions.Values.All(p => tiles.Any(t => p.X >= t.ViewBounds.X && p.X <= t.ViewBounds.X + t.ViewBounds.Width && p.Y >= t.ViewBounds.Y && p.Y <= t.ViewBounds.Y + t.ViewBounds.Height)), "Tiling omitted a Pin.");
        var failed = Path.Combine(root, "must-not-publish.pdf");
        try { SchematicPdfWriter.Write(project, failed, (_, _) => throw new InvalidOperationException("injected render failure")); throw new Exception("Injected failure did not happen."); }
        catch (InvalidOperationException) { }
        Require(!File.Exists(failed) && !Directory.GetFiles(root, ".schematic-*.pdf").Any(), "Failed export left a PDF artifact.");
        Console.WriteLine("PASS PDF main/appendix order, physical dedup, forward/return annotations, atomic failure and no source mutation");
    }
}
