using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Electrical.Editing;
using Path = System.IO.Path;
using ComponentPort = ComponentIntelligence.Electrical.Domain.ComponentPort;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Skip(1).FirstOrDefault() == "text") RunTextRegression();
            else if (args.Skip(1).FirstOrDefault() == "read-only") RunReadOnlyRegression(Path.GetFullPath(args[2]));
            else Run(Path.GetFullPath(args[0]));
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static void Run(string root)
    {
        Directory.CreateDirectory(root);
        var service = new SchematicAuthoringService();
        var template = new ArchivedCableTemplate { TemplateId = "synthetic", TemplateRevision = "r1", AssetSha256 = new('a', 64),
            Ports = Enumerable.Range(1, 2).Select(i => new ComponentPort { PortId = "P" + i, Name = "P" + i,
                Pins = [new() { PinId = "p" + i + ".1", PinNumber = "1" }] }).ToList() };
        SchematicCadAsset Asset(double y) => new() { SourceSha256 = template.AssetSha256, MillimetresPerUnit = 1, Width = 100, Height = 40,
            Primitives = [new() { Kind = "LINE", Start = new(0, y), End = new(100, y) },
                new() { Kind = "TEXT", Start = new(5, 10), Text = "CAD TEXT", TextHeight = 3 },
                new() { Kind = "TEXT", Start = new(20, 10), Text = "MICRO CAD TEXT", TextHeight = 8.999999977500001e-6 / 3 },
                new() { Kind = "MTEXT", Start = new(30, 10), Text = "MICRO\nMTEXT", TextHeight = 8.999999977500001e-6 / 3, TextWidth = .00003 }],
            ConnectionPoints = [new("CAD1", "", new(0, y), Direction: "Left"), new("CAD2", "", new(100, y), Direction: "Right")] };
        var project = new ElectricalProject { ProjectId = "synthetic-cad", Schematic = new() { Pages = [new() { PageId = "page", Title = "Synthetic", Width = 210, Height = 148 }] } };
        var bindings = new Dictionary<string, string> { ["P1"] = "CAD1", ["P2"] = "CAD2" };
        project = service.AddArchivedCable(project, template, CableConstructionType.Unknown, Asset(30), bindings, "page", new(20, 20), assetApproved: true);
        project = service.AddArchivedCable(project, template, CableConstructionType.Unknown, Asset(0), bindings, "page", new(40, 30), assetApproved: true);
        var history = new ProjectMutationHistory(); var commits = 0;
        var control = new SchematicWorkspaceControl(() => project, (p, label) => { history.RecordBeforeMutation(project, label); project = p; commits++; },
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var window = new Window { Content = control }; // Offscreen focus scope only; never shown.
        control.RefreshWorkspace();
        var sheet = (Canvas)typeof(SchematicWorkspaceControl).GetProperty("Sheet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        sheet.Measure(new Size(sheet.Width, sheet.Height)); sheet.Arrange(new Rect(0, 0, sheet.Width, sheet.Height)); sheet.UpdateLayout();
        var bitmap = Output(control, project);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(System.IO.Path.Combine(root, "transparent-overlap.png"))) encoder.Save(file);
        var x = (int)(80 / 25.4 * 300); var y = (int)(50 / 25.4 * 300);
        var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        Require(pixel[0] < 160 && pixel[1] < 160 && pixel[2] < 160, "Upper CAD blank area hides the lower line in output.");
        var hit = VisualTreeHelper.HitTest(sheet, new Point(80 * 3, 50 * 3))?.VisualHit;
        Require(hit is Line line && line.Y1 == 90, "Upper CAD blank area blocks selecting the lower line.");
        Console.WriteLine("PASS: transparent output and blank-region hit testing.");
        var id = project.Schematic!.Symbols[1].SymbolId;
        Set(control, "_selectionId", id); Set(control, "_editModuleMode", true); control.RefreshWorkspace();
        Require(((StackPanel)control.FindName("CadScalePanel")).Visibility == Visibility.Visible, "CAD scale panel is absent.");
        Require(sheet.Children.OfType<Border>().Any(b => b.Cursor == Cursors.SizeNWSE), "Selected CAD resize corner is absent.");
        var before = JsonSerializer.Serialize(project);
        ((TextBox)control.FindName("CadScaleText")).Text = "50";
        ((Button)control.FindName("CadScaleApply")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(commits == 1 && project.Schematic!.Symbols[1].Width == 50, "Percentage apply did not commit one absolute scale edit.");
        var resized = JsonSerializer.Serialize(project);
        Require(history.TryUndo(project, out project, out _) && JsonSerializer.Serialize(project) == before, "Scale undo lost native data.");
        Require(history.TryRedo(project, out project, out _) && JsonSerializer.Serialize(project) == resized, "Scale redo differs from committed data.");
        control.RefreshWorkspace();
        ((Button)control.FindName("CadScaleReset")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(project.Schematic!.Symbols[1].Width == 100 && commits == 2, "Reset did not restore native display size.");
        ((TextBox)control.FindName("CadScaleText")).Text = "NaN";
        ((Button)control.FindName("CadScaleApply")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(commits == 2, "Invalid scale input committed a mutation.");
        var selected = Output(control, project);
        selected.CopyPixels(new Int32Rect((int)(40 / 25.4 * 300), (int)(55 / 25.4 * 300), 1, 1), pixel, 4, 0);
        Require(pixel[0] > 240 && pixel[1] > 240 && pixel[2] > 240, "Selection outline leaked into exported page.");
        var wiresBefore = JsonSerializer.Serialize(project.Cables);
        Set(control, "_gestureStart", project); Set(control, "_dragResizeSymbol", id);
        Set(control, "_dragStart", new SchematicPoint(140, 70)); Set(control, "_pointer", new SchematicPoint(100, 50));
        Invoke(control, "PreviewSymbolResize");
        var preview = (ElectricalProject)Get(control, "_gesturePreview")!;
        Require(project.Schematic!.Symbols[1].Width == 100 && preview.Schematic!.Symbols[1].Width == 50, "Drag preview mutated saved project or used generic resize.");
        Invoke(control, "Sheet_Up", sheet, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left));
        Require(commits == 3 && project.Schematic!.Symbols[1].Width == 50, "CAD resize gesture did not commit exactly once.");
        Require(JsonSerializer.Serialize(project.Cables) == wiresBefore, "CAD display resizing edited engineering cable data.");
        var p2 = project.Cables[1].ArchivedCable!.Ports[1].PortId;
        Require(SchematicPortPresentation.ConnectionPoint(project, project.Schematic.Symbols[1], p2) == new SchematicPoint(90, 30), "Port did not follow drag resize.");
        SchematicAuthoringService.Validate(project);
        SchematicPdfWriter.Write(project, Path.Combine(root, "transparent-scaled.pdf"), (p, _) => Output(control, p));
        var cadCanvas = typeof(SchematicWorkspaceControl).GetMethod("CadCanvas", BindingFlags.Static | BindingFlags.NonPublic)!;
        var tiny = SchematicSymbolPresentation.Geometry(service.ScaleCadSymbol(project, id, .05).Schematic!.Symbols[1], null)!;
        var textCanvas = (Canvas)cadCanvas.Invoke(null, [tiny, null, 1d, null, false])!;
        Require(Math.Abs(textCanvas.Children.OfType<TextBlock>().Single(t => t.Text == "CAD TEXT").FontSize - .45) < 1e-9, "Small CAD scale is clamped to an incorrect text size.");
        Console.WriteLine("PASS: CAD panel, resize corner, absolute scale/reset, invalid input, undo/redo, drag preview/commit, Port position, PDF without selection outline.");
        window.Content = null;
    }

    private static BitmapSource Output(SchematicWorkspaceControl control, ElectricalProject project) =>
        (BitmapSource)typeof(SchematicWorkspaceControl).GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(control, [project, project.Schematic!.Pages[0]])!;
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Get(object obj, string field) => obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Invoke(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
}
