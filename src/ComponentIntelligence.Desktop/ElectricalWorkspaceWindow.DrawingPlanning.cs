using ComponentIntelligence.Electrical.Drawing;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    private ProjectRevisionService? _drawingRevisionService;

    private void ConfigureDrawingPlanningWorkspace(DrawingPlanningWorkspaceControl control)
    {
        var revisionRepository = new ProjectRevisionRepository(new SqliteConnectionFactory(), _databasePath);
        _drawingRevisionService = new ProjectRevisionService(revisionRepository);
        var checkpointSink = new ProjectRevisionCheckpointSink(_drawingRevisionService);
        var builder = new DrawingPlanningInputBuilder(new RepresentationPolicy(new SafeNoAssetResolver()));

        control.ProjectProvider = () => _project;
        control.ProjectReplaced = project => { _project = project; UpdateHistoryButtons(); };
        control.PlanningInputProvider = () => builder.Build(_project);
        control.CheckpointAsync = async (trigger, label) => _ = await _drawingRevisionService.CreateCheckpointAsync(_project, trigger, label);
        control.SaveProjectAsync = async () => await _repository.SaveAsync(_project);
        control.HistoryItemsAsync = async () => (await _drawingRevisionService.ListAsync(_project.ProjectId)).Select(x => x.RevisionId).ToArray();
        control.RestoreRevisionAsync = async revisionId =>
        {
            var restored = await _drawingRevisionService.RestoreAsync(_project, revisionId); _project = restored; RefreshAll(); return restored;
        };
        control.GenerationCoordinatorFactory = settings => new DrawingGenerationCoordinator(
            project => builder.Build(project),
            new PythonDrawingPlannerClient(settings),
            new PythonDrawingIrClient(settings),
            new DrawingPreflightService(),
            checkpointSink,
            executor: null);
        control.LoadPlan(_project.DrawingPlan);

        // TopologyCanvas raises MutationStarting before topology presentation/engineering edits are committed.
        // Persist the durable TopologyChange checkpoint synchronously so the snapshot is truly pre-mutation.
        TopologyCanvas.MutationStarting += (_, _) =>
            _drawingRevisionService.CreateCheckpointAsync(_project, ProjectRevisionTrigger.TopologyChange, "Topology mutation").GetAwaiter().GetResult();
    }

    // Major import entry points can call this before replacing large project regions. It is deliberately
    // separate from transient Undo/Redo and uses the same durable whole-project revision repository.
    internal Task CheckpointMajorImportAsync(string? label = null) =>
        _drawingRevisionService is null
            ? Task.CompletedTask
            : _drawingRevisionService.CreateCheckpointAsync(_project, ProjectRevisionTrigger.MajorImport, label ?? "Major import");

    private sealed class SafeNoAssetResolver : IDrawingAssetResolver
    {
        public DrawingAssetResolution? Resolve(string ownerId, DrawingRepresentationRole role) => null;
    }
}
