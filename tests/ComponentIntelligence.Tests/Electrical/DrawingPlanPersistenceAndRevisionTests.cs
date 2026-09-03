using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Drawing;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingPlanPersistenceAndRevisionTests
{
    [Fact]
    public void NewProjectAndV04Migration_UseV05WithoutInventingDrawingPlan()
    {
        Assert.Equal("0.5", new ElectricalProject { ProjectId = "NEW" }.SchemaVersion);
        var legacy = new ElectricalProject { SchemaVersion = "0.4", ProjectId = "P1", Components = [new ComponentInstance { ComponentInstanceId = "C1", ComponentDefinitionId = "D1", TypeKey = "DO_NOT_INFER" }] };
        var migrated = ElectricalProjectMigrator.Migrate(legacy);
        Assert.Equal("0.5", migrated.SchemaVersion);
        Assert.Null(migrated.DrawingPlan);
        Assert.Equal("C1", Assert.Single(migrated.Components).ComponentInstanceId);
    }

    [Fact]
    public void PresentationEdit_PreservesRouteEngineeringIdentity()
    {
        var plan = DrawingPlanJson.Rehash(new DrawingPlanDocument
        {
            ProjectId = "P1", SourcePlanningInputHash = new string('1', 64), SourcePagePlanHash = new string('2', 64),
            Pages = [new DrawingPlanPage { PageId="PAGE-1", Archetype="FieldDevices", Order=0, OrderState=DrawingPlanControlState.Auto, Bounds=new DrawingBounds(0,0,1000,700), GroupIds=["G1"] }],
            Groups = [new DrawingPlanGroup { GroupId="G1", PageId="PAGE-1", State=DrawingPlanControlState.Auto, Bounds=new DrawingBounds(0,0,900,600), RepresentationIds=["REP-A","REP-B"] }],
            Placements = [new DrawingPlacement { RepresentationId="REP-A",PageId="PAGE-1",GroupId="G1",State=DrawingPlanControlState.Auto,X=10,Y=10,Width=100,Height=50,RotationDegrees=0,AllowedRotations=[0,90] }],
            Routes = [new DrawingRoute { RouteId="R1",ConnectionId="CONN-1",EndpointAId="EA",EndpointBId="EB",State=DrawingPlanControlState.Auto,Points=[new DrawingPoint(0,0),new DrawingPoint(100,0)] }]
        });
        var changed = new DrawingPlanEditService().MoveRouteSegment(plan, "R1", 0, 10);
        var route = Assert.Single(changed.Routes);
        Assert.Equal(("CONN-1","EA","EB"), (route.ConnectionId, route.EndpointAId, route.EndpointBId));
        Assert.Equal(DrawingPlanControlState.Manual, route.State);
    }

    [Fact]
    public void ChangeSummary_SeparatesEngineeringStructureAndVisualCategories()
    {
        var before = new ElectricalProject { ProjectId = "P1" };
        var after = new ElectricalProject { ProjectId = "P1", Components = [new ComponentInstance { ComponentInstanceId="C1",ComponentDefinitionId="D1",TypeKey="X" }] };
        var summary = ProjectChangeSummary.Compare(before, after);
        Assert.NotEmpty(summary.EngineeringChanges);
        Assert.NotNull(summary.DrawingStructureChanges);
        Assert.NotNull(summary.VisualChanges);
    }
}
