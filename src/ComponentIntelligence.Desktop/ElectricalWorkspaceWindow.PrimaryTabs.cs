using System.Windows;
using System.Windows.Controls;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    private bool _primaryTabsConfigured;
    private CabinetLayoutWorkspaceControl? _cabinetLayoutWorkspace;
    private DrawingPlanningWorkspaceControl? _drawingPlanningWorkspace;

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
        var drawingTab = new TabItem { Header = "Drawing Planning｜圖面規劃", Visibility = Visibility.Visible };
        _drawingPlanningWorkspace = new DrawingPlanningWorkspaceControl();
        ConfigureDrawingPlanningWorkspace(_drawingPlanningWorkspace);
        drawingTab.Content = _drawingPlanningWorkspace;
        tabs.Items.Add(drawingTab);

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
            status => WorkspaceStatusText.Text = status,
            ResolveComponentImageAsync);
        layoutTab.Content = _cabinetLayoutWorkspace;

        tabs.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(tabs.SelectedItem, layoutTab))
                _cabinetLayoutWorkspace.RefreshWorkspace();
            else if (ReferenceEquals(tabs.SelectedItem, drawingTab))
                _drawingPlanningWorkspace.LoadPlan(_project.DrawingPlan);
        };

        tabs.SelectedItem = topologyTab;
        WorkspaceStatusText.Text = "Electrical Workspace primary flow: Topology（工程拓樸）→ Layout（實體佈局）→ Drawing Planning（圖面規劃）。";
    }
}
