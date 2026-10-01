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
        Func<ElectricalProject, SchematicPage, BitmapSource> render)
    {
        SchematicAuthoringService.Validate(project);
        if (project.Schematic?.Pages.Count is not > 0) throw new InvalidOperationException("尚無可輸出的頁面。");
        var target = Path.GetFullPath(destination);
        if (File.Exists(target)) throw new IOException("輸出檔已存在，請選擇新的檔名。");
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".schematic-{Guid.NewGuid():N}.pdf");
        try
        {
            using var document = new PdfDocument();
            document.Info.Title = (project.Name ?? "Electrical schematic") + " - DRAFT";
            document.Info.Subject = "Saved schematic draft; engineering acceptance pending; 300 DPI page rendering";
            foreach (var sheet in project.Schematic.Pages)
            {
                var bitmap = render(project, sheet);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new MemoryStream(); encoder.Save(stream); stream.Position = 0;
                using var image = XImage.FromStream(stream);
                var page = document.AddPage();
                page.Width = XUnit.FromMillimeter(sheet.Width); page.Height = XUnit.FromMillimeter(sheet.Height);
                using var graphics = XGraphics.FromPdfPage(page);
                graphics.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
            }
            foreach (var sheet in project.Schematic.Pages.Select((value, index) => (value, index)))
            foreach (var link in SchematicContinuationNavigation.Links(project, sheet.value.PageId))
            {
                var source = document.Pages[sheet.index]; var targetPage = project.Schematic.Pages[link.DestinationPageIndex];
                var mm = 72d / 25.4;
                var left = link.LinkTopLeft.X * mm; var right = (link.LinkTopLeft.X + link.LinkWidth) * mm;
                var bottom = (sheet.value.Height - link.LinkTopLeft.Y - link.LinkHeight) * mm;
                var top = (sheet.value.Height - link.LinkTopLeft.Y) * mm;
                source.AddDocumentLink(new PdfRectangle(new XPoint(left, bottom), new XPoint(right, top)),
                    link.DestinationPageIndex + 1, new XPoint(link.Destination.X * mm, (targetPage.Height - link.Destination.Y) * mm));
            }
            var count = document.PageCount;
            document.Save(temporary);
            File.Move(temporary, target, overwrite: false);
            return count;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
