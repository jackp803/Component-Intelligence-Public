using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

internal static partial class Program
{
    private static int ReviewInteractionProof(string mode)
    {
        var current = new ElectricalProject { ProjectId = "synthetic-review", Name = "SYNTHETIC REVIEW",
            Schematic = new() { Pages = [new() { PageId = "main", Title = "Main" }], Wires = [
                new() { WireId = "h", PageId = "main", Awg = 30, Points = [new(30, 90), new(150, 90)] },
                new() { WireId = "v", PageId = "main", Awg = 30, Points = [new(90, 30), new(90, 150)] }] } };
        var workspace = new SchematicWorkspaceControl(() => current, (_, _) => throw new InvalidOperationException("Preview cannot commit."),
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var owner = new Window { Content = workspace };
        var type = typeof(SchematicWorkspaceControl);
        void Set(string name, object? value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, value);
        object? Get(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace);
        void Call(string name, params object?[] values) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(workspace, values);
        Set("_pageId", "main"); Call("Render", current);
        if (mode == "review-input")
        {
            Set("_gestureStart", current); Set("_dragStart", new SchematicPoint(60, 90)); Set("_dragSegment", 0); Set("_selectionId", "h");
            var operations = new HashSet<DispatcherOperation>(); var completed = 0;
            for (var i = 0; i < 100; i++)
            {
                var index = i;
                workspace.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    Set("_pointer", new SchematicPoint(60, 100 + index / 10d)); Call("QueuePointerPreview");
                    var operation = (DispatcherOperation)Get("_pendingPointerPreview")!;
                    if (operations.Add(operation)) operation.Completed += (_, _) => completed++;
                }));
            }
            var frame = new DispatcherFrame();
            workspace.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Require(completed == 1, $"100 queued Input updates produced {completed} expensive preview callbacks instead of one.");
            var expected = new SchematicAuthoringService().MoveSegment(current, "h", 0, new(60, 109.9));
            Require(Get("_gesturePreview") is ElectricalProject preview && preview.Schematic!.Wires.Single(w => w.WireId == "h").Points
                .SequenceEqual(expected.Schematic!.Wires.Single(w => w.WireId == "h").Points),
                "Input coalescing did not retain the final route position.");
            Call("CancelGesture");
            Console.WriteLine("PASS 100 queued Input updates coalesce into one preview and retain the latest route position.");
        }
        else
        {
            var zoom = (Slider)workspace.FindName("Zoom");
            foreach (var selected in new string?[] { null, "h" })
            {
                Set("_selectionId", selected); zoom.Value = 2; Call("Render", current);
                var sheet = (Canvas)type.GetProperty("Sheet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
                var nodes = sheet.Children.Cast<UIElement>().ToArray();
                var color = selected is null ? Color.FromRgb(51, 51, 51) : Colors.DarkCyan;
                var strokes = sheet.Children.OfType<Canvas>().SelectMany(c => c.Children.OfType<Shape>()).Concat(sheet.Children.OfType<Shape>())
                    .Where(s => s is Line or Polyline or System.Windows.Shapes.Path && s.Stroke is SolidColorBrush b && b.Color == color).ToArray();
                Require(strokes.Any(s => s is System.Windows.Shapes.Path), "Zoom fixture is missing crossing jump arcs.");
                zoom.Value = .25;
                Require(nodes.SequenceEqual(sheet.Children.Cast<UIElement>()), "Zoom rebuilt CAD/page nodes.");
                var expected = 1.5 / .25 + (selected is null ? 0 : 1);
                Require(strokes.All(s => Math.Abs(s.StrokeThickness - expected) < .000001),
                    "Zoom left wire body/jump strokes at stale thicknesses.");
            }
            current.Components.Add(new() { ComponentInstanceId = "negative", ComponentDefinitionId = "synthetic-negative", TypeKey = "Terminal",
                Ports = [new ComponentIntelligence.Electrical.Domain.ComponentPort { PortId = "negative-port", Name = "DC-",
                    Pins = [new() { PinId = "NEG", PinNumber = "1", Power = new() { Polarity = Polarity.Negative,
                        Voltage = new() { Type = VoltageType.Dc, NominalVoltage = 0 } } }] }] });
            current.Schematic!.Symbols.Add(new() { SymbolId = "negative-symbol", ComponentInstanceId = "negative", PageId = "main",
                Position = new(10, 190), Anchors = [new() { EndpointId = "NEG", Position = new(20, 10), Confirmed = true }] });
            current.Schematic.Wires.Add(new() { WireId = "negative-wire", PageId = "main", Awg = 30,
                Start = SchematicAttachment.Pin("negative-symbol", "NEG"), Points = [new(30, 200), new(150, 200)] });
            Set("_selectionId", null); zoom.Value = 2; Call("Render", current);
            var negativeSheet = (Canvas)type.GetProperty("Sheet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
            var outline = negativeSheet.Children.OfType<Canvas>().SelectMany(c => c.Children.OfType<Shape>())
                .Where(s => s.Stroke is SolidColorBrush b && b.Color == Colors.DimGray).ToArray();
            Require(outline.Length > 0, "DC-negative outline fixture did not render.");
            zoom.Value = .25;
            Require(outline.All(s => Math.Abs(s.StrokeThickness - 14) < .000001), "Zoom left DC-negative outlines at stale thicknesses.");
            Console.WriteLine("PASS zoom keeps CAD/page instances and updates unselected/selected wire bodies, jump arcs and DC-negative outlines.");
        }
        owner.Content = null; owner.Close(); return 0;
    }
}
