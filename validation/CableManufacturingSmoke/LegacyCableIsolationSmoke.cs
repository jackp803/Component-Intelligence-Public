using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Electrical.Topology;
using ComponentIntelligence.Repository;
using ComponentIntelligence.Runtime;

internal static partial class Program
{
    private static int LegacyCableIsolationProof(string root)
    {
        Directory.CreateDirectory(root);
        var service = new SchematicAuthoringService();
        var project = new ElectricalProject { ProjectId = "legacy-isolation", Schematic = new() { Pages = [new() { PageId = "one", Title = "Source" }, new() { PageId = "two", Title = "Destination" }] } };
        foreach (var (name, gender, page) in new[] { ("SourceCable", ConnectorGender.Male, "one"), ("EndCable", ConnectorGender.Female, "two") })
        {
            var template = new ArchivedCableTemplate { TemplateId = name, TemplateRevision = "r1", AssetSha256 = new('a', 64), DisplayName = name,
                Mapping = [new("P1.1", "P2.1"), new("P1.2", "P2.2")], MappingConfirmed = true, MappingRevision = "m1", MappingEvidence = "synthetic verified map",
                Ports = new[] { "P1", "P2" }.Select(id => new ComponentIntelligence.Electrical.Domain.ComponentPort { PortId = id, Name = id,
                    Connector = new() { ConnectorId = name + id, Family = "M12", Coding = "A", PinCount = 2, Gender = gender },
                    Pins = new[] { "1", "2" }.Select(n => new ComponentIntelligence.Electrical.Domain.ComponentPin { PinId = id + "." + n, PinNumber = n }).ToList() }).ToList() };
            var geometry = new SchematicCadAsset { SourceSha256 = template.AssetSha256, Width = 80, Height = 30, MillimetresPerUnit = 1,
                Primitives = [new() { Kind = "LINE", Start = new(0, 0), End = new(80, 0) }, new() { Kind = "LINE", Start = new(0, 30), End = new(80, 30) }],
                ConnectionPoints = [new("front", "", new(0, 15), Direction: "Left"), new("tail", "", new(80, 15), Direction: "Right")] };
            project = service.AddArchivedCable(project, template, CableConstructionType.Custom, geometry,
                new Dictionary<string, string> { ["P1"] = "front", ["P2"] = "tail" }, page, new(80, page == "one" ? 50 : 170));
            var cable = project.Cables.Last();
            project = service.SetArchivedCableDetails(project, cable.CableInstanceId, name, 500, "2 cores", CableConstructionType.Custom, cable.ArchivedCable!.Mapping, "Original notes");
        }
        var source = project.Schematic!.Symbols[0]; var original = project.Schematic.Symbols[1];
        var alias = original with { SymbolId = "legacy-alias", Position = new(80, 50) }; project.Schematic.Symbols.Add(alias);
        project.Cables[0].ArchivedCable!.Ports[1].Pins[0].Function = "COM";
        var from = project.Cables[0].ArchivedCable!.Ports[0]; var to = project.Cables[1].ArchivedCable!.Ports[0];
        project = service.MatePorts(project, SchematicAttachment.Port(source.SymbolId, from.PortId), SchematicAttachment.Port(alias.SymbolId, to.PortId),
            ConnectorMatingService.Prepare(project, from.PortId, to.PortId), "synthetic checked mating", "J2 接頭對接");
        project = new SchematicCableDetailService().PlaceArchivedDetail(project, project.Cables[1].CableInstanceId, "two", new(210, 50), new(210, 150),
            new(project.Cables[1].CableInstanceId, null) { SketchVisible = false });
        var originalJson = JsonSerializer.Serialize(project); var originalWires = JsonSerializer.Serialize(project.Schematic!.Wires);
        var host = new ElectricalWorkspaceWindow(Path.Combine(root, "host.db"), preferences: new WorkspacePreferencesStore(Path.Combine(root, "preferences.json")));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var hostType = typeof(ElectricalWorkspaceWindow);
        object? Host(string name) => hostType.GetField(name, flags)!.GetValue(host);
        var count = (int)hostType.GetMethod("AdoptLoadedProject", flags)!.Invoke(host, [project])!;
        project = (ElectricalProject)Host("_project")!;
        Require(count == 1 && project.Cables.Count == 3, "Desktop load did not separate the existing cable views.");
        Require(((ProjectSaveSession)Host("_saveSession")!).IsDirty(project) && ((ProjectMutationHistory)Host("_history")!).CanUndo,
            "Load migration was treated as already saved or lacked Undo.");
        hostType.GetMethod("Undo_Click", flags)!.Invoke(host, [host, new RoutedEventArgs()]);
        Require(JsonSerializer.Serialize((ElectricalProject)Host("_project")!) == originalJson, "Undo did not restore the original legacy graph.");
        hostType.GetMethod("Redo_Click", flags)!.Invoke(host, [host, new RoutedEventArgs()]);
        project = (ElectricalProject)Host("_project")!;
        Require(project.Cables.Count == 3 && project.Schematic!.Wires.Select(w => w.WireId).SequenceEqual(JsonSerializer.Deserialize<List<SchematicWire>>(originalWires)!.Select(w => w.WireId)),
            "Redo changed the external route identities.");
        var workspace = new SchematicWorkspaceControl(() => project, (next, _) => project = next,
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var owner = new Window { Content = workspace }; var type = typeof(SchematicWorkspaceControl);
        object? Call(string name, params object[] values) => type.GetMethod(name, flags | BindingFlags.DeclaredOnly)!.Invoke(workspace, values);
        type.GetField("_pageId", flags)!.SetValue(workspace, "two"); type.GetField("_selectionId", flags)!.SetValue(workspace, alias.SymbolId);
        Call("Render", project);
        Require(((Button)workspace.FindName("AnotherRepresentationButton")).Visibility == Visibility.Collapsed,
            "Cable inspector still offers a synchronized representation.");
        ((TextBox)type.GetField("ReferenceText", flags)!.GetValue(workspace)!).Text = "IndependentCable2";
        Require((bool)Call("ApplyInspector")!, "Independent Reference edit failed.");
        var target = project.Cables.Single(c => c.CableInstanceId == project.Schematic!.Symbols.Single(s => s.SymbolId == alias.SymbolId).CableInstanceId);
        project = service.SetArchivedCableDetails(project, target.CableInstanceId, "IndependentCable2", 1200, "2 cores", CableConstructionType.Custom, target.ArchivedCable!.Mapping, "Lower sensor");
        Require(project.Cables[1].ReferenceDesignator == "EndCable" && project.Cables[1].ProvidedLengthMm == 500 && project.Cables[1].Notes == "Original notes",
            "Editing the migrated cable changed the original cable.");
        var pair = project.Schematic!.Continuations[0];
        type.GetField("_pageId", flags)!.SetValue(workspace, "one"); Call("Render", project);
        var sheet = (Canvas)type.GetProperty("Sheet", flags)!.GetValue(workspace)!;
        Require(sheet.Children.OfType<TextBlock>().Any(t => t.Text.StartsWith("IndependentCable2 接頭對接")) &&
            sheet.Children.OfType<TextBlock>().All(t => !t.Text.Contains("J2 接頭對接")), "Arrow caption did not update to the actual destination Reference.");
        Render(workspace, 1400, 900, Path.Combine(root, "destination-reference.png"));
        type.GetField("_pageId", flags)!.SetValue(workspace, "two"); type.GetField("_selectionId", flags)!.SetValue(workspace, alias.SymbolId); Call("Render", project);
        Render(workspace, 1400, 900, Path.Combine(root, "independent-legacy-cables.png"));
        Call("AnotherRepresentation_Click", workspace, new RoutedEventArgs()); Call("PlaceAnotherCableRepresentation", new SchematicPoint(280, 170));
        Require(project.Cables.Count == 4 && project.Cables.Last().ReferenceDesignator is null && project.Cables.Last().ProvidedLengthMm is null && project.Cables.Last().Notes is null,
            "The legacy placement shortcut still created synchronized cable metadata.");
        var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), Path.Combine(root, "synthetic-project.db"));
        repository.SaveAsync(project).GetAwaiter().GetResult(); var reopened = repository.GetAsync(project.ProjectId).GetAwaiter().GetResult()!;
        Require(reopened.Cables.Single(c => c.CableInstanceId == target.CableInstanceId).Notes == "Lower sensor" &&
            ReferenceEquals(reopened, new SchematicCableIsolationService().Separate(reopened)), "Saved separation did not remain independent after reopen.");
        SchematicPdfWriter.Write(project, Path.Combine(root, "legacy-isolation.pdf"), (p, page) => (BitmapSource)Call("RenderOutputPage", p, page)!);
        File.WriteAllText(Path.Combine(root, "legacy-isolation.json"), JsonSerializer.Serialize(new { Passed = true, LoadMigration = true, DirtyAndUndo = true,
            ExternalConnectionRetained = true, IndependentMetadata = true, DestinationReference = true, Reopen = true }));
        owner.Content = null; owner.Close(); hostType.GetField("_allowClose", flags)!.SetValue(host, true); host.Close();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Console.WriteLine("LEGACY_CABLE_ISOLATION_PASS"); return 0;
    }
}
