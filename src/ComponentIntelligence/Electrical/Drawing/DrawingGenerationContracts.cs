using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Electrical.Drawing;

public enum DrawingGenerationTarget { Preview, FullGeneration }
public enum DrawingActionableSeverity { Info, Warning, Blocker }
public enum DrawingActionableScope { Global, Page, Object }
public enum DrawingBlockerClass { Engineering, Drawing, Runtime }
public enum DrawingGenerationStatus { Blocked, PreviewReady, ReadyForCp3C }

public sealed record DrawingActionableIssue
{
    public required string IssueId { get; init; }
    public DrawingActionableSeverity Severity { get; init; }
    public DrawingActionableScope Scope { get; init; }
    public DrawingBlockerClass BlockerClass { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? PageId { get; init; }
    public string? ObjectId { get; init; }
    public string? RepairAction { get; init; }

    public static DrawingActionableIssue Runtime(string id, string code, string message, string repairAction) => new()
    {
        IssueId = id, Severity = DrawingActionableSeverity.Blocker, Scope = DrawingActionableScope.Global,
        BlockerClass = DrawingBlockerClass.Runtime, Code = code, Message = message, RepairAction = repairAction
    };
}

public sealed record DrawingPreflightResult(bool CanProceed, IReadOnlyList<string> EligiblePageIds, IReadOnlyList<DrawingActionableIssue> Issues);

public sealed record DrawingIrDocument(
    string Status,
    string? DrawingIrHash,
    string SourcePlanningInputHash,
    string SourcePagePlanHash,
    string SourceDrawingPlanHash,
    IReadOnlyList<DrawingActionableIssue> Issues,
    string RawJson);

public interface IDrawingIrClient
{
    Task<DrawingIrDocument> CompileAsync(DrawingPlanningInput input, DrawingPlanDocument plan, CancellationToken cancellationToken);
}

public interface IDrawingExecutorClient
{
    Task ExecuteAsync(DrawingIrDocument drawingIr, CancellationToken cancellationToken);
}

public interface IProjectRevisionCheckpointSink
{
    Task CheckpointAsync(ElectricalProject project, ProjectRevisionTrigger trigger, CancellationToken cancellationToken);
}

public sealed class ProjectRevisionCheckpointSink(ProjectRevisionService service) : IProjectRevisionCheckpointSink
{
    private readonly ProjectRevisionService _service = service ?? throw new ArgumentNullException(nameof(service));
    public async Task CheckpointAsync(ElectricalProject project, ProjectRevisionTrigger trigger, CancellationToken cancellationToken) =>
        _ = await _service.CreateCheckpointAsync(project, trigger, cancellationToken: cancellationToken);
}

public sealed record DrawingGenerationResult
{
    public DrawingGenerationStatus Status { get; init; }
    public DrawingPlanDocument? DrawingPlan { get; init; }
    public DrawingIrDocument? DrawingIr { get; init; }
    public required DrawingPreflightResult Preflight { get; init; }
    public bool DwgOrWdpGenerated => false;
}
