using System.Windows;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    private CabinetLayoutWorkspaceControl? _cabinetLayoutWorkspace;
    private DrawingPlanningWorkspaceControl? _drawingPlanningWorkspace;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_cabinetLayoutWorkspace is not null) return;
        TopologyCanvas.ComponentImageResolver ??= ResolveComponentImageAsync;
        TopologyCanvas.ComponentProductPageResolver ??= ResolveComponentProductPageAsync;
        _cabinetLayoutWorkspace = new CabinetLayoutWorkspaceControl(
            () => _project, RecordMutation, UpdateHistoryButtons,
            status => WorkspaceStatusText.Text = status, ResolveComponentImageAsync);
        LayoutTab.Content = _cabinetLayoutWorkspace;
        _drawingPlanningWorkspace = new DrawingPlanningWorkspaceControl();
        ConfigureDrawingPlanningWorkspace(_drawingPlanningWorkspace);
        DrawingPlanningTab.Content = _drawingPlanningWorkspace;
        WorkspaceTabs.SelectionChanged += (_, e) => {
            if (!ReferenceEquals(e.Source, WorkspaceTabs)) return;
            if (ReferenceEquals(WorkspaceTabs.SelectedItem, LayoutTab)) _cabinetLayoutWorkspace.RefreshWorkspace();
            else if (ReferenceEquals(WorkspaceTabs.SelectedItem, DrawingPlanningTab)) _drawingPlanningWorkspace.LoadPlan(_project.DrawingPlan);
        };
        WorkspaceTabs.SelectedItem = TopologyTab;
        TopologyCanvas.RefreshCanvas();
    }
}
