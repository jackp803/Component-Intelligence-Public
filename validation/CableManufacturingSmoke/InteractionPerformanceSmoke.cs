using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using ComponentPort = ComponentIntelligence.Electrical.Domain.ComponentPort;

internal static partial class Program
{
    private static int InteractionPerformanceProof(string root, bool verify)
    {
        Directory.CreateDirectory(root);
        var template = new ArchivedCableTemplate { TemplateId = "synthetic-performance", TemplateRevision = "r1", AssetSha256 = new('a', 64),
            Ports = Enumerable.Range(1, 2).Select(i => new ComponentPort { PortId = "P" + i, Name = "P" + i,
                Pins = [new() { PinId = "p" + i + ".1", PinNumber = "1" }] }).ToList() };
        var geometry = new SchematicCadAsset { SourceSha256 = template.AssetSha256, MillimetresPerUnit = 1, Width = 100, Height = 20,
            Primitives = Enumerable.Range(0, 600).Select(i => new SchematicCadPrimitive { Kind = "LINE",
                Start = new(i % 100, i / 100d), End = new(i % 100 + .5, i / 100d + .1) }).ToArray() };
        var authoring = new SchematicAuthoringService();
        var current = authoring.AddArchivedCable(new() { ProjectId = "synthetic-performance", Name = "SYNTHETIC PERFORMANCE",
            Schematic = new() { Pages = [new() { PageId = "main", Title = "Main" }] } }, template, CableConstructionType.Custom,
            geometry, new Dictionary<string, string>(), "main", new(30, 50), manufacturingGeometry: geometry);
        var detail = new SchematicCableDetailBinding(current.Cables[0].CableInstanceId, null) { DetailId = "detail-main", TableVisible = false };
        current.Schematic!.Pages[0].InlineCableDetails.Add(detail);
        for (var i = 1; i < 8; i++) current.Schematic.Pages.Add(current.Schematic.Pages[0] with { PageId = "page-" + i,
            InlineCableDetails = [detail with { DetailId = "detail-" + i }] });
        var commits = 0;
        var workspace = new SchematicWorkspaceControl(() => current, (next, _) => { current = next; commits++; },
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var owner = new Window { Content = workspace };
        var type = typeof(SchematicWorkspaceControl);
        void Set(string name, object? value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, value);
        object? Get(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace);
        void Call(string name, params object?[] values) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(workspace, values);
        Set("_pageId", "main");
        Call("Render", current);
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++) Call("Render", current);
        watch.Stop(); var singlePageMs = watch.Elapsed.TotalMilliseconds / 5;
        var canvases = (Dictionary<string, Canvas>)Get("_pageCanvases")!;
        var hiddenNodes = canvases.Where(p => p.Key != "main").Sum(p => p.Value.Children.Count);
        var first = canvases["main"].Children[0];
        Call("Zoom_Changed", workspace, new RoutedPropertyChangedEventArgs<double>(1, 1.25));
        var zoomPreservesNodes = ReferenceEquals(first, canvases["main"].Children[0]);
        Set("_allPages", true); Call("Render", current);
        watch.Restart(); for (var i = 0; i < 5; i++) Call("Render", current);
        watch.Stop(); var allPagesMs = watch.Elapsed.TotalMilliseconds / 5;
        Require(canvases.Values.All(c => c.Children.Count > 0), "All-pages view failed to render a page.");
        if (verify)
        {
            Require(hiddenNodes == 0, "Single-page rendering rebuilt hidden pages.");
            Require(zoomPreservesNodes, "Zoom replaced the visual tree.");
            Set("_allPages", false); Set("_selectionId", "detail:Sketch:" + detail.DetailId);
            Set("_gestureStart", current); Set("_dragStart", new SchematicPoint(20, 110));
            Set("_dragDetailId", detail.DetailId); Set("_dragDetailPart", SchematicCableDetailPart.Sketch);
            for (var i = 1; i <= 100; i++) { Set("_pointer", new SchematicPoint(20 + i / 10d, 120)); Call("QueuePointerPreview"); }
            Require(Get("_gesturePreview") is null && commits == 0, "Pointer burst ran expensive previews synchronously.");
            Call("FlushPointerPreview");
            var preview = (ElectricalProject)Get("_gesturePreview")!;
            Require(preview.Schematic!.Pages[0].InlineCableDetails[0].SketchPosition == new SchematicPoint(30, 120), "Flush lost the latest pointer.");
            Set("_pointer", new SchematicPoint(32.5, 122.5)); Call("QueuePointerPreview");
            Call("Sheet_Up", canvases["main"], new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                System.Windows.Input.MouseButton.Left));
            Require(commits == 1 && Get("_pendingPointerPreview") is null &&
                current.Schematic!.Pages[0].InlineCableDetails[0].SketchPosition == new SchematicPoint(32.5, 122.5),
                "Release did not flush the final pointer, commit exactly once and clear pending work.");
            var saved = JsonSerializer.Serialize(current);
            Set("_gestureStart", current); Set("_dragStart", new SchematicPoint(32.5, 122.5));
            Set("_dragDetailId", detail.DetailId); Set("_dragDetailPart", SchematicCableDetailPart.Sketch);
            Set("_pointer", new SchematicPoint(35, 125)); Call("QueuePointerPreview"); Call("CancelGesture"); Call("FlushPointerPreview");
            Require(commits == 1 && saved == JsonSerializer.Serialize(current) && Get("_gesturePreview") is null,
                "Cancellation applied queued work.");
            var frame = new DispatcherFrame();
            workspace.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Require(commits == 1 && saved == JsonSerializer.Serialize(current) && Get("_gesturePreview") is null,
                "An aborted dispatcher callback ran after release/cancellation.");
            Set("_gestureStart", current); Set("_dragStart", new SchematicPoint(32.5, 122.5));
            Set("_dragDetailId", detail.DetailId); Set("_pointer", new SchematicPoint(35, 125)); Call("QueuePointerPreview");
            Call("Sheet_LostMouseCapture", canvases["main"], new RoutedEventArgs());
            Require(Get("_gestureStart") is null && Get("_pendingPointerPreview") is null && commits == 1,
                "Lost capture retained a pending mutation.");
            Set("_pageId", "page-1"); Call("Render", current);
            Require(canvases["page-1"].Visibility == Visibility.Visible && canvases["page-1"].Children.Count > 0,
                "Switching to a previously hidden page did not render it.");
        }
        File.WriteAllText(Path.Combine(root, "performance.json"), JsonSerializer.Serialize(new { Pages = 8, PrimitivesPerSketch = 600,
            Iterations = 5, SinglePageRenderMs = singlePageMs, AllPagesRenderMs = allPagesMs, HiddenPageNodes = hiddenNodes,
            ZoomPreservesVisualTree = zoomPreservesNodes, VerifiedInteraction = verify }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Render single={singlePageMs:F1} ms, all={allPagesMs:F1} ms; hidden nodes={hiddenNodes}; zoom retained={zoomPreservesNodes}; verified={verify}");
        owner.Content = null; owner.Close();
        return 0;
    }
}
