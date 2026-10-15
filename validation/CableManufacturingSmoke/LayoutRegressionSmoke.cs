using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Electrical.Topology;
using ComponentIntelligence.Repository;

internal static partial class Program
{
    private static int LayoutRegressionProof(string root, string? only)
    {
        Directory.CreateDirectory(root);
        if (only is null or "labels") MatingCaptionDistanceProof(root);
        if (only is null or "reference") LegacyReferenceInspectorProof(root);
        if (only is null or "safety") MatingCaptionSafetyProof(root);
        File.WriteAllText(Path.Combine(root, "layout-regression.json"), JsonSerializer.Serialize(new { Passed = true, Scope = only ?? "all" }));
        Console.WriteLine("LAYOUT_REGRESSION_PASS"); return 0;
    }

    private static void MatingCaptionSafetyProof(string root)
    {
        foreach (var scenario in new[] { "paper-edge", "inline-artwork", "no-room" })
        {
            var page = new SchematicPage { PageId = "one", Title = scenario, Margin = 10 };
            var point = scenario == "paper-edge" ? new SchematicPoint(13, 110) : new SchematicPoint(150, 110);
            var project = new ElectricalProject { ProjectId = scenario, Schematic = new() { Pages = [page],
                Symbols = scenario == "paper-edge" ? [new() { SymbolId = "S", ComponentInstanceId = "C", PageId = "one", Position = new(20, 60), Width = 60, Height = 100 }] : [],
                Wires = [new() { WireId = "W", PageId = "one", Start = SchematicAttachment.Free(), End = SchematicAttachment.Marker("M"),
                    Points = [new(point.X + 7, point.Y), point] }] },
                Components = [new() { ComponentInstanceId = "C", ComponentDefinitionId = "C", TypeKey = "Connector" }] };
            var workspace = new SchematicWorkspaceControl(() => project, (_, _) => { }, () => Task.FromResult<IReadOnlyList<ComponentIR>>([]),
                _ => Task.FromResult<Uri?>(null), () => { }, () => { });
            var type = typeof(SchematicWorkspaceControl); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var canvas = new Canvas { Width = page.Width * 3, Height = page.Height * 3, Background = System.Windows.Media.Brushes.White };
            type.GetField("_renderCanvas", flags)!.SetValue(workspace, canvas);
            var caption = new TextBlock { Text = "J2 接頭對接 / 2.1-C", FontSize = 10 };
            var detail = new Rect((point.X - 40) * 3, (point.Y - 15) * 3, 40 * 3, 12 * 3);
            if (scenario == "inline-artwork")
                ((Dictionary<string, List<Rect>>)type.GetField("_renderCableDetailBounds", flags)!.GetValue(workspace)!).Add("one", [detail]);
            if (scenario == "no-room")
                ((Dictionary<string, List<Rect>>)type.GetField("_renderCableDetailBounds", flags)!.GetValue(workspace)!).Add("one", [new(0, 0, canvas.Width, canvas.Height)]);
            var placed = (bool)type.GetMethod("PositionMatingCaption", flags)!.Invoke(workspace, [project, page, new SchematicMarker { MarkerId = "M", PageId = "one", Position = point }, caption])!;
            if (scenario == "no-room") { Require(!placed && caption.Text == "J2 接頭對接 / 2.1-C", "No-room caption silently selected a rejected fallback or lost its text."); continue; }
            Require(placed, "Caption could not find existing paper-edge space.");
            var box = new Rect(Canvas.GetLeft(caption), Canvas.GetTop(caption), caption.DesiredSize.Width, caption.DesiredSize.Height);
            Require(caption.Visibility == Visibility.Visible && box.Left >= 30 && box.Top >= 30 && box.Right <= (page.Width - 10) * 3 && box.Bottom <= (page.Height - 10) * 3,
                "Caption clipped or disappeared beside paper edge: " + scenario + " " + box);
            Require(scenario != "inline-artwork" || !box.IntersectsWith(detail), "Caption covers inline manufacturing artwork.");
            Require(scenario != "paper-edge" || !box.IntersectsWith(new Rect(60, 180, 180, 300)), "Paper-edge caption covers the module.");
            canvas.Children.Add(caption);
            Render(canvas, (int)canvas.Width, (int)canvas.Height, Path.Combine(root, "caption-" + scenario + ".png"));
        }
    }

    private static void MatingCaptionDistanceProof(string root)
    {
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var project = new ElectricalProject { ProjectId = "caption-distance", Schematic = new() {
                Pages = [new() { PageId = "one", Title = "One" }, new() { PageId = "two", Title = "Two" }] } };
            foreach (var id in new[] { "A", "B" })
            {
                var port = new ComponentIntelligence.Electrical.Domain.ComponentPort { PortId = id + "P", SourcePortId = id + "P", Name = "P1", Connector = new() {
                    ConnectorId = id, Family = "M12", Coding = "A", PinCount = 1, Gender = id == "A" ? ConnectorGender.Male : ConnectorGender.Female },
                    Pins = [new() { PinId = id + "1", PinNumber = "1" }] };
                project.Components.Add(new() { ComponentInstanceId = id, ComponentDefinitionId = id, TypeKey = "Connector", Ports = [port] });
                project.Schematic!.Symbols.Add(new() { SymbolId = id, ComponentInstanceId = id, PageId = id == "A" ? "one" : "two",
                    Position = new(100, 60), Width = 120, Height = 140, Rotation = rotation,
                    CollapsedPortIds = [port.PortId], Anchors = [new() { EndpointId = id + "1", Position = new(id == "A" ? 120 : 0, 110), Direction = id == "A" ? "Right" : "Left" }],
                    CadPortBindings = [new(port.PortId, port.PortId, id, new(id == "A" ? 120 : 0, 110), id == "A" ? "Right" : "Left")],
                    Geometry = new() { SourceSha256 = new('a', 64), Width = 120, Height = 140,
                        ConnectionPoints = [new(id, "1", new(id == "A" ? 120 : 0, 110), Direction: id == "A" ? "Right" : "Left")],
                        Primitives = [new() { Kind = "LINE", Start = new(20, 90), End = new(100, 90) },
                            new() { Kind = "LINE", Start = new(100, 90), End = new(100, 130) },
                            new() { Kind = "LINE", Start = new(100, 130), End = new(20, 130) },
                            new() { Kind = "LINE", Start = new(20, 130), End = new(20, 90) }] } });
            }
            var service = new SchematicAuthoringService();
            project = service.MatePorts(project, SchematicAttachment.Port("A", "AP"), SchematicAttachment.Port("B", "BP"),
                ConnectorMatingService.Prepare(project, "AP", "BP"), "synthetic evidence", "J1 接頭對接");
            var workspace = new SchematicWorkspaceControl(() => project, (_, _) => { }, () => Task.FromResult<IReadOnlyList<ComponentIR>>([]),
                _ => Task.FromResult<Uri?>(null), () => { }, () => { });
            var type = typeof(SchematicWorkspaceControl);
            foreach (var page in project.Schematic!.Pages)
            {
                type.GetField("_pageId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, page.PageId);
                type.GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(workspace, [project]);
                var sheet = (Canvas)type.GetProperty("Sheet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
                sheet.Measure(new(sheet.Width, sheet.Height)); sheet.Arrange(new(0, 0, sheet.Width, sheet.Height));
                var caption = sheet.Children.OfType<TextBlock>().Single(t => t.Text.Contains("J1 接頭對接"));
                var marker = project.Schematic.Continuations.SelectMany(c => new[] { c.Source, c.Destination }).Single(m => m.PageId == page.PageId);
                var box = new Rect(Canvas.GetLeft(caption) / 3, Canvas.GetTop(caption) / 3, caption.DesiredSize.Width / 3, caption.DesiredSize.Height / 3);
                var dx = Math.Max(0, Math.Max(box.Left - marker.Position.X, marker.Position.X - box.Right));
                var dy = Math.Max(0, Math.Max(box.Top - marker.Position.Y, marker.Position.Y - box.Bottom));
                Require(Math.Sqrt(dx * dx + dy * dy) <= 12, $"Mating caption too far from its arrow: rotation={rotation}, page={page.PageId}, box={box}, marker={marker.Position}");
                Render(workspace, 1400, 900, Path.Combine(root, $"mating-distance-{rotation}-{page.PageId}.png"));
            }
            SchematicPdfWriter.Write(project, Path.Combine(root, $"mating-distance-{rotation}.pdf"), (p, page) =>
                (System.Windows.Media.Imaging.BitmapSource)type.GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, [p, page])!);
        }
    }

    private static void LegacyReferenceInspectorProof(string root)
    {
        SQLitePCL.Batteries_V2.Init();
        var project = new ElectricalProject { ProjectId = "legacy-reference", Schematic = new() {
            Pages = [new() { PageId = "one", Title = "Legacy CAD" }],
            Symbols = [new() { SymbolId = "cad", ComponentInstanceId = "C", PageId = "one", Position = new(0, 137.5), Width = 174, Height = 105,
                Geometry = new() { SourceSha256 = new('a', 64), MillimetresPerUnit = 1, Width = 174, Height = 105,
                    Primitives = [new() { Kind = "LINE", Start = new(20, 40), End = new(160, 75) }] } }] },
            Components = [new() { ComponentInstanceId = "C", ComponentDefinitionId = "C", TypeKey = "Connector", ReferenceDesignator = "Old" }] };
        var history = new ProjectMutationHistory(); var commits = 0;
        var workspace = new SchematicWorkspaceControl(() => project, (next, label) => {
            history.RecordBeforeMutation(project, label); project = next; commits++; },
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var type = typeof(SchematicWorkspaceControl); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        type.GetField("_pageId", flags)!.SetValue(workspace, "one"); type.GetField("_selectionId", flags)!.SetValue(workspace, "cad");
        type.GetMethod("Render", flags)!.Invoke(workspace, [project]);
        var before = JsonSerializer.Serialize(project); var symbolBefore = JsonSerializer.Serialize(project.Schematic!.Symbols[0]);
        ((TextBox)type.GetField("ReferenceText", flags)!.GetValue(workspace)!).Text = "Cable1";
        Require((bool)type.GetMethod("ApplyInspector", flags)!.Invoke(workspace, null)!, "Reference-only inspector edit was blocked by the old CAD frame.");
        Require(commits == 1 && project.Components[0].ReferenceDesignator == "Cable1", "Reference edit did not commit once.");
        Require(symbolBefore == JsonSerializer.Serialize(project.Schematic!.Symbols[0]), "Reference edit moved/resized CAD or altered its anchors.");
        Require(history.TryUndo(project, out var undone, out _) && before == JsonSerializer.Serialize(undone), "Reference undo did not restore the exact snapshot.");
        var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), Path.Combine(root, "reference-save.db"));
        var saved = repository.SaveSnapshotAsync(project).GetAwaiter().GetResult();
        var reloaded = repository.GetAsync(saved.ProjectId).GetAwaiter().GetResult();
        Require(reloaded?.Components[0].ReferenceDesignator == "Cable1", "Reference was lost after save/reload.");
        workspace.SaveOutputSnapshotAsync = () => Task.FromResult<ElectricalProject?>(saved);
        var snapshot = ((Task<ElectricalProject?>)type.GetMethod("PrepareOutputAsync", flags)!.Invoke(workspace, null)!).GetAwaiter().GetResult();
        Require(snapshot is not null, "Reference project could not prepare PDF output.");
        SchematicPdfWriter.Write(snapshot!, Path.Combine(root, "reference-after-edit.pdf"), (p, page) =>
            (System.Windows.Media.Imaging.BitmapSource)type.GetMethod("RenderOutputPage", flags)!.Invoke(workspace, [p, page])!);
        Render(workspace, 1400, 900, Path.Combine(root, "reference-after-edit.png"));
    }

}
