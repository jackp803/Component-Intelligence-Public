using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClosedXML.Excel;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Repository;
using ComponentIntelligence.SymbolArchive;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using PdfSharp.Pdf.IO;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static int Run(string[] args)
    {
        var root = Path.GetFullPath(args.First());
        var regression = args.Skip(1).SingleOrDefault() ?? "all";
        Directory.CreateDirectory(root);
        SQLitePCL.Batteries_V2.Init();
        var data = Path.Combine(root, "data"); Directory.CreateDirectory(data);
        var sources = Path.Combine(root, "source-cad"); Directory.CreateDirectory(sources);
        var wirePath = Path.Combine(sources, "synthetic-wiring.dxf");
        var sketchPath = Path.Combine(sources, "synthetic-sketch-with-old-table.dxf");
        if (File.Exists(wirePath) || File.Exists(sketchPath)) throw new IOException("Use a fresh smoke directory.");
        var dxf = new DxfDocument();
        var wireBlock = new Block("SYNTHETIC_WIRING");
        wireBlock.Entities.Add(new Line(new Vector2(0, 0), new Vector2(100, 0)));
        wireBlock.Entities.Add(new Line(new Vector2(100, 0), new Vector2(100, 20)));
        wireBlock.Entities.Add(new Line(new Vector2(100, 20), new Vector2(0, 20)));
        wireBlock.Entities.Add(new Line(new Vector2(0, 20), new Vector2(0, 0)));
        for (var i = 1; i <= 4; i++)
            wireBlock.AttributeDefinitions.Add(new AttributeDefinition("X" + (i <= 2 ? "1" : "4") + "TERM0" + i)
            { Position = new Vector3(i <= 2 ? 0 : 100, i % 2 == 1 ? 5 : 15, 0), Flags = AttributeFlags.Hidden, Value = i.ToString() });
        wireBlock.AttributeDefinitions.Add(new AttributeDefinition("REF") { Position = new Vector3(20, 16, 0), Height = 3, Value = "REF" });
        wireBlock.AttributeDefinitions.Add(new AttributeDefinition("LENGTH") { Position = new Vector3(20, 8, 0), Height = 3, Value = "LENGTH" });
        dxf.Entities.Add(new Insert(wireBlock)); dxf.Save(wirePath);
        dxf = new();
        var sketchBlock = new Block("SYNTHETIC_SKETCH");
        sketchBlock.Entities.Add(new Circle(new Vector2(10, 10), 8));
        sketchBlock.Entities.Add(new Line(new Vector2(18, 8), new Vector2(100, 8)));
        sketchBlock.Entities.Add(new Line(new Vector2(18, 12), new Vector2(100, 12)));
        sketchBlock.AttributeDefinitions.Add(new AttributeDefinition("REF") { Position = new Vector3(35, 16, 0), Height = 3, Value = "REF" });
        dxf.Entities.Add(new Insert(sketchBlock));
        dxf.Entities.Add(new Text("OLD STATIC TABLE - EXCLUDED", new Vector2(0, 45), 3));
        dxf.Entities.Add(new Line(new Vector2(0, 35), new Vector2(150, 35))); dxf.Save(sketchPath);
        var importer = new SchematicCadImporter();
        var wire = importer.ReadDxf(wirePath, 1);
        var fullSketch = importer.ReadDxf(sketchPath, 1);
        var selection = new CableCadSelection { BlockName = "SYNTHETIC_SKETCH", GeometrySha256 = new('0', 64) };
        var sketch = SchematicCadSelectionService.Select(fullSketch, selection);
        selection = selection with { GeometrySha256 = sketch.SelectionSha256! };
        Require(sketch.Primitives.All(p => p.Text?.Contains("OLD STATIC TABLE") != true), "Static CAD table was not excluded.");
        if (regression is "all" or "selection")
        {
            var loader = new SchematicCadFileLoader(stagingRoot: Path.Combine(root, "loader-staging"));
            var restore = typeof(SchematicCadFileLoader).GetMethod("ReadArchivedAsync");
            var task = restore is null ? loader.ReadAsync(sketchPath, 1) :
                (Task<SchematicCadAsset>)restore.Invoke(loader, [sketchPath, 1d, selection, null, CancellationToken.None])!;
            var loadedSketch = task.GetAwaiter().GetResult();
            Require(JsonSerializer.Serialize(loadedSketch) == JsonSerializer.Serialize(sketch), "Legacy selected CAD was reloaded as a full drawing.");
        }
        var template = new ArchivedCableTemplate
        {
            TemplateId = "synthetic-cable",
            TemplateRevision = "r1",
            DisplayName = "SYNTHETIC TEST CABLE",
            AssetSha256 = wire.SourceSha256,
            TextBindings = [new("REF", CableTextField.Reference), new("LENGTH", CableTextField.LengthMm)],
            Ports = Enumerable.Range(1, 2).Select(i => new ComponentIntelligence.Electrical.Domain.ComponentPort
            {
                PortId = "P" + i,
                Name = "P" + i,
                Connector = new() { ConnectorId = "test-" + i, Family = "TEST", PinCount = 2 },
                Pins = Enumerable.Range(1, 2).Select(j => new ComponentIntelligence.Electrical.Domain.ComponentPin { PinId = $"p{i}.{j}", PinNumber = j.ToString() }).ToList()
            }).ToList()
        };
        var editorService = new CableManufacturingEditorService(); var draft = editorService.Prepare(template);
        draft.Rows.Clear();
        draft.Rows.Add(new() { FromSourcePinId = "p1.1", ToSourcePinId = "p2.1", FromFunction = "+24V", ToFunction = "+24V" });
        draft.Rows.Add(new() { FromSourcePinId = "p1.2", ToSourcePinId = "p2.2", FromFunction = "0V", ToFunction = "0V" });
        template = editorService.Apply(template, draft);
        var repository = new SymbolArchiveRepository(data);
        var contacts = wire.ConnectionPoints.OrderBy(c => c.Tag, StringComparer.Ordinal).ToArray();
        Require(contacts.Length == 4, "Expected four exact CAD contacts.");
        var entry = new CableArchiveCreationService(repository).CreateAsync(new()
        {
            Entry = new()
            {
                Template = template,
                AssetPath = "source.dxf",
                MillimetresPerUnit = 1,
                ConstructionType = CableConstructionType.Custom,
                ConstructionEvidence = "Generated test fixture, not an engineering-approved cable",
                ContactBindings = template.Ports.SelectMany(p => p.Pins).Select((pin, i) => new SymbolPortBinding
                {
                    EngineeringEndpointId = pin.PinId,
                    ConnectionPointId = contacts[i].Tag
                }).ToArray(),
                ManufacturingAsset = new() { AssetPath = "source-sketch.dxf", SourceSha256 = sketch.SourceSha256, MillimetresPerUnit = 1, Selection = selection }
            },
            WiringSourcePath = wirePath,
            WiringGeometry = wire,
            ManufacturingSourcePath = sketchPath,
            ManufacturingGeometry = sketch
        }).GetAwaiter().GetResult();
        var p = new ElectricalProject
        {
            ProjectId = "synthetic-cable-mvp",
            Name = "SYNTHETIC TEST - NOT ENGINEERING APPROVED",
            Schematic = new() { Pages = [new() { PageId = "sheet", Title = "Wiring test" }] }
        };
        var author = new SchematicAuthoringService();
        var bindings = entry.ContactBindings.ToDictionary(b => b.EngineeringEndpointId, b => b.ConnectionPointId);
        p = author.AddArchivedCable(p, entry.Template, CableConstructionType.Custom, wire, bindings, "sheet", new(40, 60), manufacturingGeometry: sketch);
        p = author.AddArchivedCable(p, entry.Template, CableConstructionType.Custom, wire, bindings, "sheet", new(220, 60), manufacturingGeometry: sketch);
        for (var i = 0; i < 2; i++)
            p = author.SetArchivedCableDetails(p, p.Cables[i].CableInstanceId, "CB-0" + (i + 1), 1000 + i * 200, "TEST 2 CORE", CableConstructionType.Custom, p.Cables[i].ArchivedCable!.Mapping);
        var frame = new SchematicCadAsset
        {
            SourceSha256 = new('c', 64),
            MillimetresPerUnit = 1,
            Width = 420,
            Height = 297,
            Primitives = [new() { Kind = "LINE", Start = new(10, 10), End = new(410, 10) },
                new() { Kind = "LINE", Start = new(410, 10), End = new(410, 287) },
                new() { Kind = "LINE", Start = new(410, 287), End = new(10, 287) },
                new() { Kind = "LINE", Start = new(10, 287), End = new(10, 10) },
                new() { Kind = "TEXT", Start = new(230, 280), Text = "SYNTHETIC COMPANY FRAME", TextHeight = 4 }]
        };
        p.Schematic!.Pages[0] = p.Schematic.Pages[0] with { TemplateGeometry = frame, TemplateSha256 = frame.SourceSha256 };
        var detailService = new SchematicCableDetailService(); p = detailService.AddArchivedDetail(p, p.Cables[0].CableInstanceId, "sheet");
        var db = new ElectricalProjectRepository(new SqliteConnectionFactory(), Path.Combine(data, "project.db"));
        db.SaveAsync(p).GetAwaiter().GetResult(); p = db.GetAsync(p.ProjectId).GetAwaiter().GetResult()!;
        var snapshot = JsonSerializer.Serialize(p);
        Require(p.Cables.Count == 2 && p.Schematic!.Symbols.Count == 2 && p.Schematic.Pages.Count == 2, "Detail duplicated physical inventory.");
        using (var workbook = new XLWorkbook())
        {
            foreach (var name in new[] { "Components", "Ports", "Pins" })
                workbook.Worksheets.Add(name).Cell(1, 1).Value = name == "Components" ? "ComponentID" : name == "Ports" ? "PortID" : "PinID";
            workbook.SaveAs(Path.Combine(data, "Component_Intelligence_Database.xlsx"));
        }
        var table = new CableManufacturingTableEditor(editorService.Prepare(p.Cables[0]), [new("TEST", null)], false);
        Render(table, 900, 500, Path.Combine(root, "table-editor.png"));
        Require(FindGrid(table).Columns.All(c => c.ActualWidth >= 100), "Table columns are clipped.");
        if (regression is "all" or "usage")
        {
            var tableGrid = FindGrid(table);
            var usageSelector = Descendants(table).OfType<ComboBox>().First(c => c.Items.Count > 0 && c.Items[0] is CablePinUsage);
            tableGrid.SelectedItem = tableGrid.Items[0];
            usageSelector.SelectedItem = CablePinUsage.Unused;
            tableGrid.SelectedItem = tableGrid.Items[1];
            Require((CablePinUsage)usageSelector.SelectedItem == CablePinUsage.Pending, "Pin usage selector did not follow the selected table row.");
            Require(((CableManufacturingRow)tableGrid.Items[1]).FromUsage == CablePinUsage.Pending, "Selecting another row changed its usage.");
            usageSelector.SelectedItem = CablePinUsage.Unused;
            tableGrid.SelectedItem = tableGrid.Items[0];
            Require((CablePinUsage)usageSelector.SelectedItem == CablePinUsage.Unused, "Existing usage was lost on reselection.");
            tableGrid.SelectedItem = tableGrid.Items[1];
            Require(((CableManufacturingRow)tableGrid.Items[1]).FromUsage == CablePinUsage.Unused, "Consecutive unused declarations failed.");
        }
        if (regression is "all" or "custom" or "catalog")
        {
            var customDraft = editorService.Prepare(template);
            customDraft.Ends[0].Connector = null; customDraft.Rows.Clear();
            foreach (var pin in customDraft.Ends.SelectMany(e => e.Pins)) pin.Function = null;
            var catalogPort = new ComponentIntelligence.Electrical.Domain.ComponentPort
            {
                PortId = "catalog",
                Name = "Power",
                Connector = new() { ConnectorId = "catalog-power", Family = "Terminal", PinCount = 2 },
                Pins = [new() { PinId = "catalog-1", PinNumber = "L+", PinName = "Supply" }, new() { PinId = "catalog-2", PinNumber = "PE", PinName = "Earth" }]
            };
            var custom = new CableManufacturingTableEditor(customDraft, [new("Custom", null), new("Power", catalogPort)]);
            Render(custom, 900, 500, Path.Combine(root, "custom-editor.png"));
            var gender = Descendants(custom).OfType<ComboBox>().Single(c => c.Items.Count > 0 && c.Items[0] is ConnectorGender);
            gender.SelectedItem = Enum.GetValues<ConnectorGender>().First(v => v != ConnectorGender.Unknown);
            var coding = Descendants(custom).OfType<ComboBox>().Single(t => t.ToolTip?.ToString() == "接頭 Coding");
            coding.Text = "A";
            Require(custom.Commit(), "Custom connector editor could not commit.");
            if (regression != "catalog") Require(custom.Draft.Ends[0].Connector?.Gender == (ConnectorGender)gender.SelectedItem &&
                custom.Draft.Ends[0].Connector?.Coding == "A", "Custom connector gender/coding was lost.");
            var connectorSelector = Descendants(custom).OfType<ComboBox>().Single(c => c.Items.Count > 0 && c.Items[0] is CableConnectorChoice);
            connectorSelector.SelectedIndex = 1;
            if (regression != "custom") Require(custom.Draft.Ends[0].Pins.Select(pin => pin.PinNumber).SequenceEqual(new[] { "L+", "PE" }),
                "Catalog connector did not import exact terminal numbers.");
        }
        var archiveDialog = new CableArchiveEditorDialog(repository, [], entry);
        Render((FrameworkElement)archiveDialog.Content, 940, 720, Path.Combine(root, "archive-editor.png")); archiveDialog.Close();
        var workspace = new SchematicWorkspaceControl(() => p, (_, _) => throw new InvalidOperationException("Unexpected UI mutation."),
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { }, data);
        var render = typeof(SchematicWorkspaceControl).GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (regression is "all" or "recovery")
        {
            var invalid = JsonSerializer.Deserialize<ElectricalProject>(snapshot)!;
            invalid.Cables[0].Specification = new string('X', 4000);
            typeof(SchematicWorkspaceControl).GetMethod("RenderPage", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(workspace, [invalid, invalid.Schematic!.Pages[1]]);
            var sheet = (Canvas)typeof(SchematicWorkspaceControl).GetProperty("Sheet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
            Require(sheet.Children.OfType<Button>().Count(b => b.ToolTip?.ToString()?.EndsWith("位置／尺寸") == true) == 2,
                "Invalid detail lost its recovery controls.");
        }
        BitmapSource Page(ElectricalProject project, SchematicPage page) => (BitmapSource)render.Invoke(workspace, [project, page])!;
        for (var i = 0; i < p.Schematic!.Pages.Count; i++)
            Save(Page(p, p.Schematic.Pages[i]), Path.Combine(root, $"page-{i + 1}.png"));
        var pdf = Path.Combine(root, "synthetic-multipage-draft.pdf");
        Require(SchematicPdfWriter.Write(p, pdf, Page) == 2, "Wrong PDF page count.");
        using (var document = PdfReader.Open(pdf, PdfDocumentOpenMode.Import))
            Require(document.PageCount == 2 && document.Info.Title.Contains("DRAFT"), "Saved PDF invalid.");
        var failed = Path.Combine(root, "must-not-exist.pdf");
        try { SchematicPdfWriter.Write(p, failed, (_, _) => throw new InvalidOperationException("simulated failure")); throw new Exception("Failure test did not fail."); }
        catch (InvalidOperationException) { }
        Require(!File.Exists(failed) && Directory.GetFiles(root, ".schematic-*.pdf").Length == 0, "Failed PDF left a published artifact.");
        Require(JsonSerializer.Serialize(p) == snapshot, "Rendering mutated project data.");
        File.WriteAllText(Path.Combine(root, "sample-project.json"), JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: four CAD contacts; selected sketch excludes static table; SQLite reopen; 2 instances; shared WPF renderer; 2 PDF pages; failed export atomic; no project mutation.");
        return 0;
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Render(FrameworkElement element, double width, double height, string path)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(background); bitmap.Render(element); Save(bitmap, path);
    }
    private static DataGrid FindGrid(DependencyObject root)
    {
        if (root is DataGrid grid) return grid;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            try { return FindGrid(VisualTreeHelper.GetChild(root, i)); } catch (InvalidOperationException) { }
        throw new InvalidOperationException("Grid not found.");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Save(BitmapSource bitmap, string path) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(path); encoder.Save(output); }
}
