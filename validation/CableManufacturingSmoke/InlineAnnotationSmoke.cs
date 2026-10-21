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
using ComponentPort = ComponentIntelligence.Electrical.Domain.ComponentPort;
using ComponentPin = ComponentIntelligence.Electrical.Domain.ComponentPin;

internal static partial class Program
{
    private static int InlineAnnotationProof(string output)
    {
        Directory.CreateDirectory(output);
        var p = new ElectricalProject { ProjectId = "inline-annotations", Schematic = new() {
            Pages = [new() { PageId = "P", Title = "Inline annotations" }] } };
        var functions = new[] { "COM", "AC/DC 24V", "Sensor", "D-", "D+" };
        var colors = new[] { "black", "red", "white", "pink", "grey" };
        foreach (var id in new[] { "A", "B" })
        {
            var port = new ComponentPort { PortId = id + "-port", Name = "P1", Pins = Enumerable.Range(0, 5).Select(i =>
                new ComponentPin { PinId = id + i, PinNumber = (i + 1).ToString(), Function = functions[i], PinName = functions[i] + " / " + colors[i] }).ToList() };
            p.Components.Add(new() { ComponentInstanceId = id, ComponentDefinitionId = "synthetic", TypeKey = "fixture", ReferenceDesignator = id, Ports = [port] });
            p.Schematic.Symbols.Add(new() { SymbolId = id, ComponentInstanceId = id, PageId = "P", Position = new(id == "A" ? 30 : 300, 35),
                Width = 30, Height = 85, Anchors = port.Pins.Select((pin, i) => new SchematicAnchor { EndpointId = pin.PinId,
                    Position = new(id == "A" ? 30 : 0, (id == "A" ? 15 : 17) + 12 * i), Direction = id == "A" ? "Right" : "Left" }).ToList() });
        }
        for (var i = 0; i < 5; i++)
        {
            p.Connections.Add(new() { ConnectionId = "C" + i, FromEndpointId = "A" + i, ToEndpointId = "B" + i });
            p.Schematic.Wires.Add(new() { WireId = "W" + i, PageId = "P", ConnectionId = "C" + i, Designation = "Wire" + i,
                ManualDesignation = true, ManualSpecification = true, AreaMm2 = i < 2 ? .5 : .75,
                Start = SchematicAttachment.Pin("A", "A" + i), End = SchematicAttachment.Pin("B", "B" + i),
                Points = [new(60, 50 + 12 * i), new(180, 50 + 12 * i), new(180, 52 + 12 * i), new(300, 52 + 12 * i)] });
        }
        var type = typeof(SchematicWorkspaceControl);
        var workspace = new SchematicWorkspaceControl(() => p, (next, _) => p = next,
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        type.GetField("_pageId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, "P");
        void Call(string method, params object[] args) => type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(workspace, args);
        Canvas Sheet() => (Canvas)type.GetProperty("Sheet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        void Layout() { var sheet = Sheet(); sheet.Measure(new Size(sheet.Width, sheet.Height)); sheet.Arrange(new Rect(0, 0, sheet.Width, sheet.Height)); sheet.UpdateLayout(); }
        Rect Bounds(TextBlock label) => label.RenderTransform is RotateTransform { Angle: -90 }
            ? new(Canvas.GetLeft(label), Canvas.GetTop(label) - label.DesiredSize.Width, label.DesiredSize.Height, label.DesiredSize.Width)
            : new(Canvas.GetLeft(label), Canvas.GetTop(label), label.DesiredSize.Width, label.DesiredSize.Height);
        TextBlock[] Labels() => Sheet().Children.OfType<TextBlock>().Where(t => t.Tag is string tag && tag.StartsWith("wire-label:")).ToArray();
        var errors = new List<string>();
        void Check(bool condition, string message) { if (!condition) errors.Add(message); }
        void Clearance()
        {
            var labels = Labels();
            foreach (var label in labels)
            foreach (var edge in p.Schematic!.Wires.SelectMany(w => w.Points.Zip(w.Points.Skip(1))))
            {
                var line = new Rect(new Point(edge.First.X * 3, edge.First.Y * 3), new Point(edge.Second.X * 3, edge.Second.Y * 3)); line.Inflate(2, 2);
                Check(!line.IntersectsWith(Bounds(label)), "Caption overlaps a wire stroke.");
            }
            for (var i = 0; i < labels.Length; i++) for (var j = i + 1; j < labels.Length; j++)
                Check(!Bounds(labels[i]).IntersectsWith(Bounds(labels[j])), "Caption pieces overlap each other.");
        }
        void Save(string name)
        {
            var sheet = Sheet(); var image = new RenderTargetBitmap((int)sheet.Width, (int)sheet.Height, 96, 96, PixelFormats.Pbgra32); image.Render(sheet);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(output, name)); encoder.Save(file);
        }
        var before = JsonSerializer.Serialize(p);
        Call("Render", p); Layout();
        Check(Labels().All(t => t.Text.Contains("AWG") && !t.Text.Contains("mm²")), "Short on-wire AWG format was not applied.");
        Check(Labels().Sum(t => t.Text.Split("AWG").Length - 1) == 5, "Each conductor needs its compact gauge annotation.");
        Check(!Sheet().Children.OfType<TextBlock>().Any(t => Equals(t.Tag, "wire-spec-legend")), "Specifications are still in a remote legend.");
        for (var i = 0; i < 5; i++) Check(Labels().Any(t => Equals(t.Tag, "wire-label:W" + i) && t.Text.Contains(" → ") && t.ToolTip!.ToString()!.Contains("Wire" + i)), "Pin caption or retained full wire designation missing.");
        Check(!Labels().Any(t => t.Text.Contains("功能見明細")), "Caption still refers to details.");
        Clearance(); Save("inline-bundles.png");
        Check(before == JsonSerializer.Serialize(p), "Label rendering mutated engineering data.");

        p.Components[0].TypeKey = "Energy Valve / Control Valve";
        p.Components[0].ReferenceDesignator = "EnergyValveControlValve1";
        p.Components[0].Ports[0].Name = "CONTROL_LEADS";
        p.Schematic.WireNamingReferences["A"] = "EnergyValveControlValve1";
        for (var i = 0; i < p.Schematic.Wires.Count; i++)
        {
            p.Schematic.Wires[i] = p.Schematic.Wires[i] with { Designation = i == 0 ? "1P24" : "EnergyValveControlValve1_CONTROL_LEADS_" + (i + 1),
                ManualDesignation = i == 0 };
            p.Connections[i].ConductorAreaMm2 = p.Schematic.Wires[i].AreaMm2;
        }
        var connections = JsonSerializer.Serialize(p.Connections);
        Call("RefreshWireNames_Click", workspace, new RoutedEventArgs()); Layout();
        Check(p.Schematic!.Wires[0].Designation == "1P24", "UI name refresh overwrote a manual designation.");
        Check(p.Schematic.Wires.Skip(1).All(w => w.Designation!.StartsWith("Valve1_") && !w.Designation.Contains("CONTROL_LEADS")),
            "Legacy automatic source/Port names were not refreshed to short names.");
        Check(p.Schematic.Wires.Select(w => w.Designation).Distinct().Count() == 5, "Short UI names collided.");
        Check(connections == JsonSerializer.Serialize(p.Connections), "Name refresh changed electrical connection data.");
        Clearance(); Save("short-valve-wire-names.png");
        var pdfCount = SchematicPdfWriter.Write(p, Path.Combine(output, "short-inline-default.pdf"), (project, page) =>
            (BitmapSource)type.GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, [project, page])!);
        Check(pdfCount == 1, "Inline annotations still force a full ordinary-wire details appendix.");

        p = new() { ProjectId = "corner-caption", Schematic = new() { Pages = [new() { PageId = "P", Title = "Corners" }], Wires = [new()
        { WireId = "corner", PageId = "P", Designation = "CornerWire", ManualDesignation = true,
            Points = [new(40, 70), new(240, 70), new(240, 100), new(350, 100)] }] } };
        Call("Render", p);
        foreach (var item in Labels()) Sheet().Children.Remove(item);
        foreach (var y in new[] { 70 * 3 - 85d, 70 * 3 + 3d })
        {
            var reserved = new TextBlock { Text = "Existing drawing annotation", Width = 600, Height = 80 };
            Canvas.SetLeft(reserved, 40 * 3); Canvas.SetTop(reserved, y); Sheet().Children.Add(reserved);
        }
        var crossings = SchematicCrossingService.Analyze(p.Schematic.Wires);
        Call("PrepareWireLabels", p, p.Schematic.Pages[0], crossings); Call("RenderWireLabels", p, p.Schematic.Pages[0], crossings); Layout();
        Check(Labels().Any(t => t.Text.Contains("CornerWire")), "Clear shorter span of cornered route was not tried.");
        Check(Labels().Any(t => Bounds(t).Bottom >= 285 && Bounds(t).Left > 240 * 3), "Caption did not move to the clear second horizontal span.");
        Clearance(); Save("corner-span-fallback.png");
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors.Distinct()));
        File.WriteAllText(Path.Combine(output, "inline-annotations.json"), JsonSerializer.Serialize(new { Passed = true,
            ExactGroups = 2, AllSpansTried = true, LabelsClearOfStrokes = true, NoRemoteLegend = true, EngineeringSnapshotUnchanged = true,
            ShortNameUiRefresh = true, ManualNamesPreserved = true, ElectricalConnectionsPreserved = true }));
        Console.WriteLine("PASS inline color/functions, shared gauges, corner-span fallback and stroke clearance.");
        return 0;
    }
}
