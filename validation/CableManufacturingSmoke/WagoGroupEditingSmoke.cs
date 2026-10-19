using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using netDxf;
using PdfSharp.Pdf.IO;

internal static partial class Program
{
    private static int WagoGroupEditingProof(string root)
    {
        Directory.CreateDirectory(root);
        var pixels = Enumerable.Range(0, 240).SelectMany(_ => new byte[] { 30, 185, 40, 255 }).ToArray();
        var photo = BitmapSource.Create(24, 10, 96, 96, PixelFormats.Bgra32, null, pixels, 96); photo.Freeze();
        var imagePath = Path.Combine(root, "synthetic-catalog-image.png"); Save(photo, imagePath);
        var p = new ElectricalProject { ProjectId = "wago-editing", Schematic = new() { Pages = [new() { PageId = "page", Title = "Editable WAGO" }] } };
        var author = new SchematicAuthoringService();
        for (var i = 1; i <= 2; i++) {
            var owner = new ComponentInstance { ComponentInstanceId = "w" + i, ComponentDefinitionId = "wago", TypeKey = "SPLICE", DisplayName = "WAGO 221-2411",
                ReferenceDesignator = "X" + i, Ports = [new() { PortId = "port" + i, Name = "SPLICE",
                    Pins = [new() { PinId = $"p{i}1", PinNumber = "1" }, new() { PinId = $"p{i}2", PinNumber = "2" }] }] };
            p.Components.Add(owner);
            var symbol = SchematicAuthoringService.CreateSymbol(owner, "page", new(100, 60 + i * 40)) with { SymbolId = "w" + i, CollapsedPortIds = [] };
            symbol = symbol with { Anchors = symbol.Anchors.Select((a, n) => a with { Position = new(n == 0 ? 0 : 50, 15), Direction = n == 0 ? "Left" : "Right" }).ToList() };
            p.Schematic!.Symbols.Add(symbol);
            foreach (var a in symbol.Anchors) {
                var start = SchematicAuthoringService.AnchorPoint(symbol, a.EndpointId);
                p = author.DrawWire(p, "page", SchematicAttachment.Pin(symbol.SymbolId, a.EndpointId), SchematicAttachment.Free(),
                    [start, new(a.Direction == "Left" ? 20 : 240, start.Y)]);
            }
        }
        p = author.GroupWagoSymbols(p, ["w1", "w2"]);
        var commits = 0;
        var workspace = new SchematicWorkspaceControl(() => p, (next, _) => { p = next; commits++; },
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(new Uri(imagePath)), () => { }, () => { });
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        object? Get(string name) => typeof(SchematicWorkspaceControl).GetField(name, flags)!.GetValue(workspace);
        void Set(string name, object? value) => typeof(SchematicWorkspaceControl).GetField(name, flags)!.SetValue(workspace, value);
        object? Call(string name, params object?[] args) => typeof(SchematicWorkspaceControl).GetMethod(name, flags)!.Invoke(workspace, args);
        Set("_pageId", "page"); Set("_selectionId", "w1"); Set("_editModuleMode", true); workspace.RefreshWorkspace();
        ((Task)Call("GetImage", "wago")!).GetAwaiter().GetResult(); workspace.RefreshWorkspace();
        var group = SchematicWagoGroups.Build(p.Schematic!).Single();
        var canvas = ((Dictionary<string, Canvas>)Get("_pageCanvases")!)["page"];
        var image = canvas.Children.OfType<Image>().SingleOrDefault(i => Equals(i.Tag, "wago-group:" + group.Id));
        Require(image?.Source is not null, "Shared header catalog image is missing.");
        var fields = (Dictionary<string, Control>)Get("_inspectorFields")!;
        Require(fields["width"].IsEnabled && fields["height"].IsEnabled && !fields["rotation"].IsEnabled, "Grouped dimensions remain locked.");
        Require(((Button)workspace.FindName("WagoFanoutButton")).Visibility == Visibility.Visible, "Grouped fanout action is missing.");
        var menu = (ContextMenu)Call("PortFanoutMenu", p.Schematic!.Symbols[0], p.Components[0].Ports[0])!;
        var expand = menu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "展開接點引線"));
        expand.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Require(commits == 1 && p.Schematic!.Symbols[0].EndpointDisplayPlacements.Count == 2, "Grouped fanout command did not preserve exact Pins.");
        group = SchematicWagoGroups.Build(p.Schematic!).Single();
        Set("_gestureStart", p); Set("_dragResizeSymbol", "w1"); Set("_pointer", new SchematicPoint(group.Bounds.X + 40, group.Bounds.Y + group.HeaderHeight + 48));
        Call("PreviewSymbolResize"); var preview = (ElectricalProject)Get("_gesturePreview")!;
        Require(preview.Schematic!.Symbols.All(s => s.Width == 40 && s.Height == 24), "Group corner resize did not resize all rows.");
        Require(p.Schematic!.Symbols.All(s => s.Width == 24), "Resize preview mutated the saved project.");
        Call("CancelGesture");
        ((TextBox)fields["width"]).Text = "40"; ((TextBox)fields["height"]).Text = "24";
        Require((bool)Call("ApplyInspector")!, "Grouped size cannot be applied from Inspector.");
        Require(commits == 2 && p.Schematic!.Symbols.All(s => s.Width == 40 && s.Height == 24), "Group size was not committed exactly once.");
        Render(workspace, 1450, 950, Path.Combine(root, "wago-editing-workspace.png"));
        group = SchematicWagoGroups.Build(p.Schematic!).Single();
        image = canvas.Children.OfType<Image>().Single(i => Equals(i.Tag, "wago-group:" + group.Id));
        var fit = SchematicWagoGroups.ImageBounds(group, photo.PixelWidth, photo.PixelHeight);
        Require(Math.Abs(Canvas.GetLeft(image) / 3 - fit.X) < .001 && Math.Abs(image.Width / 3 - fit.Width) < .001, "Header image geometry differs from shared layout.");
        Require(canvas.Children.OfType<Border>().Any(b => b.Width == 14 && Equals(b.Tag, "wago-group:" + group.Id)), "Group corner handle is missing.");
        var before = JsonSerializer.Serialize(p);
        var output = (BitmapSource)Call("RenderOutputPage", p, p.Schematic!.Pages[0])!;
        Save(output, Path.Combine(root, "wago-editing-output.png"));
        var center = new Int32Rect((int)((fit.X + fit.Width / 2) / 25.4 * 300), (int)((fit.Y + fit.Height / 2) / 25.4 * 300), 1, 1);
        var sample = new byte[4]; output.CopyPixels(center, sample, 4, 0);
        Require(sample[1] > 120 && sample[1] > sample[2] + 60, "PDF renderer lost the actual catalog image pixels.");
        var pdf = Path.Combine(root, "wago-editing.pdf");
        SchematicPdfWriter.Write(p, pdf, (project, page) => (BitmapSource)Call("RenderOutputPage", project, page)!);
        using (var document = PdfReader.Open(pdf, PdfDocumentOpenMode.Import)) Require(document.PageCount == 1, "Editable WAGO PDF cannot reopen.");
        var raster = new SchematicRasterAsset(File.ReadAllBytes(imagePath), photo.PixelWidth, photo.PixelHeight);
        var sheet = new SchematicDxfExporter().Create(p, new Dictionary<string, SchematicRasterAsset> { ["wago"] = raster }).Single();
        File.WriteAllBytes(Path.Combine(root, "wago-editing.dxf"), sheet.Dxf);
        using (var stream = new MemoryStream(sheet.Dxf)) {
            var dxf = DxfDocument.Load(stream);
            Require(dxf.Entities.Images.Count() == 1 && sheet.Images.Count == 1, "DXF image is missing or duplicated per row.");
            Require(dxf.Entities.Lines.Any(l => l.Layer.Name == "SCHEMATIC_FANOUT"), "DXF omitted grouped fanout leads.");
        }
        Require(before == JsonSerializer.Serialize(p), "Output mutated group data.");
        File.WriteAllText(Path.Combine(root, "wago-editing-pass.txt"), "PASS independent fanout menu, shared width/row-height Inspector, corner preview, shared image pixels, PDF and DXF.");
        Console.WriteLine("WAGO_GROUP_EDITING_PASS"); return 0;
    }
}
