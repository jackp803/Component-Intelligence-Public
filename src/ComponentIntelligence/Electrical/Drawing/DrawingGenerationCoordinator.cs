using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Electrical.Drawing;

public sealed class DrawingGenerationCoordinator
{
    private readonly Func<ElectricalProject, DrawingPlanningInput> _buildInput;
    private readonly IDrawingPlannerClient _planner;
    private readonly IDrawingIrClient _ir;
    private readonly DrawingPreflightService _preflight;
    private readonly IProjectRevisionCheckpointSink _checkpoints;
    private readonly IDrawingExecutorClient? _executor;

    public DrawingGenerationCoordinator(Func<ElectricalProject, DrawingPlanningInput> buildInput, IDrawingPlannerClient planner, IDrawingIrClient ir, DrawingPreflightService preflight, IProjectRevisionCheckpointSink checkpoints, IDrawingExecutorClient? executor = null)
    {
        _buildInput = buildInput ?? throw new ArgumentNullException(nameof(buildInput)); _planner = planner ?? throw new ArgumentNullException(nameof(planner)); _ir = ir ?? throw new ArgumentNullException(nameof(ir)); _preflight = preflight ?? throw new ArgumentNullException(nameof(preflight)); _checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints)); _executor = executor;
    }

    public async Task<DrawingGenerationResult> GeneratePreviewAsync(ElectricalProject project, DrawingRuntimeValidation runtime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        await _checkpoints.CheckpointAsync(project, ProjectRevisionTrigger.GeneratePreview, cancellationToken);
        var input = _buildInput(project);
        if (!runtime.IsValid)
        {
            var blocked = _preflight.Evaluate(input, project.DrawingPlan ?? EmptyPlan(input), runtime, DrawingGenerationTarget.Preview);
            return new DrawingGenerationResult { Status = DrawingGenerationStatus.Blocked, DrawingPlan = project.DrawingPlan, Preflight = blocked };
        }
        var plan = await _planner.GenerateAsync(input, project.DrawingPlan, cancellationToken); project.DrawingPlan = plan;
        var result = _preflight.Evaluate(input, plan, runtime, DrawingGenerationTarget.Preview);
        return new DrawingGenerationResult { Status = result.CanProceed ? DrawingGenerationStatus.PreviewReady : DrawingGenerationStatus.Blocked, DrawingPlan = plan, Preflight = result };
    }

    public async Task<DrawingGenerationResult> GenerateAutoCadAsync(ElectricalProject project, DrawingRuntimeValidation runtime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        await _checkpoints.CheckpointAsync(project, ProjectRevisionTrigger.GenerateAutoCad, cancellationToken);
        var input = _buildInput(project);
        if (!runtime.IsValid)
        {
            var blocked = _preflight.Evaluate(input, project.DrawingPlan ?? EmptyPlan(input), runtime, DrawingGenerationTarget.FullGeneration);
            return new DrawingGenerationResult { Status = DrawingGenerationStatus.Blocked, DrawingPlan = project.DrawingPlan, Preflight = blocked };
        }
        var plan = project.DrawingPlan ?? await _planner.GenerateAsync(input, null, cancellationToken); project.DrawingPlan = plan;
        var preflight = _preflight.Evaluate(input, plan, runtime, DrawingGenerationTarget.FullGeneration);
        if (!preflight.CanProceed) return new DrawingGenerationResult { Status = DrawingGenerationStatus.Blocked, DrawingPlan = plan, Preflight = preflight };
        var ir = await _ir.CompileAsync(input, plan, cancellationToken);
        if (!string.Equals(ir.Status, "READY", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(ir.DrawingIrHash))
            return new DrawingGenerationResult { Status = DrawingGenerationStatus.Blocked, DrawingPlan = plan, DrawingIr = ir, Preflight = preflight with { CanProceed = false, Issues = preflight.Issues.Concat(ir.Issues).ToArray() } };
        if (_executor is null)
            return new DrawingGenerationResult { Status = DrawingGenerationStatus.ReadyForCp3C, DrawingPlan = plan, DrawingIr = ir, Preflight = preflight };

        DrawingExecutorResult execution;
        try { execution = await _executor.ExecuteAsync(ir, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            execution = new DrawingExecutorResult(
                DrawingExecutorStatus.Failed, null, null, [], null,
                [DrawingActionableIssue.Runtime("CP3C-EXECUTOR-THREW", "DRAWING_EXECUTOR_UNHANDLED_FAILURE", ex.Message, "ExecutorRuntimeSettings")], "{}");
        }

        if (execution.Status == DrawingExecutorStatus.Applied && !string.IsNullOrWhiteSpace(execution.ProjectFile) && execution.PageDrawings.Count > 0)
            return new DrawingGenerationResult { Status = DrawingGenerationStatus.Applied, DrawingPlan = plan, DrawingIr = ir, ExecutorResult = execution, Preflight = preflight };

        var failedPreflight = preflight with { CanProceed = false, Issues = preflight.Issues.Concat(execution.Issues).ToArray() };
        return new DrawingGenerationResult { Status = DrawingGenerationStatus.ExecutionFailed, DrawingPlan = plan, DrawingIr = ir, ExecutorResult = execution, Preflight = failedPreflight };
    }

    private static DrawingPlanDocument EmptyPlan(DrawingPlanningInput input) => DrawingPlanJson.Rehash(new DrawingPlanDocument { ProjectId = input.ProjectId, SourcePlanningInputHash = input.PlanningInputHash ?? new string('0', 64), SourcePagePlanHash = new string('0', 64) });
}
