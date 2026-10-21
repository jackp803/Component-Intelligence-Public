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
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Runtime;
using ComponentIntelligence.Repository;

internal static partial class Program
{
    private static int WorkspaceUsabilityProof(string root)
    {
        Directory.CreateDirectory(root);
        var p = new ElectricalProject { ProjectId = "workspace-smoke", Name = "Personal workspace", Schematic = new() { Pages = [new() { PageId = "main", Title = "Main" }] } };
        foreach (var id in new[] { "Valve", "IO" }) {
            var port = new ComponentIntelligence.Electrical.Domain.ComponentPort { PortId = id + "-port", Name = "P1", Pins = Enumerable.Range(1, 6).Select(i => new ComponentIntelligence.Electrical.Domain.ComponentPin {
                PinId = id + i, PinNumber = i.ToString(), Function = i == 1 ? "COM" : i == 2 ? "V24" : "Signal",
                PinName = (i == 1 ? "COM / black" : i == 2 ? "V24 / red" : "Signal / blue") }).ToList() };
            p.Components.Add(new() { ComponentInstanceId = id, ComponentDefinitionId = id, TypeKey = id, ReferenceDesignator = id + "1", Ports = [port] });
            p.Schematic.Symbols.Add(new() { SymbolId = id, ComponentInstanceId = id, PageId = "main", Width = 40, Height = 80, Position = new(id == "Valve" ? 25 : 250, 35),
                Anchors = port.Pins.Select((pin, i) => new SchematicAnchor { EndpointId = pin.PinId, Position = new(id == "Valve" ? 40 : 0, 12 + i * 10), Direction = id == "Valve" ? "Right" : "Left", Label = pin.PinNumber + " " + pin.PinName }).ToList() });
        }
        var service = new SchematicAuthoringService();
        for (var i = 1; i <= 5; i++) p = service.DrawWire(p, "main", SchematicAttachment.Pin("Valve", "Valve" + i), SchematicAttachment.Pin("IO", "IO" + i), [new(65, 37 + i * 10), new(250, 37 + i * 10)]);
        var history = new ProjectMutationHistory(); var commits = 0;
        var prefs = new WorkspacePreferencesStore(Path.Combine(root, "preferences.json"));
        var workspace = new SchematicWorkspaceControl(() => p, (next, label) => { history.RecordBeforeMutation(p, label); p = next; commits++; },
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { }, preferences: prefs);
        var owner = new Window { Content = workspace }; var type = typeof(SchematicWorkspaceControl);
        void Set(string name, object? value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, value);
        object? Get(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace);
        object? Call(string name, params object?[] values) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(workspace, values);
        var fields = (Dictionary<string, Control>)Get("_inspectorFields")!;
        var reference = (TextBox)Get("ReferenceText")!;
        Set("_pageId", "main"); Set("_selectionId", "Valve"); Call("Render", p);
        var before = JsonSerializer.Serialize(p);
        Require((bool)Call("ApplyInspector")! && commits == 0, "Viewing unchanged settings created an undo entry.");
        reference.Text = "Valve-custom"; ((TextBox)fields["x"]).Text = "45"; ((TextBox)fields["width"]).Text = "65";
        Call("UpdateSelection", p);
        Require(reference.Text == "Valve-custom", "Refresh erased a focused inspector draft.");
        Require((bool)Call("ApplyInspector")! && commits == 1 && p.Schematic!.Symbols[0].Position.X == 45, "Composite inspector edit failed.");
        Require(history.TryUndo(p, out var undo, out _) && JsonSerializer.Serialize(undo) == before, "One undo did not restore the whole composite edit.");
        p = undo; Call("UpdateInspector", p, true); Call("Render", p);
        ((TextBox)fields["x"]).Text = "NaN";
        Require(!(bool)Call("ApplyInspector")! && commits == 1 && JsonSerializer.Serialize(p) == before, "Invalid input changed the model/history.");
        Set("_selectionId", "IO"); Call("UpdateSelection", p);
        Require((string?)Get("_selectionId") == "Valve", "An invalid draft was silently transferred to another object.");
        Call("DiscardInspector"); Set("_selectionId", "IO"); Call("UpdateSelection", p);
        Require(reference.Text == "IO1", "New selection retained previous Reference.");
        Set("_selectionId", p.Schematic!.Wires[0].WireId); Call("Render", p);
        var wireBefore = JsonSerializer.Serialize(p);
        Require((bool)Call("ApplyInspector")! && wireBefore == JsonSerializer.Serialize(p), "Wire inspector recomputed sizing on view.");
        ((TextBox)fields["designation"]).Text = "Valve1-control"; ((ComboBox)fields["sizeMode"]).SelectedIndex = 3; ((TextBox)fields["area"]).Text = "0.75";
        Require((bool)Call("ApplyInspector")! && p.Schematic!.Wires[0].ManualSpecification && p.Schematic.Wires[0].AreaMm2 == .75, "Wire specification authority not preserved.");
        var endpoints = ((TextBlock)Get("_wireEndpoints")!).Text;
        Require(endpoints.Contains("起點") && endpoints.Contains("COM") && endpoints.Contains("black") && endpoints.Contains("終點"), "Endpoint provenance absent.");
        Set("_selectionId", "Valve"); Call("Render", p);
        foreach (var scale in new[] { 1d, 1.25, 1.5 }) foreach (var size in new[] { (1280, 800), (1920, 1080) }) {
            var width = size.Item1 / scale - 32; var height = size.Item2 / scale - 150;
            Render(workspace, (int)width, (int)height, Path.Combine(root, $"workspace-{size.Item1}-{scale:0.##}.png"));
            var actions = (Grid)Get("InspectorActions")!;
            Require(actions.ActualWidth > 180 && actions.ActualHeight > 20 && actions.TranslatePoint(new(), workspace).Y + actions.ActualHeight <= height,
                "Apply/Cancel is clipped at the simulated logical viewport.");
        }
        Call("ToggleLeftPanel_Click", workspace, new RoutedEventArgs());
        Require(((Grid)Get("LeftPanel")!).Visibility == Visibility.Collapsed, "Left panel did not collapse.");
        Render(workspace, 1248, 650, Path.Combine(root, "workspace-collapsed.png"));
        Call("ResetPanels_Click", workspace, new RoutedEventArgs());
        Require(((Grid)Get("LeftPanel")!).Visibility == Visibility.Visible && ((ColumnDefinition)Get("LeftColumn")!).Width.Value == 240, "Default layout failed to restore.");
        // Warm a linked-wire gesture, then check retained identities and separately time each preview.
        var modelBeforeDrag = JsonSerializer.Serialize(p);
        Set("_gestureStart", p); Set("_dragSymbol", "Valve"); Set("_dragStart", new SchematicPoint(25, 35)); Set("_pointer", new SchematicPoint(26, 36)); Call("ProcessPointerPreview");
        var retainedPaths = ((System.Collections.IEnumerable)Get("_dragWirePreviews")!).Cast<object>().ToArray();
        Require(retainedPaths.Length >= 5, "Linked wire preview fixture did not exercise incident wires.");
        var samples = new List<double>(); var allocation = GC.GetAllocatedBytesForCurrentThread(); var watch = new Stopwatch();
        for (var i = 0; i < 250; i++) { Set("_pointer", new SchematicPoint(26 + i * .01, 36)); watch.Restart(); Call("ProcessPointerPreview"); watch.Stop(); samples.Add(watch.Elapsed.TotalMilliseconds); }
        allocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
        var afterPaths = ((System.Collections.IEnumerable)Get("_dragWirePreviews")!).Cast<object>().ToArray();
        Require(retainedPaths.SequenceEqual(afterPaths) && modelBeforeDrag == JsonSerializer.Serialize(p), "Pointer movement replaced route visuals or changed persisted geometry.");
        var activeSheet = (Canvas)type.GetProperty("Sheet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        var firstVisual = activeSheet.Children[0];
        Call("FlushPointerPreview");
        Require(Get("_gesturePreview") is not null && ReferenceEquals(firstVisual, activeSheet.Children[0]), "Final calculation redundantly rebuilt the sheet before commit.");
        Set("_gesturePreview", null); Call("Render", p); Call("FlushPointerPreview");
        Require(Get("_gesturePreview") is ElectricalProject released && released.Schematic!.Symbols[0].Position.X > p.Schematic!.Symbols[0].Position.X,
            "Redraw followed by release at unchanged pointer lost the drag.");
        Set("_pointer", new SchematicPoint(29, 36)); Call("ProcessPointerPreview");
        var rebound = ((System.Collections.IEnumerable)Get("_dragWirePreviews")!).Cast<System.Windows.Shapes.Polyline>().ToArray();
        Require(rebound.Length >= 5 && rebound.All(path => path.Parent is Canvas) && !retainedPaths.SequenceEqual(rebound), "Redraw did not rebind retained gesture visuals.");
        Call("CancelGesture");
        Require(!((System.Collections.IEnumerable)Get("_dragWirePreviews")!).Cast<object>().Any() && modelBeforeDrag == JsonSerializer.Serialize(p), "Canceled drag retained ghosts or changed data.");
        Set("_wireMode", true); Set("_wireStart", SchematicAttachment.Free()); Set("_wirePoints", new List<SchematicPoint> { new(150, 140) }); Set("_pointer", new SchematicPoint(175, 150)); Call("ProcessPointerPreview");
        var pendingVisual = Get("_wirePreviewElement"); Set("_pointer", new SchematicPoint(185, 150)); Call("ProcessPointerPreview");
        Require(ReferenceEquals(pendingVisual, Get("_wirePreviewElement")), "Pending wire recreated its visual on every pointer.");
        Call("ClearPendingWire"); Set("_wireMode", false);
        Set("_selectionId", null); Call("Render", p);
        Require(((StackPanel)Get("ModuleInspectorPanel")!).Visibility == Visibility.Collapsed, "Empty selection exposes old module fields.");
        var geometry = new SchematicCadAsset { SourceSha256 = new('a', 64), MillimetresPerUnit = 1, Width = 50, Height = 10, Primitives = [new() { Kind = "LINE", Start = new(0, 5), End = new(50, 5) }] };
        var template = new ArchivedCableTemplate { TemplateId = "workspace-cable", TemplateRevision = "r1", AssetSha256 = geometry.SourceSha256,
            Ports = [new() { PortId = "P1", Name = "P1", Pins = [new() { PinId = "p1.1", PinNumber = "1" }] }, new() { PortId = "P2", Name = "P2", Pins = [new() { PinId = "p2.1", PinNumber = "1" }] }] };
        p = service.AddArchivedCable(p, template, CableConstructionType.Custom, geometry, new Dictionary<string, string>(), "main", new(50, 210), manufacturingGeometry: geometry);
        var cadId = p.Schematic!.Symbols.Last().SymbolId;
        p = service.ScaleCadSymbol(p, cadId, 1.234564); Set("_selectionId", cadId); Call("Render", p);
        var cadWidth = p.Schematic!.Symbols.Last().Width;
        reference.Text = "Cable-reference-only";
        Require((bool)Call("ApplyInspector")! && p.Schematic!.Symbols.Last().Width == cadWidth, "Reference-only CAD edit changed its exact scale.");
        var cableId = p.Cables[0].CableInstanceId;
        var binding = new SchematicCableDetailBinding(cableId, null) { DetailId = "inspector-detail", TablePosition = new(20, 150), SketchPosition = new(220, 150), TableWidth = 130, SketchWidth = 100, SketchHeight = 35 };
        p.Schematic!.Pages[0].InlineCableDetails.Add(binding);
        Set("_selectionId", "detail:Table:" + binding.DetailId); Call("Render", p);
        Require(((StackPanel)Get("DetailInspectorFields")!).Visibility == Visibility.Visible, "Table inspector absent.");
        var physical = JsonSerializer.Serialize(p.Cables); ((TextBox)fields["tw"]).Text = "150";
        Require((bool)Call("ApplyInspector")! && JsonSerializer.Serialize(p.Cables) == physical && p.Schematic.Pages[0].InlineCableDetails[0].TableWidth == 150, "Table layout changed physical cable authority.");
        ((TextBox)fields["tx"]).Text = "1000";
        Require(!(bool)Call("ApplyInspector")! && JsonSerializer.Serialize(p.Cables) == physical, "Out-of-paper table input was accepted.");
        Call("DiscardInspector"); Set("_selectionId", "detail:Sketch:" + binding.DetailId); Call("Render", p);
        Render(workspace, 1248, 650, Path.Combine(root, "workspace-detail.png"));
        p = new SchematicCableDetailService().RemovePart(p, "main", binding.DetailId, SchematicCableDetailPart.Table);
        Require(JsonSerializer.Serialize(p.Cables) == physical && p.Schematic.Pages[0].InlineCableDetails[0].SketchVisible, "Deleting table representation deleted cable/sketch.");
        var flush = workspace.FlushPreferencesAsync();
        while (!flush.IsCompleted) { var frame = new System.Windows.Threading.DispatcherFrame(); workspace.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false)); System.Windows.Threading.Dispatcher.PushFrame(frame); Thread.Sleep(1); }
        flush.GetAwaiter().GetResult();
        Require(!prefs.Load().LeftCollapsed, "Panel preferences were not written before closing.");
        WorkspaceSessionRaceProof(root);
        WorkspaceBackgroundImportProof(root);
        samples.Sort();
        var process = Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(root, "workspace-proof.json"), JsonSerializer.Serialize(new { Verified = true, Modules = 2, VisibleWires = 5, PreviewSamples = samples.Count,
            MedianPreviewMs = samples[samples.Count / 2], P95PreviewMs = samples[(int)(samples.Count * .95)], WorstPreviewMs = samples[^1], PreviewAllocatedBytes = allocation,
            WorkingSetBytes = process.WorkingSet64, ManagedBytes = GC.GetTotalMemory(false), RenderTier = RenderCapability.Tier >> 16,
            Viewports = "1280x800 / 1920x1080, simulated logical space at 100/125/150%; no OS DPI changes", NativeInputToPresentMeasured = false }, new JsonSerializerOptions { WriteIndented = true }));
        owner.Content = null; owner.Close();
        Console.WriteLine($"Workspace verified; linked drag p95={samples[(int)(samples.Count * .95)]:F3}ms, worst={samples[^1]:F3}ms.");
        return 0;
    }

    private static void WorkspaceSessionRaceProof(string root)
    {
        SQLitePCL.Batteries_V2.Init();
        var entered = new TaskCompletionSource<ElectricalProject>(); var release = new TaskCompletionSource();
        var db = Path.Combine(root, "session-test.db");
        var window = new ElectricalWorkspaceWindow(db, preferences: new WorkspacePreferencesStore(Path.Combine(root, "session-preferences.json")),
            persistProjectSnapshot: async snapshot => { entered.TrySetResult(snapshot); await release.Task; return await new ElectricalProjectRepository(new SqliteConnectionFactory(), db).SaveSnapshotAsync(snapshot); });
        var type = typeof(ElectricalWorkspaceWindow);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(window.Dispatcher));
        object? Get(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
        object? Call(string name, params object?[] values) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(window, values);
        T Await<T>(Task<T> task) { while (!task.IsCompleted) { var frame = new System.Windows.Threading.DispatcherFrame(); window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false)); System.Windows.Threading.Dispatcher.PushFrame(frame); Thread.Sleep(1); } return task.GetAwaiter().GetResult(); }
        var save = (Task<bool>)Call("SaveProjectAsync")!;
        var captured = Await(entered.Task); var project = (ElectricalProject)Get("_project")!;
        project.Name = "newer edit while saving";
        ((TextBox)Get("ProjectNameText")!).Text = project.Name;
        var duplicate = (Task<bool>)Call("SaveProjectAsync")!;
        Require(!Await(duplicate), "Duplicate save was not guarded.");
        release.SetResult(); Require(Await(save), "Controlled save failed.");
        Require(((ElectricalProject)Get("_project")!).Name == "newer edit while saving" && captured.Name != project.Name, "Old save replaced a newer edit.");
        Require((bool)type.GetProperty("Unsaved", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!, "Newer edit falsely marked saved.");
        var saved = new ElectricalProjectRepository(new SqliteConnectionFactory(), db).GetAsync(project.ProjectId).GetAwaiter().GetResult();
        Require(saved!.Name == captured.Name, "Persisted snapshot was mutated while awaiting I/O.");
        var repo = new ProjectRevisionRepository(new SqliteConnectionFactory(), db);
        void AwaitTask(Task task) { while (!task.IsCompleted) { var frame = new System.Windows.Threading.DispatcherFrame(); window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false)); System.Windows.Threading.Dispatcher.PushFrame(frame); Thread.Sleep(1); } task.GetAwaiter().GetResult(); }
        var idleFrame = new System.Windows.Threading.DispatcherFrame(); window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => idleFrame.Continue = false)); System.Windows.Threading.Dispatcher.PushFrame(idleFrame);
        type.GetField("_lastCompletedEdit", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, DateTimeOffset.UtcNow.AddSeconds(-119));
        AwaitTask((Task)Call("CaptureIdleRecoveryAsync")!);
        Require(repo.ListRecoveryAsync(project.ProjectId).GetAwaiter().GetResult().Count == 0, "Recovery captured before two minutes idle.");
        type.GetField("_lastCompletedEdit", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, DateTimeOffset.UtcNow.AddSeconds(-121));
        AwaitTask((Task)Call("CaptureIdleRecoveryAsync")!); AwaitTask((Task)Call("CaptureIdleRecoveryAsync")!);
        Require(repo.ListRecoveryAsync(project.ProjectId).GetAwaiter().GetResult().Count == 1, "Idle recovery missing or duplicated unchanged state.");
        Require(new ElectricalProjectRepository(new SqliteConnectionFactory(), db).GetAsync(project.ProjectId).GetAwaiter().GetResult()!.Name == captured.Name,
            "Recovery overwrote the formal project row.");
        type.GetField("_allowClose", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true); window.Close();
        SynchronizationContext.SetSynchronizationContext(previousContext);
    }

    private static void WorkspaceBackgroundImportProof(string root)
    {
        var entered = new TaskCompletionSource(); var lookupRelease = new TaskCompletionSource();
        var window = new ElectricalWorkspaceWindow(Path.Combine(root, "background-import.db"),
            preferences: new WorkspacePreferencesStore(Path.Combine(root, "background-preferences.json")),
            resolveWorkingBomComponent: async (_, _) => { entered.TrySetResult(); await lookupRelease.Task; return null; });
        var type = typeof(ElectricalWorkspaceWindow);
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(window.Dispatcher));
        object? Get(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
        void Set(string name, object? value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
        object? Call(string name, params object?[] args) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(window, args);
        void Await(Task task) {
            while (!task.IsCompleted) {
                var frame = new System.Windows.Threading.DispatcherFrame();
                window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame); Thread.Sleep(1);
            }
            task.GetAwaiter().GetResult();
        }
        var rows = new BomRow[] { new() { RowId = "1", Manufacturer = "Synthetic", ModelOrPartNumber = "Sensor", UsedQuantity = 1 } };
        window.SynchronizeWorkingBomOnLoad(rows);
        var first = (Task)Call("SynchronizeWorkingBomAsync", (object)rows)!; Await(entered.Task);
        var project = (ElectricalProject)Get("_project")!;
        project.Name = "Edit during BOM lookup"; ((TextBox)Get("ProjectNameText")!).Text = project.Name;
        lookupRelease.SetResult(); Await(first);
        Require(((ElectricalProject)Get("_project")!).BomItems.Count == 0 && ((Button)Get("RetryWorkingBomButton")!).Visibility == Visibility.Visible,
            "Stale initial BOM replaced current content or offered no retry.");
        Await((Task)Call("SynchronizeWorkingBomAsync", (object)rows)!);
        project = (ElectricalProject)Get("_project")!;
        Require(project.Components.Count == 1 && project.Name == "Edit during BOM lookup" && ((Button)Get("RetryWorkingBomButton")!).Visibility == Visibility.Collapsed,
            "Retry did not merge against the latest project.");
        var symbol = new SchematicSymbol { SymbolId = "held-drag", ComponentInstanceId = project.Components[0].ComponentInstanceId,
            PageId = project.Schematic!.Pages[0].PageId, Position = new(30, 30), Width = 20, Height = 25 };
        project.Schematic.Symbols.Add(symbol);
        var schematic = new SchematicWorkspaceControl(() => (ElectricalProject)Get("_project")!, (next, _) => Set("_project", next),
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        Set("_schematicWorkspace", schematic); ((System.Windows.Controls.TabItem)Get("SchematicTab")!).Content = schematic;
        var schematicType = typeof(SchematicWorkspaceControl);
        void GestureSet(string name, object? value) => schematicType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(schematic, value);
        object? GestureCall(string name, params object?[] args) => schematicType.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(schematic, args);
        entered = new(); lookupRelease = new();
        var extraRows = rows.Append(new BomRow { RowId = "2", Manufacturer = "Synthetic", ModelOrPartNumber = "Other", UsedQuantity = 1 }).ToArray();
        var heldRetry = (Task)Call("SynchronizeWorkingBomAsync", (object)extraRows)!; Await(entered.Task);
        GestureSet("_pageId", symbol.PageId); GestureSet("_selectionId", symbol.SymbolId); GestureCall("Render", project);
        GestureSet("_gestureStart", project); GestureSet("_dragStart", new SchematicPoint(30, 30)); GestureSet("_pointer", new SchematicPoint(45, 40));
        GestureSet("_dragSymbol", symbol.SymbolId); GestureSet("_dragActivated", true); GestureCall("ProcessPointerPreview");
        var duringDrag = JsonSerializer.Serialize(Get("_project"));
        lookupRelease.SetResult(); Await(heldRetry);
        Require(JsonSerializer.Serialize(Get("_project")) == duringDrag && ((Button)Get("RetryWorkingBomButton")!).Visibility == Visibility.Visible,
            "BOM replaced a gesture base while the mouse was still down.");
        GestureCall("Sheet_Up", schematicType.GetProperty("Sheet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(schematic),
            new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left));
        Await((Task)Call("SynchronizeWorkingBomAsync", (object)extraRows)!);
        project = (ElectricalProject)Get("_project")!;
        Require(project.Components.Count == 2 && project.Schematic!.Symbols[0].Position == new SchematicPoint(45, 40), "Retry after mouse-up lost BOM or final module position.");
        Await((Task)Call("SaveProjectAsync")!);
        entered = new(); lookupRelease = new();
        var closingRows = extraRows.Append(new BomRow { RowId = "3", Manufacturer = "Synthetic", ModelOrPartNumber = "Late", UsedQuantity = 1 }).ToArray();
        var late = (Task)Call("SynchronizeWorkingBomAsync", (object)closingRows)!; Await(entered.Task);
        var beforeClose = JsonSerializer.Serialize(Get("_project"));
        var closeRelease = new TaskCompletionSource(); Set("_queuedCheckpoints", closeRelease.Task);
        window.Close();
        Require((bool)Get("_leavingSession")! && !window.IsEnabled, "Accepted close did not invalidate background imports before awaiting writes.");
        lookupRelease.SetResult(); Await(late);
        Require(JsonSerializer.Serialize(Get("_project")) == beforeClose, "Background BOM mutated the project after close was accepted.");
        closeRelease.SetResult();
        while (!(bool)Get("_allowClose")!) {
            var frame = new System.Windows.Threading.DispatcherFrame();
            window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame); Thread.Sleep(1);
        }
        SynchronizationContext.SetSynchronizationContext(previous);
    }
}
