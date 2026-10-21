using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicContinuationArrowTests
{
    [Theory]
    [InlineData(20, 20, 20, 40, 20, 40)]
    [InlineData(20, 40, 20, 20, 20, 20)]
    [InlineData(20, 20, 40, 20, 40, 20)]
    [InlineData(40, 20, 20, 20, 20, 20)]
    public void ArrowPointsAlongTheIncidentRoute(double fromX, double fromY,
        double markerX, double markerY, double tipX, double tipY)
    {
        var marker = new SchematicMarker { MarkerId = "M", PageId = "P", Position = new(markerX, markerY) };
        var doc = new SchematicDocument
        {
            Wires = [new() { WireId = "W", PageId = "P", Start = SchematicAttachment.Free(),
                End = SchematicAttachment.Marker("M"), Points = [new(fromX, fromY), marker.Position] }]
        };
        var triangle = SchematicContinuationArrow.Points(doc, marker);
        Assert.Equal(new SchematicPoint(tipX, tipY), triangle[0]);
        var center = new SchematicPoint((triangle[1].X + triangle[2].X) / 2,
            (triangle[1].Y + triangle[2].Y) / 2);
        Assert.True((triangle[0].X - center.X) * (markerX - fromX) +
            (triangle[0].Y - center.Y) * (markerY - fromY) > 0);
    }
}
