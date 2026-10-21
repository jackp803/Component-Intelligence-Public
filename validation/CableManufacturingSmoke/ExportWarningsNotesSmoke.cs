using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using PdfSharp.Pdf.IO;

internal static partial class Program
{
    private static int ExportWarningsNotesProof(string root)
    {
        Directory.CreateDirectory(root);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var type = typeof(SchematicWorkspaceControl);
        var failures = new List<string>();
        var checks = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Proof(string name, Action action)
        {
            try { action(); checks.Add(name); Console.WriteLine("PASS " + name); }
            catch (Exception error) { failures.Add(name + ": " + error.GetBaseException().Message); }
        }
        object? Call(SchematicWorkspaceControl workspace, string name, params object?[] args)
        {
            try { return type.GetMethod(name, flags)!.Invoke(workspace, args); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        SchematicWorkspaceControl Workspace(ElectricalProject project) => new(() => project, (_, _) => { },
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        BitmapSource Page(SchematicWorkspaceControl workspace, ElectricalProject project, SchematicPage page) =>
            (BitmapSource)Call(workspace, "RenderOutputPage", project, page)!;
        TextBlock Status(SchematicWorkspaceControl workspace) => (TextBlock)type.GetField("Status", flags)!.GetValue(workspace)!;
        void Png(BitmapSource bitmap, string name)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(root, name)); encoder.Save(stream);
        }
        int? Export(SchematicWorkspaceControl workspace, string name)
        {
            var task = (Task<int?>)Call(workspace, "ExportPdfAsync", Path.Combine(root, name), false)!;
            return task.GetAwaiter().GetResult();
        }
        void Throws<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException(message);
        }
        void NoPartial(string name)
        {
            Check(!File.Exists(Path.Combine(root, name)), "Failed export published a target: " + name);
            Check(!Directory.EnumerateFiles(root, ".schematic-*.pdf").Any(), "Failed export left a temporary PDF.");
        }
        var crowded = new ElectricalProject { ProjectId = "synthetic-export-warning", Schematic = new() {
            Pages = [new() { PageId = "crowded-1", Title = "Crowded sensor page" }, new() { PageId = "crowded-2", Title = "Second sensor page" },
                new() { PageId = "clear", Title = "Clear page" }] } };
        foreach (var page in crowded.Schematic.Pages.Take(2))
        {
            var componentId = "blocker-" + page.PageId;
            crowded.Components.Add(new() { ComponentInstanceId = componentId, ComponentDefinitionId = "synthetic", TypeKey = "fixture",
                ReferenceDesignator = componentId });
            crowded.Schematic.Symbols.Add(new() { SymbolId = componentId, ComponentInstanceId = componentId,
                PageId = page.PageId, Position = new(0, 0), Width = 420, Height = 297 });
            crowded.Schematic.Wires.Add(new() { WireId = "wire-" + page.PageId, PageId = page.PageId,
                Designation = page.PageId == "crowded-1" ? "Sensor1_11" : "Sensor2_22", ManualDesignation = true,
                Points = [new(30, 80), new(200, 80)] });
        }
        var warningWorkspace = Workspace(crowded);
        var warningBefore = JsonSerializer.Serialize(crowded);
        Proof("nonfatal warning PDF and recoverable page/wire names", () =>
        {
            type.GetField("_pageId", flags)!.SetValue(warningWorkspace, "crowded-1");
            Call(warningWorkspace, "Render", crowded);
            Check(Status(warningWorkspace).Text.Contains("標示空間不足"), "Ordinary screen warning disappeared.");
            var screenWarning = Status(warningWorkspace).Text;
            var count = SchematicPdfWriter.Write(crowded, Path.Combine(root, "warnings.pdf"),
                (p, page) => { var bitmap = Page(warningWorkspace, p, page); if (page.PageId == "crowded-1") Png(bitmap, "warning-sheet.png"); return bitmap; });
            using var pdf = PdfReader.Open(Path.Combine(root, "warnings.pdf"), PdfDocumentOpenMode.Import);
            Check(count == 3 && pdf.PageCount == 3, "Warning PDF added a wire-detail appendix or lost source pages.");
            Check(Status(warningWorkspace).Text == screenWarning, "Output rendering overwrote the ordinary screen warning.");
            var warnings = type.GetProperty("OutputLabelWarnings")?.GetValue(warningWorkspace);
            Check(warnings is not null, "Output warnings are not recoverable after PDF completion.");
            var json = JsonSerializer.Serialize(warnings, warnings!.GetType());
            File.WriteAllText(Path.Combine(root, "warnings.json"), json);
            foreach (var value in new[] { "crowded-1", "crowded-2", "wire-crowded-1", "wire-crowded-2", "Sensor1_11", "Sensor2_22" })
                Check(json.Contains(value), "Export warning omitted affected page or wire: " + value);
            Check(warningBefore == JsonSerializer.Serialize(crowded), "Warning export changed source JSON.");
        });
        Proof("shared export success summary and warning lifecycle", () =>
        {
            warningWorkspace.SaveOutputSnapshotAsync = () => Task.FromResult<ElectricalProject?>(crowded);
            Check(Export(warningWorkspace, "summary.pdf") == 3, "Shared export did not complete.");
            Check(Status(warningWorkspace).Text.Contains("標示空間不足") &&
                ((Button)warningWorkspace.FindName("OutputWarningsButton")).Visibility == Visibility.Visible,
                "Success summary did not expose recoverable warnings.");
            var details = (string)Call(warningWorkspace, "OutputLabelWarningDetails")!;
            foreach (var value in new[] { "crowded-1", "crowded-2", "wire-crowded-1", "wire-crowded-2", "Sensor1_11", "Sensor2_22" })
                Check(details.Contains(value), "Warning details omitted page or wire: " + value);
            var clear = new ElectricalProject { ProjectId = "clear-export", Schematic = new() {
                Pages = [new() { PageId = "clear", Title = "Clear", Width = 100, Height = 80 }] } };
            warningWorkspace.SaveOutputSnapshotAsync = () => Task.FromResult<ElectricalProject?>(clear);
            Check(Export(warningWorkspace, "clear.pdf") == 1 && !Status(warningWorkspace).Text.Contains("Sensor1_11"),
                "A later clean export retained warnings from the previous export.");
        });
        Proof("save validation path existing-target and render failures propagate", () =>
        {
            var project = new ElectricalProject { ProjectId = "failure-fixture", Schematic = new() {
                Pages = [new() { PageId = "failure", Title = "Failure guards", Width = 100, Height = 80 }] } };
            var workspace = Workspace(project);
            workspace.SaveOutputSnapshotAsync = () => throw new IOException("synthetic save failure");
            Throws<IOException>(() => Export(workspace, "save-failed.pdf"), "Save failure was suppressed."); NoPartial("save-failed.pdf");
            workspace.SaveOutputSnapshotAsync = () => Task.FromResult<ElectricalProject?>(project);
            var valid = project.Schematic.Pages[0]; project.Schematic.Pages.Add(valid);
            Throws<InvalidOperationException>(() => Export(workspace, "validation-failed.pdf"), "Validation failure was suppressed."); NoPartial("validation-failed.pdf");
            project.Schematic.Pages.RemoveAt(1);
            var existing = Path.Combine(root, "existing.pdf"); File.WriteAllText(existing, "synthetic-existing-target");
            Throws<IOException>(() => Export(workspace, "existing.pdf"), "Existing target was overwritten.");
            Check(File.ReadAllText(existing) == "synthetic-existing-target", "Existing output contents changed.");
            Throws<ArgumentException>(() => Export(workspace, "bad\0path.pdf"), "Invalid output path was suppressed.");
            Throws<DirectoryNotFoundException>(() => Export(workspace, "missing-directory/output.pdf"), "PDF save failure was suppressed.");
            NoPartial("missing-directory/output.pdf");
            project.Schematic.Pages[0] = valid with { Width = 2000, Height = 2000 };
            Throws<InvalidOperationException>(() => Export(workspace, "render-failed.pdf"), "Real renderer size failure was suppressed.");
            NoPartial("render-failed.pdf");
            project.Schematic.Pages[0] = valid;
            Throws<InvalidOperationException>(() => SchematicPdfWriter.Write(project, Path.Combine(root, "callback-failed.pdf"),
                (_, _) => throw new InvalidOperationException("synthetic render failure")), "Writer swallowed a render failure.");
            NoPartial("callback-failed.pdf");
        });
        Proof("many warnings preserve working area", () =>
        {
            var workspace = Workspace(new() { ProjectId = "bounded-warning-footer" });
            var warnings = (Dictionary<string, SchematicOutputLabelWarning>)type.GetField("_outputLabelWarnings", flags)!.GetValue(workspace)!;
            for (var i = 1; i <= 50; i++)
                warnings.Add("page-" + i, new(i, "page-" + i, "Warning page " + i,
                    ["wire-" + i], ["Sensor" + i + "_11"]));
            Call(workspace, "SetOutputSummary", "Export complete");
            var host = new Window { Content = workspace, Width = 1000, Height = 800, Opacity = 0, ShowInTaskbar = false };
            host.Show();
            try
            {
                workspace.UpdateLayout();
                Check(Status(workspace).ActualHeight <= 80 && ((FrameworkElement)workspace.FindName("PageScroll")).ActualHeight > 200,
                    "Many export warnings collapsed the working area.");
                Exception? failure = null;
                workspace.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => {
                    var dialog = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.Title == "匯出標示警告")
                        ?? System.Windows.PresentationSource.CurrentSources.OfType<System.Windows.Interop.HwndSource>()
                            .Select(s => s.RootVisual).OfType<Window>().Single(w => w.Title == "匯出標示警告");
                    dialog.Opacity = 0; dialog.ShowInTaskbar = false;
                    try {
                        var details = (TextBox)dialog.FindName("OutputWarningDetails");
                        Check(details.IsReadOnly && details.VerticalScrollBarVisibility == ScrollBarVisibility.Auto &&
                            details.Text.Contains("page-50") && details.Text.Contains("Sensor50_11") && details.Text.Contains("wire-50"),
                            "Bounded warning dialog lost scrollability or final page/wire identities.");
                    } catch (Exception error) { failure = error; }
                    finally { dialog.DialogResult = false; }
                }));
                Call(workspace, "OutputWarnings_Click", workspace, new RoutedEventArgs());
                if (failure is not null) throw failure;
            }
            finally { host.Content = null; host.Close(); }
        });
        Proof("physical cable specification notes wrapping same-page fitting and PDF", () =>
        {
            var template = new ArchivedCableTemplate { TemplateId = "metadata", TemplateRevision = "r1", AssetSha256 = new('a', 64),
                Ports = new[] { "A", "B" }.Select(id => new ComponentIntelligence.Electrical.Domain.ComponentPort { PortId = id, Name = id,
                    Pins = [new() { PinId = id + "1", PinNumber = "1", Function = "COM" }] }).ToList(), Mapping = [new("A1", "B1")],
                Manufacturing = new() { InventoryOnly = true, WiringDiagram = new() {
                    PortBounds = new() { ["A"] = new(10, 10, 60, 40), ["B"] = new(180, 10, 60, 40) },
                    PinPositions = new() { ["A1"] = new(70, 32), ["B1"] = new(180, 32) } } } };
            var members = Enumerable.Range(1, 9).Select(i => {
                var cable = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
                cable.ReferenceDesignator = "C" + i;
                cable.ProvidedLengthMm = i == 1 ? null : 400 + i;
                cable.Specification = "L1=5000mm";
                cable.Notes = i == 1 ? "" : "Owner-" + i + " " + string.Concat(Enumerable.Repeat("獨立備註 multi-line note ", 40)) + "\nEND-" + i;
                return cable;
            }).ToArray();
            members[0].ArchivedCable = members[0].ArchivedCable! with { ManufacturingGeometry = AppendixSketchCad() };
            var page = new SchematicPage { PageId = "cables", Title = "Synthetic physical cables",
                CoordinateGrid = new(5, 5, 390, 275), TemplateGeometry = new() { SourceSha256 = new('b', 64), Width = 420, Height = 297,
                    Primitives = [new() { Kind = "LINE", Start = new(230, 235), End = new(395, 235) },
                        new() { Kind = "TEXT", Start = new(235, 250), Text = "TITLE BLOCK", TextHeight = 3 }] } };
            var project = new ElectricalProject { ProjectId = "synthetic-member-metadata", Cables = members.ToList(), Schematic = new() {
                Pages = [page], Symbols = members.Select((cable, i) => new SchematicSymbol {
                    SymbolId = "cable-" + i, CableInstanceId = cable.CableInstanceId, PageId = page.PageId,
                    Position = new(20 + i % 3 * 100, 35 + i / 3 * 40), Width = 75, Height = 20,
                    Geometry = new() { SourceSha256 = new('a', 64), Width = 75, Height = 20,
                        Primitives = [new() { Kind = "LINE", Start = new(0, 0), End = new(75, 20) }] } }).ToList() } };
            var before = JsonSerializer.Serialize(project);
            var sections = CableWiringAppendixPlan.Build(project).Sections;
            Check(sections.Count == 1 && sections[0].CableInstanceIds.Count == 9, "Notes or actual lengths split shared internal wiring.");
            var tiles = CableWiringAppendixRenderer.Plan(members[0], page, 9, members);
            Check(tiles.Count == 1, "Metadata forced duplicate internal wiring diagrams.");
            var tile = tiles[0];
            var memberPages = CableWiringAppendixRenderer.MemberPages(tile, page);
            Check(memberPages.Count == 0 && tile.MemberLabels.Count == 27, "Independent cable labels left the same-page assembly.");
            var allLines = tile.MemberLines.Concat(memberPages.SelectMany(lines => lines)).ToArray();
            var metadata = string.Join("\n", allLines);
            Check(metadata.Split("L1=5000mm").Length - 1 == 9, "Specification was omitted or deduplicated across physical cables.");
            Check(metadata.Contains("C1 / 長度待填"), "L1 specification text was incorrectly inferred as actual cable length.");
            Check(allLines.Contains("C1 / 規格：L1=5000mm") && allLines.Contains("C1 / 備註：未填"),
                "The blank-note cable needs separate, attributed specification and note fields in user-facing terminology.");
            Check(!allLines.Any(line => line.Contains("Specification") || line.Contains("UserNotes")),
                "Appendix metadata exposes internal property identifiers.");
            for (var i = 2; i <= 9; i++)
            {
                Check(metadata.Contains("C" + i + " / " + (400 + i) + " mm"), "Independent physical length was omitted.");
                Check(metadata.Contains("Owner-" + i) && metadata.Contains("END-" + i), "Notes were clipped or lost at a page boundary.");
                Check(allLines.Contains("C" + i + " / 規格：L1=5000mm") && allLines.Any(line => line.StartsWith("C" + i + " / 備註：Owner-" + i)),
                    "Independent specification and note fields lost their physical cable attribution.");
            }
            foreach (var line in allLines)
            {
                var text = new TextBlock { Text = line, FontFamily = new("Microsoft JhengHei"), FontSize = 12,
                    Width = tile.HeaderWidth * 4, TextWrapping = TextWrapping.Wrap };
                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Check(text.DesiredSize.Height / 4 <= 5, "A wrapped metadata row overlaps the next row.");
            }
            Check(tile.MemberLabels.All(label => label.Bounds.X > tile.ContentBounds.X + tile.ContentBounds.Width &&
                label.Bounds.Y + label.Bounds.Height <= tile.MetadataBounds!.Y + tile.MetadataBounds.Height),
                "Independent cable metadata overlaps wiring or escapes its area.");
            CheckAppendixSketchClearance(tile, tile.SketchBounds!, page);
            foreach (var lines in memberPages)
                Check(page.EffectiveGrid().Y + 18 + lines.Count * 5 <= CableWiringAppendixRenderer.MemberReturnBounds(page).Y,
                    "Metadata continuation overlaps the return link or title block.");
            var independent = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
            independent.ReferenceDesignator = "UNIQUE"; independent.ProvidedLengthMm = 725;
            independent.Specification = "UNIQUE-SPEC " + string.Concat(Enumerable.Repeat("長規格 wrap-spec ", 20)) + " END-SPEC";
            independent.Notes = "UNIQUE-NOTE\nEND-NOTE";
            var distinctTile = CableWiringAppendixRenderer.Plan(members[0], page, 1, [members[0], independent])[0];
            var distinctText = string.Join("\n", distinctTile.MemberLines.Concat(CableWiringAppendixRenderer.MemberPages(distinctTile, page).SelectMany(lines => lines)));
            Check(distinctText.Contains("UNIQUE-SPEC") && distinctText.Contains("END-SPEC") && distinctText.Contains("UNIQUE-NOTE") && distinctText.Contains("END-NOTE"),
                "Renderer substituted a representative cable's metadata for another member.");
            independent.ReferenceDesignator = string.Concat(Enumerable.Repeat("LONGOWNER", 30));
            var longOwner = CableWiringAppendixRenderer.Plan(independent, page, 1)[0];
            var longLines = longOwner.MemberLines.Concat(CableWiringAppendixRenderer.MemberPages(longOwner, page).SelectMany(lines => lines))
                .Where(line => !line.StartsWith("其餘適用線材")).ToArray();
            var longText = string.Concat(longLines);
            Check(longText.Contains(independent.ReferenceDesignator + " / 規格：") && longText.Contains("END-SPEC") && longText.Contains("END-NOTE"),
                "A long ownership prefix clipped a field heading or its value.");
            foreach (var line in longLines)
            {
                var text = new TextBlock { Text = line, FontFamily = new("Microsoft JhengHei"), FontSize = 12,
                    Width = longOwner.HeaderWidth * 4, TextWrapping = TextWrapping.Wrap };
                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Check(text.DesiredSize.Height / 4 <= 5, "A long ownership prefix overlaps its next metadata row.");
            }
            var single = CableWiringAppendixRenderer.Plan(members[0], page, 1)[0];
            Check(string.Join("\n", single.MemberLines).Contains("L1=5000mm"), "Single cable with blank notes omitted its Specification.");
            var workspace = Workspace(project);
            var background = Page(workspace, project, page with { PageId = "blank" });
            Png(CableWiringAppendixRenderer.Render(members[0], page, tile, [], background), "cable-wiring.png");
            File.WriteAllText(Path.Combine(root, "member-lines.json"), JsonSerializer.Serialize(new { tile.MemberLines, Pages = memberPages }));
            var pdfPath = Path.Combine(root, "cable-metadata.pdf");
            var count = SchematicPdfWriter.Write(project, pdfPath, (p, sheet) => Page(workspace, p, sheet));
            using var pdf = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
            Check(pdf.PageCount == count && count == 2 + memberPages.Count, "PDF lost metadata pages or duplicated shared internal wiring.");
            Check(before == JsonSerializer.Serialize(project), "Appendix planning/rendering/export changed source JSON.");
        });
        File.WriteAllText(Path.Combine(root, "export-warnings-notes.json"), JsonSerializer.Serialize(new { Passed = failures.Count == 0, Checks = checks, Failures = failures }));
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
        Console.WriteLine("EXPORT_WARNINGS_NOTES_PASS"); return 0;
    }
}

#if EXPORT_WARNINGS_NOTES_PROOF
internal static class ExportWarningsNotesProofRunner
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { return (int)typeof(Program).GetMethod("ExportWarningsNotesProof", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [Path.GetFullPath(args[0])])!; }
        catch (Exception error) { Console.Error.WriteLine(error.GetBaseException()); return 1; }
    }
}
#endif
