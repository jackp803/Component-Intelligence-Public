namespace ComponentIntelligence.Electrical.Drawing;

public sealed class DrawingPreflightService
{
    public DrawingPreflightResult Evaluate(DrawingPlanningInput input, DrawingPlanDocument plan, DrawingRuntimeValidation runtime, DrawingGenerationTarget target)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(runtime);
        var issues = new List<DrawingActionableIssue>(runtime.Issues);
        foreach (var issue in input.Issues)
            issues.Add(Convert(issue, DrawingBlockerClass.Engineering, ResolvePage(plan, input, issue.TargetKind, issue.TargetId)));
        foreach (var issue in plan.Issues)
            issues.Add(Convert(issue, DrawingBlockerClass.Drawing, ResolvePage(plan, input, issue.TargetKind, issue.TargetId)));

        var pages = plan.Pages.Select(x => x.PageId).ToHashSet(StringComparer.Ordinal);
        var blockerIssues = issues.Where(x => x.Severity == DrawingActionableSeverity.Blocker).ToArray();
        if (target == DrawingGenerationTarget.FullGeneration)
            return new DrawingPreflightResult(blockerIssues.Length == 0, blockerIssues.Length == 0 ? pages.OrderBy(x => x, StringComparer.Ordinal).ToArray() : [], issues.OrderBy(x => x.IssueId, StringComparer.Ordinal).ToArray());

        if (blockerIssues.Any(x => x.BlockerClass == DrawingBlockerClass.Runtime || x.Scope == DrawingActionableScope.Global))
            return new DrawingPreflightResult(false, [], issues.OrderBy(x => x.IssueId, StringComparer.Ordinal).ToArray());
        var blockedPages = blockerIssues.Where(x => x.PageId is not null).Select(x => x.PageId!).ToHashSet(StringComparer.Ordinal);
        var eligible = pages.Except(blockedPages, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        return new DrawingPreflightResult(eligible.Length > 0, eligible, issues.OrderBy(x => x.IssueId, StringComparer.Ordinal).ToArray());
    }

    private static DrawingActionableIssue Convert(DrawingPlanningIssue issue, DrawingBlockerClass blockerClass, string? pageId) => new()
    {
        IssueId = issue.IssueId,
        Severity = issue.Severity switch { DrawingPlanningIssueSeverity.Blocker => DrawingActionableSeverity.Blocker, DrawingPlanningIssueSeverity.Warning => DrawingActionableSeverity.Warning, _ => DrawingActionableSeverity.Info },
        Scope = pageId is null ? DrawingActionableScope.Global : DrawingActionableScope.Object,
        BlockerClass = blockerClass,
        Code = issue.Code,
        Message = issue.Message,
        PageId = pageId,
        ObjectId = issue.TargetId,
        RepairAction = RepairAction(issue.Code)
    };

    private static DrawingActionableIssue Convert(DrawingPlanIssue issue, DrawingBlockerClass blockerClass, string? pageId) => new()
    {
        IssueId = issue.IssueId,
        Severity = issue.Severity switch { DrawingPlanningIssueSeverity.Blocker => DrawingActionableSeverity.Blocker, DrawingPlanningIssueSeverity.Warning => DrawingActionableSeverity.Warning, _ => DrawingActionableSeverity.Info },
        Scope = pageId is null ? DrawingActionableScope.Global : DrawingActionableScope.Object,
        BlockerClass = blockerClass,
        Code = issue.Code,
        Message = issue.Message,
        PageId = pageId,
        ObjectId = issue.TargetId,
        RepairAction = RepairAction(issue.Code)
    };

    private static string RepairAction(string code) => code switch
    {
        "DRAWING_REQUIRED_ENGINEERING_EVIDENCE_MISSING" => "EngineeringEvidence",
        "DRAWING_LOCKED_LAYOUT_CONFLICT" => "DrawingObject",
        "DRAWING_ROUTE_ENDPOINT_PLACEMENT_MISSING" => "DrawingObject",
        _ => "IssueTarget"
    };

    private static string? ResolvePage(DrawingPlanDocument plan, DrawingPlanningInput input, string targetKind, string targetId)
    {
        if (targetKind.Contains("Page", StringComparison.OrdinalIgnoreCase) && plan.Pages.Any(x => x.PageId == targetId)) return targetId;
        var placement = plan.Placements.FirstOrDefault(x => x.RepresentationId == targetId);
        if (placement is not null) return placement.PageId;
        if (targetKind.Contains("Placement", StringComparison.OrdinalIgnoreCase)) return plan.Placements.FirstOrDefault(x => x.RepresentationId == targetId)?.PageId;
        var repIds = input.Representations.Where(x => x.RepresentationId == targetId || x.OwnerId == targetId).Select(x => x.RepresentationId).ToHashSet(StringComparer.Ordinal);
        var pages = plan.Placements.Where(x => repIds.Contains(x.RepresentationId)).Select(x => x.PageId).Distinct(StringComparer.Ordinal).ToArray();
        return pages.Length == 1 ? pages[0] : null;
    }
}
