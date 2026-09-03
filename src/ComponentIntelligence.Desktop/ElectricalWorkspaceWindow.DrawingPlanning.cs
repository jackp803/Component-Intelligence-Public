using ComponentIntelligence.Electrical.Drawing;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    private ProjectRevisionService? _drawingRevisionService;

    private ProjectRevisionService EnsureDrawingRevisionService() =>
        _drawingRevisionService ??= new ProjectRevisionService(
            new ProjectRevisionRepository(new SqliteConnectionFactory(), _databasePath));

    private void ConfigureDrawingPlanningWorkspace(DrawingPlanningWorkspaceControl control)
    {
        var revisionService = EnsureDrawingRevisionService();
        var checkpointSink = new ProjectRevisionCheckpointSink(revisionService);
        var builder = new DrawingPlanningInputBuilder(new RepresentationPolicy(new SafeNoAssetResolver()));

        control.ProjectProvider = () => _project;
        control.ProjectReplaced = project => { _project = project; UpdateHistoryButtons(); };
        control.PlanningInputProvider = () => builder.Build(_project);
        control.CheckpointAsync = async (trigger, label) => _ = await revisionService.CreateCheckpointAsync(_project, trigger, label);
        control.SaveProjectAsync = async () => await _repository.SaveAsync(_project);
        control.HistoryItemsAsync = async () => (await revisionService.ListAsync(_project.ProjectId)).Select(x => x.RevisionId).ToArray();
        control.RestoreRevisionAsync = async revisionId =>
        {
            var restored = await revisionService.RestoreAsync(_project, revisionId); _project = restored; RefreshAll(); return restored;
        };
        control.GenerationCoordinatorFactory = settings =>
        {
            IDrawingExecutorClient? executor = null;
            var executorSettings = control.ExecutorRuntimeSettingsStore.Load();
            if (executorSettings is not null)
            {
                var validation = DrawingExecutorRuntimeSettingsValidator.Validate(executorSettings);
                if (!validation.IsValid) throw new InvalidOperationException(string.Join("; ", validation.Issues.Select(x => x.Message)));
                executor = new LocalDrawingExecutorClient(executorSettings, productionSqlitePaths: [_databasePath]);
            }
            return new DrawingGenerationCoordinator(
                project => builder.Build(project),
                new PythonDrawingPlannerClient(settings),
                new PythonDrawingIrClient(settings),
                new DrawingPreflightService(),
                checkpointSink,
                executor);
        };
        control.LoadPlan(_project.DrawingPlan);

        // TopologyCanvas raises MutationStarting before topology presentation/engineering edits are committed.
        // Persist the durable TopologyChange checkpoint synchronously so the snapshot is truly pre-mutation.
        TopologyCanvas.MutationStarting += (_, _) =>
            revisionService.CreateCheckpointAsync(_project, ProjectRevisionTrigger.TopologyChange, "Topology mutation").GetAwaiter().GetResult();
    }

    // Called by real project-wide import/synchronization entry points before they mutate ElectricalProject.
    // This lazy initialization prevents a major import from silently skipping revision evidence merely
    // because the user has not opened the Drawing Planning tab yet.
    internal async Task CheckpointMajorImportAsync(string? label = null) =>
        _ = await EnsureDrawingRevisionService().CreateCheckpointAsync(
            _project,
            ProjectRevisionTrigger.MajorImport,
            label ?? "Major import");

    private sealed class SafeNoAssetResolver : IDrawingAssetResolver
    {
        public DrawingAssetResolution? Resolve(string ownerId, DrawingRepresentationRole role) => null;
    }
}
