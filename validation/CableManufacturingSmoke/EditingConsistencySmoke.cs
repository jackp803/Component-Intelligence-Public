using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using ComponentPort = ComponentIntelligence.Electrical.Domain.ComponentPort;
using ComponentPin = ComponentIntelligence.Electrical.Domain.ComponentPin;

internal static partial class Program
{
    private static int EditingConsistencyProof(string output)
    {
        Directory.CreateDirectory(output);
        var p = new ElectricalProject { ProjectId = "synthetic-bn", Name = "Editing consistency", Schematic = new()
            { Pages = [new() { PageId = "main", Title = "Main" }] } };
        foreach (var side in new[] { "Valve", "IO" })
        {
            var port = new ComponentPort { PortId = side + "-port", Name = "P1", Pins = Enumerable.Range(1, 6).Select(i =>
                new ComponentPin { PinId = side + i, PinNumber = i.ToString(), Function = i == 1 ? "COM" : i == 2 ? "V24" : "Signal", PinName = i == 1 ? "COM / black" : "Signal" }).ToList() };
            p.Components.Add(new() { ComponentInstanceId = side, ComponentDefinitionId = side, TypeKey = "Module", ReferenceDesignator = side + "1", Ports = [port] });
            p.Schematic.Symbols.Add(new() { SymbolId = side, ComponentInstanceId = side, PageId = "main", Width = 40, Height = 80,
                Position = new(side == "Valve" ? 30 : 260, 40), Anchors = port.Pins.Select((pin, i) => new SchematicAnchor
                { EndpointId = pin.PinId, Position = new(side == "Valve" ? 40 : 0, 12 + i * 10), Direction = side == "Valve" ? "Right" : "Left", Label = "P1 / " + pin.PinNumber + " " + pin.PinName }).ToList() });
        }
        var service = new SchematicAuthoringService();
        for (var i = 1; i <= 6; i++) p = service.DrawWire(p, "main", SchematicAttachment.Pin("Valve", "Valve" + i),
            SchematicAttachment.Pin("IO", "IO" + i), [new(70, 42 + i * 10), new(260, 42 + i * 10)]);
        var first = p.Schematic!.Wires[0].WireId;
        p = service.SetWireDesignation(p, first, "1P24"); p = service.SetWireAwg(p, first, 18);
        var workspace = new SchematicWorkspaceControl(() => p, (next, _) => p = next,
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var owner = new Window { Content = workspace };
        var type = typeof(SchematicWorkspaceControl);
        void Call(string name, params object?[] args) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(workspace, args);
        void Set(string name, object? value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, value);
        Set("_pageId", "main"); Call("Render", p);
        var sheet = (Canvas)type.GetProperty("Sheet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        void Layout() { sheet.Measure(new Size(sheet.Width, sheet.Height)); sheet.Arrange(new Rect(0, 0, sheet.Width, sheet.Height)); sheet.UpdateLayout(); }
        Layout();
        var label = sheet.Children.OfType<TextBlock>().Single(t => Equals(t.Tag, "wire-label:" + first));
        Require(label.Text.Contains("1P24") && label.Text.Contains("AWG 18") && label.Text.Contains("0.823"), "Named wire and unique specification are not adjacent on the wire.");
        Require(!label.Text.Contains("0.823046"), "Display leaked unrounded area.");
        var serialized = JsonSerializer.Serialize(p);
        Call("SelectWire", first, false); Call("SelectWire", p.Schematic.Wires[1].WireId, true);
        var selection = (HashSet<string>)type.GetField("_selectedWires", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        Require(selection.Count == 2, "Ctrl selection did not retain both wires.");
        Call("SelectWire", first, true); Require(selection.Count == 1 && !selection.Contains(first), "Ctrl toggle did not deselect.");
        selection.Clear(); Set("_selectionId", null);
        Set("_marqueeStart", new Point(200, 130)); Call("FinishWireMarquee", new Point(800, 350));
        Require(selection.Count == 6, "Box selection did not select all six wires.");
        Require(serialized == JsonSerializer.Serialize(p), "Selection mutated the project.");
        p = service.AddPage(p, "Empty"); workspace.RefreshWorkspace();
        var pages = (ListBox)type.GetField("PageList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        pages.SelectedIndex = 1;
        Require(selection.Count == 0, "Page navigation retained a hidden wire batch.");
        pages.SelectedIndex = 0;
        p = service.TransformSymbol(p, "Valve", new(30, 85), 0); selection.Clear(); Set("_selectionId", null); Call("Render", p); Layout();
        Require(!SchematicCrossingService.Analyze(p.Schematic!.Wires).Conflicts.Any(), "Move left bundle still creates apparent contacts.");
        var labels = sheet.Children.OfType<TextBlock>().Where(t => t.Tag is string tag && tag.StartsWith("wire-label:")).ToArray();
        Require(labels.Length == 6, "Move lost a wire label.");
        foreach (var text in sheet.Children.OfType<TextBlock>().Where(t => t.Tag is string tag && tag.StartsWith("wire-")))
        {
            var top = Canvas.GetTop(text); var left = Canvas.GetLeft(text);
            if (text.RenderTransform is System.Windows.Media.RotateTransform) continue;
            Require(top >= 0 && top + text.DesiredSize.Height <= p.Schematic.Pages[0].Height * 3 && left >= 0 && left + text.DesiredSize.Width <= p.Schematic.Pages[0].Width * 3, "Wire annotation is outside the paper.");
            var box = new Rect(left, top, text.DesiredSize.Width, text.DesiredSize.Height);
            foreach (var wire in p.Schematic.Wires)
            foreach (var segment in wire.Points.Zip(wire.Points.Skip(1)))
            { var stroke = new Rect(new Point(segment.First.X * 3, segment.First.Y * 3), new Point(segment.Second.X * 3, segment.Second.Y * 3)); stroke.Inflate(1, 1); Require(!stroke.IntersectsWith(box), "Wire annotation covers a wire."); }
        }
        var bitmap = new RenderTargetBitmap((int)sheet.Width, (int)sheet.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(sheet);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(output, "editing.png"))) encoder.Save(file);
        var count = SchematicPdfWriter.Write(p, Path.Combine(output, "editing.pdf"), (project, page) =>
            (BitmapSource)type.GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, [project, page])!, includeWireDetails: true);
        Require(count >= 2, "PDF omitted full wire detail appendix.");
        using (var pdf = PdfSharp.Pdf.IO.PdfReader.Open(Path.Combine(output, "editing.pdf"), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
            Require(pdf.Pages[0].Annotations.Count >= 6, "Wire bodies do not link directly to their full detail appendix.");
        var net = p.Connections.Select(c => (c.ConnectionId, c.FromEndpointId, c.ToEndpointId)).ToArray();
        p = service.SetPortFanout(p, "Valve", "Valve-port", true, 10, 25); Call("Render", p); Layout();
        Require(sheet.Children.OfType<FrameworkElement>().Count(e => e.Tag is string tag && tag.StartsWith("port-fanout:")) == 6, "Fan-out lost a separate Pin lead.");
        Require(net.SequenceEqual(p.Connections.Select(c => (c.ConnectionId, c.FromEndpointId, c.ToEndpointId))), "Fan-out changed net connectivity.");
        var fanoutBitmap = new RenderTargetBitmap((int)sheet.Width, (int)sheet.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); fanoutBitmap.Render(sheet);
        encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(fanoutBitmap)); using (var file = File.Create(Path.Combine(output, "fanout.png"))) encoder.Save(file);
        File.WriteAllText(Path.Combine(output, "editing.json"), JsonSerializer.Serialize(new { Passed = true, Count = labels.Length, PdfPages = count, BatchSelection = true, NoOutsideAnnotations = true }));
        Console.WriteLine("PASS named-wire specification, Ctrl/box selection, route independence, in-sheet annotations and PDF details."); return 0;
    }
}
