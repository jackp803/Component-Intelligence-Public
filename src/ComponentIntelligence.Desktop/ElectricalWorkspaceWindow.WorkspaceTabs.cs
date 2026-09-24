using System.Windows;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    private CabinetLayoutWorkspaceControl? _cabinetLayoutWorkspace;
    private DrawingPlanningWorkspaceControl? _drawingPlanningWorkspace;
    private SchematicWorkspaceControl? _schematicWorkspace;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_cabinetLayoutWorkspace is not null) return;
        TopologyCanvas.ComponentImageResolver ??= ResolveComponentImageAsync;
        TopologyCanvas.ComponentProductPageResolver ??= ResolveComponentProductPageAsync;
        _schematicWorkspace = new SchematicWorkspaceControl(() => _project, (project, description) =>
        {
            EnsureDrawingRevisionService().CreateCheckpointAsync(_project, ProjectRevisionTrigger.TopologyChange, description).GetAwaiter().GetResult();
            RecordMutation(description);
            _project = project;
            UpdateHistoryButtons();
            WorkspaceStatusText.Text = description;
        }, async () => System.IO.File.Exists(_centralWorkbookPath)
            ? await new WorkbookComponentKnowledgeStore(_centralWorkbookPath!).ListAsync()
            : await new ComponentIrCatalogReader(_databasePath).ListAsync(),
            ResolveComponentImageAsync, () => Undo_Click(this, new RoutedEventArgs()), () => Redo_Click(this, new RoutedEventArgs()));
        SchematicTab.Content = _schematicWorkspace;
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
            else if (ReferenceEquals(WorkspaceTabs.SelectedItem, SchematicTab)) _schematicWorkspace.RefreshWorkspace();
            else if (ReferenceEquals(WorkspaceTabs.SelectedItem, TopologyTab)) TopologyCanvas.SetProject(_project);
        };
        WorkspaceTabs.SelectedItem = _project.Schematic is null ? TopologyTab : SchematicTab;
        TopologyCanvas.RefreshCanvas();
    }
}
