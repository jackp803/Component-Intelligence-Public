using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using Microsoft.Win32;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    public Func<Task<ElectricalProject?>>? SaveOutputSnapshotAsync { get; set; }
    private bool _renderingOutput, _outputBusy;

    private async Task<ElectricalProject?> PrepareOutputAsync()
    {
        if (!FinishPendingDraft()) return null;
        if (SaveOutputSnapshotAsync is null) throw new InvalidOperationException("尚未連接專案儲存服務。");
        var project = await SaveOutputSnapshotAsync();
        if (project is null) return null;
        SchematicAuthoringService.Validate(project);
        if (project.Schematic?.Pages.Count is not > 0) throw new InvalidOperationException("尚無可輸出的頁面。");
        foreach (var id in project.Schematic.Symbols.Where(s => s.Geometry is null)
            .Select(s => SchematicSymbolOwner.Resolve(project, s).DefinitionId)
            .Distinct(StringComparer.Ordinal))
            await GetImage(id);
        return project;
    }

    private BitmapSource RenderOutputPage(ElectricalProject project, SchematicPage page)
    {
        var previousCanvas = _renderCanvas;
        var previousSelection = _selectionId;
        var canvas = new Canvas { Background = Brushes.White, Width = page.Width * PixelsPerMm, Height = page.Height * PixelsPerMm };
        try
        {
            _renderingOutput = true; _selectionId = null; _renderCanvas = canvas;
            RenderPage(project, page);
            canvas.Measure(new Size(canvas.Width, canvas.Height));
            canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
            canvas.UpdateLayout();
            // Render the saved sheet, not a screen capture; viewport zoom/selection are excluded.
            const double dpi = 300;
            var pixelWidth = checked((int)Math.Ceiling(page.Width / 25.4 * dpi));
            var pixelHeight = checked((int)Math.Ceiling(page.Height / 25.4 * dpi));
            if ((long)pixelWidth * pixelHeight > 100_000_000)
                throw new InvalidOperationException("頁面超過目前 300 DPI 輸出的大小上限；未縮小或裁切圖面。");
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
                drawing.DrawRectangle(new VisualBrush(canvas) { Stretch = Stretch.Fill }, null,
                    new Rect(0, 0, page.Width / 25.4 * 96, page.Height / 25.4 * 96));
            var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(visual); bitmap.Freeze();
            return bitmap;
        }
        finally { _renderCanvas = previousCanvas; _selectionId = previousSelection; _renderingOutput = false; }
    }

    private async void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_outputBusy) return;
        var dialog = new SaveFileDialog { Title = "儲存並匯出完整 PDF 草稿", Filter = "PDF (*.pdf)|*.pdf",
            FileName = "schematic-draft.pdf", AddExtension = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        _outputBusy = true; IsEnabled = false;
        string? temporary = null;
        try
        {
            if (File.Exists(dialog.FileName)) throw new IOException("輸出檔已存在，請選擇新的檔名。");
            var project = await PrepareOutputAsync(); if (project is null) return;
            using var document = new PdfDocument();
            document.Info.Title = (project.Name ?? "Electrical schematic") + " - DRAFT";
            document.Info.Subject = "Saved schematic draft; engineering acceptance pending; 300 DPI page rendering";
            foreach (var sheet in project.Schematic!.Pages)
            {
                var bitmap = RenderOutputPage(project, sheet);
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
                var source = document.Pages[sheet.index];
                var target = project.Schematic.Pages[link.DestinationPageIndex];
                var mm = 72d / 25.4;
                var left = link.LinkTopLeft.X * mm;
                var right = (link.LinkTopLeft.X + link.LinkWidth) * mm;
                var bottom = (sheet.value.Height - link.LinkTopLeft.Y - link.LinkHeight) * mm;
                var top = (sheet.value.Height - link.LinkTopLeft.Y) * mm;
                source.AddDocumentLink(new PdfRectangle(new XPoint(left, bottom), new XPoint(right, top)),
                    link.DestinationPageIndex + 1,
                    new XPoint(link.Destination.X * mm, (target.Height - link.Destination.Y) * mm));
            }
            temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dialog.FileName)!, $".schematic-{Guid.NewGuid():N}.pdf");
            document.Save(temporary);
            File.Move(temporary, dialog.FileName, overwrite: false);
            Status.Text = $"已輸出 {document.PageCount} 頁 PDF 草稿（未工程核准）：{dialog.FileName}";
        }
        catch (Exception error) { Status.Text = "PDF 未完成：" + error.Message; }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
            _outputBusy = false; IsEnabled = true;
        }
    }

    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_outputBusy) return;
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        _outputBusy = true; IsEnabled = false;
        try
        {
            var project = await PrepareOutputAsync(); if (project is null) return;
            var document = new FixedDocument();
            var capabilities = dialog.PrintQueue.GetPrintCapabilities(dialog.PrintTicket);
            var area = capabilities.PageImageableArea
                ?? throw new InvalidOperationException("印表機未提供可列印範圍，無法確認圖面不會被裁切。");
            foreach (var sheet in project.Schematic!.Pages)
            {
                var width = sheet.Width / 25.4 * 96; var height = sheet.Height / 25.4 * 96;
                var mediaWidth = capabilities.OrientedPageMediaWidth;
                var mediaHeight = capabilities.OrientedPageMediaHeight;
                if (mediaWidth is null || mediaHeight is null ||
                    Math.Abs(width - mediaWidth.Value) > 1 || Math.Abs(height - mediaHeight.Value) > 1)
                    throw new InvalidOperationException("請選擇與圖紙相符的紙張／方向；未自動縮放或裁切。");
                var bitmap = RenderOutputPage(project, sheet);
                var stride = checked(bitmap.PixelWidth * 4);
                var pixels = new byte[checked(stride * bitmap.PixelHeight)];
                bitmap.CopyPixels(pixels, stride, 0);
                var scale = bitmap.DpiX / 96;
                if (!double.IsFinite(area.OriginWidth) || !double.IsFinite(area.OriginHeight) ||
                    !double.IsFinite(area.ExtentWidth) || !double.IsFinite(area.ExtentHeight) ||
                    SchematicPrintSafety.HasInkOutside(pixels, bitmap.PixelWidth, bitmap.PixelHeight, stride,
                        checked((int)Math.Ceiling(area.OriginWidth * scale)),
                        checked((int)Math.Ceiling(area.OriginHeight * scale)),
                        checked((int)Math.Floor((area.OriginWidth + area.ExtentWidth) * scale)),
                        checked((int)Math.Floor((area.OriginHeight + area.ExtentHeight) * scale))))
                    throw new InvalidOperationException("圖面內容超出印表機可列印範圍；未送出、縮放或裁切。請改用合適的印表機或 PDF。");
                var page = new FixedPage { Width = width, Height = height };
                page.Children.Add(new Image { Source = bitmap, Width = width, Height = height });
                var content = new PageContent { Child = page }; document.Pages.Add(content);
            }
            dialog.PrintDocument(document.DocumentPaginator, project.Name ?? "Electrical schematic draft");
            Status.Text = "已送出草稿列印；請核對實際紙張。";
        }
        catch (Exception error) { Status.Text = "列印未完成：" + error.Message; }
        finally { _outputBusy = false; IsEnabled = true; }
    }
}
