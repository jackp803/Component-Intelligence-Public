using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingPageTransferTests
{
    private static DrawingPlanDocument Plan() => DrawingPlanJson.Rehash(DrawingEditorGestureTests.Plan() with
    {
        Pages = [new() { PageId="PAGE", Archetype="FieldDevices", Bounds=new(0,0,1000,700) }, new() { PageId="TARGET", Archetype="FieldDevices", Order=1, Bounds=new(0,0,1000,700) }],
        Groups = [new() { GroupId="G", PageId="PAGE", Bounds=new(0,0,1000,600), RepresentationIds=["REP"] }],
        Placements = [new() { RepresentationId="REP", PageId="PAGE", GroupId="G", X=40,Y=40,Width=100,Height=60 }]
    });
    private static DrawingPlanningWorkspaceController Controller(DrawingPlanDocument plan)
    {
        var edits = new DrawingPlanEditService();
        edits.SetEndpointBindings(new Dictionary<string,IReadOnlySet<string>> { ["REP"] = new HashSet<string>{"A"} });
        var result = new DrawingPlanningWorkspaceController(edits);
        result.Load(plan); result.SelectRepresentations(["REP"]); return result;
    }

    [Fact]
    public async Task FailedRebuildLeavesPlanSelectionAndUndoUntouched()
    {
        var plan=Plan(); var controller=Controller(plan);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.TransferSelectedAsync("TARGET", proposal =>
        {
            Assert.Equal("TARGET", proposal.Placements[0].PageId);
            Assert.Empty(proposal.Routes);
            Assert.Equal(plan.DrawingPlanHash, controller.CurrentPlan!.DrawingPlanHash);
            throw new InvalidOperationException("planner failed");
        }));
        Assert.Equal(plan.DrawingPlanHash, controller.CurrentPlan!.DrawingPlanHash);
        Assert.Equal("PAGE",controller.SelectedPageId);
        Assert.False(controller.CanUndo);
    }

    [Fact]
    public async Task PreservedRouteRequiresExplicitResolutionBeforeTransfer()
    {
        var plan=DrawingPlanJson.Rehash(Plan() with { Routes=[Plan().Routes[0] with { State=DrawingPlanControlState.Locked }] });
        var controller=Controller(plan); var invoked=false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.TransferSelectedAsync("TARGET", proposal => { invoked=true; return Task.FromResult(proposal); }));
        Assert.False(invoked); Assert.Equal(plan.DrawingPlanHash,controller.CurrentPlan!.DrawingPlanHash);
    }

    [Fact]
    public async Task SuccessfulTransferIsOneUndoAndSelectionFollowsItsPage()
    {
        var plan=DrawingPlanJson.Rehash(Plan() with { Routes=[] });
        var controller=Controller(plan);
        await controller.TransferSelectedAsync("TARGET", Task.FromResult);
        Assert.Equal("TARGET", controller.SelectedPageId);
        Assert.Equal("TARGET", controller.CurrentPlan!.Placements[0].PageId);
        Assert.True(controller.Undo());
        Assert.Equal("PAGE", controller.SelectedPageId);
        Assert.Equal(plan.DrawingPlanHash, controller.CurrentPlan!.DrawingPlanHash);
        Assert.False(controller.CanUndo);
        Assert.True(controller.Redo());
        Assert.Equal("TARGET", controller.SelectedPageId);
    }

    [Fact]
    public async Task MissingRebuiltConnectionCannotBeCommitted()
    {
        var plan=Plan(); var controller=Controller(plan);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.TransferSelectedAsync("TARGET", Task.FromResult));
        Assert.Equal(plan.DrawingPlanHash,controller.CurrentPlan!.DrawingPlanHash);
        Assert.False(controller.CanUndo);
    }
}
