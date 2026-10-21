using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using Path = System.IO.Path;

internal static partial class Program
{
    public static int ProjectSearchProof(string root)
    {
        Directory.CreateDirectory(root);
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var project = SearchProofFixture();
        var original = JsonSerializer.Serialize(project);
        var commits = 0;
        var workspace = new SchematicWorkspaceControl(() => project, (_, _) => commits++,
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var host = new Window { Content = workspace, Width = 1280, Height = 840, Opacity = 0, ShowInTaskbar = false };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var type = typeof(SchematicWorkspaceControl);
        object? Get(string name) => type.GetField(name, flags)!.GetValue(workspace);
        void Set(string name, object? value) => type.GetField(name, flags)!.SetValue(workspace, value);
        object? Call(string name, params object?[] args)
        {
            var method = type.GetMethod(name, flags);
            Require(method is not null, "Missing project-search operation: " + name);
            try { return method!.Invoke(workspace, args); }
            catch (TargetInvocationException error) when (error.InnerException is not null) { throw error.InnerException; }
        }
        Canvas Sheet() => (Canvas)type.GetProperty("Sheet", flags)!.GetValue(workspace)!;
        Canvas[] Overlays() => ((Dictionary<string, Canvas>)Get("_pageCanvases")!).Values
            .SelectMany(c => c.Children.OfType<Canvas>()).Where(c => Equals(c.Tag, "ProjectSearchHighlights")).ToArray();
        void Drain()
        {
            workspace.UpdateLayout();
            var frame = new DispatcherFrame();
            workspace.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            workspace.UpdateLayout();
        }
        void Navigate(SchematicProjectSearchResult result)
        {
            Require(Call("NavigateSearchResult", result) is true, "A current search result was not navigable.");
            Drain();
        }
        void Centered(SchematicProjectSearchResult result)
        {
            var scroll = (ScrollViewer)workspace.FindName("PageScroll");
            var bounds = result.FocusBounds;
            var center = Sheet().TransformToAncestor(scroll).Transform(new Point((bounds.X + bounds.Width / 2) * 3,
                (bounds.Y + bounds.Height / 2) * 3));
            Require(Math.Abs(center.X - scroll.ViewportWidth / 2) < 4 && Math.Abs(center.Y - scroll.ViewportHeight / 2) < 4,
                $"Search target was not centered through sheet/scroll transforms: {center}; viewport {scroll.ViewportWidth} x {scroll.ViewportHeight}.");
        }
        host.Show();
        try
        {
            Set("_pageId", "first"); workspace.RefreshWorkspace(); Drain();
            var wire = SchematicProjectSearch.Find(project, "Sensor1_10").Single();
            Require(wire.PageNumber == 2 && wire.EndpointSymbolIds.SequenceEqual(new[] { "source-symbol", "target-symbol" }),
                "All-page search lost page or endpoint identities.");
            Set("_gestureStart", project); Set("_gesturePreview", project); Set("_dragSymbol", "source-symbol");
            Set("_dragStart", new SchematicPoint(90, 90)); Set("_wireMode", true);
            Set("_wireStart", SchematicAttachment.Free()); Set("_wirePoints", new List<SchematicPoint> { new(20, 20), new(80, 20) });
            Set("_panStart", new Point(10, 10));
            Navigate(wire);
            Require(Equals(Get("_pageId"), "second") && Equals(Get("_selectionId"), "search-wire"), "Search did not select its page/wire.");
            Require(((HashSet<string>)Get("_selectedWires")!).SetEquals(["search-wire"]), "Search selected wire state is inconsistent.");
            Require(Get("_gestureStart") is null && Get("_gesturePreview") is null && Get("_wireStart") is null &&
                Get("_panStart") is null && !Sheet().IsMouseCaptured, "Search retained a transient gesture or draft.");
            var overlay = Overlays().Single();
            Require(!overlay.IsHitTestVisible && overlay.Children.OfType<Shape>().All(s => !s.IsHitTestVisible), "Search overlay intercepts editing input.");
            Require(overlay.Children.OfType<Polyline>().Any(p => p.Points.Count >= 2 && Equals(p.Tag, "search-wire")), "Selected wire has no visible search stroke.");
            Require(overlay.Children.OfType<Rectangle>().Select(r => r.Tag).ToHashSet().SetEquals(new object[] { "source-symbol", "target-symbol" }),
                "Both endpoint components must be highlighted independently.");
            Centered(wire);
            void FitsViewport()
            {
                var viewport = (ScrollViewer)workspace.FindName("PageScroll");
                var focusLeft = Sheet().TransformToAncestor(viewport).Transform(new Point(wire.FocusBounds.X * 3, wire.FocusBounds.Y * 3));
                var focusRight = Sheet().TransformToAncestor(viewport).Transform(new Point(
                    (wire.FocusBounds.X + wire.FocusBounds.Width) * 3, (wire.FocusBounds.Y + wire.FocusBounds.Height) * 3));
                Require(focusLeft.X >= 0 && focusRight.X <= viewport.ViewportWidth,
                    "Search did not zoom out enough to show the wire and both endpoint owners.");
            }
            FitsViewport();
            var output = (BitmapSource)Call("RenderOutputPage", project, project.Schematic!.Pages[1])!;
            Require(Overlays().Length == 1, "Output rendering cleared the screen highlight.");
            Call("ClearSearchHighlights");
            var plain = (BitmapSource)Call("RenderOutputPage", project, project.Schematic.Pages[1])!;
            Require(SearchBitmapHash(output).SequenceEqual(SearchBitmapHash(plain)), "Search highlights leaked into output pixels.");
            Console.WriteLine("PASS all-page wire navigation cancels transients, selects the wire, centers it and highlights both endpoints; output pixels exclude highlights.");

            foreach (var allPages in new[] { false, true })
            foreach (var zoom in new[] { .65, 1.3 })
            {
                Set("_allPages", allPages); ((Slider)workspace.FindName("Zoom")).Value = zoom;
                workspace.RefreshWorkspace(); Navigate(wire); Centered(wire);
                FitsViewport();
            }
            Console.WriteLine("PASS transformed centering at two zoom levels in single-page and all-page layouts.");

            Call("SelectWire", "other-wire", false); Drain();
            Require(Overlays().Length == 0, "A new selection retained stale search highlights.");
            Navigate(wire); Call("ActivatePage", "first"); Drain();
            Require(Overlays().Length == 0, "Activating another page retained a cached search highlight.");
            Navigate(wire);
            project = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(project))!;
            project.Name = "Synthetic project replacement";
            workspace.RefreshWorkspace(); Drain();
            Require(Overlays().Length == 0, "Replacing the project retained stale highlights.");
            project.Name = "Synthetic search proof";
            Navigate(wire);
            project.Schematic!.Wires.RemoveAll(w => w.WireId == "search-wire");
            workspace.RefreshWorkspace(); Drain();
            Require(Overlays().Length == 0 && Call("NavigateSearchResult", wire) is false, "Deleted results remain navigable/highlighted.");
            Console.WriteLine("PASS highlights clear after selection/page/project/deletion changes and deleted results are rejected.");

            project = SearchProofFixture(); workspace.RefreshWorkspace();
            var row = SchematicProjectSearch.Find(project, "XW2").Single(); Navigate(row);
            Require(Equals(Get("_selectionId"), "row-2-symbol") && ((HashSet<string>)Get("_selectedWires")!).Count == 0,
                "Group row search merged row identity or retained a wire selection.");
            Require(Overlays().Single().Children.OfType<Rectangle>().Single().Tag is "row-2-symbol", "Search highlighted the entire group instead of the matching row.");
            var cable = SchematicProjectSearch.Find(project, "CB2").Single(); Navigate(cable);
            Require(Equals(Get("_selectionId"), "cable-2-symbol"), "Cable search lost the individual owner.");
            Console.WriteLine("PASS individual WAGO row and cable-owner navigation preserve distinct selection identities.");

            // Exercise the actual modal handler; all interaction is queued in this STA dispatcher.
            void Dialog(Action<Window> interact)
            {
                Exception? failure = null;
                workspace.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => {
                    var dialog = Application.Current.Windows.OfType<Window>().Single(w => w.GetType().Name == "SchematicProjectSearchDialog");
                    dialog.Opacity = 0; dialog.ShowInTaskbar = false;
                    try { interact(dialog); }
                    catch (Exception error) { failure = error; dialog.DialogResult = false; }
                }));
                Call("ProjectSearch_Click", workspace, new RoutedEventArgs());
                if (failure is not null) throw failure;
                Drain();
            }
            void PressKey(Window dialog, Key key)
            {
                var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog), 0, key)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                dialog.RaiseEvent(args); Require(args.Handled, "Modal search did not handle " + key);
            }
            var selection = Get("_selectionId");
            Dialog(dialog => {
                var query = (TextBox)dialog.FindName("ProjectSearchQuery");
                var grid = (DataGrid)dialog.FindName("ProjectSearchResults");
                var count = (TextBlock)dialog.FindName("ProjectSearchState");
                Require(grid.Items.Count == 0 && !string.IsNullOrWhiteSpace(count.Text), "Empty modal has no state.");
                query.Text = "["; Require(grid.Items.Count == 0 && !string.IsNullOrWhiteSpace(count.Text), "Unmatched literal query has no state.");
                PressKey(dialog, Key.Escape);
            });
            Require(Equals(Get("_selectionId"), selection), "Escape changed workspace selection.");
            Dialog(dialog => {
                ((TextBox)dialog.FindName("ProjectSearchQuery")).Text = " Sensor1_10 ";
                var grid = (DataGrid)dialog.FindName("ProjectSearchResults");
                Require(grid.Items.Count == 1 && grid.SelectedIndex == 0, "Modal query did not select the matching result.");
                dialog.UpdateLayout();
                var content = (FrameworkElement)dialog.Content;
                Render(content, content.ActualWidth + content.Margin.Left + content.Margin.Right,
                    content.ActualHeight + content.Margin.Top + content.Margin.Bottom, Path.Combine(root, "project-search-dialog.png"));
                PressKey(dialog, Key.Enter);
            });
            Require(Equals(Get("_selectionId"), "search-wire"), "Enter did not navigate from the modal.");
            Dialog(dialog => {
                ((TextBox)dialog.FindName("ProjectSearchQuery")).Text = "CB2";
                var grid = (DataGrid)dialog.FindName("ProjectSearchResults"); dialog.UpdateLayout();
                var item = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
                var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Control.MouseDoubleClickEvent, Source = item };
                grid.RaiseEvent(args); Require(args.Handled, "Result double-click was ignored.");
            });
            Require(Equals(Get("_selectionId"), "cable-2-symbol"), "Double-click did not navigate from the modal.");
            Console.WriteLine("PASS modal empty/no-match states, literal query, Enter/double-click navigation and Escape cancellation.");
            Navigate(SchematicProjectSearch.Find(project, "Sensor1_10").Single());
            Require(commits == 0 && original == JsonSerializer.Serialize(project), "Search/navigation mutated the synthetic project.");
            Render(workspace, 1280, 840, Path.Combine(root, "project-search-workspace.png"));
            var renderedPages = new List<string>();
            var pages = SchematicPdfWriter.Write(project, Path.Combine(root, "project-search.pdf"),
                (p, page) => { renderedPages.Add(page.PageId); return (BitmapSource)Call("RenderOutputPage", p, page)!; }, false);
            Require(renderedPages.Contains("first") && renderedPages.Contains("second") && pages == renderedPages.Count,
                "Synthetic search PDF lost a schematic page.");
            Require(original == JsonSerializer.Serialize(project), "Output rendering mutated the synthetic project.");
            Console.WriteLine($"PASS read-only search and navigation; synthetic PNG and {pages}-page PDF proof written to " + root);
        }
        finally { type.GetMethod("ClearSearchHighlights", flags)?.Invoke(workspace, null); host.Content = null; host.Close(); }
        return 0;
    }

    private static byte[] SearchBitmapHash(BitmapSource image)
    {
        var stride = image.PixelWidth * 4;
        var pixels = new byte[stride * 32];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var y = 0; y < image.PixelHeight; y += 32)
        {
            var rows = Math.Min(32, image.PixelHeight - y);
            image.CopyPixels(new Int32Rect(0, y, image.PixelWidth, rows), pixels, stride, 0);
            hash.AppendData(pixels, 0, rows * stride);
        }
        return hash.GetHashAndReset();
    }

    private static ElectricalProject SearchProofFixture()
    {
        var project = new ElectricalProject { ProjectId = "synthetic-project-search", Name = "Synthetic search proof",
            Schematic = new() { Pages = [new() { PageId = "first", Title = "Overview", Width = 900, Height = 650 },
                new() { PageId = "second", Title = "Field", Width = 900, Height = 650 }] } };
        foreach (var end in new[] { "source", "target" })
        {
            project.Components.Add(new() { ComponentInstanceId = end, ComponentDefinitionId = end + "-definition", TypeKey = "Sensor",
                ReferenceDesignator = end == "source" ? "Sensor1" : "PLC1", DisplayName = "Synthetic " + end,
                Ports = [new() { PortId = end + "-port", Name = "P1", Pins = [new() { PinId = end + "-pin", PinNumber = "10", Function = "24V" }] }] });
            project.Schematic.Symbols.Add(new() { SymbolId = end + "-symbol", ComponentInstanceId = end, PageId = "second",
                Position = new(end == "source" ? 380 : 560, 300), ManualSize = true,
                Anchors = [new() { EndpointId = end + "-pin", Position = new(end == "source" ? 40 : 0, 10),
                    Direction = end == "source" ? "Right" : "Left", Confirmed = true }] });
        }
        project.Schematic.Wires.Add(new() { WireId = "search-wire", PageId = "second", Designation = "Sensor1_10",
            Start = SchematicAttachment.Pin("source-symbol", "source-pin"), End = SchematicAttachment.Pin("target-symbol", "target-pin"),
            Points = [new(420, 310), new(560, 310)] });
        project.Schematic.Wires.Add(new() { WireId = "other-wire", PageId = "second", Designation = "Other_1", Points = [new(400, 400), new(560, 400)] });
        foreach (var i in new[] { 1, 2 })
        {
            project.Components.Add(new() { ComponentInstanceId = "row-" + i, ComponentDefinitionId = "wago", TypeKey = "Terminal",
                DisplayName = "WAGO 221-2411", ReferenceDesignator = "XW" + i, Ports = [new() { PortId = "row-port-" + i, Name = "P1",
                    Pins = [new() { PinId = "row-pin-" + i + "-1", PinNumber = "1" }, new() { PinId = "row-pin-" + i + "-2", PinNumber = "2" }] }] });
            project.Schematic.Symbols.Add(new() { SymbolId = "row-" + i + "-symbol", ComponentInstanceId = "row-" + i,
                WagoVisualGroupId = "synthetic-group", PageId = "first", Position = new(400, 300 + (i - 1) * 18),
                ManualSize = true, Width = 24, Height = 18, Anchors = [new() { EndpointId = "row-pin-" + i + "-1", Direction = "Left", Position = new(0, 9), Confirmed = true },
                    new() { EndpointId = "row-pin-" + i + "-2", Position = new(24, 9), Confirmed = true }] });
            project.Cables.Add(new() { CableInstanceId = "cable-" + i, CableDefinitionId = "synthetic-template", ReferenceDesignator = "CB" + i,
                DisplayName = "Synthetic harness", ArchivedCable = new() { Template = new() { TemplateId = "synthetic-template", TemplateRevision = "r1", AssetSha256 = new('0', 64),
                    Ports = [new() { PortId = "template-port", Name = "Cable end", Pins = [new() { PinId = "template-pin", PinNumber = "1" }] }] },
                    Ports = [new() { PortId = "cable-port-" + i, SourcePortId = "template-port", Name = "Cable end",
                        Pins = [new() { PinId = "cable-pin-" + i, SourcePinId = "template-pin", PinNumber = "1" }] }] } });
            project.Schematic.Symbols.Add(new() { SymbolId = "cable-" + i + "-symbol", CableInstanceId = "cable-" + i, PageId = "first",
                Position = new(450 + i * 50, 350), ManualSize = true,
                Geometry = new() { SourceSha256 = new('0', 64), Width = 40, Height = 30,
                    Primitives = [new() { Kind = "LINE", Start = new(0, 0), End = new(40, 30) }] },
                Anchors = [new() { EndpointId = "cable-pin-" + i, SourcePortId = "template-port", SourcePinId = "template-pin",
                    Position = new(0, 15), Direction = "Left", Confirmed = true }] });
        }
        return project;
    }
}

#if PROJECT_SEARCH_STANDALONE
// Allows this proof to run before the coordinator adds the main dispatcher hook.
internal static class ProjectSearchSmokeEntry
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { return Program.ProjectSearchProof(System.IO.Path.GetFullPath(args[0])); }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
#endif
