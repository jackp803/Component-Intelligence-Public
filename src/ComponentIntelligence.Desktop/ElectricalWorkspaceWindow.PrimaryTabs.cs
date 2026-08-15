using System.Windows;
using System.Windows.Controls;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    private bool _primaryTabsConfigured;
    private CabinetLayoutWorkspaceControl? _cabinetLayoutWorkspace;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        ConfigurePrimaryWorkspaceTabs();
        TopologyCanvas.ComponentImageResolver ??= ResolveComponentImageAsync;
        TopologyCanvas.ComponentProductPageResolver ??= ResolveComponentProductPageAsync;
        TopologyCanvas.RefreshCanvas();
    }

    private void ConfigurePrimaryWorkspaceTabs()
    {
        if (_primaryTabsConfigured || Content is not Grid root) return;

        var tabs = root.Children.OfType<TabControl>().FirstOrDefault();
        if (tabs is null) return;

        var topologyTab = tabs.Items.OfType<TabItem>().FirstOrDefault(item =>
            (item.Header?.ToString() ?? string.Empty).Contains("Topology", StringComparison.OrdinalIgnoreCase));
        var layoutTab = tabs.Items.OfType<TabItem>().FirstOrDefault(item =>
            (item.Header?.ToString() ?? string.Empty).Contains("Physical Layout", StringComparison.OrdinalIgnoreCase));
        if (topologyTab is null || layoutTab is null) return;

        _primaryTabsConfigured = true;

        foreach (var item in tabs.Items.OfType<TabItem>())
            item.Visibility = ReferenceEquals(item, topologyTab) || ReferenceEquals(item, layoutTab)
                ? Visibility.Visible
                : Visibility.Collapsed;

        topologyTab.Header = "Topology｜電路拓樸";
        layoutTab.Header = "Layout｜實體佈局";

        // The old single-select Layer Combo is superseded by the canvas-level checkbox view filters.
        TopologyLayerCombo.Visibility = Visibility.Collapsed;
        if (TopologyLayerCombo.Parent is WrapPanel topologyToolbar)
        {
            foreach (var label in topologyToolbar.Children.OfType<TextBlock>())
            {
                if (label.Text.Contains("Layer Filter", StringComparison.OrdinalIgnoreCase))
                    label.Visibility = Visibility.Collapsed;
            }
        }

        _cabinetLayoutWorkspace = new CabinetLayoutWorkspaceControl(
            () => _project,
            description => RecordMutation(description),
            () => UpdateHistoryButtons(),
            status => WorkspaceStatusText.Text = status);
        layoutTab.Content = _cabinetLayoutWorkspace;

        tabs.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(tabs.SelectedItem, layoutTab))
                _cabinetLayoutWorkspace.RefreshWorkspace();
        };

        tabs.SelectedItem = topologyTab;
        WorkspaceStatusText.Text = "Electrical Workspace 已收斂為 Topology（拓樸）與 Layout（實體佈局）；Net / Wiring / Terminal / Validation 等保留在底層 Engine 或操作按鈕。";
    }
}
