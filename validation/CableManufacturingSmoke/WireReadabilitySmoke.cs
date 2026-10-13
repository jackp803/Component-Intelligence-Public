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
    private static int WireReadabilityProof(string output)
    {
        Directory.CreateDirectory(output);
        var p = new ElectricalProject { ProjectId = "synthetic-readability", Name = "Wire readability",
            Schematic = new() { Pages = [new() { PageId = "main", Title = "Main" }] } };
        var functions = new[] { "COM", "V24", "Sensor", "D-", "D+", "Feedback" };
        var colors = new[] { "black", "red", "white", "pink", "grey", "orange" };
        foreach (var side in new[] { "Valve", "IO" })
        {
            var port = new ComponentIntelligence.Electrical.Domain.ComponentPort { PortId = side + "-port", Name = "P1" };
            for (var i = 0; i < functions.Length; i++) port.Pins.Add(new() { PinId = side + i, PinNumber = (i + 1).ToString(),
                Function = functions[i], PinName = side == "Valve" ? functions[i] + " / " + colors[i] : functions[i],
                Power = i < 2 ? new() { Role = side == "IO" ? PowerRole.Input : PowerRole.Source,
                    RequiredCurrentAmp = side == "IO" ? .2 : null, Voltage = new() { Type = VoltageType.Dc, NominalVoltage = 24 } } : null });
            p.Components.Add(new() { ComponentInstanceId = side, ComponentDefinitionId = side, TypeKey = side,
                ReferenceDesignator = side + "1", Ports = [port] });
            p.Schematic.Symbols.Add(new() { SymbolId = side, ComponentInstanceId = side, PageId = "main", Width = 40, Height = 95,
                Position = new(side == "Valve" ? 30 : 280, 40), Anchors = port.Pins.Select((pin, i) => new SchematicAnchor
                { EndpointId = pin.PinId, Position = new(side == "Valve" ? 40 : 0, 15 + i * 12), Direction = side == "Valve" ? "Right" : "Left",
                    Label = "P1 / " + pin.PinNumber + " " + pin.PinName }).ToList() });
        }
        var service = new SchematicAuthoringService();
        for (var i = 0; i < 5; i++) p = service.DrawWire(p, "main", SchematicAttachment.Pin("Valve", "Valve" + i),
            SchematicAttachment.Pin("IO", "IO" + i), [new(70, 55 + i * 12), new(280, 55 + i * 12)]);
        var workspace = new SchematicWorkspaceControl(() => p, (next, _) => p = next,
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var owner = new Window { Content = workspace };
        var type = typeof(SchematicWorkspaceControl);
        type.GetField("_pageId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, "main");
        var render = type.GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
        render.Invoke(workspace, [p]);
        var sheet = (Canvas)type.GetProperty("Sheet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        sheet.Measure(new Size(sheet.Width, sheet.Height)); sheet.Arrange(new Rect(0, 0, sheet.Width, sheet.Height)); sheet.UpdateLayout();
        Require(!sheet.Children.OfType<TextBlock>().Any(t => t.Text == "P1 / 1 COM / black"), "Connected pin retained its duplicate full label.");
        Require(sheet.Children.OfType<TextBlock>().Any(t => t.Text == "P1 / 6 Feedback / orange"), "Unconnected pin lost its full function label.");
        var labels = sheet.Children.OfType<TextBlock>().Where(t => t.Tag is string tag && tag.StartsWith("wire-label:")).ToArray();
        Require(labels.Length == 5, "Every connected wire needs exactly one combined label.");
        var boxes = labels.Select(t => new Rect(Canvas.GetLeft(t), Canvas.GetTop(t), t.DesiredSize.Width, t.DesiredSize.Height)).ToArray();
        foreach (var label in labels)
        {
            var wire = p.Schematic!.Wires.Single(w => "wire-label:" + w.WireId == (string)label.Tag);
            var box = new Rect(Canvas.GetLeft(label), Canvas.GetTop(label), label.DesiredSize.Width, label.DesiredSize.Height);
            var y = wire.Points[0].Y * 3;
            Require(Math.Min(Math.Abs(box.Bottom - y), Math.Abs(box.Top - y)) <= 10,
                "A label was moved away from its own wire despite sufficient adjacent space.");
        }
        for (var i = 0; i < boxes.Length; i++) for (var j = i + 1; j < boxes.Length; j++)
            Require(!boxes[i].IntersectsWith(boxes[j]), "Combined wire labels overlap.");
        foreach (var box in boxes)
        foreach (var edge in p.Schematic!.Wires.SelectMany(w => w.Points.Zip(w.Points.Skip(1))))
        {
            var lineBounds = new Rect(new Point(edge.First.X * 3, edge.First.Y * 3), new Point(edge.Second.X * 3, edge.Second.Y * 3));
            lineBounds.Inflate(1, 1);
            Require(!box.IntersectsWith(lineBounds), "Text covers an actual wire, including its own wire.");
        }
        Require(labels[0].Text.Contains("COM") && labels[1].Text.Contains("V24"), "Function labels were dropped.");
        var zoom = (Slider)type.GetField("Zoom", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        zoom.Value = .25;
        sheet.Measure(new Size(sheet.Width, sheet.Height)); sheet.Arrange(new Rect(0, 0, sheet.Width, sheet.Height)); sheet.UpdateLayout();
        foreach (var label in sheet.Children.OfType<TextBlock>().Where(t => t.Tag is string tag && tag.StartsWith("wire-label:")))
        foreach (var wire in p.Schematic!.Wires)
        foreach (var edge in wire.Points.Zip(wire.Points.Skip(1)))
        {
            var spec = SchematicWirePresentation.Resolve(p, wire);
            var weight = Math.Max(1.5 / .25, SchematicWirePresentation.StrokeWidthMm(spec.Awg) * 3) +
                (SchematicWireEvidence.Resolve(p, wire).NeedsOutline ? 2 / .25 : 0);
            var lineBounds = new Rect(new Point(edge.First.X * 3, edge.First.Y * 3), new Point(edge.Second.X * 3, edge.Second.Y * 3));
            lineBounds.Inflate(weight / 2, weight / 2);
            Require(!lineBounds.IntersectsWith(new Rect(Canvas.GetLeft(label), Canvas.GetTop(label), label.DesiredSize.Width, label.DesiredSize.Height)),
                "Zoom-out allows text to obscure a white/yellow wire outline.");
        }
        zoom.Value = 1;
        render.Invoke(workspace, [p]);
        var baseline = p;
        var white = p.Schematic!.Wires[2];
        var whiteLabel = sheet.Children.OfType<TextBlock>().Single(t => Equals(t.Tag, "wire-label:" + white.WireId));
        p = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(p))!;
        p.Schematic!.Wires.Clear();
        p.Schematic.Wires.AddRange([white with { LabelPosition = new(175, white.Points[0].Y - (whiteLabel.DesiredSize.Height / 2 + 10) / 3) },
            new() { WireId = "outline-arc-crossing", PageId = "main", Start = SchematicAttachment.Free(), End = SchematicAttachment.Free(),
                Points = [new(175, white.Points[0].Y - 1), new(175, white.Points[0].Y + 40)] }]);
        render.Invoke(workspace, [p]); zoom.Value = .25;
        sheet.Measure(new Size(sheet.Width, sheet.Height)); sheet.Arrange(new Rect(0, 0, sheet.Width, sheet.Height)); sheet.UpdateLayout();
        var arcLabel = sheet.Children.OfType<TextBlock>().Single(t => Equals(t.Tag, "wire-label:" + white.WireId));
        var arcBox = new Rect(175 * 3 - 11.5, white.Points[0].Y * 3 - 11.5, 23, 11.5);
        Require(!arcBox.IntersectsWith(new Rect(Canvas.GetLeft(arcLabel), Canvas.GetTop(arcLabel), arcLabel.DesiredSize.Width, arcLabel.DesiredSize.Height)),
            "A saved caption obscures an outlined crossover arc at 25 percent zoom.");
        p = baseline; zoom.Value = 1; render.Invoke(workspace, [p]);
        var bitmap = new RenderTargetBitmap((int)sheet.Width, (int)sheet.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(sheet);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(output, "wire-readability.png"))) encoder.Save(stream);
        var count = SchematicPdfWriter.Write(p, Path.Combine(output, "wire-readability.pdf"), (project, page) =>
            (BitmapSource)type.GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, [project, page])!);
        var first = p.Schematic!.Wires[0];
        p = service.DeleteWire(p, first.WireId); render.Invoke(workspace, [p]);
        Require(sheet.Children.OfType<TextBlock>().Any(t => t.Text == "P1 / 1 COM / black"), "Deleting the wire failed to restore separate COM label.");
        var pin = sheet.Children.OfType<System.Windows.Shapes.Ellipse>().First(t => t.ToolTip?.ToString()?.StartsWith("P1 / 6 Feedback") == true);
        Require(pin.ContextMenu?.Items.Count == 5, "Virtual Pin is missing move/sort controls.");
        var functionsBefore = JsonSerializer.Serialize(p.Components);
        ((MenuItem)pin.ContextMenu!.Items[4]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Require(functionsBefore == JsonSerializer.Serialize(p.Components), "Pin sorting changed electrical pin metadata.");
        var sorted = SchematicAuthoringService.PortPinsInVisualOrder(p.Schematic!.Symbols[0], p.Components[0].Ports[0]);
        Require(sorted.First().EndpointId == "Valve5", "Descending Pin order menu was not applied.");
        var a = SchematicAuthoringService.AnchorPoint(p.Schematic.Symbols[0], "Valve5");
        var b = SchematicAuthoringService.AnchorPoint(p.Schematic.Symbols[1], "IO5");
        var at = type.GetMethod("WireAt", BindingFlags.Instance | BindingFlags.NonPublic)!;
        at.Invoke(workspace, [SchematicAttachment.Pin("Valve", "Valve5"), a, false]);
        at.Invoke(workspace, [SchematicAttachment.Free(), new SchematicPoint(80, 145), false]);
        at.Invoke(workspace, [SchematicAttachment.Free(), new SchematicPoint(250, 145), false]);
        at.Invoke(workspace, [SchematicAttachment.Pin("IO", "IO5"), b, false]);
        Require(p.Schematic!.Wires.Last().ManualRoute && p.Schematic.Wires.Last().Points.Any(point => point.Y == 145),
            "Explicitly clicked wire bends were replaced by automatic routing.");
        void Set(string name, object? value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, value);
        void Call(string name, params object?[] values) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(workspace, values);
        var savedProject = JsonSerializer.Serialize(p);
        var beforeNodes = sheet.Children.Cast<UIElement>().ToArray();
        Set("_gestureStart", p); Set("_dragStart", new SchematicPoint(30, 40)); Set("_dragSymbol", "Valve");
        Set("_pointer", new SchematicPoint(35, 45)); Call("ProcessPointerPreview");
        Require(savedProject == JsonSerializer.Serialize(p), "Incident-wire drag preview mutated electrical data.");
        Require(beforeNodes.OfType<FrameworkElement>().Any(e => e.RenderTransform is TransformGroup), "Block visual failed to translate.");
        Require(beforeNodes.OfType<TextBlock>().Any(e => e.Tag is string tag && tag.StartsWith("wire-label:") && e.Visibility == Visibility.Collapsed),
            "Incident wire retained its stale caption during drag.");
        Call("CancelGesture");
        Require(beforeNodes.All(n => n.Visibility == Visibility.Visible) && beforeNodes.SequenceEqual(sheet.Children.Cast<UIElement>()),
            "Drag cancellation left hidden wire strokes or temporary geometry.");
        var parent = p.Schematic!.Wires[0];
        p.Schematic.Wires.Add(new() { WireId = "synthetic-branch", PageId = "main", Start = SchematicAttachment.Junction(parent.WireId),
            End = SchematicAttachment.Free(), Points = [new(150, parent.Points[0].Y), new(150, 25)], ManualRoute = true });
        render.Invoke(workspace, [p]);
        var crossing = SchematicCrossingService.Analyze(p.Schematic.Wires);
        Require(crossing.Junctions.Count > 0 && sheet.Children.OfType<System.Windows.Shapes.Ellipse>().Count(e => e.Width == 5 && Equals(e.Fill, Brushes.Black)) == crossing.Junctions.Count,
            "Junctions were duplicated once for each wire.");
        p.Schematic.Symbols.Add(p.Schematic.Symbols[0] with { SymbolId = "space-blocker", Position = new(0, 0), Width = 420, Height = 297 });
        render.Invoke(workspace, [p]);
        Require(sheet.Children.OfType<TextBlock>().Any(t => Equals(t.Tag, "wire-label-warning") && t.Text.Contains("V24")),
            "Unplaceable wire captions disappeared without visible complete functions.");
        Call("Apply", new Func<ElectricalProject, ElectricalProject>(project => project), "synthetic identity edit");
        var status = (TextBlock)type.GetField("Status", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        Require(status.Text.Contains("標籤空間不足"), "Successful edit overwrote the unplaced-caption warning.");
        var circleAsset = new SchematicCadAsset { Width = 30, Height = 30, SourceSha256 = "synthetic",
            Primitives = Enumerable.Range(0, 48).Select(_ => new SchematicCadPrimitive { Kind = "CIRCLE", Start = new(15, 15), Radius = 4 }).ToArray() };
        var cad = type.GetMethod("CadCanvas", BindingFlags.Static | BindingFlags.NonPublic)!;
        var batched = (Canvas)cad.Invoke(null, [circleAsset, null, 1d, null, false])!;
        var original = (Canvas)cad.Invoke(null, [circleAsset, Brushes.Black, 1d, null, false])!;
        foreach (var canvas in new[] { batched, original }) { canvas.Measure(new Size(90, 90)); canvas.Arrange(new Rect(0, 0, 90, 90)); canvas.UpdateLayout(); }
        Require(VisualTreeHelper.GetDescendantBounds(batched) == VisualTreeHelper.GetDescendantBounds(original), "Batched circle changed its original ink bounds.");
        File.WriteAllText(Path.Combine(output, "wire-readability.json"), JsonSerializer.Serialize(new { Passed = true, CombinedLabels = labels.Length,
            FunctionsPreserved = true, LabelsDoNotOverlap = true, ZoomOutOutlineClearance = true, ManualUiRoute = true, PinOrderUi = true,
            IncidentWireDragCancel = true, JunctionsNotDuplicated = true, UnplacedCaptionsVisible = true, CadCircleBoundsUnchanged = true,
            DisconnectRestoresLabels = true, PdfPages = count }));
        Console.WriteLine("PASS combined functions/colors, separate unconnected labels, non-overlap, delete restoration, PDF.");
        return 0;
    }
}
