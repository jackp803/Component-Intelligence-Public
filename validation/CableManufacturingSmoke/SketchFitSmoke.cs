using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Repository;
using Microsoft.Data.Sqlite;
using PdfSharp.Pdf.IO;
using ComponentPort = ComponentIntelligence.Electrical.Domain.ComponentPort;

internal static partial class Program
{
    private static int SketchFitProof(string root, string? existingDatabase)
    {
        Directory.CreateDirectory(root);
        var template = new ArchivedCableTemplate { TemplateId = "synthetic-fit", TemplateRevision = "r1", AssetSha256 = new('a', 64),
            TextBindings = [new("REF", CableTextField.Reference)],
            Ports = Enumerable.Range(1, 2).Select(i => new ComponentPort { PortId = "P" + i, Name = "P" + i,
                Pins = [new() { PinId = "p" + i + ".1", PinNumber = "1" }] }).ToList() };
        var wire = new SchematicCadAsset { SourceSha256 = template.AssetSha256, MillimetresPerUnit = 1, Width = 100, Height = 20,
            Primitives = [new() { Kind = "LINE", Start = new(0, 10), End = new(100, 10) }] };
        var sourceSketch = wire with { SourceSha256 = new('b', 64), Primitives = [
            new() { Kind = "LINE", Start = new(10, 15), End = new(100, 15) },
            new() { Kind = "CIRCLE", Start = new(10, 15), Radius = 6 },
            new() { Kind = "ARC", Start = new(100, 15), Radius = 6, StartAngle = 0, EndAngle = 180 },
            new() { Kind = "SOLID_HATCH", Start = new(35, 17), Contours = [new SchematicPoint[] { new(35, 17), new(40, 23), new(45, 17) }] },
            new() { Kind = "TEXT", Start = new(95, 0), Text = "REF", AttributeTag = "REF", TextHeight = 3 },
            new() { Kind = "MTEXT", Start = new(60, 25), Text = "COMPLETE LABEL\nSECOND LINE", TextHeight = 3, TextWidth = 30, TextAttachment = "TopLeft" }
        ] };
        var project = new SchematicAuthoringService().AddArchivedCable(new() { ProjectId = "synthetic-sketch-fit", Name = "SYNTHETIC FIT PROOF",
            Schematic = new() { Pages = [new() { PageId = "main", Title = "Main" }] } }, template, CableConstructionType.Custom, wire,
            new Dictionary<string, string>(), "main", new(30, 50), manufacturingGeometry: sourceSketch);
        project.Cables[0].ReferenceDesignator = "CB-COMPLETE-OUTER-REFERENCE";
        project.Cables[0].ProvidedLengthMm = 500;
        var details = new SchematicCableDetailService();
        SchematicCableDetailBinding Layout(ElectricalProject p) => new(p.Cables[0].CableInstanceId, null)
        { TableVisible = false, SketchVisible = true, SketchPosition = new(20, 110), SketchWidth = 180, SketchHeight = 70 };
        var before = JsonSerializer.Serialize(project.Cables);
        project = details.AddArchivedDetail(project, project.Cables[0].CableInstanceId, "main", Layout(project));
        Require(JsonSerializer.Serialize(project.Cables) == before, "Placement changed native cable data.");
        var baseArchive = project.Cables[0].ArchivedCable!;
        var textCases = 0;
        foreach (var kind in new[] { "TEXT", "MTEXT" })
        foreach (var attachment in kind == "TEXT" ? new[] { "BaselineLeft", "BaselineCenter", "BaselineRight", "TopLeft", "MiddleCenter", "BottomRight" } :
            new[] { "TopLeft", "TopCenter", "TopRight", "MiddleLeft", "MiddleCenter", "MiddleRight", "BottomLeft", "BottomCenter", "BottomRight", null })
        foreach (var angle in new[] { 0d, 45d, 90d, -90d, 180d })
        foreach (var factor in kind == "TEXT" ? new[] { .5, 1d, 2d } : new[] { 1d })
        {
            var text = new SchematicCadPrimitive { Kind = kind, Start = new(100, 0), Text = "完整文字 COMPLETE LABEL\nSECOND LINE",
                TextHeight = 3, TextWidth = kind == "MTEXT" ? 18 : 0, TextWidthFactor = factor, TextAttachment = attachment, Rotation = angle };
            project.Cables[0].ArchivedCable = baseArchive with { ManufacturingGeometry = sourceSketch with { Primitives = [text] } };
            var content = details.Build(project, project.Schematic!.Pages.Last());
            Require(content.Primitives.Single().Text == text.Text, "Fitting clipped or replaced source text.");
            try { CheckSketchInk(content, project.Schematic.Pages.Last()); }
            catch (InvalidOperationException e) { throw new InvalidOperationException($"{kind} {attachment} rotation={angle} factor={factor}: {e.Message}", e); }
            textCases++;
        }
        project.Cables[0].ArchivedCable = baseArchive;
        foreach (var attachment in new[] { "TopLeft", "TopCenter", "BottomRight" })
        foreach (var angle in new[] { 0d, 45d, 90d, -90d, 180d })
        foreach (var sample in new[] { (Text: string.Join(" ", Enumerable.Repeat("WWWWWWWWWW", 10)), Width: 54d), (Text: "W", Width: 1d) })
        {
            project.Cables[0].ArchivedCable = baseArchive with { ManufacturingGeometry = sourceSketch with { Primitives = [
                new() { Kind = "MTEXT", Start = new(100, 0), Text = sample.Text, TextHeight = 3,
                    TextWidth = sample.Width, TextAttachment = attachment, Rotation = angle }
            ] } };
            CheckSketchInk(details.Build(project, project.Schematic!.Pages.Last()), project.Schematic.Pages.Last()); textCases++;
        }
        project.Cables[0].ArchivedCable = baseArchive;
        CheckSketchInk(details.Build(project, project.Schematic!.Pages.Last()), project.Schematic.Pages.Last());
        SQLitePCL.Batteries_V2.Init();
        var database = Path.Combine(root, "synthetic-project.db");
        if (File.Exists(database)) throw new IOException("Use a fresh smoke directory.");
        var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), database);
        repository.SaveAsync(project).GetAwaiter().GetResult();
        var reopened = repository.GetAsync(project.ProjectId).GetAwaiter().GetResult()!;
        var page = reopened.Schematic!.Pages.Last(); var binding = page.CableDetail!;
        reopened = details.SetLayout(reopened, page.PageId, binding.DetailId,
            binding with { SketchPosition = new(25, 120), SketchWidth = 160, SketchHeight = 60 });
        repository.SaveAsync(reopened).GetAwaiter().GetResult();
        reopened = repository.GetAsync(reopened.ProjectId).GetAwaiter().GetResult()!;
        page = reopened.Schematic!.Pages.Last();
        CheckSketchInk(details.Build(reopened, page), page);
        Require(JsonSerializer.Serialize(reopened.Cables) == before, "SQLite reopen or layout edit changed source cable data.");
        var snapshot = JsonSerializer.Serialize(reopened);
        var control = new SchematicWorkspaceControl(() => reopened, (_, _) => throw new InvalidOperationException("Read-only render cannot commit."),
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var render = typeof(SchematicWorkspaceControl).GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        BitmapSource Page(ElectricalProject p, SchematicPage sheet) => (BitmapSource)render.Invoke(control, [p, sheet])!;
        Save(Page(reopened, page), Path.Combine(root, "fitted-sketch.png"));
        var pdf = Path.Combine(root, "fitted-sketch.pdf");
        var count = SchematicPdfWriter.Write(reopened, pdf, Page);
        using (var document = PdfReader.Open(pdf, PdfDocumentOpenMode.Import)) Require(document.PageCount == count && count >= 2, "Fitted multipage PDF is invalid.");
        Require(snapshot == JsonSerializer.Serialize(reopened), "Export changed saved authority.");
        Console.WriteLine($"PASS {textCases} offscreen TEXT/MTEXT rotation/attachment/width-factor cases, actual ink containment, 180x70 placement, edit, SQLite reopen, {count}-page PDF and native-source preservation.");
        if (existingDatabase is not null) CheckExistingSketches(existingDatabase, root, details, Layout);
        return 0;
    }

    private static void CheckSketchInk(SchematicCableDetailPresentation content, SchematicPage page)
    {
        var asset = new SchematicCadAsset { SourceSha256 = new('c', 64), Width = page.Width, Height = page.Height, Primitives = content.Primitives };
        var canvas = (Canvas)typeof(SchematicWorkspaceControl).GetMethod("CadCanvas", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [asset, null, 1d, null, false])!;
        canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height)); canvas.UpdateLayout();
        var box = content.PartBounds[SchematicCableDetailPart.Sketch];
        foreach (var child in canvas.Children.OfType<FrameworkElement>())
        {
            var ink = VisualTreeHelper.GetDescendantBounds(child);
            if (ink.IsEmpty) continue;
            var actual = child.TransformToAncestor(canvas).TransformBounds(ink);
            Require(actual.Left >= box.X * 3 - .05 && actual.Top >= box.Y * 3 - .05 &&
                actual.Right <= (box.X + box.Width) * 3 + .05 && actual.Bottom <= (box.Y + box.Height) * 3 + .05,
                $"Actual {child.GetType().Name} ink {actual} exceeds sketch rectangle {box}; " +
                (child is TextBlock text ? $"font={text.FontSize} desired={text.DesiredSize} baseline={text.BaselineOffset} transform={text.RenderTransform.Value}" : ""));
        }
    }

    private static void CheckExistingSketches(string path, string root, SchematicCableDetailService details,
        Func<ElectricalProject, SchematicCableDetailBinding> layout)
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
                var page = saved.Schematic!.Pages[0];
                var binding = layout(saved) with { CableInstanceId = cable.CableInstanceId };
                var content = details.BuildArchived(saved, page, binding);
                CheckSketchInk(content, page); count++;
                var control = new SchematicWorkspaceControl(() => saved, (_, _) => throw new InvalidOperationException("Live project cannot commit."),
                    () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
                var next = details.PlaceArchivedDetail(saved, cable.CableInstanceId, page.PageId, binding.TablePosition, binding.SketchPosition, binding);
                var render = typeof(SchematicWorkspaceControl).GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Save((BitmapSource)render.Invoke(control, [next, next.Schematic!.Pages[0]])!, Path.Combine(root, $"saved-sketch-{count}.png"));
            }
            Require(before == JsonSerializer.Serialize(saved), "Read-only sketch check changed live project data.");
        }
        Require(count > 0, "No saved manufacturing sketch was checked.");
        Console.WriteLine($"PASS {count} existing archived manufacturing sketch(es) at180x70 from read-only SQLite; no source writes or native window.");
    }
}
