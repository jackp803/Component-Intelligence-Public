using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using PdfSharp.Pdf.IO;

internal static partial class Program
{
    private static int SharedCableMetadataProof(string root)
    {
        Directory.CreateDirectory(root);
        const string commonNotes = "CABLE SPECIFICATION\nCABLE SIZE: 22AWG\nCABLE LENGTH: 5000mm\nTEST: 100% CONTINUITY";
        var template = AppendixThreePortTemplate() with {
            TextBindings = [new("REF", CableTextField.Reference), new("LEN", CableTextField.LengthMm),
                new("SPEC", CableTextField.Specification)] };
        CableInstance Cable(string reference, double length, string specification, string notes)
        {
            var cable = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
            cable.ReferenceDesignator = reference; cable.ProvidedLengthMm = length;
            cable.Specification = specification; cable.Notes = notes;
            var cad = AppendixSketchCad();
            cable.ArchivedCable = cable.ArchivedCable! with { ManufacturingGeometry = cad with {
                Primitives = cad.Primitives.Append(new SchematicCadPrimitive { Kind = "TEXT", Start = new(1020, 558),
                    Text = "SOURCE SPECIFICATION", AttributeTag = "SPEC", TextHeight = 3 }).ToArray() } };
            return cable;
        }
        var members = new[] {
            Cable("Valva1_Cable", 5000, "L1=200mm; L2=4800mm", commonNotes),
            Cable("Valva2_cable", 5000, "L1=200mm / L2=4800mm", commonNotes.Replace("\n", "\r\n")) };
        var page = new SchematicPage { PageId = "main", Title = "Shared cable assemblies", CoordinateGrid = new(5, 5, 390, 275),
            TitleBlockSlots = [new("SheetContent", new(230, 240, 165, 40))] };
        var project = new ElectricalProject { ProjectId = "shared-cable-metadata", Cables = members.ToList(), Schematic = new() {
            Pages = [page], Symbols = members.Select((c, i) => new SchematicSymbol {
                SymbolId = "cable-" + i, CableInstanceId = c.CableInstanceId, PageId = page.PageId,
                Position = new(20, 40 + i * 60), Width = 80, Height = 20, Geometry = new() {
                    SourceSha256 = template.AssetSha256, Width = 80, Height = 20,
                    Primitives = [new() { Kind = "LINE", Start = new(0, 0), End = new(80, 20) }] } }).ToList() } };
        var before = JsonSerializer.Serialize(project);
        var tile = CableWiringAppendixRenderer.Plan(members[0], page, 2, members).Single();
        Require(tile.MemberLabels.Count == 5, "Identical notes must be printed once after independent name/length/specification rows.");
        var note = tile.MemberLabels.Single(l => l.Text.Contains("TEST: 100% CONTINUITY"));
        Require(note.Text.Contains("共用備註") && members.All(m => note.Text.Contains(m.ReferenceDesignator!)),
            "Shared notes do not identify every applicable cable.");
        foreach (var member in members)
        {
            Require(tile.MemberLabels.Any(l => l.Text == member.ReferenceDesignator + " / 5000 mm"), "Per-cable five-metre label is missing.");
            Require(tile.MemberLabels.Any(l => l.Text == member.ReferenceDesignator + " / 規格：" + member.Specification),
                "Independent named specification was omitted or replaced.");
        }
        Require(string.Join("\n", tile.MemberLines).Split("CABLE LENGTH: 5000mm").Length == 2,
            "Compatibility metadata repeats or rewrites the common fixed-length note.");
        Require(tile.SketchPrimitives.Count(p => p.Kind == "SOLID_HATCH") == 1 &&
            tile.SketchPrimitives.Any(p => p.Text is { } text && text.Contains("共用示意圖") &&
                members.All(m => text.Contains(m.ReferenceDesignator!))), "Shared sketch is duplicated or its applicability is unclear.");
        CheckAppendixSketchClearance(tile, tile.SketchBounds!, page);
        var metadata = tile.MetadataBounds!;
        Require(tile.MemberLabels.All(l => l.Bounds.Y >= metadata.Y && l.Bounds.Y + l.Bounds.Height <= metadata.Y + metadata.Height),
            "Metadata escapes the single-page region.");
        var workspace = new SchematicWorkspaceControl(() => project, (_, _) => { },
            () => Task.FromResult<IReadOnlyList<ComponentIntelligence.Contracts.ComponentIR>>([]),
            _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var render = typeof(SchematicWorkspaceControl).GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        BitmapSource Background(ElectricalProject p, SchematicPage sheet) => (BitmapSource)render.Invoke(workspace, [p, sheet])!;
        var canvas = CableWiringAppendixRenderer.CreateCanvas(members[0], page, tile, [], Background(project, page));
        Require(canvas.Children.OfType<TextBlock>().Count(t => t.Text.Contains("TEST: 100% CONTINUITY")) == 1,
            "Preview duplicated common notes.");
        Save(CableWiringAppendixRenderer.Render(members[0], page, tile, [], Background(project, page)),
            Path.Combine(root, "shared-cable-metadata.png"));
        var path = Path.Combine(root, "shared-cable-metadata.pdf");
        Require(SchematicPdfWriter.Write(project, path, Background) == 2, "Equivalent cables with different specification text still export separate diagrams.");
        using (var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import))
        {
            Require(pdf.PageCount == 2 && Math.Abs(pdf.Pages[1].Width.Millimeter - 420) < .1,
                "Shared drawing did not stay on one A3 appendix page.");
            Require(pdf.Pages[1].Annotations.Count >= 2, "Shared page lost independent cable return links.");
        }
        Require(before == JsonSerializer.Serialize(project), "Export synchronized names/lengths/specifications/notes or saved wiring.");

        var third = Cable("SensorCable", 2000, "L1=500; L2=1500", "Install at lower sensor");
        var mixed = CableWiringAppendixRenderer.Plan(members[0], page, 3, [..members, third]).Single();
        Require(mixed.MemberLabels.Count == 8 && mixed.MemberLabels.Count(l => l.Text.Contains("TEST: 100% CONTINUITY")) == 1 &&
            mixed.MemberLabels.Any(l => l.Text == "SensorCable / 備註：Install at lower sensor") &&
            mixed.MemberLabels.Any(l => l.Text == "SensorCable / 2000 mm"), "Different cable metadata was merged into common notes.");
        Require(!mixed.MemberLabels.Single(l => l.Text.Contains("TEST: 100% CONTINUITY")).Text.Contains("SensorCable"),
            "Common notes were incorrectly attributed to a cable with different notes.");
        var distinct = Cable("DifferentNotes", 1200, "AWG18", commonNotes + "\nEXCEPTION");
        var separate = CableWiringAppendixRenderer.Plan(members[0], page, 2, [members[0], distinct]).Single();
        Require(separate.MemberLabels.Count == 6 && separate.MemberLabels.Count(l => l.Text.Contains("CABLE SPECIFICATION")) == 2,
            "Nonidentical notes were silently collapsed.");
        var narrowMembers = JsonSerializer.Deserialize<CableInstance[]>(JsonSerializer.Serialize(members))!;
        foreach (var member in narrowMembers)
            member.ArchivedCable = member.ArchivedCable! with { ManufacturingGeometry = new() {
                SourceSha256 = new('c', 64), Width = 20, Height = 200,
                Primitives = [new() { Kind = "LINE", Start = new(0, 0), End = new(20, 200) }] } };
        var narrow = CableWiringAppendixRenderer.Plan(narrowMembers[0], page, 2, narrowMembers).Single();
        var caption = narrow.SketchPrimitives.Single(p => p.Text?.Contains("共用示意圖") == true);
        Require(caption.Start.X + caption.TextWidth <= narrow.SketchBounds!.X + narrow.SketchBounds.Width + .000001,
            "A tall narrow shared sketch ignores its caption width and clips applicable references outside the sheet.");
        CheckAppendixSketchClearance(narrow, narrow.SketchBounds!, page);
        File.WriteAllText(Path.Combine(root, "shared-cable-metadata-pass.txt"),
            "PASS independent named specifications, common notes once, shared sketch applicability, A3 vector PDF, no data mutation.");
        Console.WriteLine("SHARED_CABLE_METADATA_PASS"); return 0;
    }
}
