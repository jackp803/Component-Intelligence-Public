using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingIoPortLabelTests
{
    [Fact]
    public void Project_UsesExplicitPortNameAtSavedRouteAnchor()
    {
        var input = Input();
        var plan = Plan();

        var label = Assert.Single(DrawingIoPortLabels.Build(input, plan));

        Assert.Equal("X01", label.Text);
        Assert.Equal("PORT-OPAQUE", label.EngineeringEndpointId);
        Assert.Equal("PAGE-20-001", label.PageId);
        Assert.Equal(203, label.X);
        Assert.Equal(103, label.Y);
    }

    [Fact]
    public void Project_RejectsLabelsFromStalePlanningInput()
    {
        Assert.Empty(DrawingIoPortLabels.Build(Input() with { PlanningInputHash = new string('B', 64) }, Plan()));
    }

    private static DrawingPlanningInput Input() => new()
    {
        ProjectId = "P", PlanningInputHash = new string('A', 64),
        Representations = [new DrawingRepresentationDecision
        {
            RepresentationId = "REP:IO", OwnerId = "IO", OwnerKind = DrawingRepresentationOwnerKind.Component,
            Role = DrawingRepresentationRole.Schematic, Family = DrawingRepresentationFamily.FunctionalGeneric,
            PhysicalModuleId = "IO", PortBindings = [new DrawingPortBinding
            { EngineeringEndpointId = "PORT-OPAQUE", ConnectionPointId = "PORT:PORT-OPAQUE", DisplayLabel = "X01" }]
        }]
    };

    private static DrawingPlanDocument Plan() => new()
    {
        ProjectId = "P", SourcePlanningInputHash = new string('A', 64), SourcePagePlanHash = new string('B', 64),
        Pages = [new DrawingPlanPage { PageId = "PAGE-20-001", Archetype = "PlcIo", Bounds = new DrawingBounds(0, 0, 1000, 700) }],
        Placements = [new DrawingPlacement { RepresentationId = "REP:IO", PageId = "PAGE-20-001", GroupId = "G", X = 100, Y = 100, Width = 700, Height = 80 }],
        Routes = [new DrawingRoute { RouteId = "ROUTE:C", PageId = "PAGE-20-001", ConnectionId = "C", EndpointAId = "PORT-OPAQUE",
            EndpointBId = "DEVICE", Points = [new DrawingPoint(200, 100), new DrawingPoint(200, 50)] }]
    };
}
