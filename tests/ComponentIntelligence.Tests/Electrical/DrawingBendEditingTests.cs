using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingBendEditingTests
{
    [Fact]
    public void BendGestureHasOneUndoAndCancelDoesNotPersistPreview()
    {
        var edits = new DrawingPlanEditService();
        var plan = edits.AddBendPoint(DrawingEditorGestureTests.Plan(), "R", 0, 200, 100);
        var controller = new DrawingPlanningWorkspaceController(edits);
        controller.Load(plan);
        controller.BeginGesture();
        controller.PreviewBendPoint("R", 1, 200, 130);
        controller.PreviewBendPoint("R", 1, 220, 150);
        Assert.Equal(plan.DrawingPlanHash, controller.CurrentPlan!.DrawingPlanHash);
        Assert.False(controller.CanUndo);
        controller.CancelGesture();
        Assert.Equal(plan.DrawingPlanHash, controller.DisplayPlan!.DrawingPlanHash);
        controller.BeginGesture();
        controller.PreviewBendPoint("R", 1, 220, 150);
        controller.CommitGesture();
        var saved = DrawingPlanJson.Serialize(controller.CurrentPlan!);
        Assert.True(controller.Undo());
        Assert.False(controller.CanUndo);
        Assert.Equal(plan.DrawingPlanHash, controller.CurrentPlan!.DrawingPlanHash);
        Assert.True(controller.Redo());
        Assert.Equal(saved, DrawingPlanJson.Serialize(controller.CurrentPlan!));
    }

    [Fact]
    public void MovingInsertedBendBuildsLocalOrthogonalDetourWithoutChangingAnchors()
    {
        var edits = new DrawingPlanEditService();
        var seed = DrawingEditorGestureTests.Plan();
        var inserted = edits.AddBendPoint(seed, "R", 0, 200, 100);
        var moved = edits.MoveBendPoint(inserted, "R", 1, 210, 150);
        var route = Assert.Single(moved.Routes);
        Assert.Equal(seed.Routes[0].Points[0], route.Points[0]);
        Assert.Equal(seed.Routes[0].Points[^1], route.Points[^1]);
        Assert.Contains(new DrawingPoint(210, 150), route.Points);
        Assert.Equal(new DrawingPoint[] { new(100, 100), new(100, 150), new(210, 150), new(300, 150), new(300, 100) }, route.Points);
        Assert.Equal(("C", "A", "B"), (route.ConnectionId, route.EndpointAId, route.EndpointBId));
        Assert.All(route.Points.Zip(route.Points.Skip(1)), pair =>
            Assert.True(pair.First.X == pair.Second.X || pair.First.Y == pair.Second.Y));
        Assert.Equal(DrawingPlanControlState.Manual, route.State);
        Assert.Equal(2, seed.Routes[0].Points.Count);
    }

    [Fact]
    public void VerticalBendDetourAndCornerMoveKeepUneditedVertices()
    {
        var seed = DrawingEditorGestureTests.Plan();
        var vertical = DrawingPlanJson.Rehash(seed with { Routes = [seed.Routes[0] with
            { Points = [new(100, 100), new(100, 200), new(100, 300)] }] });
        var edits = new DrawingPlanEditService();
        Assert.Equal(new DrawingPoint[] { new(100, 100), new(150, 100), new(150, 210), new(150, 300), new(100, 300) },
            edits.MoveBendPoint(vertical, "R", 1, 150, 210).Routes[0].Points);
        var corner = DrawingPlanJson.Rehash(seed with { Routes = [seed.Routes[0] with
            { Points = [new(100, 100), new(300, 100), new(300, 200), new(400, 200)] }] });
        Assert.Equal(new DrawingPoint[] { new(100, 100), new(250, 100), new(250, 150), new(300, 150), new(300, 200), new(400, 200) },
            edits.MoveBendPoint(corner, "R", 1, 250, 150).Routes[0].Points);
    }

    [Fact]
    public void BendNoOpDoesNotMarkAutoRouteManual()
    {
        var seed = DrawingEditorGestureTests.Plan();
        var plan = DrawingPlanJson.Rehash(seed with { Routes = [seed.Routes[0] with
            { Points = [new(100, 100), new(200, 100), new(300, 100)] }] });
        var moved = new DrawingPlanEditService().MoveBendPoint(plan, "R", 1, 200, 100);
        Assert.Equal(plan.DrawingPlanHash, moved.DrawingPlanHash);
    }

    [Fact]
    public void BendDeletionRejectsDiagonalShortcutAndPreservesLockedRoute()
    {
        var seed = DrawingEditorGestureTests.Plan();
        var corner = DrawingPlanJson.Rehash(seed with { Routes = [seed.Routes[0] with
            { Points = [new(100, 100), new(300, 100), new(300, 200)] }] });
        var edits = new DrawingPlanEditService();
        Assert.Throws<InvalidOperationException>(() => edits.DeleteBendPoint(corner, "R", 1));
        var locked = edits.SetRouteState(corner, "R", DrawingPlanControlState.Locked);
        Assert.Throws<InvalidOperationException>(() => edits.MoveBendPoint(locked, "R", 1, 200, 150));
        Assert.Throws<InvalidOperationException>(() => edits.AddBendPoint(locked, "R", 0, 200, 100));
        Assert.Equal(3, corner.Routes[0].Points.Count);
    }
}
