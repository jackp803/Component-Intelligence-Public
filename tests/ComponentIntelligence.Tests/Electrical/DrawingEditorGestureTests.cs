using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingEditorGestureTests
{
    [Fact]
    public void SelectedPlacementState_IsOneAtomicUndoForTheWholeSelection()
    {
        var seed = WithPlacement();
        var plan = DrawingPlanJson.Rehash(seed with
        {
            Groups = [seed.Groups[0] with { RepresentationIds = ["REP", "REP2"] }],
            Placements = [seed.Placements[0], seed.Placements[0] with { RepresentationId = "REP2", X = 400 }]
        });
        var controller = new DrawingPlanningWorkspaceController(new DrawingPlanEditService());
        controller.Load(plan);
        controller.SelectRepresentations(["REP", "REP2"]);
        controller.SetSelectedPlacementState(DrawingPlanControlState.Locked);
        Assert.All(controller.CurrentPlan!.Placements, p => Assert.Equal(DrawingPlanControlState.Locked, p.State));
        Assert.True(controller.Undo());
        Assert.Equal(plan.DrawingPlanHash, controller.CurrentPlan!.DrawingPlanHash);
        Assert.False(controller.CanUndo);
        Assert.True(controller.Redo());
        Assert.All(controller.CurrentPlan!.Placements, p => Assert.Equal(DrawingPlanControlState.Locked, p.State));
    }

    [Fact]
    public void SelectedStateReflectsUndoAndRouteSelection()
    {
        var controller = new DrawingPlanningWorkspaceController(new DrawingPlanEditService());
        controller.Load(WithPlacement()); controller.SelectRepresentations(["REP"]);
        Assert.Equal(DrawingPlanControlState.Auto, controller.SelectionState());
        controller.SetPlacementState("REP", DrawingPlanControlState.Locked);
        Assert.Equal(DrawingPlanControlState.Locked, controller.SelectionState());
        controller.Undo();
        Assert.Equal(DrawingPlanControlState.Auto, controller.SelectionState());
        controller.SetRouteState("R", DrawingPlanControlState.Manual);
        Assert.Equal(DrawingPlanControlState.Manual, controller.SelectionState("R"));
    }
    internal static DrawingPlanDocument Plan() => DrawingPlanJson.Rehash(new DrawingPlanDocument
    {
        ProjectId = "P", SourcePlanningInputHash = new string('1', 64), SourcePagePlanHash = new string('2', 64),
        Pages = [new DrawingPlanPage { PageId = "PAGE", Archetype = "FieldDevices", Bounds = new(0, 0, 1000, 700) }],
        Routes = [new DrawingRoute { RouteId = "R", PageId = "PAGE", ConnectionId = "C", EndpointAId = "A", EndpointBId = "B",
            Points = [new(100, 100), new(300, 100)] }]
    });

    [Fact]
    public void DragSingleSegment_PreservesBothAnchorsAndAddsOrthogonalLeads()
    {
        var before = Plan();
        var after = new DrawingPlanEditService().MoveRouteSegment(before, "R", 0, 40);
        var route = Assert.Single(after.Routes);
        Assert.Equal(before.Routes[0].Points[0], route.Points[0]);
        Assert.Equal(before.Routes[0].Points[^1], route.Points[^1]);
        Assert.Equal(new DrawingPoint[] { new(100,100), new(100,140), new(300,140), new(300,100) }, route.Points);
        Assert.Equal(("C", "A", "B"), (route.ConnectionId, route.EndpointAId, route.EndpointBId));
    }

    [Fact]
    public void DragLastSegment_DoesNotMoveDestinationAnchor()
    {
        var before = Plan() with { Routes = [Plan().Routes[0] with { Points = [new(100,100),new(200,100),new(200,200)] }] };
        var route = Assert.Single(new DrawingPlanEditService().MoveRouteSegment(before, "R", 1, 30).Routes);
        Assert.Equal(new DrawingPoint(200,200), route.Points[^1]);
        Assert.Equal(new DrawingPoint(100,100), route.Points[0]);
        Assert.Contains(new DrawingPoint(230,200), route.Points);
    }

    [Fact]
    public void NoOpEdit_DoesNotCreateUndoEntry()
    {
        var controller = new DrawingPlanningWorkspaceController(new DrawingPlanEditService());
        controller.Load(Plan());
        controller.MoveRouteSegment("R", 0, 0);
        Assert.False(controller.CanUndo);
    }

    [Fact]
    public void Gesture_ManyDraftMovesCommitExactlyOneUndo_AndCancelIsReadOnly()
    {
        var controller = new DrawingPlanningWorkspaceController(new DrawingPlanEditService());
        var plan = Plan(); controller.Load(plan);
        controller.BeginGesture();
        controller.PreviewRouteSegment("R", 0, 10);
        controller.PreviewRouteSegment("R", 0, 40);
        Assert.Equal(plan.DrawingPlanHash, controller.CurrentPlan!.DrawingPlanHash);
        Assert.False(controller.CanUndo);
        Assert.NotEqual(plan.DrawingPlanHash, controller.DisplayPlan!.DrawingPlanHash);
        controller.CancelGesture();
        Assert.Equal(plan.DrawingPlanHash, controller.DisplayPlan!.DrawingPlanHash);
        controller.BeginGesture(); controller.PreviewRouteSegment("R", 0, 40); controller.CommitGesture();
        var edited = controller.CurrentPlan!.DrawingPlanHash;
        Assert.True(controller.Undo()); Assert.False(controller.CanUndo);
        Assert.Equal(plan.DrawingPlanHash, controller.CurrentPlan!.DrawingPlanHash);
        Assert.True(controller.Redo()); Assert.Equal(edited, controller.CurrentPlan!.DrawingPlanHash);
    }

    [Fact]
    public void PlacementMove_TranslatesBoundEndpoint_PreservesPeerAndManualMiddle()
    {
        var plan = WithPlacement(); var edits = BoundEdits();
        var moved = edits.MovePlacement(plan, "REP", 140, 180);
        Assert.Equal(new DrawingPoint(140,180), moved.Routes[0].Points[0]);
        Assert.Equal(plan.Routes[0].Points[^1], moved.Routes[0].Points[^1]);
        Assert.Equal(plan.Routes[0].ConnectionId, moved.Routes[0].ConnectionId);
        Assert.Equal(plan.Routes[0].EndpointAId, moved.Routes[0].EndpointAId);
    }

    [Fact]
    public void AutoContinuationMove_TranslatesTheWholeLocalStub()
    {
        var source = WithPlacement();
        var plan = DrawingPlanJson.Rehash(source with
        {
            Pages = [source.Pages[0], source.Pages[0] with { PageId="PEER",Order=1 }],
            Routes = [source.Routes[0],source.Routes[0] with { RouteId="PEER-R",PageId="PEER" }],
            CrossPageRelations = [new DrawingCrossPageRelation { RelationId="REL",RelationKind="ElectricalConnectionContinuation",EngineeringId="C",
                SourceRepresentationId="REP",DestinationRepresentationId="PEER-REP",SourcePageId="PAGE",DestinationPageId="PEER",SourceRouteId="R",DestinationRouteId="PEER-R" }]
        });
        var moved = BoundEdits().MovePlacement(plan,"REP",140,180);
        var route = moved.Routes.Single(r=>r.RouteId=="R");
        Assert.Equal(new DrawingPoint[] {new(140,180),new(340,180)},route.Points);
        Assert.Equal(plan.Routes.Single(r=>r.RouteId=="PEER-R").Points,moved.Routes.Single(r=>r.RouteId=="PEER-R").Points);
    }

    [Fact]
    public void PlacementMove_WithLockedAttachedRoute_IsRejectedAtomically()
    {
        var plan = WithPlacement() with { Routes = [Plan().Routes[0] with { State = DrawingPlanControlState.Locked }] };
        Assert.Throws<InvalidOperationException>(() => BoundEdits().MovePlacement(plan, "REP", 140, 180));
        Assert.Equal(100, plan.Placements[0].X);
    }

    private static DrawingPlanEditService BoundEdits()
    {
        var edits = new DrawingPlanEditService();
        edits.SetEndpointBindings(new Dictionary<string, IReadOnlySet<string>> { ["REP"] = new HashSet<string> { "A" } });
        return edits;
    }

    private static DrawingPlanDocument WithPlacement() => DrawingPlanJson.Rehash(Plan() with
    {
        Groups = [new DrawingPlanGroup { GroupId="G", PageId="PAGE", Bounds=new(0,0,1000,700), RepresentationIds=["REP"] }],
        Placements = [new DrawingPlacement { RepresentationId="REP", PageId="PAGE", GroupId="G", X=100,Y=100, Width=80,Height=60 }]
    });

    [Fact]
    public void LockedSegment_RejectsWithoutChangingPlan()
    {
        var before = DrawingPlanJson.Rehash(Plan() with { Routes = [Plan().Routes[0] with { State = DrawingPlanControlState.Locked }] });
        var controller = new DrawingPlanningWorkspaceController(new DrawingPlanEditService());
        controller.Load(before);
        Assert.Throws<InvalidOperationException>(() => controller.MoveRouteSegment("R", 0, 20));
        Assert.Equal(before.DrawingPlanHash, controller.CurrentPlan!.DrawingPlanHash);
        Assert.False(controller.CanUndo);
    }
}
