using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using Microsoft.Win32;

namespace ComponentIntelligence.Desktop;

public sealed record SchematicOutputLabelWarning(int PageNumber, string PageId, string PageTitle,
    IReadOnlyList<string> WireIds, IReadOnlyList<string> WireNames);

public partial class SchematicWorkspaceControl
{
    public Func<Task<ElectricalProject?>>? SaveOutputSnapshotAsync { get; set; }
    public IReadOnlyList<SchematicOutputLabelWarning> OutputLabelWarnings => _outputLabelWarnings.Values.ToArray();
    private readonly Dictionary<string, SchematicOutputLabelWarning> _outputLabelWarnings = new(StringComparer.Ordinal);
    private bool _renderingOutput, _outputBusy;

    private string WithOutputLabelWarnings(string summary) => _outputLabelWarnings.Count == 0 ? summary : summary +
        $"\n標示空間不足：{_outputLabelWarnings.Count} 頁、{_outputLabelWarnings.Values.Sum(w => w.WireIds.Count)} 條導線的部分標籤未顯示。";

    private string OutputLabelWarningDetails() =>
        string.Join("\n", _outputLabelWarnings.Values.Select(w => $"第 {w.PageNumber} 頁 {w.PageTitle} [{w.PageId}]：" +
            string.Join("、", w.WireNames.Zip(w.WireIds, (name, id) => $"{name} [{id}]"))));

    private void SetOutputSummary(string summary)
    {
        Status.Text = WithOutputLabelWarnings(summary);
        Status.ToolTip = Status.Text;
        OutputWarningsButton.Visibility = _outputLabelWarnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OutputWarnings_Click(object sender, RoutedEventArgs e)
    {
        if (_outputLabelWarnings.Count == 0) return;
        var dialog = new Window { Title = "匯出標示警告", Width = 820, Height = 480, MinWidth = 420, MinHeight = 260,
            Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        NameScope.SetNameScope(dialog, new NameScope());
        var panel = new DockPanel { Margin = new(12) }; dialog.Content = panel;
        var close = new Button { Content = "關閉", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 80, Padding = new(10, 5, 10, 5), Margin = new(0, 10, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        var details = new TextBox { Name = "OutputWarningDetails", Text = OutputLabelWarningDetails(), IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(8) };
        panel.Children.Add(details); dialog.RegisterName(details.Name, details);
        dialog.ShowDialog();
    }

    private async Task<int?> ExportPdfAsync(string destination, bool? includeWireDetails)
    {
        _outputLabelWarnings.Clear();
        OutputWarningsButton.Visibility = Visibility.Collapsed;
        if (File.Exists(destination)) throw new IOException("輸出檔已存在，請選擇新的檔名。");
        var project = await PrepareOutputAsync(); if (project is null) return null;
        var count = SchematicPdfWriter.Write(project, destination, RenderOutputPage, includeWireDetails == true);
        SetOutputSummary($"已輸出 {count} 頁 PDF 草稿（未工程核准）：{destination}");
        return count;
    }

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
            FileName = "schematic-draft.pdf", AddExtension = true, InitialDirectory = DialogDirectory(false) };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        _outputBusy = true; IsEnabled = false;
        try
        {
            if (await ExportPdfAsync(dialog.FileName, PdfWireDetails.IsChecked) is null) return;
            RememberDialogDirectory(dialog.FileName, false);
        }
        catch (Exception error) { Status.Text = "PDF 未完成：" + error.Message; }
        finally
        {
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
            _outputLabelWarnings.Clear();
            OutputWarningsButton.Visibility = Visibility.Collapsed;
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
            SetOutputSummary("已送出草稿列印；請核對實際紙張。");
        }
        catch (Exception error) { Status.Text = "列印未完成：" + error.Message; }
        finally { _outputBusy = false; IsEnabled = true; }
    }
}
