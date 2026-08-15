using System.Windows;

namespace ComponentIntelligence.Desktop;

public partial class MainWindow
{
    private void OpenNotionConnection_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NotionConnectionDialog { Owner = this };
        dialog.ShowDialog();

        if (!dialog.SettingsChanged) return;
        StatusText.Text = T(
            "Notion 中央電料庫設定已更新；可按 Notion 中央庫再次測試連線。",
            "Notion central knowledge settings updated; open Notion Central again to test the connection.");
    }
}
