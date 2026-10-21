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
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Schematic;

internal static partial class Program
{
    private static int PinSizingWorkflowProof(string root)
    {
        Directory.CreateDirectory(root);
        var project = new ElectricalProject { ProjectId = "pin-sizing", Schematic = new() { Pages = [new() { PageId = "P", Title = "Pin functions" }] } };
        foreach (var id in new[] { "A", "B" })
        {
            var port = new ComponentIntelligence.Electrical.Domain.ComponentPort { PortId = id + "P", Name = "LONG_CONTROL_LEADS", Pins = [new() {
                PinId = id + "1", PinNumber = "1", Function = "Common for 24 V supply and non-isolated communication reference", PinName = "COM / black",
                Power = new() { Role = id == "A" ? PowerRole.Input : PowerRole.Source, RequiredCurrentAmp = id == "A" ? .2 : null,
                    Voltage = new() { Type = VoltageType.Dc, NominalVoltage = 24 } } }] };
            project.Components.Add(new() { ComponentInstanceId = id, ComponentDefinitionId = id, TypeKey = id == "A" ? "Sensor" : "PLC",
                ReferenceDesignator = id == "A" ? "MyValve" : "CabinetIO", Ports = [port] });
            project.Schematic!.Symbols.Add(new() { SymbolId = id, ComponentInstanceId = id, PageId = "P", Position = new(id == "A" ? 30 : 280, 45), Width = 40, Height = 60,
                Anchors = [new() { EndpointId = id + "1", Position = new(id == "A" ? 40 : 0, 30), Direction = id == "A" ? "Right" : "Left" }] });
        }
        var service = new SchematicAuthoringService();
        project = service.DrawWire(project, "P", SchematicAttachment.Pin("A", "A1"), SchematicAttachment.Pin("B", "B1"), [new(70, 75), new(280, 75)]);
        var idWire = project.Schematic!.Wires[0].WireId;
        var workspace = new SchematicWorkspaceControl(() => project, (next, _) => project = next,
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var owner = new Window { Content = workspace };
        var type = typeof(SchematicWorkspaceControl); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        object? Call(string name, params object[] args) => type.GetMethod(name, flags)!.Invoke(workspace, args);
        type.GetField("_pageId", flags)!.SetValue(workspace, "P"); type.GetField("_selectionId", flags)!.SetValue(workspace, idWire);
        Call("Render", project);
        var sheet = (Canvas)type.GetProperty("Sheet", flags)!.GetValue(workspace)!;
        sheet.Measure(new(sheet.Width, sheet.Height)); sheet.Arrange(new(0, 0, sheet.Width, sheet.Height));
        var labels = sheet.Children.OfType<TextBlock>().Where(t => Equals(t.Tag, "wire-label:" + idWire)).ToArray();
        Require(labels.Any(t => t.Text.Contains("1_COM → 1_COM") && t.Text.Contains("AWG 20")), "Wire does not show both Pin functions and the available AWG proposal.");
        Require(labels.All(t => !t.Text.Contains("MyValve") && !t.Text.Contains("LONG_CONTROL") && !t.Text.Contains("G1") && !t.Text.Contains("待核對")), "Old verbose label content remains.");
        Require(sheet.Children.OfType<TextBlock>().Any(t => t.Text == "MyValve"), "Reference disappeared from component body.");
        foreach (var text in labels)
        { var box = new Rect(Canvas.GetLeft(text), Canvas.GetTop(text), text.DesiredSize.Width, text.DesiredSize.Height); Require(!box.IntersectsWith(new Rect(70 * 3, 75 * 3 - 2, 210 * 3, 4)), "Caption covers the conductor."); }
        var inspectorButtons = ((StackPanel)type.GetField("WireInspectorFields", flags)!.GetValue(workspace)!).Children.OfType<Button>().ToArray();
        Require(inspectorButtons.Any(b => Equals(b.Content, "採用建議") && b.IsEnabled) && inspectorButtons.Any(b => Equals(b.Content, "查看依據")) &&
            inspectorButtons.Any(b => Equals(b.Content, "手動修改")), "Inspector sizing workflow actions are missing.");
        var dialog = new SchematicWireSettingsDialog(project, idWire, command => { project = command(project); return true; }, showBasis: true);
        var dialogType = typeof(SchematicWireSettingsDialog);
        var accept = dialogType.GetField("_accept", flags)?.GetValue(dialog) as Button;
        Require(accept is { IsEnabled: true }, "AWG dialog has no enabled adoption action.");
        var rejectedDialog = new SchematicWireSettingsDialog(project, idWire, _ => false);
        Require(!rejectedDialog.TryAcceptSuggestion() && ((TextBlock)dialogType.GetField("_status", flags)!.GetValue(rejectedDialog)!).Text.Contains("未採用"),
            "Rejected adoption has no visible feedback in its dialog.");
        var basis = dialogType.GetField("_basis", flags)?.GetValue(dialog) as TextBlock;
        Require(basis?.Text.Contains("LAPP") == true && basis.Text.Contains("0.2") && basis.Text.Contains("待核對"), "Sizing basis omits provenance, known load or unresolved conditions.");
        var accepted = (bool)dialogType.GetMethod("TryAcceptSuggestion", flags | BindingFlags.Public)!.Invoke(dialog, null)!;
        Require(accepted && project.Schematic!.Wires[0].AdoptedSizingProposal is not null && project.Schematic.Wires[0].AreaMm2 == .5, "Adoption did not retain exact metric size and basis.");
        Call("Render", project);
        Render(workspace, 1400, 900, Path.Combine(root, "pin-sizing-inspector.png"));
        Render((FrameworkElement)dialog.Content, 540, 650, Path.Combine(root, "sizing-basis.png"));
        var restored = ProjectSaveSession.Clone(project); Require(restored.Schematic!.Wires[0].AdoptedSizingProposal is not null, "Adoption evidence did not survive snapshot serialization.");
        project = service.SetWireAwg(project, idWire, 18); Require(project.Schematic!.Wires[0].AdoptedSizingProposal is null, "Manual override retained stale adoption state.");
        var unknown = service.SetWireAwg(project, idWire, null);
        unknown.Connections[0].ConductorAreaMm2 = null;
        var manual = new SchematicWireSettingsDialog(unknown, idWire, command => { unknown = command(unknown); return true; }, manual: true);
        var gauge = (ComboBox)dialogType.GetField("_awg", flags)!.GetValue(manual)!;
        Require(gauge.SelectedItem is null && !manual.TryApply() && unknown.Schematic!.Wires[0].Awg is null,
            "Opening manual edit on an unknown wire silently applied AWG 20.");
        SchematicPdfWriter.Write(project, Path.Combine(root, "pin-functions.pdf"), (p, page) => (BitmapSource)Call("RenderOutputPage", p, page)!);
        project = service.TransformSymbol(project, "B", new(280, 60), 0);
        var connections = JsonSerializer.Serialize(project.Connections);
        var anchors = JsonSerializer.Serialize(project.Schematic!.Symbols.Select(s => s.Anchors));
        Call("Apply", new Func<ElectricalProject, ElectricalProject>(p => service.SetPortFanout(p, "A", "AP", true)), "Expand first Port");
        Require(project.Schematic!.Symbols[1].EndpointDisplayPlacements.Count == 0, "First Port expansion unexpectedly expanded its peer.");
        Call("Apply", new Func<ElectricalProject, ElectricalProject>(p => service.SetPortFanout(p, "B", "BP", true)), "Expand second Port");
        Require(SchematicAuthoringService.AnchorPoint(project.Schematic!.Symbols[0], "A1").Y == SchematicAuthoringService.AnchorPoint(project.Schematic.Symbols[1], "B1").Y,
            "Expanding the second connected Port did not automatically align display contacts.");
        Require(project.Schematic.Wires[0].Points.Select(p => p.Y).Distinct().Count() == 1, "Automatically aligned contacts retained an unnecessary route corner.");
        Require(connections == JsonSerializer.Serialize(project.Connections) && anchors == JsonSerializer.Serialize(project.Schematic.Symbols.Select(s => s.Anchors)), "Automatic alignment changed electrical connections or native pins.");
        Render(workspace, 1400, 900, Path.Combine(root, "automatic-fanout-alignment.png"));
        type.GetField("_autoFanoutNotice", flags)!.SetValue(workspace, "Old alignment success");
        type.GetField("_gestureStart", flags)!.SetValue(workspace, project);
        type.GetField("_gesturePreview", flags)!.SetValue(workspace, null);
        type.GetField("_dragStart", flags)!.SetValue(workspace, new SchematicPoint(20, 20));
        type.GetField("_pointer", flags)!.SetValue(workspace, new SchematicPoint(25, 25));
        var status = (TextBlock)type.GetField("Status", flags)!.GetValue(workspace)!; status.Text = "Current drag could not be applied";
        Call("Sheet_Up", workspace, new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left));
        Require(!status.Text.Contains("Old alignment success"), "Stale automatic-alignment success hid this gesture failure.");
        File.WriteAllText(Path.Combine(root, "pin-sizing.json"), JsonSerializer.Serialize(new { Passed = true, BothPins = true, ImmediateSuggestion = true,
            ReferenceOnBody = true, AdoptionEvidence = true, ManualOverride = true, LabelsClearOfWire = true }));
        owner.Content = null; owner.Close();
        Console.WriteLine("PIN_SIZING_WORKFLOW_PASS"); return 0;
    }
}
