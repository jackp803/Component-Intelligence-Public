namespace ComponentIntelligence.Electrical.Drawing;

public sealed class DrawingPlanningWorkspaceController(DrawingPlanEditService editService)
{
    private readonly DrawingPlanEditService _edits = editService ?? throw new ArgumentNullException(nameof(editService));
    private readonly Stack<DrawingPlanDocument> _undo = new();
    private readonly Stack<DrawingPlanDocument> _redo = new();
    private DrawingPlanDocument? _gestureBase;
    private DrawingPlanDocument? _gestureDraft;
    public DrawingPlanDocument? CurrentPlan { get; private set; }
    public DrawingPlanDocument? DisplayPlan => _gestureDraft ?? CurrentPlan;
    public string? SelectedPageId { get; private set; }
    public IReadOnlyList<string> SelectedRepresentationIds { get; private set; } = [];
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public DrawingPlanControlState? SelectionState(string? routeId = null)
    {
        if (routeId is not null) return CurrentPlan?.Routes.SingleOrDefault(r => r.RouteId == routeId)?.State;
        var states = CurrentPlan?.Placements.Where(p => SelectedRepresentationIds.Contains(p.RepresentationId))
            .Select(p => p.State).Distinct().ToArray() ?? [];
        return states.Length == 1 ? states[0] : null;
    }
    public IReadOnlyList<DrawingRoute> VisibleRoutes => CurrentPlan?.Routes
        .Where(route => string.Equals(route.PageId, SelectedPageId, StringComparison.Ordinal)).ToArray() ?? [];

    public void Load(DrawingPlanDocument? plan) { CancelGesture(); CurrentPlan = plan; _undo.Clear(); _redo.Clear(); SelectedPageId = plan?.Pages.OrderBy(x => x.Order).FirstOrDefault()?.PageId; SelectedRepresentationIds = []; }
    public void BeginGesture() { if (_gestureBase is not null) throw new InvalidOperationException("An edit gesture is already active."); _gestureBase = RequirePlan(); _gestureDraft = _gestureBase; }
    public void PreviewRouteSegment(string id, int segmentIndex, long delta) => _gestureDraft = _edits.MoveRouteSegment(RequireGesture(), id, segmentIndex, delta);
    public void PreviewBendPoint(string id, int pointIndex, long x, long y) => _gestureDraft = _edits.MoveBendPoint(RequireGesture(), id, pointIndex, x, y);
    public void PreviewPlacement(string id, long x, long y) => _gestureDraft = _edits.MovePlacement(RequireGesture(), id, x, y);
    public void CommitGesture() { var before = RequireGesture(); var after = _gestureDraft!; if (!ReferenceEquals(before, CurrentPlan)) throw new InvalidOperationException("Plan changed during gesture."); CancelGesture(); Apply(_ => after); }
    public void CancelGesture() { _gestureBase = null; _gestureDraft = null; }
    private DrawingPlanDocument RequireGesture() => _gestureBase ?? throw new InvalidOperationException("No edit gesture is active.");
    public void SelectPage(string pageId) { RequirePlan(); if (!CurrentPlan!.Pages.Any(x => x.PageId == pageId)) throw new InvalidOperationException("Page not found."); SelectedPageId = pageId; SelectedRepresentationIds = []; }
    public void SelectRepresentations(IEnumerable<string> ids) { var values = ids.Distinct(StringComparer.Ordinal).ToArray(); RequirePlan(); if (values.Any(id => !CurrentPlan!.Placements.Any(x => x.RepresentationId == id))) throw new InvalidOperationException("Unknown representation selection."); SelectedRepresentationIds = values; }

    public void MovePlacement(string id, long x, long y) => Apply(plan => _edits.MovePlacement(plan, id, x, y));
    public void RotatePlacement(string id, int degrees) => Apply(plan => _edits.RotatePlacement(plan, id, degrees));
    public void SetPlacementState(string id, DrawingPlanControlState state) => Apply(plan => _edits.SetPlacementState(plan, id, state));
    public void SetSelectedPlacementState(DrawingPlanControlState state) => Apply(plan =>
    {
        foreach (var id in SelectedRepresentationIds) plan = _edits.SetPlacementState(plan, id, state);
        return plan;
    });
    public void MovePage(string id, int index) => Apply(plan => _edits.MovePage(plan, id, index));
    public async Task TransferSelectedAsync(string targetPageId, Func<DrawingPlanDocument, Task<DrawingPlanDocument>> rebuild)
    {
        var before = RequirePlan();
        var selected = SelectedRepresentationIds.ToArray();
        var proposal = _edits.PreparePageTransfer(before, selected, targetPageId);
        if (ReferenceEquals(before, proposal)) return;
        var after = DrawingPlanJson.Rehash(await rebuild(proposal));
        if (!ReferenceEquals(before, CurrentPlan)) throw new InvalidOperationException("搬頁期間圖面已變更，請重新操作。結果未套用。");
        if (after.ProjectId != before.ProjectId || after.SourcePlanningInputHash != before.SourcePlanningInputHash ||
            !before.Placements.Select(p => p.RepresentationId).ToHashSet(StringComparer.Ordinal).SetEquals(after.Placements.Select(p => p.RepresentationId)))
            throw new InvalidOperationException("搬頁不得變更專案或 representation 身分。結果未套用。");
        foreach (var id in selected)
            if (after.Placements.Single(p => p.RepresentationId == id).PageId != targetPageId)
                throw new InvalidOperationException("Planner 未保留明確搬頁結果。請確認配對 runtime。");
        foreach (var route in before.Routes)
            if (!after.Routes.Any(r => r.ConnectionId == route.ConnectionId && r.EndpointAId == route.EndpointAId && r.EndpointBId == route.EndpointBId))
                throw new InvalidOperationException("搬頁後部分接線無法重建，原圖面未變更。");
        if (after.Issues.Any(i => i.Severity == DrawingPlanningIssueSeverity.Blocker && selected.Contains(i.TargetId)))
            throw new InvalidOperationException("目標頁面存在位置衝突；請調整位置後再搬頁。原圖面未變更。");
        Apply(_ => after);
        SelectedPageId = targetPageId;
        SelectedRepresentationIds = selected;
    }
    public void SetPageOrderState(string id, DrawingPlanControlState state) => Apply(plan => _edits.SetPageOrderState(plan, id, state));
    public void MoveRouteSegment(string id, int segmentIndex, long delta) => Apply(plan => _edits.MoveRouteSegment(plan, id, segmentIndex, delta));
    public void MoveBendPoint(string id, int pointIndex, long x, long y) => Apply(plan => _edits.MoveBendPoint(plan, id, pointIndex, x, y));
    public void AddBendPoint(string id, int segmentIndex, long x, long y) => Apply(plan => _edits.AddBendPoint(plan, id, segmentIndex, x, y));
    public void DeleteBendPoint(string id, int pointIndex) => Apply(plan => _edits.DeleteBendPoint(plan, id, pointIndex));
    public void SetRouteState(string id, DrawingPlanControlState state) => Apply(plan => _edits.SetRouteState(plan, id, state));
    public void Align(DrawingAlignment alignment) => Apply(plan => _edits.AlignPlacements(plan, SelectedRepresentationIds, alignment));
    public void Distribute(DrawingDistribution distribution) => Apply(plan => _edits.DistributePlacements(plan, SelectedRepresentationIds, distribution));
    public void ResetPlacement(string id) => Apply(plan => _edits.ResetPlacementToAuto(plan, id));
    public void ResetRoute(string id) => Apply(plan => _edits.ResetRouteToAuto(plan, id));
    public void ResetGroup(string id) => Apply(plan => _edits.ResetGroupToAuto(plan, id));
    public void ResetPage(string id) => Apply(plan => _edits.ResetPageToAuto(plan, id));

    public bool Undo() { if (_undo.Count == 0 || CurrentPlan is null) return false; _redo.Push(CurrentPlan); CurrentPlan = _undo.Pop(); ReconcileSelection(); return true; }
    public bool Redo() { if (_redo.Count == 0 || CurrentPlan is null) return false; _undo.Push(CurrentPlan); CurrentPlan = _redo.Pop(); ReconcileSelection(); return true; }
    private void ReconcileSelection()
    {
        var selected = RequirePlan().Placements.Where(p => SelectedRepresentationIds.Contains(p.RepresentationId)).ToArray();
        SelectedRepresentationIds = selected.Select(p => p.RepresentationId).ToArray();
        var pages = selected.Select(p => p.PageId).Distinct().ToArray();
        if (pages.Length == 1) SelectedPageId = pages[0];
    }

    private DrawingPlanDocument RequirePlan() => CurrentPlan ?? throw new InvalidOperationException("Drawing Plan is not loaded.");
    private void Apply(Func<DrawingPlanDocument, DrawingPlanDocument> operation) { var before = RequirePlan(); var after = operation(before); if (DrawingPlanJson.Serialize(before) == DrawingPlanJson.Serialize(after)) return; _undo.Push(before); _redo.Clear(); CurrentPlan = after; }
}
