using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Schematic;
using Microsoft.Win32;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private bool _exporting;
    private async void ExportDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_exporting || !FinishPendingDraft()) return;
        _exporting = true;
        try
        {
            var project = EngineeringReviewService.Clone(_getProject());
            var images = new Dictionary<string, SchematicRasterAsset>(StringComparer.Ordinal);
            var definitions = project.Schematic?.Symbols.Where(s => s.Geometry is null)
                .Select(s => SchematicSymbolOwner.Resolve(project, s).DefinitionId)
                .Distinct(StringComparer.Ordinal).ToArray() ?? [];
            foreach (var id in definitions)
            {
                BitmapImage? bitmap;
                try { bitmap = await GetImage(id); }
                catch { continue; } // The exporter reports every absent picture on its exact symbol.
                if (bitmap is null) continue;
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var buffer = new MemoryStream(); encoder.Save(buffer);
                images[id] = new(buffer.ToArray(), bitmap.PixelWidth, bitmap.PixelHeight);
            }
            var exporter = new SchematicDxfExporter();
            var pages = exporter.Create(project, images);
            if (pages.Count == 0) { Status.Text = "尚無可匯出的頁面"; return; }
            var warnings = pages.SelectMany((p, i) => p.Diagnostics.Select(d => $"{i + 1}. {p.Title}: {d}")).ToArray();
            if (warnings.Length > 0 && MessageBox.Show(Window.GetWindow(this),
                "DXF 草稿尚有待確認項目：\n" + string.Join("\n", warnings.Take(12)) +
                (warnings.Length > 12 ? $"\n其餘 {warnings.Length - 12} 項將列入報告。" : "") + "\n\n仍要匯出草稿？",
                "匯出 DXF 草稿", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var dialog = new SaveFileDialog { Title = "匯出 DXF 草稿包", Filter = "DXF 草稿包 (*.zip)|*.zip",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                FileName = "schematic-draft-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip", AddExtension = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            // CreateNew preserves an existing output even when a save dialog offered replacement.
            using (var stream = new FileStream(dialog.FileName, FileMode.CreateNew, FileAccess.Write))
                exporter.WritePackage(stream, project, images);
            Status.Text = "已匯出 DXF 草稿（未驗證）：" + dialog.FileName;
        }
        catch (Exception error) { Status.Text = "DXF 草稿匯出失敗：" + error.Message; }
        finally { _exporting = false; }
    }
}
