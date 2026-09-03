using System.Windows;

namespace ComponentIntelligence.Desktop;

public partial class MainWindow
{
    private void OpenBlockArchive_Click(object sender, RoutedEventArgs e)
    {
        var workbookPath = RequireCentralWorkbookPath();
        if (workbookPath is null) return;

        var window = new BlockArchiveBatchWindow(workbookPath)
        {
            Owner = this
        };
        window.ShowDialog();
    }
}
