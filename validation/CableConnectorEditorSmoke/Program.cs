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
                Field<ComboBox>(typed, "_ends").SelectedIndex = 1;
                Require(numeric.SelectedItem is int value && value == 1, "Switching endpoints retained another endpoint's numeric selection.");
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
                var coding = Children(editor).OfType<ComboBox>().SingleOrDefault(c => c.ToolTip?.ToString() == "接頭 Coding");
                Require(coding is not null, "Coding is not a dropdown.");
                var families = Children(editor).OfType<ComboBox>().Single(c => c.ToolTip?.ToString() == "接頭形式");
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
