namespace ComponentIntelligence.Electrical.Schematic;

public static class SchematicContinuationArrow
{
    public static IReadOnlyList<SchematicPoint> Points(SchematicDocument document, SchematicMarker marker)
    {
        var route = document.Wires.SingleOrDefault(w => w.Start.MarkerId == marker.MarkerId || w.End.MarkerId == marker.MarkerId);
        var adjacent = route is null || route.Points.Count < 2 ? null :
            route.Start.MarkerId == marker.MarkerId ? route.Points[1] : route.Points[^2];
        var dx = adjacent is null ? 1 : Math.Sign(marker.Position.X - adjacent.X);
        var dy = adjacent is null ? 0 : Math.Sign(marker.Position.Y - adjacent.Y);
        if (dx != 0 && dy != 0 || dx == 0 && dy == 0) { dx = 1; dy = 0; }
        var tip = marker.Position;
        var baseCenter = new SchematicPoint(tip.X - dx * 3, tip.Y - dy * 3);
        return [tip, new(baseCenter.X - dy * 5d / 3, baseCenter.Y + dx * 5d / 3),
            new(baseCenter.X + dy * 5d / 3, baseCenter.Y - dx * 5d / 3)];
    }
}
