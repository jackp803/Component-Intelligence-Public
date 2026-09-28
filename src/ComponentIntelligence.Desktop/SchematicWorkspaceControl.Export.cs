using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using ComponentIntelligence.Electrical.Schematic;
using Microsoft.Win32;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private void ExportDraft_Click(object sender, RoutedEventArgs e)
    {
        if (!FinishPendingDraft()) return;
        try
        {
            var project = _getProject();
            var pages = new SchematicDxfExporter().Create(project);
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
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                for (var i = 0; i < pages.Count; i++)
                {
                    using var output = zip.CreateEntry($"page-{i + 1:000}.dxf").Open();
                    output.Write(pages[i].Dxf);
                }
                using var report = zip.CreateEntry("manifest.json").Open();
                JsonSerializer.Serialize(report, new
                {
                    schemaVersion = "schematic-draft-exchange.v1", status = "DRAFT_NOT_VERIFIED",
                    displayProfile = SchematicWireEvidence.Profile,
                    projectId = project.ProjectId,
                    sourceProjectSha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(project))),
                    sourceSchematicSha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(project.Schematic))),
                    limitations = new[] { "Not an AutoCAD Electrical WDP project.", "Catalog raster pictures are not embedded; review diagnostics.", "Font metrics and imported text formatting require visual review." },
                    pages = pages.Select((p, i) => new { p.PageId, p.Title, file = $"page-{i + 1:000}.dxf",
                        sha256 = Convert.ToHexString(SHA256.HashData(p.Dxf)), p.Diagnostics })
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            Status.Text = "已匯出 DXF 草稿（未驗證）：" + dialog.FileName;
        }
        catch (Exception error) { Status.Text = "DXF 草稿匯出失敗：" + error.Message; }
    }
}
