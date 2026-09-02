using System.Windows;

namespace ComponentIntelligence.Desktop;

public partial class MainWindow
{
    private void OpenElectricalWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (_importedRows.Count > 0 && !EnsureBomProcessedBeforeElectricalView()) return;

        var workspace = new ElectricalWorkspaceWindow(_databasePath, LoadCentralWorkbookPath())
        {
            Owner = this
        };
        if (_importedRows.Count > 0)
            workspace.SynchronizeWorkingBomOnLoad(_importedRows);
        workspace.Show();
    }

    private void OpenTopology_Click(object sender, RoutedEventArgs e)
    {
        if (_importedRows.Count > 0 && !EnsureBomProcessedBeforeElectricalView()) return;

        var workspace = new ElectricalWorkspaceWindow(_databasePath, openTopology: true, LoadCentralWorkbookPath())
        {
            Owner = this
        };
        if (_importedRows.Count > 0)
            workspace.SynchronizeWorkingBomOnLoad(_importedRows);
        workspace.Show();
    }
}
