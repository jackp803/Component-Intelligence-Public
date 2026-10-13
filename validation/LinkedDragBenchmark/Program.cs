using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Repository;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try {
            SQLitePCL.Batteries_V2.Init();
            var root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
            foreach (var count in new[] { 0, 12, 60 }) Measure(root, count);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Measure(string root, int cadSymbols)
    {
        var p = new ElectricalProject { ProjectId = "benchmark-" + cadSymbols, Schematic = new() { Pages = [new() { PageId = "main", Title = "Benchmark", Width = 900, Height = 900 }] } };
        foreach (var id in new[] { "Valve", "IO" }) {
            var port = new ComponentIntelligence.Electrical.Domain.ComponentPort { PortId = id + "-P", Name = "P1", Pins = Enumerable.Range(1, 6).Select(i => new ComponentIntelligence.Electrical.Domain.ComponentPin {
                PinId = id + i, PinNumber = i.ToString(), Function = "Signal", PinName = "Signal / blue" }).ToList() };
            p.Components.Add(new() { ComponentInstanceId = id, ComponentDefinitionId = id, TypeKey = id, ReferenceDesignator = id + "1", Ports = [port] });
            p.Schematic.Symbols.Add(new() { SymbolId = id, ComponentInstanceId = id, PageId = "main", Width = 40, Height = 80, Position = new(id == "Valve" ? 25 : 250, 35),
                Anchors = port.Pins.Select((pin, i) => new SchematicAnchor { EndpointId = pin.PinId, Position = new(id == "Valve" ? 40 : 0, 12 + i * 10), Direction = id == "Valve" ? "Right" : "Left", Label = pin.PinNumber + " Signal" }).ToList() });
        }
        var geometry = new SchematicCadAsset { SourceSha256 = new('a', 64), MillimetresPerUnit = 1, Width = 25, Height = 12,
            Primitives = Enumerable.Range(0, 600).Select(i => new SchematicCadPrimitive { Kind = "LINE", Start = new(i % 25, i / 60d), End = new(i % 25 + .25, i / 60d + .1) }).ToArray() };
        for (var i = 0; i < cadSymbols; i++) {
            var id = "CAD" + i; p.Components.Add(new() { ComponentInstanceId = id, ComponentDefinitionId = id, TypeKey = "IO" });
            p.Schematic.Symbols.Add(new() { SymbolId = id, ComponentInstanceId = id, PageId = "main", Position = new(25 + i % 10 * 77, 200 + i / 10 * 75), Width = 25, Height = 12, Geometry = geometry });
        }
        var service = new SchematicAuthoringService();
        for (var i = 1; i <= 5; i++) p = service.DrawWire(p, "main", SchematicAttachment.Pin("Valve", "Valve" + i), SchematicAttachment.Pin("IO", "IO" + i), [new(65, 37 + i * 10), new(250, 37 + i * 10)]);
        var workspace = new SchematicWorkspaceControl(() => p, (next, _) => p = next, () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var owner = new Window { Content = workspace }; var type = workspace.GetType();
        void Set(string name, object? value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, value);
        object? Get(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace);
        void Call(string name, params object?[] values) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(workspace, values);
        Set("_pageId", "main"); Set("_selectionId", "Valve"); Call("Render", p);
        Set("_gestureStart", p); Set("_dragSymbol", "Valve"); Set("_dragStart", new SchematicPoint(25, 35));
        for (var i = 0; i < 15; i++) { Set("_pointer", new SchematicPoint(26 + i * .01, 36)); Call("ProcessPointerPreview"); }
        var initial = ((System.Collections.IEnumerable)Get("_dragWirePreviews")!).Cast<object>().ToArray();
        var allocation = GC.GetAllocatedBytesForCurrentThread(); var samples = new List<double>(); var watch = new Stopwatch();
        for (var i = 0; i < 1000; i++) { Set("_pointer", new SchematicPoint(26 + i % 100 * .01, 36)); watch.Restart(); Call("ProcessPointerPreview"); watch.Stop(); samples.Add(watch.Elapsed.TotalMilliseconds); }
        allocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
        var retained = initial.SequenceEqual(((System.Collections.IEnumerable)Get("_dragWirePreviews")!).Cast<object>());
        watch.Restart(); Call("FlushPointerPreview"); watch.Stop(); var commitCalculation = watch.Elapsed.TotalMilliseconds;
        Call("CancelGesture");
        var render = new List<double>();
        for (var i = 0; i < 7; i++) { watch.Restart(); Call("Render", p); watch.Stop(); render.Add(watch.Elapsed.TotalMilliseconds); }
        var selection = new List<double>();
        for (var i = 0; i < 20; i++) { Set("_selectionId", i % 2 == 0 ? "Valve" : "IO"); watch.Restart(); Call("UpdateSelection", p); watch.Stop(); selection.Add(watch.Elapsed.TotalMilliseconds); }
        watch.Restart(); new ElectricalProjectRepository(new SqliteConnectionFactory(), Path.Combine(root, "benchmark.db")).SaveAsync(p).GetAwaiter().GetResult(); watch.Stop(); var saveMs = watch.Elapsed.TotalMilliseconds;
        var sheet = (Canvas)type.GetProperty("Sheet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        File.WriteAllText(Path.Combine(root, "benchmark-" + cadSymbols + ".json"), JsonSerializer.Serialize(new { Version = typeof(SchematicWorkspaceControl).Assembly.Location, Symbols = 2 + cadSymbols, Wires = 5,
            CadPrimitives = 600 * cadSymbols, Preview = Stats(samples), AllocatedPreviewBytes = allocation, RetainedRouteVisuals = retained,
            FinalPointerCalculationMs = commitCalculation, Render = Stats(render), Selection = Stats(selection), SaveMs = saveMs,
            SheetVisualNodes = sheet.Children.Count, RenderingTier = RenderCapability.Tier >> 16, ManagedBytes = GC.GetTotalMemory(false), WorkingSetBytes = Process.GetCurrentProcess().WorkingSet64,
            NativeInputToPresentMeasured = false, Note = "Offscreen CPU processing; same synthetic visible content, warmup15, samples1000; no FPS assertion" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"CAD={cadSymbols}, preview p95={Stats(samples).P95Ms:0.000}ms, allocated={allocation}, retained={retained}, final={commitCalculation:0.0}ms");
        owner.Content = null; owner.Close();
    }
    private sealed record TimingStats(double MedianMs, double P95Ms, double WorstMs);
    private static TimingStats Stats(List<double> values) { values.Sort(); return new(values[values.Count / 2], values[Math.Min(values.Count - 1, (int)(values.Count * .95))], values[^1]); }
}
