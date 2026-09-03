namespace ComponentIntelligence.Electrical.Drawing;

public sealed class DrawingPlanningWorkspaceController(DrawingPlanEditService editService)
{
    private readonly DrawingPlanEditService _edits = editService ?? throw new ArgumentNullException(nameof(editService));
    private readonly Stack<DrawingPlanDocument> _undo = new();
    private readonly Stack<DrawingPlanDocument> _redo = new();
    public DrawingPlanDocument? CurrentPlan { get; private set; }
    public string? SelectedPageId { get; private set; }
    public IReadOnlyList<string> SelectedRepresentationIds { get; private set; } = [];
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Load(DrawingPlanDocument? plan) { CurrentPlan = plan; _undo.Clear(); _redo.Clear(); SelectedPageId = plan?.Pages.OrderBy(x => x.Order).FirstOrDefault()?.PageId; SelectedRepresentationIds = []; }
    public void SelectPage(string pageId) { RequirePlan(); if (!CurrentPlan!.Pages.Any(x => x.PageId == pageId)) throw new InvalidOperationException("Page not found."); SelectedPageId = pageId; SelectedRepresentationIds = []; }
    public void SelectRepresentations(IEnumerable<string> ids) { var values = ids.Distinct(StringComparer.Ordinal).ToArray(); RequirePlan(); if (values.Any(id => !CurrentPlan!.Placements.Any(x => x.RepresentationId == id))) throw new InvalidOperationException("Unknown representation selection."); SelectedRepresentationIds = values; }

    public void MovePlacement(string id, long x, long y) => Apply(plan => _edits.MovePlacement(plan, id, x, y));
    public void RotatePlacement(string id, int degrees) => Apply(plan => _edits.RotatePlacement(plan, id, degrees));
    public void SetPlacementState(string id, DrawingPlanControlState state) => Apply(plan => _edits.SetPlacementState(plan, id, state));
    public void MovePage(string id, int index) => Apply(plan => _edits.MovePage(plan, id, index));
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

    public bool Undo() { if (_undo.Count == 0 || CurrentPlan is null) return false; _redo.Push(CurrentPlan); CurrentPlan = _undo.Pop(); return true; }
    public bool Redo() { if (_redo.Count == 0 || CurrentPlan is null) return false; _undo.Push(CurrentPlan); CurrentPlan = _redo.Pop(); return true; }

    private DrawingPlanDocument RequirePlan() => CurrentPlan ?? throw new InvalidOperationException("Drawing Plan is not loaded.");
    private void Apply(Func<DrawingPlanDocument, DrawingPlanDocument> operation) { var before = RequirePlan(); var after = operation(before); _undo.Push(before); _redo.Clear(); CurrentPlan = after; }
}
