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
    public async Task Coordinator_ReturnsReadyForCp3cWithoutExecutorAndNeverClaimsDwg()
    {
        var input = Input([]);
        var plan = Plan();
        var planner = new FakePlanner(plan);
        var ir = new FakeIr(new DrawingIrDocument("READY", new string('A', 64), input.PlanningInputHash!, plan.SourcePagePlanHash, plan.DrawingPlanHash!, [], "{}"));
        var checkpoints = new FakeCheckpoints();
        var coordinator = new DrawingGenerationCoordinator(_ => input, planner, ir, new DrawingPreflightService(), checkpoints, executor: null);
        var project = new ElectricalProject { ProjectId = input.ProjectId };
        var result = await coordinator.GenerateAutoCadAsync(project, DrawingRuntimeValidation.Valid(), CancellationToken.None);
        Assert.Equal(DrawingGenerationStatus.ReadyForCp3C, result.Status);
        Assert.NotNull(result.DrawingIr);
        Assert.Equal(ProjectRevisionTrigger.GenerateAutoCad, Assert.Single(checkpoints.Triggers));
        Assert.False(result.DwgOrWdpGenerated);
    }

    private static DrawingPlanningInput Input(IReadOnlyList<DrawingPlanningIssue> issues)
    {
        var input = new DrawingPlanningInput
        {
            ProjectId = "P1",
            Representations =
            [
                Rep("REP-A", "OWNER-A", "EA"),
                Rep("REP-B", "OWNER-B", "EB")
            ],
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
    private sealed class FakeCheckpoints : IProjectRevisionCheckpointSink
    {
        public List<ProjectRevisionTrigger> Triggers { get; } = [];
        public Task CheckpointAsync(ElectricalProject project, ProjectRevisionTrigger trigger, CancellationToken cancellationToken) { Triggers.Add(trigger); return Task.CompletedTask; }
    }
}
