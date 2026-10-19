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
    private static int WagoGroupProof(string root)
    {
        Directory.CreateDirectory(root);
        var project = new ElectricalProject { ProjectId = "wago-smoke", Schematic = new() { Pages = [new() { PageId = "page", Title = "WAGO groups" }] } };
        var author = new SchematicAuthoringService();
        for (var i = 1; i <= 3; i++)
        {
            var component = new ComponentInstance { ComponentInstanceId = "w" + i, ComponentDefinitionId = "wago", TypeKey = "SPLICE",
                DisplayName = "WAGO 221-2411", ReferenceDesignator = "X" + i,
                Ports = [new() { PortId = "port" + i, Name = "SPLICE", Pins = [new() { PinId = $"p{i}1", PinNumber = "1" }, new() { PinId = $"p{i}2", PinNumber = "2" }] }] };
            project.Components.Add(component);
            var symbol = SchematicAuthoringService.CreateSymbol(component, "page", new(120, 60 + i * 40)) with { SymbolId = "w" + i, CollapsedPortIds = [] };
            symbol = symbol with { Anchors = symbol.Anchors.Select((a, n) => a with { Direction = n == 0 ? "Left" : "Right", Position = new(n == 0 ? 0 : 50, 15) }).ToList() };
            project.Schematic!.Symbols.Add(symbol);
            foreach (var pin in symbol.Anchors)
            {
                var point = SchematicAuthoringService.AnchorPoint(symbol, pin.EndpointId);
                project = author.DrawWire(project, "page", SchematicAttachment.Pin(symbol.SymbolId, pin.EndpointId), SchematicAttachment.Free(),
                    [point, new(pin.Direction == "Left" ? 40 : 240, point.Y)]);
            }
        }
        var commits = 0;
        var workspace = new SchematicWorkspaceControl(() => project, (next, _) => { project = next; commits++; },
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        object? Get(string name) => typeof(SchematicWorkspaceControl).GetField(name, flags)!.GetValue(workspace);
        void Set(string name, object? value) => typeof(SchematicWorkspaceControl).GetField(name, flags)!.SetValue(workspace, value);
        object? Call(string name, params object?[] args) => typeof(SchematicWorkspaceControl).GetMethod(name, flags)!.Invoke(workspace, args);
        Set("_pageId", "page"); Set("_selectionId", "w1"); workspace.RefreshWorkspace();
        var button = (Button)workspace.FindName("WagoGroupButton");
        Require(button.Visibility == Visibility.Visible, "Eligible WAGO action is hidden.");
        var dialogType = typeof(SchematicWorkspaceControl).Assembly.GetType("ComponentIntelligence.Desktop.WagoGroupSelectionDialog")!;
        var before = JsonSerializer.Serialize(project);
        var dialog = (Window)Activator.CreateInstance(dialogType, BindingFlags.Instance | BindingFlags.NonPublic, null, [project, project.Schematic!.Symbols[0]], null)!;
        var panel = (DockPanel)dialog.Content;
        var actions = panel.Children.OfType<StackPanel>().Single();
        var accept = actions.Children.OfType<Button>().Single(b => Equals(b.Content, "合併顯示"));
        var choices = ((StackPanel)((ScrollViewer)panel.Children.OfType<ScrollViewer>().Single()).Content).Children.OfType<CheckBox>().ToArray();
        Require(choices.Length == 3 && choices.Count(c => c.IsChecked == true) == 1 && !accept.IsEnabled, "Same-page chooser or minimum count is wrong.");
        choices[1].IsChecked = true; Require(accept.IsEnabled, "Two selected WAGOs cannot be merged.");
        dialog.Close(); Require(before == JsonSerializer.Serialize(project), "Cancelled chooser mutated the project.");
        project = author.GroupWagoSymbols(project, ["w1", "w2", "w3"]); workspace.RefreshWorkspace();
        Render(workspace, 1500, 950, Path.Combine(root, "group-workspace.png"));
        var canvas = ((Dictionary<string, Canvas>)Get("_pageCanvases")!)["page"];
        var group = SchematicWagoGroups.Build(project.Schematic!).Single();
        var groupCanvas = canvas.Children.OfType<Canvas>().Single(c => Equals(c.Tag, "wago-group:" + group.Id));
        Require(groupCanvas.Children.OfType<TextBlock>().Count(t => t.Text == "WAGO 221-2411") == 1, "Model title is duplicated.");
        Require(groupCanvas.Children.OfType<TextBlock>().Count(t => t.Text is "X1" or "X2" or "X3") == 3, "Independent row References are missing.");
        Require(!canvas.Children.OfType<TextBlock>().Any(t => t.Text == "WAGO 221-2411"), "Individual body titles still render.");
        Require(!canvas.Children.OfType<Border>().Any(b => b.Child is TextBlock { Text: "▾" }), "Port collapse toggles overlap group rows.");
        var fields = (Dictionary<string, Control>)Get("_inspectorFields")!;
        Require(fields["width"].IsEnabled && fields["height"].IsEnabled && !fields["rotation"].IsEnabled, "Grouped size editing or rotation guard is wrong.");
        var zoom = new DrawingVisual();
        using (var drawing = zoom.RenderOpen()) drawing.DrawRectangle(new VisualBrush(canvas) { ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(240, 240, 300, 240), Stretch = Stretch.Fill }, null, new Rect(0, 0, 750, 600));
        var bitmap = new RenderTargetBitmap(750, 600, 96, 96, PixelFormats.Pbgra32); bitmap.Render(zoom); Save(bitmap, Path.Combine(root, "group-detail.png"));
        var first = project.Schematic!.Symbols[0];
        Set("_gestureStart", project); Set("_dragStart", first.Position); Set("_pointer", new SchematicPoint(first.Position.X + 15, first.Position.Y + 5)); Set("_dragSymbol", first.SymbolId);
        Call("PreviewSymbolTranslation");
        var translations = (Dictionary<UIElement, TranslateTransform>)Get("_dragTranslations")!;
        var tracked = (Dictionary<string, UIElement[]>)Get("_symbolVisuals")!;
        foreach (var member in group.Members)
            Require(tracked[member.SymbolId].All(v => translations.TryGetValue(v, out var t) && t.X == 45 && t.Y == 15), "Drag preview left a row behind.");
        Require(translations.ContainsKey(groupCanvas), "Common frame does not follow drag preview.");
        Call("CancelGesture");
        var unchanged = JsonSerializer.Serialize(project);
        var pdf = Path.Combine(root, "wago-group.pdf");
        Require(SchematicPdfWriter.Write(project, pdf, (p, page) => (BitmapSource)Call("RenderOutputPage", p, page)!) == 1, "Group PDF missing.");
        using (var document = PdfReader.Open(pdf, PdfDocumentOpenMode.Import)) Require(document.PageCount == 1, "Group PDF cannot reopen.");
        var sheet = new SchematicDxfExporter().Create(project).Single(); File.WriteAllBytes(Path.Combine(root, "wago-group.dxf"), sheet.Dxf);
        using (var stream = new MemoryStream(sheet.Dxf))
        {
            var dxf = DxfDocument.Load(stream);
            Require(dxf.Entities.MTexts.Count(t => t.Value == "WAGO 221-2411") == 1, "DXF duplicates WAGO model title.");
            Require(dxf.Entities.MTexts.Count(t => t.Value is "X1" or "X2" or "X3") == 3, "DXF row References lost.");
        }
        Require(unchanged == JsonSerializer.Serialize(project), "Output changed the project.");
        Call("WagoGroup_Click", button, new RoutedEventArgs());
        Require(commits == 1 && project.Schematic!.Symbols.All(s => s.WagoVisualGroupId is null), "Dissolve action did not commit exactly once.");
        File.WriteAllText(Path.Combine(root, "wago-group-pass.txt"), "PASS chooser/cancel, one frame, independent References and Pins, group drag preview, dissolve, PDF and DXF.");
        Console.WriteLine("WAGO_GROUP_PASS"); return 0;
    }
}
