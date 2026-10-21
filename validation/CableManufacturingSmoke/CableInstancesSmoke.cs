using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

internal static partial class Program
{
    private static int CableInstancesProof(string root)
    {
        Directory.CreateDirectory(root);
        var template = new ArchivedCableTemplate { TemplateId = "synthetic-cable", TemplateRevision = "r1", AssetSha256 = new('a', 64),
            DisplayName = "Two-pin sensor cable", Mapping = [new("A1", "B1"), new("A2", "B2")],
            Ports = new[] { "A", "B" }.Select(id => new ComponentIntelligence.Electrical.Domain.ComponentPort { PortId = id, Name = id,
                Pins = new[] { "1", "2" }.Select(n => new ComponentIntelligence.Electrical.Domain.ComponentPin { PinId = id + n, PinNumber = n, Function = n == "1" ? "24V" : "COM" }).ToList() }).ToList() };
        var geometry = new SchematicCadAsset { SourceSha256 = template.AssetSha256, Width = 60, Height = 20, MillimetresPerUnit = 1,
            Primitives = [new() { Kind = "LINE", Start = new(0, 10), End = new(60, 10) }] };
        var service = new SchematicAuthoringService();
        var project = service.AddArchivedCable(new() { ProjectId = "independent-cables", Schematic = new() { Pages = [new() { PageId = "P", Title = "Cable instances" }] } },
            template, CableConstructionType.Custom, geometry, new Dictionary<string, string>(), "P", new(20, 20));
        var firstId = project.Cables[0].CableInstanceId; var firstSymbolId = project.Schematic!.Symbols[0].SymbolId;
        project = service.SetArchivedCableDetails(project, firstId, "C1", 500, "2 cores", CableConstructionType.Custom, template.Mapping, "Upper sensor");
        var details = new SchematicCableDetailService();
        project = details.PlaceArchivedDetail(project, firstId, "P", new(20, 65), new(20, 150), new(firstId, null) { SketchVisible = false });
        var workspace = new SchematicWorkspaceControl(() => project, (next, _) => project = next,
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var owner = new Window { Content = workspace };
        var type = typeof(SchematicWorkspaceControl); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        object? Call(string name, params object[] args) => type.GetMethod(name, flags)!.Invoke(workspace, args);
        type.GetField("_pageId", flags)!.SetValue(workspace, "P"); type.GetField("_selectionId", flags)!.SetValue(workspace, firstSymbolId);
        Call("Render", project);
        var sheet = (Canvas)type.GetProperty("Sheet", flags)!.GetValue(workspace)!;
        Require(sheet.Children.OfType<TextBlock>().All(t => !t.Text.Contains("草稿／未完成")), "Printable page still contains the draft/incomplete caption.");
        sheet.Measure(new(sheet.Width, sheet.Height)); sheet.Arrange(new(0, 0, sheet.Width, sheet.Height));
        var footerBounds = sheet.Children.OfType<TextBlock>().Where(t => Canvas.GetTop(t) >= sheet.Height - 24)
            .Select(t => new Rect(Canvas.GetLeft(t), Canvas.GetTop(t), t.DesiredSize.Width, t.DesiredSize.Height)).ToArray();
        for (var i = 0; i < footerBounds.Length; i++)
            for (var j = i + 1; j < footerBounds.Length; j++) Require(!footerBounds[i].IntersectsWith(footerBounds[j]), "Page footer captions overlap.");
        var duplicate = (Button)workspace.FindName("DuplicateCableButton");
        Require(duplicate is { Visibility: Visibility.Visible }, "Selected cable has no distinct physical-copy action.");
        duplicate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(project.Cables.Count == 1 && ((ElectricalProject)type.GetField("_placementPreview", flags)!.GetValue(workspace)!).Cables.Count == 2,
            "Starting a physical copy committed the preview or reused the original cable.");
        Call("CancelCommand"); Require(project.Cables.Count == 1, "Cancelling a physical copy changed the project.");
        type.GetField("_selectionId", flags)!.SetValue(workspace, firstSymbolId); Call("Render", project);
        duplicate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Call("PlaceAnotherCableRepresentation", new SchematicPoint(230, 20));
        Require(project.Cables.Count == 2 && project.Schematic!.Symbols.Count == 2, "Physical-copy placement did not create a separate cable.");
        var secondId = project.Cables[1].CableInstanceId;
        var dialogType = type.Assembly.GetType("ComponentIntelligence.Desktop.ArchivedCableSettingsDialog");
        Require(dialogType is not null, "No testable cable settings workflow is available.");
        var dialog = (Window)Activator.CreateInstance(dialogType!, project, secondId, new List<CableConnectorChoice>(),
            new Func<Func<ElectricalProject, ElectricalProject>, bool>(command => { project = command(project); return true; }))!;
        TextBox Field(string name) => (TextBox)dialogType!.GetField(name, flags)!.GetValue(dialog)!;
        Field("_reference").Text = "C2"; Field("_length").Text = "1200"; Field("_notes").Text = "Lower sensor\nService loop";
        Require((bool)dialogType!.GetMethod("TryApply")!.Invoke(dialog, null)!, "Cable settings did not apply independent length and notes.");
        Require(project.Cables[0].ProvidedLengthMm == 500 && project.Cables[0].Notes == "Upper sensor" &&
            project.Cables[1].ProvidedLengthMm == 1200 && project.Cables[1].Notes == "Lower sensor\nService loop", "Settings leaked between physical cables.");
        Require(project.Cables[0].ArchivedCable!.Mapping.SequenceEqual(project.Cables[1].ArchivedCable!.Mapping), "Changing length/notes changed the internal wiring.");
        Render((FrameworkElement)dialog.Content, 960, 780, Path.Combine(root, "individual-cable-settings.png"));
        var stale = new ArchivedCableSettingsDialog(project, secondId, [], command => { project = command(project); return true; });
        var rejected = new ArchivedCableSettingsDialog(project, secondId, [], _ => false);
        Require(!rejected.TryApply() && ((TextBlock)dialogType.GetField("_error", flags)!.GetValue(rejected)!).Text.Length > 0,
            "Rejected settings callback has no visible feedback.");
        var wrapped = new ArchivedCableSettingsDialog(project, secondId, [], command => (bool)Call("Apply", command, "Update cable")!);
        ((TextBox)dialogType.GetField("_length", flags)!.GetValue(wrapped)!).Text = "0";
        Require(!wrapped.TryApply() && ((TextBlock)dialogType.GetField("_error", flags)!.GetValue(wrapped)!).Text.Contains("正數"),
            "Workspace error handling hid the actionable rejection reason behind the settings dialog.");
        var original = JsonSerializer.Serialize(project);
        ((TextBox)dialogType.GetField("_length", flags)!.GetValue(stale)!).Text = "NaN";
        Require(!stale.TryApply() && original == JsonSerializer.Serialize(project), "Invalid length partially changed the cable.");
        project.Cables[1].Notes = "Changed outside the dialog";
        ((TextBox)dialogType.GetField("_length", flags)!.GetValue(stale)!).Text = "1000";
        Require(!stale.TryApply() && project.Cables[1].Notes == "Changed outside the dialog", "Stale settings overwrote newer cable data.");
        project.Cables[1].Notes = "Lower sensor\nService loop";
        project = details.PlaceArchivedDetail(project, secondId, "P", new(230, 65), new(230, 150), new(secondId, null) { SketchVisible = false, TableWidth = 165 });
        project = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(project))!;
        Call("Render", project); Render(workspace, 1400, 900, Path.Combine(root, "independent-cable-details.png"));
        Require(SchematicDraftReview.Inspect(project).Any(i => i.Code == "CABLE_MAPPING_UNCONFIRMED"), "Removing printed diagnostics also suppressed the review warning.");
        SchematicPdfWriter.Write(project, Path.Combine(root, "independent-cables.pdf"), (p, page) => (BitmapSource)Call("RenderOutputPage", p, page)!);
        File.WriteAllText(Path.Combine(root, "cable-instances.json"), JsonSerializer.Serialize(new { Passed = true, SeparateInstances = true,
            IndependentLengthAndNotes = true, SameMapping = true, CancelSafe = true, ReviewRetained = true }));
        owner.Content = null; owner.Close();
        Console.WriteLine("CABLE_INSTANCES_WORKFLOW_PASS"); return 0;
    }
}
