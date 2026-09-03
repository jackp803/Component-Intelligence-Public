using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Drawing;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingGenerationGateTests
{
    [Fact]
    public void ProgressivePreview_ExcludesLocalizedBlockedPageButKeepsUnaffectedPage()
    {
        var input = Input([new DrawingPlanningIssue { IssueId = "I1", Severity = DrawingPlanningIssueSeverity.Blocker, Code = "DRAWING_REQUIRED_ENGINEERING_EVIDENCE_MISSING", Message = "missing", TargetKind = "Representation", TargetId = "REP-A" }]);
        var plan = Plan();
        var result = new DrawingPreflightService().Evaluate(input, plan, DrawingRuntimeValidation.Valid(), DrawingGenerationTarget.Preview);
        Assert.True(result.CanProceed);
        Assert.DoesNotContain("PAGE-A", result.EligiblePageIds);
        Assert.Contains("PAGE-B", result.EligiblePageIds);
    }

    [Fact]
    public void FullGeneration_BlocksAnyEngineeringOrRuntimeBlocker()
    {
        var input = Input([new DrawingPlanningIssue { IssueId = "I1", Severity = DrawingPlanningIssueSeverity.Blocker, Code = "DRAWING_REQUIRED_ENGINEERING_EVIDENCE_MISSING", Message = "missing", TargetKind = "Representation", TargetId = "REP-A" }]);
        var service = new DrawingPreflightService();
        Assert.False(service.Evaluate(input, Plan(), DrawingRuntimeValidation.Valid(), DrawingGenerationTarget.FullGeneration).CanProceed);
        var runtime = new DrawingRuntimeValidation(false, [DrawingActionableIssue.Runtime("R1", "DRAWING_RUNTIME_PYTHON_MISSING", "python missing", "RuntimeSettings")]);
        Assert.False(service.Evaluate(Input([]), Plan(), runtime, DrawingGenerationTarget.FullGeneration).CanProceed);
    }

    [Fact]
    public async Task NoExecutorConfigured_ReturnsReadyForCp3cAndNeverClaimsDwg()
    {
        var input = Input([]); var plan = Plan(); var checkpoints = new FakeCheckpoints();
        var coordinator = Coordinator(input, plan, ReadyIr(input, plan), checkpoints, executor: null);
        var result = await coordinator.GenerateAutoCadAsync(new ElectricalProject { ProjectId = input.ProjectId }, DrawingRuntimeValidation.Valid(), CancellationToken.None);
        Assert.Equal(DrawingGenerationStatus.ReadyForCp3C, result.Status);
        Assert.False(result.DwgOrWdpGenerated);
        Assert.Equal(ProjectRevisionTrigger.GenerateAutoCad, Assert.Single(checkpoints.Triggers));
    }

    [Fact]
    public async Task ReadyIr_WithAppliedExecutor_ReturnsAppliedAndOutputPaths()
    {
        var input = Input([]); var plan = Plan();
        var executor = new FakeExecutor(new DrawingExecutorResult(DrawingExecutorStatus.Applied, "C:\\stage", "C:\\stage\\P1.wdp", ["C:\\stage\\001.dwg"], new string('9',64), [], "{}"));
        var result = await Coordinator(input, plan, ReadyIr(input, plan), new FakeCheckpoints(), executor).GenerateAutoCadAsync(new ElectricalProject { ProjectId = input.ProjectId }, DrawingRuntimeValidation.Valid(), CancellationToken.None);
        Assert.Equal(DrawingGenerationStatus.Applied, result.Status);
        Assert.True(result.DwgOrWdpGenerated);
        Assert.Equal("C:\\stage\\P1.wdp", result.ExecutorResult!.ProjectFile);
        Assert.Equal(1, executor.CallCount);
    }

    [Fact]
    public async Task ReadyIr_WithFailedExecutor_ReturnsExecutionFailedAndDoesNotClaimOutput()
    {
        var input = Input([]); var plan = Plan();
        var issue = DrawingActionableIssue.Runtime("E1", "EXECUTION_FAILED", "failed", "ExecutorRuntimeSettings");
        var executor = new FakeExecutor(new DrawingExecutorResult(DrawingExecutorStatus.Failed, null, null, [], null, [issue], "{}"));
        var result = await Coordinator(input, plan, ReadyIr(input, plan), new FakeCheckpoints(), executor).GenerateAutoCadAsync(new ElectricalProject { ProjectId = input.ProjectId }, DrawingRuntimeValidation.Valid(), CancellationToken.None);
        Assert.Equal(DrawingGenerationStatus.ExecutionFailed, result.Status);
        Assert.False(result.DwgOrWdpGenerated);
        Assert.Null(result.ExecutorResult!.ProjectFile);
        Assert.Contains(result.Preflight.Issues, x => x.Code == "EXECUTION_FAILED");
    }

    [Fact]
    public async Task BlockedPreflight_NeverCallsExecutor()
    {
        var input = Input([new DrawingPlanningIssue { IssueId="I-B", Severity=DrawingPlanningIssueSeverity.Blocker, Code="DRAWING_REQUIRED_ENGINEERING_EVIDENCE_MISSING", Message="missing", TargetKind="Representation", TargetId="REP-A" }]);
        var plan = Plan(); var executor = new FakeExecutor(Applied());
        var result = await Coordinator(input, plan, ReadyIr(input, plan), new FakeCheckpoints(), executor).GenerateAutoCadAsync(new ElectricalProject { ProjectId=input.ProjectId }, DrawingRuntimeValidation.Valid(), CancellationToken.None);
        Assert.Equal(DrawingGenerationStatus.Blocked, result.Status);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task BlockedIr_NeverCallsExecutor()
    {
        var input = Input([]); var plan = Plan(); var executor = new FakeExecutor(Applied());
        var blocked = new DrawingIrDocument("BLOCKED", null, input.PlanningInputHash!, plan.SourcePagePlanHash, plan.DrawingPlanHash!, [], "{}");
        var result = await Coordinator(input, plan, blocked, new FakeCheckpoints(), executor).GenerateAutoCadAsync(new ElectricalProject { ProjectId=input.ProjectId }, DrawingRuntimeValidation.Valid(), CancellationToken.None);
        Assert.Equal(DrawingGenerationStatus.Blocked, result.Status);
        Assert.Equal(0, executor.CallCount);
        Assert.False(result.DwgOrWdpGenerated);
    }

    private static DrawingGenerationCoordinator Coordinator(DrawingPlanningInput input, DrawingPlanDocument plan, DrawingIrDocument ir, FakeCheckpoints checkpoints, IDrawingExecutorClient? executor) =>
        new(_ => input, new FakePlanner(plan), new FakeIr(ir), new DrawingPreflightService(), checkpoints, executor);

    private static DrawingIrDocument ReadyIr(DrawingPlanningInput input, DrawingPlanDocument plan) =>
        new("READY", new string('A', 64), input.PlanningInputHash!, plan.SourcePagePlanHash, plan.DrawingPlanHash!, [], "{}");

    private static DrawingExecutorResult Applied() => new(DrawingExecutorStatus.Applied, "C:\\stage", "C:\\stage\\P1.wdp", ["C:\\stage\\001.dwg"], new string('9',64), [], "{}");

    private static DrawingPlanningInput Input(IReadOnlyList<DrawingPlanningIssue> issues)
    {
        var input = new DrawingPlanningInput
        {
            ProjectId = "P1",
            Representations = [Rep("REP-A", "OWNER-A", "EA"), Rep("REP-B", "OWNER-B", "EB")],
            Issues = issues.ToList()
        };
        return DrawingPlanningJson.Deserialize(DrawingPlanningJson.Serialize(input));
    }

    private static DrawingRepresentationDecision Rep(string id, string owner, string endpoint) => new()
    {
        RepresentationId = id, OwnerKind = DrawingRepresentationOwnerKind.Component, OwnerId = owner,
        Role = DrawingRepresentationRole.Schematic, Family = DrawingRepresentationFamily.FunctionalGeneric,
        ControlState = DrawingRepresentationControlState.Auto, AllowedRotations = [0],
        PortBindings = [new DrawingPortBinding { EngineeringEndpointId = endpoint, ConnectionPointId = "CP-" + endpoint }]
    };

    private static DrawingPlanDocument Plan() => DrawingPlanJson.Rehash(new DrawingPlanDocument
    {
        ProjectId = "P1", SourcePlanningInputHash = new string('1', 64), SourcePagePlanHash = new string('2', 64),
        Pages =
        [
            new DrawingPlanPage { PageId = "PAGE-A", Archetype = "FieldDevices", Order = 0, OrderState = DrawingPlanControlState.Auto, Bounds = new DrawingBounds(0,0,1000,700), GroupIds = ["G-A"] },
            new DrawingPlanPage { PageId = "PAGE-B", Archetype = "FieldDevices", Order = 1, OrderState = DrawingPlanControlState.Auto, Bounds = new DrawingBounds(0,0,1000,700), GroupIds = ["G-B"] }
        ],
        Groups =
        [
            new DrawingPlanGroup { GroupId = "G-A", PageId = "PAGE-A", State = DrawingPlanControlState.Auto, Bounds = new DrawingBounds(0,0,400,300), RepresentationIds = ["REP-A"] },
            new DrawingPlanGroup { GroupId = "G-B", PageId = "PAGE-B", State = DrawingPlanControlState.Auto, Bounds = new DrawingBounds(0,0,400,300), RepresentationIds = ["REP-B"] }
        ],
        Placements =
        [
            new DrawingPlacement { RepresentationId = "REP-A", PageId = "PAGE-A", GroupId = "G-A", State = DrawingPlanControlState.Auto, X=10,Y=10,Width=100,Height=50,RotationDegrees=0,AllowedRotations=[0] },
            new DrawingPlacement { RepresentationId = "REP-B", PageId = "PAGE-B", GroupId = "G-B", State = DrawingPlanControlState.Auto, X=10,Y=10,Width=100,Height=50,RotationDegrees=0,AllowedRotations=[0] }
        ]
    });

    private sealed class FakePlanner(DrawingPlanDocument plan) : IDrawingPlannerClient
    {
        public Task<DrawingPlanDocument> GenerateAsync(DrawingPlanningInput input, DrawingPlanDocument? priorPlan, CancellationToken cancellationToken) => Task.FromResult(plan with { SourcePlanningInputHash = input.PlanningInputHash! });
    }
    private sealed class FakeIr(DrawingIrDocument document) : IDrawingIrClient
    {
        public Task<DrawingIrDocument> CompileAsync(DrawingPlanningInput input, DrawingPlanDocument plan, CancellationToken cancellationToken) => Task.FromResult(document with { SourcePlanningInputHash = input.PlanningInputHash!, SourcePagePlanHash = plan.SourcePagePlanHash, SourceDrawingPlanHash = plan.DrawingPlanHash! });
    }
    private sealed class FakeExecutor(DrawingExecutorResult result) : IDrawingExecutorClient
    {
        public int CallCount { get; private set; }
        public Task<DrawingExecutorResult> ExecuteAsync(DrawingIrDocument drawingIr, CancellationToken cancellationToken) { CallCount++; return Task.FromResult(result); }
    }
    private sealed class FakeCheckpoints : IProjectRevisionCheckpointSink
    {
        public List<ProjectRevisionTrigger> Triggers { get; } = [];
        public Task CheckpointAsync(ElectricalProject project, ProjectRevisionTrigger trigger, CancellationToken cancellationToken) { Triggers.Add(trigger); return Task.CompletedTask; }
    }
}
