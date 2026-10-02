using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingPageTransferTests
{
    [Fact]
    public async Task LabelOnlyInputUpgradeAllowsAtomicPageTransferButOtherInputChangesDoNot()
    {
        var oldInput = DrawingPlanningJson.Deserialize(DrawingPlanningJson.Serialize(new DrawingPlanningInput
        {
            ProjectId = "P", Representations = [new DrawingRepresentationDecision
            {
                RepresentationId = "REP", OwnerId = "IO", Role = DrawingRepresentationRole.Schematic,
                PortBindings = [new DrawingPortBinding { EngineeringEndpointId = "A", ConnectionPointId = "PORT:A" }]
            }]
        }));
        var labeled = DrawingPlanningJson.Deserialize(DrawingPlanningJson.Serialize(oldInput with
        {
            Representations = [oldInput.Representations[0] with { DisplayLabel = "AL1342", PortBindings = [oldInput.Representations[0].PortBindings[0] with { DisplayLabel = "X01" }] }]
        }));
        Assert.True(DrawingPlanningInputHashCompatibility.IsDisplayLabelOnlyUpgrade(labeled, oldInput.PlanningInputHash!));
        var oriented = DrawingPlanningJson.Deserialize(DrawingPlanningJson.Serialize(labeled with
        {
            Representations = [labeled.Representations[0] with
            {
                PortBindings = [labeled.Representations[0].PortBindings[0] with { PhysicalSide = "Left" }]
            }]
        }));
        Assert.True(DrawingPlanningInputHashCompatibility.IsPresentationMetadataOnlyUpgrade(oriented, oldInput.PlanningInputHash!));
        Assert.True(DrawingPlanningInputHashCompatibility.IsPresentationMetadataOnlyUpgrade(oriented, labeled.PlanningInputHash!));
        var changed = DrawingPlanningJson.Deserialize(DrawingPlanningJson.Serialize(labeled with
        {
            Connections = [new DrawingConnectionPlanningItem { ConnectionId = "NEW", FromEndpointId = "A", ToEndpointId = "B" }]
        }));
        Assert.False(DrawingPlanningInputHashCompatibility.IsDisplayLabelOnlyUpgrade(changed, oldInput.PlanningInputHash!));
        Assert.False(DrawingPlanningInputHashCompatibility.IsPresentationMetadataOnlyUpgrade(changed, oldInput.PlanningInputHash!));

        var plan = DrawingPlanJson.Rehash(Plan() with { SourcePlanningInputHash = oldInput.PlanningInputHash!, Routes = [] });
        var controller = Controller(plan);
        await controller.TransferSelectedAsync("TARGET", proposal => Task.FromResult(DrawingPlanJson.Rehash(
            proposal with { SourcePlanningInputHash = labeled.PlanningInputHash! })), labeled);
        Assert.Equal("TARGET", controller.CurrentPlan!.Placements[0].PageId);
        Assert.True(controller.Undo());
        Assert.Equal(plan.DrawingPlanHash, controller.CurrentPlan!.DrawingPlanHash);

        var blocked = Controller(plan);
        await Assert.ThrowsAsync<InvalidOperationException>(() => blocked.TransferSelectedAsync("TARGET", proposal => Task.FromResult(DrawingPlanJson.Rehash(
            proposal with { SourcePlanningInputHash = changed.PlanningInputHash! })), changed));
        Assert.False(blocked.CanUndo);
    }

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
