using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

internal static partial class Program
{
    private static int CableLibraryReviewProof(string root, string? only)
    {
        Directory.CreateDirectory(root);
        var template = new ArchivedCableTemplate { TemplateId = "review", TemplateRevision = "r1", AssetSha256 = new('a', 64),
            Ports = new[] { "A", "B" }.Select(id => new ComponentIntelligence.Electrical.Domain.ComponentPort { PortId = id, Name = id,
                Pins = [new() { PinId = id + "1", PinNumber = "1", Function = "COM" }] }).ToList(), Mapping = [new("A1", "B1")] };
        template = template with { Manufacturing = new() { InventoryOnly = true, WiringDiagram = new() {
            PortBounds = new() { ["A"] = new(10, 10, 60, 40), ["B"] = new(180, 10, 60, 40) },
            PinPositions = new() { ["A1"] = new(70, 32), ["B1"] = new(180, 32) } } } };
        var cable = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
        var page = new SchematicPage { PageId = "one", Title = "Review" };
        if (only is null or "heading")
        {
            cable.ArchivedCable!.Ports[0].Name = string.Concat(Enumerable.Repeat("Long port name with a detailed physical description ", 3));
            var tile = CableWiringAppendixRenderer.Plan(cable, page, 1).Single();
            foreach (var port in tile.Diagram.PortBounds.Values)
            {
                var boxes = tile.Diagram.Primitives.Where(p => p.Kind == "TEXT" && p.Start.X >= port.X && p.Start.X < port.X + port.Width && p.Start.Y >= port.Y && p.Start.Y < port.Y + port.Height)
                    .Select(p => { var text = new TextBlock { Text = p.Text, Width = p.TextWidth * 4, FontSize = Math.Max(3.2, p.TextHeight) * 4,
                        FontFamily = new("Microsoft JhengHei"), TextWrapping = TextWrapping.Wrap };
                        text.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
                        return new Rect(p.Start.X, p.Start.Y, text.DesiredSize.Width / 4, text.DesiredSize.Height / 4); }).ToArray();
                for (var i = 0; i < boxes.Length; i++)
                for (var j = i + 1; j < boxes.Length; j++) Require(!boxes[i].IntersectsWith(boxes[j]), "Wrapped Port headings overlap other labels.");
            }
        }
        if (only is null or "members")
        {
            var members = Enumerable.Range(1, 50).Select(i => {
                var member = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
                member.ReferenceDesignator = "C" + i; member.ProvidedLengthMm = 100 + i; member.Notes = "Sensor " + i; return member; }).ToArray();
            var tiles = CableWiringAppendixRenderer.Plan(members[0], page, 1, members);
            Require(tiles.Count == 1, "Member metadata duplicated the wiring diagram.");
            var metadata = typeof(CableWiringAppendixRenderer).GetMethod("MemberPages");
            Require(metadata is not null, "Large shared groups have no metadata pagination.");
            var pages = (IReadOnlyList<IReadOnlyList<string>>)metadata!.Invoke(null, [tiles[0], page])!;
            var lines = tiles[0].MemberLines.Concat(pages.SelectMany(p => p)).ToArray();
            foreach (var member in members)
            {
                Require(lines.Contains(member.ReferenceDesignator + " / " + member.ProvidedLengthMm + " mm"),
                    "A shared member's independent length was omitted.");
                Require(lines.Contains(member.ReferenceDesignator + " / 備註：" + member.Notes),
                    "A shared member's independent notes were omitted.");
                Require(lines.Contains(member.ReferenceDesignator + " / 規格：" +
                    (string.IsNullOrWhiteSpace(member.Specification) ? "待確認" : member.Specification)),
                    "A shared member's independent specification was omitted.");
            }
            File.WriteAllText(Path.Combine(root, "member-page-count.txt"), pages.Count.ToString());
            var project = new ElectricalProject { ProjectId = "many-members", Cables = members.ToList(), Schematic = new() {
                Pages = Enumerable.Range(1, 4).Select(i => page with { PageId = "sheet-" + i, Title = "Members " + i }).ToList(),
                Symbols = members.Select((member, i) => new SchematicSymbol { SymbolId = "body-" + i, CableInstanceId = member.CableInstanceId,
                    PageId = "sheet-" + (1 + i / 16), Position = new(20 + i % 4 * 90, 30 + i / 4 % 4 * 55), Width = 70, Height = 17.5,
                    Geometry = new() { SourceSha256 = template.AssetSha256, Width = 80, Height = 20, Primitives = [
                        new() { Kind = "LINE", Start = new(0, 0), End = new(80, 0) }, new() { Kind = "LINE", Start = new(0, 20), End = new(80, 20) }] } }).ToList() } };
            var workspace = new SchematicWorkspaceControl(() => project, (_, _) => { }, () => Task.FromResult<IReadOnlyList<ComponentIntelligence.Contracts.ComponentIR>>([]),
                _ => Task.FromResult<Uri?>(null), () => { }, () => { });
            var render = typeof(SchematicWorkspaceControl).GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var count = SchematicPdfWriter.Write(project, Path.Combine(root, "many-members.pdf"), (p, sheet) =>
                (System.Windows.Media.Imaging.BitmapSource)render.Invoke(workspace, [p, sheet])!);
            Require(count == 5 + pages.Count, "Large-group PDF duplicated wiring diagrams or omitted member continuation pages.");
        }
        File.WriteAllText(Path.Combine(root, "review-pass.txt"), "PASS " + (only ?? "all"));
        Console.WriteLine("CABLE_LIBRARY_REVIEW_PASS"); return 0;
    }
}
