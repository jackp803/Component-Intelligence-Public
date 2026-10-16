using System.Windows;

namespace ComponentIntelligence.Desktop;

public partial class MainWindow
{
    private void OpenElectricalWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureBomProcessedBeforeElectricalView()) return;

        var workspace = new ElectricalWorkspaceWindow(_databasePath, LoadCentralWorkbookPath())
        {
            Owner = this
        };
        workspace.SynchronizeWorkingBomOnLoad(_importedRows);
        workspace.Show();
    }

    private void OpenTopology_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureBomProcessedBeforeElectricalView()) return;

        var workspace = new ElectricalWorkspaceWindow(_databasePath, openTopology: true, LoadCentralWorkbookPath())
        {
            Owner = this
        };
        workspace.SynchronizeWorkingBomOnLoad(_importedRows);
        workspace.Show();
    }
}
