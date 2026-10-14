using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Ellipse = System.Windows.Shapes.Ellipse;
using Shape = System.Windows.Shapes.Shape;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Connectivity;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Electrical.Topology;

internal static partial class Program
{
    private static int MatingProof(string output)
    {
        Directory.CreateDirectory(output);
        var project = new ElectricalProject { ProjectId = "synthetic-mating-ui", Name = "Connector mating",
            Schematic = new() { Pages = [new() { PageId = "one", Title = "Sensor" }, new() { PageId = "two", Title = "PLC" }] } };
        foreach (var id in new[] { "Sensor", "PLC" })
        {
            var port = new ComponentIntelligence.Electrical.Domain.ComponentPort { PortId = id + "-port", Name = "P1", Connector = new()
                { ConnectorId = id, Family = "M12", Coding = "A", PinCount = 2, Gender = id == "Sensor" ? ConnectorGender.Male : ConnectorGender.Female },
                Pins = [new() { PinId = id + ".1", PinNumber = "1", Function = id == "Sensor" ? "GND" : null }, new() { PinId = id + ".7", PinNumber = "7", Function = id == "Sensor" ? "24V" : null }] };
            project.Components.Add(new() { ComponentInstanceId = id, ComponentDefinitionId = id, TypeKey = id, ReferenceDesignator = id + "1", Ports = [port] });
            project.Schematic.Symbols.Add(new() { SymbolId = id, ComponentInstanceId = id, PageId = id == "Sensor" ? "one" : "two",
                Width = 40, Height = 40, Position = new(80, 50), Anchors = port.Pins.Select((p, i) => new SchematicAnchor
                { EndpointId = p.PinId, Position = new(id == "Sensor" ? 40 : 0, 12 + i * 12), Direction = id == "Sensor" ? "Right" : "Left", Label = "P1 / " + p.PinNumber + " " + p.Function }).ToList() });
        }
        var history = new ProjectMutationHistory();
        var workspace = new SchematicWorkspaceControl(() => project, (next, label) => { history.RecordBeforeMutation(project, label); project = next; },
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var owner = new Window { Content = workspace }; var type = typeof(SchematicWorkspaceControl);
        void Set(string name, object? value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, value);
        object? Call(string name, params object?[] values) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(workspace, values);
        Canvas Sheet() => (Canvas)type.GetProperty("Sheet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        Set("_pageId", "one"); Call("Render", project); Call("Continuation_Click", workspace, new RoutedEventArgs());
        Require(Sheet().Children.OfType<Ellipse>().Count() == 1, "Mating mode must show one whole-Port target, not individual Pin targets.");
        var from = SchematicAttachment.Port("Sensor", "Sensor-port"); var to = SchematicAttachment.Port("PLC", "PLC-port");
        Call("PickMatingPort", from); Set("_pageId", "two"); Call("Render", project);
        Require(Equals(type.GetField("_matingFirst", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace), from), "Page change lost first Port.");
        Call("CancelCommand"); Require(type.GetField("_matingFirst", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace) is null, "Esc did not clear Port selection.");
        var before = ProjectSaveSession.Clone(project); var service = new SchematicAuthoringService();
        project = service.MatePorts(project, from, to, ConnectorMatingService.Prepare(project, from.EndpointId!, to.EndpointId!), "synthetic checked", "J1 接頭對接");
        history.RecordBeforeMutation(before, "mate"); Set("_pageId", "two"); Call("Render", project);
        Require(Sheet().Children.OfType<TextBlock>().Any(t => t.Text.Contains("P1 / 1 GND")), "Inferred Pin function did not appear on destination module.");
        Require(Sheet().Children.OfType<TextBlock>().Any(t => t.Text.Contains("J1 接頭對接")), "Mating arrow lost its short caption.");
        Require(!Sheet().Children.OfType<TextBlock>().Any(t => t.Text.Contains("AWG")), "Mating generated an AWG caption or overflow warning.");
        Require(Sheet().Children.OfType<Shape>().Any(s => s.StrokeDashArray?.Count == 2), "Mating link is indistinguishable from a physical conductor.");
        Set("_selectionId", project.Schematic!.Wires[1].WireId); Call("UpdateInspector", project, true);
        var fields = (StackPanel)type.GetField("WireInspectorFields", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        Require(fields.Visibility == Visibility.Collapsed, "Mating exposed physical wire/AWG controls.");
        var sheet = Sheet(); sheet.Measure(new Size(sheet.Width, sheet.Height)); sheet.Arrange(new Rect(0, 0, sheet.Width, sheet.Height)); sheet.UpdateLayout();
        var caption = sheet.Children.OfType<TextBlock>().Single(t => t.Text.Contains("J1 接頭對接"));
        Rect Box(TextBlock t) => new(Canvas.GetLeft(t), Canvas.GetTop(t), t.DesiredSize.Width, t.DesiredSize.Height);
        Require(sheet.Children.OfType<TextBlock>().Where(t => t != caption).All(t => !Box(t).IntersectsWith(Box(caption))), "Mating arrow caption overlaps Pin labels or Port name.");
        Render(workspace, 1400, 900, Path.Combine(output, "mating-workspace.png"));
        var count = SchematicPdfWriter.Write(project, Path.Combine(output, "mating.pdf"), (p, page) =>
            (System.Windows.Media.Imaging.BitmapSource)Call("RenderOutputPage", p, page)!);
        Require(count == 2, "Cross-page mate unexpectedly added physical cable appendix pages.");
        var deleted = service.DeleteWire(project, project.Schematic.Wires[0].WireId);
        Require(deleted.Connections.Count == 0 && ProjectPinFunctionService.Pins(deleted).Single(p => p.PinId == "PLC.1").DisplayFunction is null, "Disconnect left stale inference.");
        Require(history.TryUndo(project, out var undo, out _) && undo.Connections.Count == 0, "Undo did not restore pre-mating snapshot.");
        File.WriteAllText(Path.Combine(output, "mating-proof.json"), JsonSerializer.Serialize(new { WholePortPicking = true, CrossPageSelection = true,
            Escape = true, ExactPinInference = true, DashedLink = true, NoAwg = true, Disconnect = true, Undo = true, PdfPages = count }));
        Console.WriteLine("MATING_UI_PDF_PASS"); return 0;
    }
}
