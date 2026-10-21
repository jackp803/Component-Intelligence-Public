using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingTerminalContactPresentationTests
{
    [Fact]
    public void Build_UsesRouteAnchorsForUsedHolesAndKeepsBridgeAsPotentialOnly()
    {
        var input = new DrawingPlanningInput { ProjectId = "P", Representations = [new DrawingRepresentationDecision
        {
            RepresentationId = "T", OwnerId = "T1", SourceType = "ProjectTerminalContact",
            PortBindings = [
                new DrawingPortBinding { EngineeringEndpointId = "H1", ConnectionPointId = "TERMINAL_CONTACT:H1" },
                new DrawingPortBinding { EngineeringEndpointId = "H2", ConnectionPointId = "TERMINAL_CONTACT:H2" },
                new DrawingPortBinding { EngineeringEndpointId = "H3", ConnectionPointId = "TERMINAL_CONTACT:H3" },
                new DrawingPortBinding { EngineeringEndpointId = "H4", ConnectionPointId = "TERMINAL_CONTACT:H4" },
                new DrawingPortBinding { EngineeringEndpointId = "B", ConnectionPointId = "PORT:B" }
            ]
        }] };
        var plan = new DrawingPlanDocument { ProjectId = "P", SourcePlanningInputHash = "I", SourcePagePlanHash = "G",
            Placements = [new DrawingPlacement { RepresentationId = "T", PageId = "PAGE", GroupId = "G", X = 100, Y = 100, Width = 80, Height = 60 }],
            Routes = [
                new DrawingRoute { RouteId = "R1", PageId = "PAGE", ConnectionId = "C1", EndpointAId = "H1", EndpointBId = "OTHER1", Points = [new DrawingPoint(100, 115), new DrawingPoint(80, 115)] },
                new DrawingRoute { RouteId = "R2", PageId = "PAGE", ConnectionId = "C2", EndpointAId = "OTHER2", EndpointBId = "H3", Points = [new DrawingPoint(200, 145), new DrawingPoint(180, 145)] },
                new DrawingRoute { RouteId = "R3", PageId = "PAGE", ConnectionId = "C3", EndpointAId = "B", EndpointBId = "OTHER3", Points = [new DrawingPoint(140, 100), new DrawingPoint(140, 80)] },
                new DrawingRoute { RouteId = "R4:DESTINATION", PageId = "PAGE", ConnectionId = "C4", EndpointAId = "OTHER4", EndpointBId = "H2", Points = [new DrawingPoint(100, 130), new DrawingPoint(80, 130)] }
            ] };

        var result = Assert.Single(DrawingTerminalContactPresentation.Build(input, plan));
        Assert.Equal(3, result.Contacts.Count);
        Assert.Contains(result.Contacts, p => p.EngineeringEndpointId == "H1" && p.Point == new DrawingPoint(100, 115));
        Assert.Contains(result.Contacts, p => p.EngineeringEndpointId == "H3" && p.Point == new DrawingPoint(180, 145));
        Assert.Contains(result.Contacts, p => p.EngineeringEndpointId == "H2" && p.Point == new DrawingPoint(100, 130));
        Assert.Equal(4, result.PotentialAnchors.Count);
        Assert.DoesNotContain(result.Contacts, p => p.EngineeringEndpointId == "B");
    }
}
