namespace ComponentIntelligence.Electrical.Schematic;

public static class SchematicDraftRoute
{
    public static List<SchematicPoint> Append(IReadOnlyList<SchematicPoint> source, SchematicPoint end)
    {
        var points = source.ToList();
        if (points.Count == 0) { points.Add(end); return points; }
        var last = points[^1];
        if (last == end) return points;
        if (last.X != end.X && last.Y != end.Y) AddTail(new(end.X, last.Y));
        AddTail(end);
        return points;

        void AddTail(SchematicPoint point)
        {
            points.Add(point);
            while (points.Count >= 2)
            {
                if (points[^1] == points[^2]) { points.RemoveAt(points.Count - 2); continue; }
                if (points.Count < 3) break;
                var a = points[^3]; var b = points[^2]; var c = points[^1];
                if (a.X == b.X && b.X == c.X || a.Y == b.Y && b.Y == c.Y)
                    points.RemoveAt(points.Count - 2);
                else break;
            }
        }
    }
}
