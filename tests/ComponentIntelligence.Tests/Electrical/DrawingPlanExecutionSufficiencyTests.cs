using System.Text.Json;
using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingPlanExecutionSufficiencyTests
{
    [Fact]
    public void DrawingPlanJson_RoundTripsPageLocalRouteAndContinuationReferences()
    {
        var plan = DrawingPlanJson.Rehash(new DrawingPlanDocument
        {
            ProjectId = "P1",
            SourcePlanningInputHash = new string('A', 64),
            SourcePagePlanHash = new string('B', 64),
            Pages =
            [
                new DrawingPlanPage { PageId = "PAGE-A", Archetype = "PlcIo", Order = 0, Bounds = new DrawingBounds(0, 0, 1000, 700) },
                new DrawingPlanPage { PageId = "PAGE-B", Archetype = "FieldDevices", Order = 1, Bounds = new DrawingBounds(0, 0, 1000, 700) }
            ],
            Routes =
            [
                new DrawingRoute { RouteId = "ROUTE:C1:SOURCE", PageId = "PAGE-A", ConnectionId = "C1", EndpointAId = "EA", EndpointBId = "EB", Points = [new DrawingPoint(100, 100), new DrawingPoint(980, 100)] },
                new DrawingRoute { RouteId = "ROUTE:C1:DESTINATION", PageId = "PAGE-B", ConnectionId = "C1", EndpointAId = "EA", EndpointBId = "EB", Points = [new DrawingPoint(100, 200), new DrawingPoint(20, 200)] }
            ],
            CrossPageRelations =
            [
                new DrawingCrossPageRelation
                {
                    RelationId = "REL:CONNECTION:C1",
                    SourceRepresentationId = "REP-A",
                    DestinationRepresentationId = "REP-B",
                    RelationKind = "ElectricalConnectionContinuation",
                    EngineeringId = "C1",
                    SourcePageId = "PAGE-A",
                    DestinationPageId = "PAGE-B",
                    SourceRouteId = "ROUTE:C1:SOURCE",
                    DestinationRouteId = "ROUTE:C1:DESTINATION"
                }
            ]
        });

        var json = DrawingPlanJson.Serialize(plan);
        var roundTrip = DrawingPlanJson.Deserialize(json);
        Assert.Equal("PAGE-A", roundTrip.Routes[0].PageId);
        var relation = Assert.Single(roundTrip.CrossPageRelations);
        Assert.Equal("PAGE-A", relation.SourcePageId);
        Assert.Equal("PAGE-B", relation.DestinationPageId);
        Assert.Equal("ROUTE:C1:SOURCE", relation.SourceRouteId);
        Assert.Equal("ROUTE:C1:DESTINATION", relation.DestinationRouteId);

        using var document = JsonDocument.Parse(json);
        Assert.Equal("PAGE-A", document.RootElement.GetProperty("routes")[0].GetProperty("pageId").GetString());
    }

    [Fact]
    public void DrawingPlanJson_RejectsRouteWhosePageDoesNotExist()
    {
        var plan = new DrawingPlanDocument
        {
            ProjectId = "P1",
            SourcePlanningInputHash = new string('A', 64),
            SourcePagePlanHash = new string('B', 64),
            Pages = [new DrawingPlanPage { PageId = "PAGE-A", Archetype = "PlcIo", Order = 0, Bounds = new DrawingBounds(0, 0, 1000, 700) }],
            Routes = [new DrawingRoute { RouteId = "ROUTE:C1", PageId = "PAGE-MISSING", ConnectionId = "C1", EndpointAId = "EA", EndpointBId = "EB", Points = [new DrawingPoint(0, 0), new DrawingPoint(10, 0)] }]
        };

        Assert.Throws<InvalidDataException>(() => DrawingPlanJson.Serialize(plan));
    }
}
