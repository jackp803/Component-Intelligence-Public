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

internal static partial class Program
{
    private static int CompactSymbolProof(string root)
    {
        Directory.CreateDirectory(root);
        var component = new ComponentInstance { ComponentInstanceId = "wago", ComponentDefinitionId = "synthetic-wago", TypeKey = "SPLICE", DisplayName = "WAGO 221-2411",
            Ports = [new() { PortId = "splice", Name = "SPLICE", Pins = [new() { PinId = "pin1", PinNumber = "1" }, new() { PinId = "pin2", PinNumber = "2" }] }] };
        var symbol = SchematicAuthoringService.CreateSymbol(component, "page", new(60, 60)) with { CollapsedPortIds = [] };
        symbol = symbol with { Anchors = symbol.Anchors.Select((a, i) => a with { Direction = i == 0 ? "Left" : "Right", Position = new(i == 0 ? 0 : 50, 15) }).ToList() };
        var project = new ElectricalProject { ProjectId = "compact", Components = [component], Schematic = new() { Pages = [new() { PageId = "page", Title = "Compact connector" }], Symbols = [symbol] } };
        var author = new SchematicAuthoringService();
        foreach (var pin in symbol.Anchors) {
            var start = SchematicAuthoringService.AnchorPoint(symbol, pin.EndpointId);
            project = author.DrawWire(project, "page", SchematicAttachment.Pin(symbol.SymbolId, pin.EndpointId), SchematicAttachment.Free(),
                [start, new(pin.Direction == "Left" ? 10 : 230, start.Y)]);
        }
        var before = JsonSerializer.Serialize(project); var updates = 0;
        var workspace = new SchematicWorkspaceControl(() => project, (next, _) => { project = next; updates++; },
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        object? Get(string name) => typeof(SchematicWorkspaceControl).GetField(name, flags)!.GetValue(workspace);
        void Set(string name, object? value) => typeof(SchematicWorkspaceControl).GetField(name, flags)!.SetValue(workspace, value);
        object? Call(string name, params object?[] args) => typeof(SchematicWorkspaceControl).GetMethod(name, flags)!.Invoke(workspace, args);
        Set("_pageId", "page"); Set("_selectionId", symbol.SymbolId); Set("_editModuleMode", true); workspace.RefreshWorkspace();
        Set("_gestureStart", project); Set("_dragResizeSymbol", symbol.SymbolId); Set("_pointer", new SchematicPoint(80, 78));
        Call("PreviewSymbolResize");
        var preview = (ElectricalProject)Get("_gesturePreview")!;
        Require(preview.Schematic!.Symbols.Single().Width == 20 && preview.Schematic.Symbols.Single().Height == 18,
            "Corner drag is still clamped to the old 30mm frame.");
        Require(before == JsonSerializer.Serialize(project) && updates == 0, "Resize preview mutated the project.");
        Call("CancelGesture");
        var fields = (Dictionary<string, Control>)Get("_inspectorFields")!;
        ((TextBox)fields["width"]).Text = "20"; ((TextBox)fields["height"]).Text = "18";
        Require((bool)Call("ApplyInspector")!, "Inspector rejected a compact module.");
        Require(updates == 1 && project.Schematic!.Symbols.Single().Width == 20 && project.Schematic.Symbols.Single().Height == 18,
            "Compact size was not committed exactly once.");
        foreach (var wire in project.Schematic!.Wires)
            Require(wire.Points[0] == SchematicAuthoringService.AnchorPoint(project.Schematic.Symbols.Single(), wire.Start.EndpointId!), "A wire detached during shrink.");
        Render(workspace, 1400, 900, Path.Combine(root, "compact-workspace.png"));
        var canvas = ((Dictionary<string, Canvas>)Get("_pageCanvases")!)["page"];
        var title = canvas.Children.OfType<TextBlock>().Single(t => t.Text == "WAGO 221-2411");
        Require(Canvas.GetTop(title) >= 60 * 3 && Canvas.GetTop(title) + title.ActualHeight <= 78 * 3 + 1,
            "Compact module caption extends outside the body.");
        var titleBounds = new Rect(Canvas.GetLeft(title), Canvas.GetTop(title), title.ActualWidth, title.ActualHeight);
        foreach (var handle in canvas.Children.OfType<Border>().Where(b => b.Child is TextBlock { Text: "▾" }))
            Require(!titleBounds.IntersectsWith(new Rect(Canvas.GetLeft(handle), Canvas.GetTop(handle), handle.ActualWidth, handle.ActualHeight)),
                "Port toggle obscures the compact module caption.");
        var zoom = new DrawingVisual();
        using (var drawing = zoom.RenderOpen()) drawing.DrawRectangle(new VisualBrush(canvas) { ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(65, 135, 420, 150), Stretch = Stretch.Fill }, null, new Rect(0, 0, 1008, 360));
        var bitmap = new RenderTargetBitmap(1008, 360, 96, 96, PixelFormats.Pbgra32); bitmap.Render(zoom); Save(bitmap, Path.Combine(root, "compact-detail.png"));
        var count = SchematicPdfWriter.Write(project, Path.Combine(root, "compact-symbol.pdf"), (p, page) =>
            (BitmapSource)typeof(SchematicWorkspaceControl).GetMethod("RenderOutputPage", flags)!.Invoke(workspace, [p, page])!);
        Require(count == 1, "Compact PDF page missing.");
        project = JsonSerializer.Deserialize<ElectricalProject>(before)!;
        project = author.SetSymbolDetails(project, symbol.SymbolId, null,
            symbol.Anchors.Select(a => a with { Position = a.Position with { Y = 9.3 } }).ToList());
        project = author.ResizeGenericSymbol(project, symbol.SymbolId, 20, 22); workspace.RefreshWorkspace();
        Render(workspace, 1400, 900, Path.Combine(root, "fractional-layout.png"));
        title = canvas.Children.OfType<TextBlock>().Single(t => t.Text == "WAGO 221-2411");
        titleBounds = new Rect(Canvas.GetLeft(title), Canvas.GetTop(title), title.ActualWidth, title.ActualHeight);
        foreach (var handle in canvas.Children.OfType<Border>().Where(b => b.Child is TextBlock { Text: "▾" }))
            Require(!titleBounds.IntersectsWith(new Rect(Canvas.GetLeft(handle), Canvas.GetTop(handle), handle.ActualWidth, handle.ActualHeight)),
                "Fractional contact placement hides a one-pixel toggle/caption overlap.");
        var reloaded = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(project))!; SchematicAuthoringService.Validate(reloaded);
        File.WriteAllText(Path.Combine(root, "compact-symbol-pass.txt"), "PASS 20x18mm drag/inspector, contact and wire identity, caption bounds, reload and PDF.");
        Console.WriteLine("COMPACT_SYMBOL_PASS"); return 0;
    }
}
