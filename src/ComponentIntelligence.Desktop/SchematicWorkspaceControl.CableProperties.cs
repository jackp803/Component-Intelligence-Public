using System.Windows;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private bool TryEditSelectedArchivedCable(string? detailCableId = null)
    {
        var project = _getProject();
        var symbol = project.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == _selectionId);
        var cableId = detailCableId ?? symbol?.CableInstanceId ?? SelectedCableDetail()?.CableInstanceId;
        if (cableId is null) return false;
        if (detailCableId is null && symbol?.Locked == true) { Status.Text = "請先解鎖這個線材表示。"; return true; }
        var dialog = new ArchivedCableSettingsDialog(project, cableId, CableConnectorCatalog.Choices(_catalog,
            string.IsNullOrWhiteSpace(_archiveRoot) ? null : new ComponentIntelligence.SymbolArchive.SymbolArchiveRepository(_archiveRoot).Load().CableTemplates),
            command => Apply(command, "已更新此條線材的資料；共用模板保持不變")) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog(); return true;
    }
}
