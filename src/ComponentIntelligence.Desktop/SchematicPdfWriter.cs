using System.IO;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ComponentIntelligence.Desktop;

public static class SchematicPdfWriter
{
    public static int Write(ElectricalProject project, string destination,
        Func<ElectricalProject, SchematicPage, BitmapSource> render, bool includeWireDetails = false)
    {
        SchematicAuthoringService.Validate(project);
        if (project.Schematic?.Pages.Count is not > 0) throw new InvalidOperationException("尚無可輸出的頁面。");
        var target = Path.GetFullPath(destination);
        if (File.Exists(target)) throw new IOException("輸出檔已存在，請選擇新的檔名。");
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".schematic-{Guid.NewGuid():N}.pdf");
        try
        {
            var plan = CableWiringAppendixPlan.Build(project);
            var output = plan.MainPages.Select(page => new OutputPage(page, null, null)).ToList();
            var sectionDestinations = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var section in plan.Sections)
            {
                foreach (var detail in section.ExistingDetailPages) output.Add(new(detail, null, section));
                var cable = plan.MainProject.Cables.Single(c => c.CableInstanceId == section.CableInstanceId);
                var paper = (plan.MainPages.FirstOrDefault() ?? section.ExistingDetailPages.FirstOrDefault() ?? project.Schematic.Pages[0]) with
                { PageId = "cable-wiring-" + Guid.NewGuid().ToString("N"), Title = (cable.ReferenceDesignator ?? cable.DisplayName ?? "線材") + " / 接線圖",
                    CableDetail = null, InlineCableDetails = [], TitleBlockContentOverride = (cable.ReferenceDesignator ?? cable.DisplayName ?? "線材") + " / 接線圖" };
                var tiles = CableWiringAppendixRenderer.Plan(cable, paper, section.SourceLinks.DistinctBy(s => s.SourcePageId).Count());
                foreach (var tile in tiles)
                {
                    var sheet = paper with { PageId = "cable-wiring-" + Guid.NewGuid().ToString("N") };
                    sectionDestinations.TryAdd(section.CableInstanceId, sheet.PageId); output.Add(new(sheet, tile, section));
                }
            }
            if (includeWireDetails)
            foreach (var source in plan.MainPages.Select((page, index) => (page, index)))
            foreach (var tile in SchematicWireDetailsRenderer.Plan(SchematicWireLegendPresentation.Entries(plan.MainProject, source.page.PageId), source.page, source.index + 1))
                output.Add(new(source.page with { PageId = "wire-details-" + Guid.NewGuid().ToString("N"),
                    Title = "導線明細 / 工程圖 " + (source.index + 1), CableDetail = null, InlineCableDetails = [],
                    TitleBlockContentOverride = "導線明細 / 工程圖 " + (source.index + 1) }, null, null) { WireDetails = tile });
            var snapshot = plan.MainProject;
            snapshot.Schematic!.Pages.Clear(); snapshot.Schematic.Pages.AddRange(output.Select(o => o.Sheet));
            var indices = output.Select((page, index) => (page.Sheet.PageId, index)).ToDictionary(p => p.PageId, p => p.index, StringComparer.Ordinal);
            using var document = new PdfDocument();
            document.Info.Title = (project.Name ?? "Electrical schematic") + " - DRAFT";
            document.Info.Subject = "Saved schematic draft; engineering acceptance pending; 300 DPI page rendering";
            foreach (var item in output)
            {
                var sheet = item.Sheet;
                var bitmap = render(snapshot, sheet);
                if (item.Tile is { } tile && item.Section is { } section)
                    bitmap = CableWiringAppendixRenderer.Render(snapshot.Cables.Single(c => c.CableInstanceId == section.CableInstanceId), sheet, tile,
                        CableWiringAppendixRenderer.ReturnLinks(tile, section.SourceLinks, indices, sheet.Margin), bitmap);
                if (item.WireDetails is { } details) bitmap = SchematicWireDetailsRenderer.Render(sheet, details, bitmap);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new MemoryStream(); encoder.Save(stream); stream.Position = 0;
                using var image = XImage.FromStream(stream);
                var page = document.AddPage();
                page.Width = XUnit.FromMillimeter(sheet.Width); page.Height = XUnit.FromMillimeter(sheet.Height);
                using var graphics = XGraphics.FromPdfPage(page);
                graphics.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
            }
            foreach (var sheet in snapshot.Schematic.Pages.Select((value, index) => (value, index)))
            foreach (var link in SchematicContinuationNavigation.Links(snapshot, sheet.value.PageId))
            {
                var source = document.Pages[sheet.index]; var targetPage = snapshot.Schematic.Pages[link.DestinationPageIndex];
                var mm = 72d / 25.4;
                var left = link.LinkTopLeft.X * mm; var right = (link.LinkTopLeft.X + link.LinkWidth) * mm;
                var bottom = (sheet.value.Height - link.LinkTopLeft.Y - link.LinkHeight) * mm;
                var top = (sheet.value.Height - link.LinkTopLeft.Y) * mm;
                source.AddDocumentLink(new PdfRectangle(new XPoint(left, bottom), new XPoint(right, top)),
                    link.DestinationPageIndex + 1, new XPoint(link.Destination.X * mm, (targetPage.Height - link.Destination.Y) * mm));
            }
            foreach (var section in plan.Sections)
            {
                var targetIndex = indices[sectionDestinations[section.CableInstanceId]];
                foreach (var link in section.SourceLinks)
                    AddLink(indices[link.SourcePageId], new(link.TopLeft.X, link.TopLeft.Y, link.Width, link.Height), targetIndex,
                        new(output[targetIndex].Sheet.Margin, output[targetIndex].Sheet.Margin));
            }
            foreach (var item in output.Where(p => p.Tile is not null))
            foreach (var link in CableWiringAppendixRenderer.ReturnLinks(item.Tile!, item.Section!.SourceLinks, indices, item.Sheet.Margin))
                AddLink(indices[item.Sheet.PageId], link.Bounds, indices[link.PageId], link.Destination);
            foreach (var item in output.Where(p => p.WireDetails is not null))
            {
                var tile = item.WireDetails!;
                foreach (var row in tile.Blocks.SelectMany(block => block.Rows))
                {
                    var wire = snapshot.Schematic.Wires.Single(w => w.WireId == row.Entry.WireId && w.PageId == tile.SourcePageId);
                    foreach (var (a, b) in wire.Points.Zip(wire.Points.Skip(1)))
                    {
                        var length = Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
                        if (length <= 6) continue;
                        var bounds = a.Y == b.Y ? new SchematicGridBounds(Math.Min(a.X, b.X) + 3, a.Y - 1.2, length - 6, 2.4) :
                            new SchematicGridBounds(a.X - 1.2, Math.Min(a.Y, b.Y) + 3, 2.4, length - 6);
                        AddLink(indices[tile.SourcePageId], bounds, indices[item.Sheet.PageId], new(item.Sheet.Margin + 4, item.Sheet.Margin + 22));
                    }
                }
                AddLink(indices[item.Sheet.PageId], new(item.Sheet.Margin + 4,
                    item.Sheet.Height - item.Sheet.Margin - (item.Sheet.TemplateGeometry is null ? 12 : 66), 55, 7),
                    indices[tile.SourcePageId], new(item.Sheet.Margin, item.Sheet.Margin));
            }
            void AddLink(int from, SchematicGridBounds bounds, int to, SchematicPoint destination)
            {
                const double mm = 72d / 25.4;
                var sourcePage = output[from].Sheet; var targetPage = output[to].Sheet;
                var left = Math.Clamp(bounds.X, 0, sourcePage.Width); var right = Math.Clamp(bounds.X + bounds.Width, 0, sourcePage.Width);
                var top = Math.Clamp(bounds.Y, 0, sourcePage.Height); var bottom = Math.Clamp(bounds.Y + bounds.Height, 0, sourcePage.Height);
                if (right <= left || bottom <= top) return;
                document.Pages[from].AddDocumentLink(new PdfRectangle(new XPoint(left * mm, (sourcePage.Height - bottom) * mm),
                    new XPoint(right * mm, (sourcePage.Height - top) * mm)), to + 1,
                    new XPoint(destination.X * mm, (targetPage.Height - destination.Y) * mm));
            }
            var count = document.PageCount;
            document.Save(temporary);
            File.Move(temporary, target, overwrite: false);
            return count;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private sealed record OutputPage(SchematicPage Sheet, CableWiringAppendixTile? Tile, CableWiringAppendixSection? Section)
    {
        public SchematicWireDetailsTile? WireDetails { get; init; }
    }
}
