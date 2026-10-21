using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Repository;
using Microsoft.Data.Sqlite;
using ComponentPort = ComponentIntelligence.Electrical.Domain.ComponentPort;

internal static partial class Program
{
    private static int SketchFrameProof(string root, string? existingDatabase)
    {
        Directory.CreateDirectory(root);
        var template = new ArchivedCableTemplate { TemplateId = "synthetic-frame", TemplateRevision = "r1", AssetSha256 = new('a', 64),
            Ports = Enumerable.Range(1, 2).Select(i => new ComponentPort { PortId = "P" + i, Name = "P" + i,
                Pins = [new() { PinId = "p" + i + ".1", PinNumber = "1" }] }).ToList() };
        var wire = new SchematicCadAsset { SourceSha256 = template.AssetSha256, MillimetresPerUnit = 1, Width = 100, Height = 20,
            Primitives = [new() { Kind = "LINE", Start = new(0, 10), End = new(100, 10) }] };
        var sketch = wire with { SourceSha256 = new('b', 64), Primitives = [
            new() { Kind = "LINE", Start = new(10, 15), End = new(150, 15) },
            new() { Kind = "CIRCLE", Start = new(10, 15), Radius = 6 },
            new() { Kind = "ARC", Start = new(150, 15), Radius = 6, StartAngle = 0, EndAngle = 40 },
            new() { Kind = "TEXT", Start = new(10, 7), Text = "P1", TextHeight = 3 },
            new() { Kind = "TEXT", Start = new(150, 7), Text = "P2", TextHeight = 3, Rotation = 25 },
            new() { Kind = "TEXT", Start = new(-100, -70), Text = "", TextHeight = 3 }
        ] };
        var service = new SchematicAuthoringService();
        var current = service.AddArchivedCable(new() { ProjectId = "synthetic-frame", Name = "SYNTHETIC FRAME TEST",
            Schematic = new() { Pages = [new() { PageId = "main", Title = "Main" }] } }, template, CableConstructionType.Custom, wire,
            new Dictionary<string, string>(), "main", new(30, 50), manufacturingGeometry: sketch);
        var details = new SchematicCableDetailService();
        var layout = new SchematicCableDetailBinding(current.Cables[0].CableInstanceId, null)
        { TableVisible = true, SketchVisible = true, SketchPosition = new(20, 110), SketchWidth = 180, SketchHeight = 70 };
        current = details.AddArchivedDetail(current, current.Cables[0].CableInstanceId, "main", layout);
        var page = current.Schematic!.Pages.Last(); var binding = page.CableDetail!;
        var snapshot = JsonSerializer.Serialize(current); var cables = JsonSerializer.Serialize(current.Cables);
        var history = new ProjectMutationHistory(); var commits = 0;
        var workspace = new SchematicWorkspaceControl(() => current, (next, label) => {
            history.RecordBeforeMutation(current, label); current = next; commits++;
        }, () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var owner = new Window { Content = workspace };
        var type = typeof(SchematicWorkspaceControl);
        void Set(string field, object? value) => type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, value);
        void Call(string method, params object?[] arguments) => type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, arguments);
        Set("_pageId", page.PageId); Set("_selectionId", "detail:Sketch:" + binding.DetailId);
        var frame = CheckFrame(workspace, current, page, binding, Path.Combine(root, "tight-frame.png"));
        var sheet = (Canvas)type.GetProperty("Sheet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        var table = sheet.Children.OfType<Rectangle>().Single(r => Equals(r.Tag, SchematicCableDetailPart.Table));
        var tableBox = details.Build(current, page).PartBounds[SchematicCableDetailPart.Table];
        Require(Math.Abs(table.Width - tableBox.Width * 3) < .01 && Math.Abs(table.Height - tableBox.Height * 3) < .01,
            "Sketch fitting changed the table selection frame.");
        var start = new SchematicPoint(frame.Right / 3, frame.Bottom / 3);
        Set("_gestureStart", current); Set("_dragStart", start); Set("_pointer", start);
        Set("_dragDetailId", binding.DetailId); Set("_dragDetailPart", SchematicCableDetailPart.Sketch); Set("_dragDetailResize", true);
        Call("PreviewDetailGesture");
        var preview = (ElectricalProject)type.GetField("_gesturePreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        Require(JsonSerializer.Serialize(preview.Schematic!.Pages.Last().CableDetail) == JsonSerializer.Serialize(binding),
            "Grabbing the visible corner without moving it changed the sketch layout.");
        Set("_pointer", new SchematicPoint(start.X + frame.Width / 12, start.Y + frame.Height / 12));
        Call("PreviewDetailGesture");
        preview = (ElectricalProject)type.GetField("_gesturePreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        var resized = preview.Schematic!.Pages.Last().CableDetail!;
        Require(Math.Abs(resized.SketchWidth / binding.SketchWidth - 1.25) < .001 &&
            Math.Abs(resized.SketchHeight / binding.SketchHeight - 1.25) < .001, "Visible-corner resize did not scale both sketch limits.");
        Require(commits == 0 && !history.CanUndo, "Sketch resize preview committed a mutation.");
        Call("Sheet_Up", sheet, new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
            System.Windows.Input.MouseButton.Left));
        Require(commits == 1 && JsonSerializer.Serialize(current.Cables) == cables, "Resize did not commit once or changed source authority.");
        Require(history.TryUndo(current, out var undone, out _) && JsonSerializer.Serialize(undone) == snapshot, "Sketch resize undo lost the layout.");
        Require(history.TryRedo(undone, out current, out _), "Sketch resize redo failed.");
        page = current.Schematic!.Pages.Last(); binding = page.CableDetail!;
        CheckFrame(workspace, current, page, binding, Path.Combine(root, "resized-frame.png"));
        Set("_gestureStart", current); Set("_dragStart", new SchematicPoint(30, 120)); Set("_pointer", new SchematicPoint(40, 130));
        Set("_dragDetailId", binding.DetailId); Set("_dragDetailPart", SchematicCableDetailPart.Sketch); Set("_dragDetailResize", false);
        Call("PreviewDetailGesture");
        Call("Sheet_Up", sheet, new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
            System.Windows.Input.MouseButton.Left));
        Require(commits == 2 && current.Schematic!.Pages.Last().CableDetail!.SketchPosition == new SchematicPoint(30, 120),
            "Tight selection frame broke dragging.");
        SQLitePCL.Batteries_V2.Init();
        var dbPath = Path.Combine(root, "synthetic-frame.db");
        if (File.Exists(dbPath)) throw new IOException("Use a fresh smoke directory.");
        var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), dbPath);
        repository.SaveAsync(current).GetAwaiter().GetResult(); current = repository.GetAsync(current.ProjectId).GetAwaiter().GetResult()!;
        page = current.Schematic!.Pages.Last(); binding = page.CableDetail!;
        CheckFrame(workspace, current, page, binding, null);
        Require(JsonSerializer.Serialize(current.Cables) == cables, "Save/reopen changed native geometry or engineering data.");
        var removed = details.RemovePart(current, page.PageId, binding.DetailId, SchematicCableDetailPart.Sketch);
        Require(!removed.Schematic!.Pages.Last().CableDetail!.SketchVisible && removed.Schematic.Pages.Last().CableDetail!.TableVisible,
            "Deleting the sketch also removed its table.");
        current = details.SetLayout(current, page.PageId, binding.DetailId, binding with { TableVisible = false,
            SketchPosition = new(200, 110), SketchWidth = 180, SketchHeight = 300 });
        page = current.Schematic!.Pages.Last(); binding = page.CableDetail!;
        frame = CheckFrame(workspace, current, page, binding, null);
        start = new(frame.Right / 3, frame.Bottom / 3);
        Set("_gestureStart", current); Set("_dragStart", start);
        Set("_pointer", new SchematicPoint(start.X + frame.Width / 60, start.Y + frame.Height / 60));
        Set("_dragDetailId", binding.DetailId); Set("_dragDetailPart", SchematicCableDetailPart.Sketch); Set("_dragDetailResize", true);
        Call("PreviewDetailGesture");
        preview = (ElectricalProject)type.GetField("_gesturePreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        Require(Math.Abs(preview.Schematic!.Pages.Last().CableDetail!.SketchWidth - 189) < .001,
            "Enlarging visible content was blocked by an unused maximum height outside the page.");
        CheckFrame(workspace, preview, preview.Schematic.Pages.Last(), preview.Schematic.Pages.Last().CableDetail!, Path.Combine(root, "edge-enlarged-frame.png"));
        Require(JsonSerializer.Serialize(preview.Cables) == cables, "Edge enlargement changed source geometry.");
        Call("CancelGesture");
        owner.Content = null; owner.Close();
        Console.WriteLine("PASS tight ink frame, rotated/empty labels, independent table, no-jump corner resize, edge enlargement with unused max height, drag, undo/redo, SQLite reopen and preserved source.");
        if (existingDatabase is not null) CheckSavedFrames(existingDatabase, root);
        return 0;
    }

    private static Rect CheckFrame(SchematicWorkspaceControl workspace, ElectricalProject project, SchematicPage page,
        SchematicCableDetailBinding binding, string? output)
    {
        var type = typeof(SchematicWorkspaceControl);
        type.GetField("_selectionId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, "detail:Sketch:" + binding.DetailId);
        type.GetMethod("RenderPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, [project, page]);
        var sheet = (Canvas)type.GetProperty("Sheet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        var content = new SchematicCableDetailService().BuildArchived(project, page, binding with { TableVisible = false });
        var asset = new SchematicCadAsset { SourceSha256 = "", Width = page.Width, Height = page.Height, Primitives = content.Primitives };
        var canvas = (Canvas)type.GetMethod("CadCanvas", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [asset, null, 1d, null, false])!;
        canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
        var ink = Rect.Empty;
        foreach (var child in canvas.Children.OfType<FrameworkElement>())
        {
            if (child is TextBlock text && string.IsNullOrWhiteSpace(text.Text)) continue;
            var bounds = VisualTreeHelper.GetDescendantBounds(child);
            if (!bounds.IsEmpty) ink.Union(child.TransformToAncestor(canvas).TransformBounds(bounds));
        }
        var surface = sheet.Children.OfType<Rectangle>().Single(r => Equals(r.Tag, SchematicCableDetailPart.Sketch));
        var frame = new Rect(Canvas.GetLeft(surface), Canvas.GetTop(surface), surface.Width, surface.Height);
        Require(frame.Contains(ink) && Math.Abs(frame.Width - ink.Width - 6) < .05 && Math.Abs(frame.Height - ink.Height - 6) < .05,
            $"Sketch frame {frame} still contains unnecessary blank space around ink {ink}.");
        var handle = sheet.Children.OfType<Button>().Single(b => b.ToolTip?.ToString() == "製作示意圖位置／尺寸");
        Require(Math.Abs(Canvas.GetTop(handle) - frame.Top) < .01 && Math.Abs(Canvas.GetLeft(handle) - (frame.Left - 24)) < .01,
            "Sketch edit handle stayed in the old blank area.");
        if (output is not null)
        {
            sheet.Measure(new Size(sheet.Width, sheet.Height)); sheet.Arrange(new Rect(0, 0, sheet.Width, sheet.Height));
            var image = new RenderTargetBitmap((int)Math.Ceiling(sheet.Width), (int)Math.Ceiling(sheet.Height), 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var context = background.RenderOpen()) context.DrawRectangle(Brushes.White, null, new Rect(0, 0, sheet.Width, sheet.Height));
            image.Render(background);
            image.Render(sheet); Save(image, output);
        }
        return frame;
    }

    private static void CheckSavedFrames(string path, string root)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path),
            Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT SnapshotJson FROM ElectricalProjects";
        using var reader = command.ExecuteReader();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        var count = 0;
        while (reader.Read())
        {
            var saved = JsonSerializer.Deserialize<ElectricalProject>(reader.GetString(0), options)!;
            var before = JsonSerializer.Serialize(saved);
            foreach (var cable in saved.Cables.Where(c => c.ArchivedCable?.ManufacturingGeometry is not null))
            {
                var details = new SchematicCableDetailService(); var page = saved.Schematic!.Pages[0];
                var binding = new SchematicCableDetailBinding(cable.CableInstanceId, null) { TableVisible = false };
                var next = details.PlaceArchivedDetail(saved, cable.CableInstanceId, page.PageId, binding.TablePosition, binding.SketchPosition, binding);
                var selected = next.Schematic!.Pages[0].InlineCableDetails.Last();
                var workspace = new SchematicWorkspaceControl(() => next, (_, _) => throw new InvalidOperationException("Read-only preview cannot commit."),
                    () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
                CheckFrame(workspace, next, next.Schematic.Pages[0], selected, Path.Combine(root, $"saved-frame-{++count}.png"));
            }
            Require(before == JsonSerializer.Serialize(saved), "Read-only preview changed saved source.");
        }
        Require(count > 0, "No existing manufacturing sketch frame was checked.");
        Console.WriteLine($"PASS {count} existing manufacturing sketch frames; readonly source, no native window.");
    }
}
