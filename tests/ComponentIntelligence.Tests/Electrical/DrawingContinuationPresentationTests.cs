using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingContinuationPresentationTests
{
    [Fact]
    public void DenseContinuationsAvoidEachOtherAndDevicePlacements()
    {
        var page = new DrawingPlanPage { PageId = "P", Archetype = "MainPower", Bounds = new(0, 0, 1000, 700) };
        var plan = new DrawingPlanDocument
        {
            ProjectId = "P", SourcePlanningInputHash = new string('A', 64), SourcePagePlanHash = new string('B', 64),
            Pages = [page],
            Placements =
            [
                new() { RepresentationId = "A", PageId = "P", GroupId = "G", X = 115, Y = 64, Width = 180, Height = 80 },
                new() { RepresentationId = "B", PageId = "P", GroupId = "G", X = 380, Y = 64, Width = 180, Height = 80 },
                new() { RepresentationId = "C", PageId = "P", GroupId = "G", X = 640, Y = 160, Width = 180, Height = 80 }
            ]
        };
        var labels = Enumerable.Range(0, 16).Select(i => new DrawingContinuationLabel(
            $"REL:{i}", "P", "PEER", "未命名訊號 → P.17 / PHOENIX CONTACT PT 2,5-QUATTRO (3209578) / OUTPUT / 4 OUT2",
            new DrawingPoint(i < 5 ? 335 : i < 11 ? 560 : 820, 90 + i * 15), new DrawingPoint(400, 200))).ToArray();

        var placed = DrawingContinuationLayout.Place(plan, labels, "P");

        Assert.Equal(labels.Length, placed.Count);
        Assert.All(placed, item => Assert.True(item.FitsWithoutOverlap));
        foreach (var item in placed)
        {
            Assert.InRange(item.Box.X, 0, page.Bounds.Width - item.Box.Width);
            Assert.InRange(item.Box.Y, 0, page.Bounds.Height - item.Box.Height);
            Assert.DoesNotContain(plan.Placements, p => Overlaps(item.Box, new DrawingBounds(p.X, p.Y, p.Width, p.Height)));
        }
        for (var i = 0; i < placed.Count; i++)
            for (var j = i + 1; j < placed.Count; j++)
                Assert.False(Overlaps(placed[i].Box, placed[j].Box));
    }

    private static bool Overlaps(DrawingBounds a, DrawingBounds b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    [Fact]
    public void UsesExactEndpointsAndRecomputesBothSidesAfterPageOrderAndMarkerMove()
    {
        var input = DrawingPlanningJson.Deserialize(DrawingPlanningJson.Serialize(new DrawingPlanningInput
        {
            ProjectId = "P",
            Representations =
            [
                new() { RepresentationId = "RA", OwnerId = "A", PortBindings = [new() { EngineeringEndpointId = "A:PIN", ConnectionPointId = "PIN:A", DisplayLabel = "X31 / 1 +24 VDC" }] },
                new() { RepresentationId = "RB", OwnerId = "B", PortBindings = [new() { EngineeringEndpointId = "B:PIN", ConnectionPointId = "PIN:B", DisplayLabel = "WIRE-B / 1" }] }
            ],
            Connections = [new() { ConnectionId = "C", FromEndpointId = "A:PIN", ToEndpointId = "B:PIN" }]
        }));
        var plan = new DrawingPlanDocument
        {
            ProjectId = "P", SourcePlanningInputHash = input.PlanningInputHash!, SourcePagePlanHash = new string('A', 64),
            Pages = [new() { PageId = "PA", Archetype = "PlcIo", Order = 0, Bounds = new(0, 0, 1000, 700) }, new() { PageId = "PB", Archetype = "FieldDevices", Order = 1, Bounds = new(0, 0, 1000, 700) }],
            Routes = [new() { RouteId = "RS", PageId = "PA", ConnectionId = "C", EndpointAId = "A:PIN", EndpointBId = "B:PIN", Points = [new(800, 100), new(850, 100)] },
                new() { RouteId = "RD", PageId = "PB", ConnectionId = "C", EndpointAId = "A:PIN", EndpointBId = "B:PIN", Points = [new(560, 210), new(600, 210)] }],
            CrossPageRelations = [new() { RelationId = "REL:C", RelationKind = "ElectricalConnectionContinuation", EngineeringId = "C", SourceRepresentationId = "RA", DestinationRepresentationId = "RB", SourcePageId = "PA", DestinationPageId = "PB", SourceRouteId = "RS", DestinationRouteId = "RD" }]
        };
        var labels = new Dictionary<string, string> { ["RA"] = "AL1342", ["RB"] = "M12 adapter" };
        var pair = DrawingContinuationPresentation.Build(input, plan, labels);
        Assert.Equal(2, pair.Count);
        Assert.Contains("未命名訊號", pair[0].Text);
        Assert.Contains("P.2", pair[0].Text);
        Assert.Contains("WIRE-B / 1", pair[0].Text);
        Assert.Contains("M12 adapter", pair[0].Text);
        Assert.Contains("X31 / 1 +24 VDC", pair[1].Text);
        Assert.Equal("PB", pair[0].PeerPageId);
        Assert.Equal(new DrawingPoint(560, 210), pair[0].PeerAnchor);

        var changed = plan with { Pages = [plan.Pages[0] with { Order = 1 }, plan.Pages[1] with { Order = 0 }],
            Routes = [plan.Routes[0], plan.Routes[1] with { Points = [new(520, 240), new(600, 240)] }] };
        var updated = DrawingContinuationPresentation.Build(input, changed, labels);
        Assert.Contains("P.1", updated[0].Text);
        Assert.Equal(new DrawingPoint(520, 240), updated[0].PeerAnchor);
    }
}
