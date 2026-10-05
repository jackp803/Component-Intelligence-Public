using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using PdfSharp.Pdf.IO;
using ComponentPort = ComponentIntelligence.Electrical.Domain.ComponentPort;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var mode = args.FirstOrDefault() ?? "all";
            var root = args.Skip(1).FirstOrDefault();
            if (mode == "wire") { WireSmoke(root); Console.WriteLine("PASS wire"); return 0; }
            var catalog = typeof(CableManufacturingTableEditor).Assembly.GetType("ComponentIntelligence.Desktop.CableConnectorCatalog")!;
            var choices = (IReadOnlyList<CableConnectorChoice>)catalog.GetMethod("Choices", BindingFlags.Static | BindingFlags.Public)!
                .Invoke(null, [Array.Empty<ComponentIR>()])!;
            var template = new ArchivedCableTemplate { TemplateId = "fixture", TemplateRevision = "r1", AssetSha256 = new('a', 64),
                Ports = Enumerable.Range(1, 2).Select(i => new ComponentPort { PortId = "P" + i, Name = "P" + i,
                    Pins = [new() { PinId = "pin" + i, PinNumber = "1" }] }).ToList() };
            var original = JsonSerializer.Serialize(template);
            var initialDraft = new CableManufacturingEditorService().Prepare(template);
            var editor = new CableManufacturingTableEditor(initialDraft, choices);
            editor.Measure(new Size(920, 460)); editor.Arrange(new Rect(0, 0, 920, 460)); editor.UpdateLayout();
            if (mode is "panels")
            {
                var counts = Children(editor).OfType<ComboBox>().Where(c => c.ToolTip?.ToString() == "Pin 數 (Alt+Enter)").ToArray();
                Require(counts.Length == 2, "P1/P2 must both have a visible Pin count selector.");
                Require(!Children(editor).OfType<ComboBox>().Any(c => c.ToolTip?.ToString() == "接頭端"), "Port selection is still a dropdown.");
                counts[0].SelectedItem = 4; counts[1].SelectedItem = 8;
                Require(editor.Draft.Ends[0].Pins.Count == 4 && editor.Draft.Ends[1].Pins.Count == 8, "Editing a visible panel changed another Port.");
                Call(editor, "AddEnd"); editor.UpdateLayout();
                Require(Children(editor).OfType<ComboBox>().Count(c => c.ToolTip?.ToString() == "Pin 數 (Alt+Enter)") == 3, "Adding P3 did not add its own visible panel.");
                Call(editor, "DeleteEnd"); editor.UpdateLayout();
                Require(editor.Draft.Ends.Count == 2, "Removing P3 affected another Port.");
            }
            if (mode is "all" or "noop")
            {
                Require(JsonSerializer.Serialize(initialDraft) == JsonSerializer.Serialize(editor.Draft), "Opening the editor modified connector metadata.");
                var changes = 0; editor.PinsRemoved += _ => changes++;
                typeof(CableManufacturingTableEditor).GetMethod("ApplyCount", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, [null]);
                Require(changes == 0, "Unchanged Pin count emitted a destructive reconciliation event.");
            }
            if (mode is "all" or "legacy")
            {
                var legacy = new CableManufacturingEditorService().Prepare(template);
                legacy.Ends[0].Connector = new() { ConnectorId = "legacy", Family = "M12", Coding = "A-code", PinCount = 1 };
                var opened = new CableManufacturingTableEditor(legacy, choices);
                Require(JsonSerializer.Serialize(legacy) == JsonSerializer.Serialize(opened.Draft), "Opening changed a legacy Coding value.");
            }
            if (mode is "all" or "typing")
            {
                var typedDraft = new CableManufacturingEditorService().Prepare(template);
                new CableManufacturingEditorService().SetPinCount(typedDraft, "P1", 4);
                var typed = new CableManufacturingTableEditor(typedDraft, choices);
                typed.Measure(new Size(920, 460)); typed.Arrange(new Rect(0, 0, 920, 460)); typed.UpdateLayout();
                var numeric = Field<ComboBox>(typed, "_count"); numeric.ApplyTemplate();
                var input = (TextBox)numeric.Template.FindName("PART_EditableTextBox", numeric);
                var ids = typed.Draft.Ends[0].Pins.Select(p => p.PinId).ToArray();
                input.SelectAll(); input.Text = "1";
                Require(ids.SequenceEqual(typed.Draft.Ends[0].Pins.Select(p => p.PinId)), "Typing the first digit removed stable Pins before commit.");
                input.Text = "12";
                Require(typed.Draft.Ends[0].Pins.Count == 4, "Typed Pin count applied before commit.");
                typeof(CableManufacturingTableEditor).GetMethod("ApplyCount", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(typed, [null]);
                Require(typed.Draft.Ends[0].Pins.Count == 12 && ids.SequenceEqual(typed.Draft.Ends[0].Pins.Take(4).Select(p => p.PinId)), "Committing typed count did not preserve original Pin IDs.");
                var other = Children(typed).OfType<ComboBox>().Where(c => c.ToolTip?.ToString() == "Pin 數 (Alt+Enter)").Skip(1).First();
                Require(other.SelectedItem is int value && value == 1 && typed.Draft.Ends[0].Pins.Count == 12,
                    "Another endpoint reused the first endpoint's numeric selection.");
            }
            if (mode is "all" or "count")
            {
                var count = Field<ComboBox>(editor, "_count"); count.SelectedItem = 2;
                Require(editor.Draft.Ends[0].Pins.Count == 2, "Selecting Pin count did not apply it immediately.");
                Require(editor.Draft.Ends.Count == 2, "Changing Pin count added another connector.");
            }
            if (mode is "all" or "delete")
            {
                Call(editor, "AddEnd"); Require(editor.Draft.Ends.Count == 3, "Add connector did not create an endpoint.");
                var deleted = editor.Draft.Ends[2].Pins.Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
                Call(editor, "DeleteEnd"); Require(editor.Draft.Ends.Count == 2, "An empty new endpoint could not be deleted.");
                Require(editor.Draft.Rows.All(r => r.FromSourcePinId is null || !deleted.Contains(r.FromSourcePinId)), "Deleted Pin remained in table.");
            }
            if (mode is "all" or "confirm")
            {
                var mapped = JsonSerializer.Deserialize<ArchivedCableTemplate>(JsonSerializer.Serialize(template))!;
                mapped.Ports[0].Pins.Add(new() { PinId = "pin1.2", PinNumber = "2" });
                mapped = mapped with { Mapping = [new("pin1.2", "pin2")] };
                var pending = new CableManufacturingEditorService().Prepare(mapped);
                var bound = new HashSet<string> { "pin1.2" }; var prompts = 0;
                var cancel = new CableManufacturingTableEditor(pending, choices, boundPins: () => bound,
                    confirmRemoval: _ => { prompts++; return false; });
                var before = JsonSerializer.Serialize(cancel.Draft);
                Field<ComboBox>(cancel, "_count").SelectedItem = 1;
                Require(prompts == 1 && before == JsonSerializer.Serialize(cancel.Draft), "Cancelling Pin removal changed draft data.");
                var accept = new CableManufacturingTableEditor(pending, choices, boundPins: () => bound, confirmRemoval: _ => true);
                accept.PinsRemoved += removed => bound.ExceptWith(removed);
                Field<ComboBox>(accept, "_count").SelectedItem = 1;
                Require(bound.Count == 0 && accept.Draft.Ends[0].Pins.Count == 1, "Confirmed removal did not reconcile CAD binding.");
                Require(accept.Draft.Rows.All(r => r.FromSourcePinId != "pin1.2" && r.ToSourcePinId != "pin1.2"), "Removed Pin is still referenced.");
                var next = new CableManufacturingEditorService().Apply(mapped, accept.Draft);
                Require(next.Mapping.Count == 0 && !next.MappingConfirmed, "Removed Pin still has confirmed continuity.");
                var locked = new CableManufacturingTableEditor(pending, choices, allowInventoryChanges: false);
                Call(locked, "AddEnd"); Call(locked, "DeleteEnd");
                Field<ComboBox>(locked, "_count").SelectedItem = 1;
                Require(locked.Draft.Ends.Count == 2 && locked.Draft.Ends[0].Pins.Count == 2, "Placed instance inventory changed.");
            }
            if (mode is "all" or "families")
            {
                Require(choices.Any(c => c.Label == "M12") && choices.Any(c => c.Label == "RJ45"), "Common connector families are missing without a BOM.");
                Require(choices.All(c => !c.Label.Contains(" / ")), "Connector choices still contain BOM models or Ports.");
                var coding = Children(editor).OfType<ComboBox>().FirstOrDefault(c => c.ToolTip?.ToString() == "接頭 Coding");
                Require(coding is not null, "Coding is not a dropdown.");
                var families = Children(editor).OfType<ComboBox>().First(c => c.ToolTip?.ToString() == "接頭形式");
                families.SelectedItem = families.Items.Cast<CableConnectorChoice>().Single(c => c.Label == "M12");
                Require(coding!.Items.Cast<object>().Any(c => c.ToString() == "D-code"), "M12 Coding dropdown lacks D-code.");
                coding.SelectedItem = "D-code";
                Require(editor.Draft.Ends[0].Connector?.Family == "M12" && editor.Draft.Ends[0].Connector?.Coding == "D", "Family/Coding selections were not saved in the draft.");
                Require(editor.Draft.Ends[0].Pins[0].PinId == "pin1", "Connector selection replaced a stable Pin identity.");
                Require(editor.Draft.Rows.All(r => r.FromSourcePinId is null || r.ToSourcePinId is null), "A family selection inferred continuity.");
                families.SelectedItem = families.Items.Cast<CableConnectorChoice>().Single(c => c.Label == "RJ45");
                Require(editor.Draft.Ends[0].Connector?.Coding is null, "M12 Coding leaked into RJ45.");
            }
            Require(original == JsonSerializer.Serialize(template), "Editor changed shared source template.");
            if (root is not null)
            {
                Directory.CreateDirectory(root);
                editor.Background = Brushes.White;
                editor.Measure(new Size(920, 460)); editor.Arrange(new Rect(0, 0, 920, 460)); editor.UpdateLayout();
                var bitmap = new RenderTargetBitmap(920, 460, 96, 96, PixelFormats.Pbgra32); bitmap.Render(editor);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(root, "connector-editor.png")); png.Save(output);
            }
            Console.WriteLine("PASS " + mode); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void WireSmoke(string? root)
    {
        var project = new ElectricalProject { ProjectId = "offscreen-wire", Name = "Wire settings draft",
            Schematic = new() { Pages = [new() { PageId = "page", Title = "Supply" }] } };
        project.Components.Add(new() { ComponentInstanceId = "io", ComponentDefinitionId = "io", TypeKey = "IO-Link Master",
            Ports = [new() { PortId = "X1", Name = "X1", Pins = [new() { PinId = "A", PinNumber = "1", Layer = ElectricalLayer.Power,
                Power = new() { Role = PowerRole.Source, Voltage = new() { Type = VoltageType.Dc, NominalVoltage = 24 } } }] }] });
        project.Components.Add(new() { ComponentInstanceId = "sensor", ComponentDefinitionId = "sensor", TypeKey = "Sensor",
            Ports = [new() { PortId = "P1", Name = "P1", Pins = [new() { PinId = "B", PinNumber = "1", Layer = ElectricalLayer.Power,
                Power = new() { Role = PowerRole.Input, RequiredCurrentAmp = .2, Voltage = new() { Type = VoltageType.Dc, NominalVoltage = 24 } } }] }] });
        project.Schematic.Symbols.Add(new() { SymbolId = "S1", PageId = "page", ComponentInstanceId = "io", Position = new(20, 40), Width = 40, Height = 30,
            Anchors = [new() { EndpointId = "A", Position = new(40, 15), Direction = "Right" }] });
        project.Schematic.Symbols.Add(new() { SymbolId = "S2", PageId = "page", ComponentInstanceId = "sensor", Position = new(190, 40), Width = 40, Height = 30,
            Anchors = [new() { EndpointId = "B", Position = new(0, 15), Direction = "Left" }] });
        var service = new SchematicAuthoringService();
        project = service.DrawWire(project, "page", SchematicAttachment.Pin("S1", "A"), SchematicAttachment.Pin("S2", "B"), [new(60, 55), new(190, 55)]);
        var wireId = project.Schematic!.Wires[0].WireId;
        var before = JsonSerializer.Serialize(project); var commits = 0;
        var dialog = new SchematicWireSettingsDialog(project, wireId, update => { project = update(project); commits++; return true; });
        var mode = Field<ComboBox>(dialog, "_mode"); var area = Field<ComboBox>(dialog, "_area");
        mode.SelectedIndex = 3; area.Text = ".75";
        Require(before == JsonSerializer.Serialize(project) && commits == 0, "Size preview mutated the project.");
        Field<TextBox>(dialog, "_name").Text = "IOLink1_X1_1";
        Require(dialog.TryApply() && commits == 1 && project.Schematic!.Wires[0].AreaMm2 == .75, "Metric/name apply did not commit once.");
        var applied = JsonSerializer.Serialize(project); area.Text = "invalid";
        Require(!dialog.TryApply() && applied == JsonSerializer.Serialize(project) && commits == 1, "Invalid area changed saved data.");
        mode.SelectedIndex = 1;
        area.Text = ".5";
        Require(dialog.TryApply() && project.Schematic!.Wires[0].SizingProposal is not null && project.Schematic.Wires[0].AreaMm2 == .5,
            "Automatic selection did not restore a pending proposal.");
        project = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(project))!;
        before = JsonSerializer.Serialize(project);
        var workspace = new SchematicWorkspaceControl(() => project, (_, _) => throw new InvalidOperationException("Unexpected mutation"),
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        workspace.RefreshWorkspace();
        var sheet = (Canvas)typeof(SchematicWorkspaceControl).GetProperty("Sheet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        Require(Children(sheet).OfType<TextBlock>().Any(t => t.Text.Contains("IOLink1_X1_1") && t.Text.Contains("mm²") && t.Text.Contains("AWG")),
            "Actual canvas did not render the general wire specification.");
        if (root is not null)
        {
            Directory.CreateDirectory(root);
            var content = (FrameworkElement)dialog.Content;
            content.Measure(new Size(490, 440)); content.Arrange(new Rect(0, 0, 490, 440)); content.UpdateLayout();
            var settings = new RenderTargetBitmap(490, 440, 96, 96, PixelFormats.Pbgra32); settings.Render(content);
            SaveBitmap(settings, Path.Combine(root, "wire-settings.png"));
            var render = typeof(SchematicWorkspaceControl).GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
            BitmapSource Page(ElectricalProject p, SchematicPage page) => (BitmapSource)render.Invoke(workspace, [p, page])!;
            var pageImage = Page(project, project.Schematic!.Pages[0]);
            SaveBitmap(pageImage, Path.Combine(root, "wire-page.png"));
            var pdf = Path.Combine(root, "wire-specification.pdf");
            Require(SchematicPdfWriter.Write(project, pdf, Page) == 1, "Wire PDF page missing.");
            using var saved = PdfReader.Open(pdf, PdfDocumentOpenMode.Import);
            Require(saved.PageCount == 1 && saved.Info.Title.Contains("DRAFT"), "Wire PDF lost draft status.");
            Require(before == JsonSerializer.Serialize(project), "Rendering changed saved wire metadata.");
        }
        dialog.Close();
    }

    private static void SaveBitmap(BitmapSource bitmap, string path)
    {
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); png.Save(output);
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Call(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Children(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
