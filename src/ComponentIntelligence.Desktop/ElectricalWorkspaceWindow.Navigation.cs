using System.Windows.Controls;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    public ElectricalWorkspaceWindow(string databasePath, bool openTopology)
        : this(databasePath)
    {
        if (!openTopology) return;
        SelectTopologyTab();
        WorkspaceStatusText.Text = "已直接進入 Topology（電氣拓樸）頁面。左鍵拖曳元件，右鍵旋轉 90°。";
    }

    private void SelectTopologyTab()
    {
        if (Content is not Grid root) return;
        var tabs = root.Children.OfType<TabControl>().FirstOrDefault();
        if (tabs is not null && tabs.Items.Count > 1)
            tabs.SelectedIndex = 1;
    }
}
