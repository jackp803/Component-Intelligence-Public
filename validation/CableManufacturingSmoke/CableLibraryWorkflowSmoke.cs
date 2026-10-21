using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.SymbolArchive;
using netDxf;
using netDxf.Entities;
using PdfSharp.Pdf.IO;

internal static partial class Program
{
    private static int CableLibraryWorkflowProof(string root, string? only)
    {
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        Directory.CreateDirectory(root);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var repository = new SymbolArchiveRepository(Path.Combine(root, "archive"));
        var source = Path.Combine(root, "custom.dxf");
        var dxf = new DxfDocument(); dxf.Entities.Add(new Line(new Vector2(0, 0), new Vector2(80, 0)));
        dxf.Entities.Add(new Line(new Vector2(0, 25), new Vector2(80, 25))); dxf.Save(source);
        var geometry = new SchematicCadImporter().ReadDxf(source, 1);
        if (only is null or "ui")
        foreach (var kind in new[] { CableLibraryItemKind.Cable, CableLibraryItemKind.Connector })
        {
            var constructor = typeof(CableArchiveEditorDialog).GetConstructors().SingleOrDefault(c => c.GetParameters().Length == 4);
            Require(constructor is not null, "Library editor has no explicit cable/connector import entry.");
            var editor = (CableArchiveEditorDialog)constructor!.Invoke([repository, Array.Empty<ComponentIntelligence.Contracts.ComponentIR>(), null!, kind]);
            var kindSelector = (ComboBox)typeof(CableArchiveEditorDialog).GetField("_kind", flags)!.GetValue(editor)!;
            Require((CableLibraryItemKind)kindSelector.SelectedValue == kind, "Import category was not selected.");
            ((TextBox)typeof(CableArchiveEditorDialog).GetField("_name", flags)!.GetValue(editor)!).Text = "Custom " + kind;
            var cad = typeof(CableArchiveEditorDialog).GetField("_wiring", flags)!.GetValue(editor)!;
            cad.GetType().GetMethod("Restore")!.Invoke(cad, [source, geometry, null!, 1d]);
            if (kind == CableLibraryItemKind.Connector)
            {
                var table = (CableManufacturingTableEditor)typeof(CableArchiveEditorDialog).GetField("_table", flags)!.GetValue(editor)!;
                table.Draft.Ends[0].Pins[0].PinNumber = "A-7";
                new CableWiringEditorService().SetPinFunction(table.Draft, table.Draft.Ends[0].Pins[0].PinId, "SENSE");
            }
            var save = typeof(CableArchiveEditorDialog).GetMethod("SaveCandidateAsync");
            Require(save is not null, "Import save path is not independently testable.");
            var task = (Task<CableArchiveEntry?>)save!.Invoke(editor, null)!;
            var frame = new System.Windows.Threading.DispatcherFrame();
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            var entry = task.GetAwaiter().GetResult();
            Require(entry is not null && entry.Template.LibraryItemKind == kind, "Imported item category was not persisted: " + kind);
            if (kind == CableLibraryItemKind.Connector)
            {
                var catalogType = typeof(CableArchiveEditorDialog).Assembly.GetType("ComponentIntelligence.Desktop.CableConnectorCatalog")!;
                var choicesMethod = catalogType.GetMethod("Choices")!;
                Require(choicesMethod.GetParameters().Length == 2, "Imported connectors are absent from cable-end choices.");
                var choices = (IReadOnlyList<CableConnectorChoice>)choicesMethod.Invoke(null,
                    [Array.Empty<ComponentIntelligence.Contracts.ComponentIR>(), repository.Load().CableTemplates])!;
                Require(choices.Any(c => c.Label.Contains("Custom Connector") && c.Source?.Pins.Count > 0), "Imported connector Pin inventory is missing from the cable editor.");
                var imported = choices.Single(c => c.Label.Contains("Custom Connector"));
                var cableTemplate = repository.Load().CableTemplates.Single(e => e.Template.LibraryItemKind == CableLibraryItemKind.Cable).Template;
                var table = new CableManufacturingTableEditor(new CableManufacturingEditorService().Prepare(cableTemplate), choices);
                Render(table, 950, 600, Path.Combine(root, "choose-imported-connector.png"));
                var selector = Descendants(table).OfType<ComboBox>().First(c => c.Items.Cast<object>().Contains(imported));
                selector.SelectedItem = imported;
                Require(table.Commit() && table.Draft.Ends[0].Pins[0].PinNumber == "A-7" && table.Draft.Ends[0].Pins[0].Function == "SENSE",
                    "Selecting an imported connector lost its exact Pin number or function: " + JsonSerializer.Serialize(table.Draft.Ends[0]) +
                    " / source: " + JsonSerializer.Serialize(imported.Source) + " / " + ((TextBlock)typeof(CableManufacturingTableEditor).GetField("_error", flags)!.GetValue(table)!).Text);
            }
            SynchronizationContext.SetSynchronizationContext(null);
            var resolved = new CableArchiveResolver(repository).ResolveAsync(entry!.Template.TemplateId, entry.Template.TemplateRevision).GetAwaiter().GetResult();
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
            Require(File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(resolved.AbsolutePath)), "Import changed the source CAD.");
            Render((FrameworkElement)editor.Content, 970, 740, Path.Combine(root, "import-" + kind + ".png")); editor.Close();
        }
        if (only is null or "ui") LibraryBrowserProof(root, repository);
        var template = new ArchivedCableTemplate { TemplateId = "grouped", TemplateRevision = "r1", AssetSha256 = geometry.SourceSha256,
            MappingConfirmed = true, MappingRevision = "r1", MappingEvidence = "synthetic fixture",
            Ports = new[] { "A", "B" }.Select((id, index) => new ComponentIntelligence.Electrical.Domain.ComponentPort {
                PortId = id, Name = "P" + (index + 1), Connector = new() { ConnectorId = id, Family = "M12", Coding = "A", PinCount = 2, Gender = index == 0 ? ConnectorGender.Male : ConnectorGender.Female },
                Pins = Enumerable.Range(1, 2).Select(n => new ComponentIntelligence.Electrical.Domain.ComponentPin { PinId = id + n, PinNumber = n.ToString(),
                    Function = index == 1 ? "Shared auxiliary-switch contact; continuity is checked between S1-S2 or S1-S3 depending on switch state." : n == 1 ? "COM" : "24V" }).ToList() }).ToList(),
            Mapping = [new("A1", "B1"), new("A2", "B2")] };
        // Reproduce a saved narrow diagram with long function text.
        template = template with { Manufacturing = new() { InventoryOnly = true, WiringDiagram = new() {
            PortBounds = new() { ["A"] = new(10, 10, 60, 40), ["B"] = new(180, 10, 60, 40) },
            PinPositions = new() { ["A1"] = new(70, 32), ["A2"] = new(70, 39), ["B1"] = new(180, 32), ["B2"] = new(180, 39) } } } };
        if (only is null or "export")
        {
            var cable = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
            var before = JsonSerializer.Serialize(cable);
            var page = new SchematicPage { PageId = "one", Title = "Main" };
            var tiles = CableWiringAppendixRenderer.Plan(cable, page, 1);
            Require(tiles.Count == 1, "Long connector functions still create clipped nearly-empty continuation pages.");
            Require(JsonSerializer.Serialize(cable) == before, "Export layout modified the saved cable.");
            var topPins = JsonSerializer.Deserialize<CableInstance>(before)!;
            topPins.ArchivedCable!.Manufacturing!.WiringDiagram!.PinPositions["A1"] = new(30, 10);
            Require(CableWiringAppendixRenderer.Plan(topPins, page, 1).Count == 1, "Top-edge Pin labels cannot be exported safely.");
            foreach (var text in tiles[0].Diagram.Primitives.Where(p => p.Kind is "TEXT" or "MTEXT"))
            {
                var box = tiles[0].Diagram.PortBounds.Values.Single(b => text.Start.X >= b.X && text.Start.X < b.X + b.Width && text.Start.Y >= b.Y && text.Start.Y < b.Y + b.Height);
                Require(text.TextWidth > 0 && text.Start.X + text.TextWidth <= box.X + box.Width, "An export label is not constrained inside its connector.");
            }
            var author = new SchematicAuthoringService();
            var project = new ElectricalProject { ProjectId = "library-workflow", Schematic = new() { Pages = [page] } };
            foreach (var (reference, length, note) in new[] { ("C1", 500d, "Upper sensor"), ("C2", 1200d, "Lower sensor") })
            {
                project = author.AddArchivedCable(project, template, CableConstructionType.Custom, geometry, new Dictionary<string, string>(), page.PageId, new(30, length == 500 ? 30 : 110));
                var current = project.Cables.Last(); current.ReferenceDesignator = reference; current.ProvidedLengthMm = length; current.Notes = note;
            }
            project = author.AddArchivedCable(project, template with { TemplateId = "variant", Mapping = [new("A1", "B2"), new("A2", "B1")] },
                CableConstructionType.Custom, geometry, new Dictionary<string, string>(), page.PageId, new(180, 30));
            project = author.AddArchivedCable(project, template with { TemplateId = "connector", LibraryItemKind = CableLibraryItemKind.Connector },
                CableConstructionType.Custom, geometry, new Dictionary<string, string>(), page.PageId, new(180, 110));
            var snapshot = JsonSerializer.Serialize(project);
            var plan = CableWiringAppendixPlan.Build(project);
            Require(plan.Sections.Count == 2 && plan.Sections[0].CableInstanceIds.Count == 2, "Wiring variants or connector-only appendix suppression are wrong.");
            var memberTiles = CableWiringAppendixRenderer.Plan(project.Cables[0], page, 1, project.Cables.Take(2).ToArray());
            var returns = CableWiringAppendixRenderer.ReturnLinks(memberTiles[0], plan.Sections[0].SourceLinks, new Dictionary<string, int> { [page.PageId] = 0 }, page.Margin);
            Require(returns.Count == 2 && returns[0].Destination != returns[1].Destination, "Shared diagram loses the separate return locations for cables on the same sheet.");
            foreach (var line in new[] { "C1 / 500 mm", "C1 / 備註：Upper sensor", "C2 / 1200 mm", "C2 / 備註：Lower sensor" })
                Require(memberTiles[0].MemberLines.Contains(line), "Shared diagram lost an independent cable field: " + line);
            var workspace = new SchematicWorkspaceControl(() => project, (_, _) => { }, () => Task.FromResult<IReadOnlyList<ComponentIntelligence.Contracts.ComponentIR>>([]),
                _ => Task.FromResult<Uri?>(null), () => { }, () => { });
            var render = typeof(SchematicWorkspaceControl).GetMethod("RenderOutputPage", flags)!;
            var path = Path.Combine(root, "grouped-wiring.pdf");
            var count = SchematicPdfWriter.Write(project, path, (p, sheet) => (BitmapSource)render.Invoke(workspace, [p, sheet])!);
            Require(count == 3, "PDF should contain one engineering sheet and two distinct wiring diagrams.");
            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            Require(pdf.Pages[0].Annotations.Count >= 3, "Shared wiring source navigation was lost.");
            Require(snapshot == JsonSerializer.Serialize(project), "Export synchronized independent instance metadata.");
            var cableId = project.Cables[1].CableInstanceId;
            var settings = new ArchivedCableSettingsDialog(project, cableId, [], command => { project = command(project); return true; });
            ((ComboBox)typeof(ArchivedCableSettingsDialog).GetField("_kind", flags)!.GetValue(settings)!).SelectedValue = CableLibraryItemKind.Connector;
            Require(settings.TryApply() && CableWiringEquivalence.Kind(project.Cables[1]) == CableLibraryItemKind.Connector &&
                CableWiringEquivalence.Kind(project.Cables[0]) == CableLibraryItemKind.Cable, "Existing item classification affected another physical cable.");
            Require(project.Cables[1].ProvidedLengthMm == 1200 && project.Cables[1].Notes == "Lower sensor", "Classification lost independent metadata.");
            Render((FrameworkElement)settings.Content, 950, 760, Path.Combine(root, "existing-item-category.png")); settings.Close();
        }
        File.WriteAllText(Path.Combine(root, "library-workflow.json"), JsonSerializer.Serialize(new { Passed = true, Scope = only ?? "all" }));
        Console.WriteLine("CABLE_LIBRARY_GROUPING_PASS"); return 0;
    }

    private static void LibraryBrowserProof(string root, SymbolArchiveRepository repository)
    {
        var project = new ElectricalProject { ProjectId = "browser", Schematic = new() { Pages = [new() { PageId = "sheet", Title = "Main" }] } };
        var workspace = new SchematicWorkspaceControl(() => project, (_, _) => { }, () => Task.FromResult<IReadOnlyList<ComponentIntelligence.Contracts.ComponentIR>>([]),
            _ => Task.FromResult<Uri?>(null), () => { }, () => { }, repository.ArchiveRoot);
        var owner = new Window { Content = workspace, Width = 1100, Height = 850, Left = -20000, Top = -20000,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        owner.Show();
        typeof(SchematicWorkspaceControl).GetField("_pageId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, "sheet");
        Exception? failure = null; var checkedDialog = false;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => {
            if (owner.OwnedWindows.Cast<Window>().SingleOrDefault(w => w.Title == "線材／接頭庫") is not { } dialog) return;
            timer.Stop();
            try
            {
                var content = (FrameworkElement)dialog.Content; Render(content, 690, 480, Path.Combine(root, "library-browser.png"));
                Require(Descendants(content).OfType<Button>().Any(b => Equals(b.Content, "匯入線材")) &&
                    Descendants(content).OfType<Button>().Any(b => Equals(b.Content, "匯入接頭")), "Library import commands are not visible.");
                var filter = Descendants(content).OfType<ComboBox>().Single(c => c.Items.Count == 3 && Equals(c.Items[0], "全部"));
                var list = Descendants(content).OfType<ListBox>().Single();
                Require(list.Items.Count == 2, "Imported items are missing from the library.");
                filter.SelectedIndex = 2; Require(list.Items.Count == 1 && list.Items[0]!.ToString()!.Contains("接頭"), "Connector filter selected the wrong inventory.");
                checkedDialog = true;
            }
            catch (Exception error) { failure = error; }
            finally { dialog.Close(); }
        };
        timer.Start();
        typeof(SchematicWorkspaceControl).GetMethod("CableLibrary_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, [null!, new RoutedEventArgs()]);
        timer.Stop(); owner.Close();
        if (failure is not null) throw failure;
        Require(checkedDialog, "Library browser did not open.");
    }
}
