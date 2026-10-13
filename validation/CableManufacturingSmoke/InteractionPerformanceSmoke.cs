using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using ComponentIntelligence.Electrical.Editing;
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
        var beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++) Call("Render", current);
        watch.Stop(); var singlePageMs = watch.Elapsed.TotalMilliseconds / 5;
        var renderAllocation = (GC.GetAllocatedBytesForCurrentThread() - beforeAllocation) / 5;
        var canvases = (Dictionary<string, Canvas>)Get("_pageCanvases")!;
        var hiddenNodes = canvases.Where(p => p.Key != "main").Sum(p => p.Value.Children.Count);
        var largestCadChildren = canvases["main"].Children.OfType<Canvas>().Select(c => c.Children.Count).DefaultIfEmpty().Max();
        var first = canvases["main"].Children[0];
        double symbolPreviewMs = 0, wirePreviewMs = 0;
        if (verify)
        {
            var symbol = current.Schematic!.Symbols[0];
            var saved = JsonSerializer.Serialize(current);
            Set("_gestureStart", current); Set("_dragStart", new SchematicPoint(30, 50)); Set("_dragSymbol", symbol.SymbolId);
            Set("_pointer", new SchematicPoint(40, 60)); Call("ProcessPointerPreview");
            Require(Get("_gesturePreview") is null && ReferenceEquals(first, canvases["main"].Children[0]),
                "Symbol drag still clones the project and rebuilds the sheet on every pointer update.");
            Require(saved == JsonSerializer.Serialize(current), "Visual drag mutated the live project before release.");
            watch.Restart();
            for (var i = 0; i < 100; i++)
            { Set("_pointer", new SchematicPoint(40 + i / 100d, 60)); Call("ProcessPointerPreview"); }
            watch.Stop(); symbolPreviewMs = watch.Elapsed.TotalMilliseconds / 100;
            Set("_pointer", new SchematicPoint(40, 60));
            Set("_lastPreviewPointer", new SchematicPoint(40, 60)); Call("FlushPointerPreview");
            var moved = (ElectricalProject)Get("_gesturePreview")!;
            Require(moved.Schematic!.Symbols[0].Position == new SchematicPoint(40, 60), "Release failed to calculate the final symbol position.");
            Call("CancelGesture"); Call("Render", current);
            first = canvases["main"].Children[0];
            Set("_wireMode", true); Set("_wireStart", SchematicAttachment.Free());
            Set("_wirePoints", new List<SchematicPoint> { new(60, 80) }); Set("_pointer", new SchematicPoint(70, 90));
            Call("ProcessPointerPreview");
            Require(ReferenceEquals(first, canvases["main"].Children[0]), "Wire preview rebuilt unrelated CAD/frame objects.");
            watch.Restart();
            for (var i = 0; i < 100; i++)
            { Set("_pointer", new SchematicPoint(70 + i / 100d, 90)); Call("ProcessPointerPreview"); }
            watch.Stop(); wirePreviewMs = watch.Elapsed.TotalMilliseconds / 100;
            Call("ClearPendingWire"); Set("_wireMode", false); Call("Render", current);
            first = canvases["main"].Children[0];
        }
        Call("Zoom_Changed", workspace, new RoutedPropertyChangedEventArgs<double>(1, 1.25));
        var zoomPreservesNodes = ReferenceEquals(first, canvases["main"].Children[0]);
        Set("_allPages", true); Call("Render", current);
        watch.Restart(); for (var i = 0; i < 5; i++) Call("Render", current);
        watch.Stop(); var allPagesMs = watch.Elapsed.TotalMilliseconds / 5;
        Require(canvases.Values.All(c => c.Children.Count > 0), "All-pages view failed to render a page.");
        if (verify)
        {
            Require(largestCadChildren < 40, "CAD linework still allocates hundreds of individual visual controls per drawing.");
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
            Set("_gestureStart", current); Set("_dragStart", new SchematicPoint(32.5, 122.5));
            Set("_dragDetailId", detail.DetailId); Set("_pointer", new SchematicPoint(35, 125)); Call("QueuePointerPreview");
            Call("ActivatePage", "page-1"); Call("FlushPointerPreview"); Call("Render", current);
            Require(Get("_gestureStart") is null && Get("_pendingPointerPreview") is null && commits == 1,
                "Page switch retained a pending mutation.");
            Require(canvases["page-1"].Visibility == Visibility.Visible && canvases["page-1"].Children.Count > 0,
                "Switching to a previously hidden page did not render it.");
        }
        var history = new ProjectMutationHistory();
        for (var i = 0; i < 50; i++) history.RecordBeforeMutation(current, "synthetic drag");
        var rawHistoryBytes = (long)JsonSerializer.Serialize(current).Length * 2 * 50;
        File.WriteAllText(Path.Combine(root, "performance.json"), JsonSerializer.Serialize(new { Pages = 8, PrimitivesPerSketch = 600,
            Iterations = 5, SinglePageRenderMs = singlePageMs, AllPagesRenderMs = allPagesMs, HiddenPageNodes = hiddenNodes,
            LargestCadChildren = largestCadChildren, RenderAllocationBytes = renderAllocation,
            RenderingTier = RenderCapability.Tier >> 16, UndoCompressedBytes = history.RetainedBytes, UndoRawUtf16Bytes = rawHistoryBytes,
            ZoomPreservesVisualTree = zoomPreservesNodes, SymbolPointerPreviewMs = symbolPreviewMs, WirePointerPreviewMs = wirePreviewMs,
            VerifiedInteraction = verify }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Render single={singlePageMs:F1} ms, all={allPagesMs:F1} ms; hidden nodes={hiddenNodes}; zoom retained={zoomPreservesNodes}; verified={verify}");
        owner.Content = null; owner.Close();
        return 0;
    }
}
