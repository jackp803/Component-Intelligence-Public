namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicCrossover(string HorizontalWireId, SchematicPoint Position);
public sealed record SchematicRouteConflict(string Code, string FirstWireId, string SecondWireId, SchematicPoint Position);
public sealed record SchematicCrossingResult(IReadOnlyList<SchematicCrossover> Crossovers, IReadOnlyList<SchematicPoint> Junctions, IReadOnlyList<SchematicRouteConflict> Conflicts);

public static class SchematicCrossingService
{
    public static IReadOnlyList<SchematicCadPrimitive> RoutePrimitives(SchematicWire wire, SchematicCrossingResult result)
    {
        var output = new List<SchematicCadPrimitive>();
        foreach (var (a, b) in wire.Points.Zip(wire.Points.Skip(1)))
        {
            var direction = Math.Sign(b.X - a.X);
            var crossings = a.Y == b.Y ? result.Crossovers.Where(c => c.HorizontalWireId == wire.WireId && c.Position.Y == a.Y &&
                c.Position.X > Math.Min(a.X, b.X) && c.Position.X < Math.Max(a.X, b.X)).OrderBy(c => direction * c.Position.X).ToArray() : [];
            var cursor = a;
            foreach (var crossing in crossings)
            {
                var before = new SchematicPoint(crossing.Position.X - direction * 1.5, a.Y);
                var after = new SchematicPoint(crossing.Position.X + direction * 1.5, a.Y);
                if (cursor != before) output.Add(new() { Kind = "LINE", Start = cursor, End = before });
                output.Add(new() { Kind = "ARC", Start = crossing.Position, Radius = 1.5, StartAngle = 0, EndAngle = 180 });
                cursor = after;
            }
            if (cursor != b) output.Add(new() { Kind = "LINE", Start = cursor, End = b });
        }
        return output;
    }

    public static SchematicCrossingResult Analyze(IReadOnlyList<SchematicWire> wires)
    {
        var crossings = new HashSet<SchematicCrossover>(); var junctions = new HashSet<SchematicPoint>(); var conflicts = new HashSet<SchematicRouteConflict>();
        for (var i = 0; i < wires.Count; i++)
        for (var j = i + 1; j < wires.Count; j++)
        {
            var first = wires[i]; var second = wires[j]; if (first.PageId != second.PageId) continue;
            foreach (var a in first.Points.Zip(first.Points.Skip(1)))
            foreach (var b in second.Points.Zip(second.Points.Skip(1)))
            {
                var ah = a.First.Y == a.Second.Y; var bh = b.First.Y == b.Second.Y;
                if (ah == bh)
                {
                    var sameLane = ah ? a.First.Y == b.First.Y : a.First.X == b.First.X;
                    var low = ah ? Math.Max(Math.Min(a.First.X, a.Second.X), Math.Min(b.First.X, b.Second.X)) : Math.Max(Math.Min(a.First.Y, a.Second.Y), Math.Min(b.First.Y, b.Second.Y));
                    var high = ah ? Math.Min(Math.Max(a.First.X, a.Second.X), Math.Max(b.First.X, b.Second.X)) : Math.Min(Math.Max(a.First.Y, a.Second.Y), Math.Max(b.First.Y, b.Second.Y));
                    if (sameLane && low < high) conflicts.Add(new("COLLINEAR_OVERLAP", first.WireId, second.WireId, ah ? new(low, a.First.Y) : new(a.First.X, low)));
                    continue;
                }
                var h = ah ? a : b; var v = ah ? b : a; var point = new SchematicPoint(v.First.X, h.First.Y);
                if (point.X < Math.Min(h.First.X, h.Second.X) || point.X > Math.Max(h.First.X, h.Second.X) ||
                    point.Y < Math.Min(v.First.Y, v.Second.Y) || point.Y > Math.Max(v.First.Y, v.Second.Y)) continue;
                var aEnd = AttachmentAt(first, point); var bEnd = AttachmentAt(second, point);
                if (aEnd?.Kind == SchematicAttachmentKind.Pin && aEnd == bEnd) { junctions.Add(point); continue; }
                if (point == a.First || point == a.Second || point == b.First || point == b.Second)
                    conflicts.Add(new("UNCONNECTED_CONTACT", first.WireId, second.WireId, point));
                else if (Math.Min(Math.Abs(point.X - h.First.X), Math.Abs(point.X - h.Second.X)) < 2)
                    conflicts.Add(new("CROSSOVER_CLEARANCE", first.WireId, second.WireId, point));
                else crossings.Add(new(ah ? first.WireId : second.WireId, point));
            }
        }
        return new(crossings.OrderBy(c => c.HorizontalWireId, StringComparer.Ordinal).ThenBy(c => c.Position.X).ThenBy(c => c.Position.Y).ToArray(),
            junctions.OrderBy(p => p.X).ThenBy(p => p.Y).ToArray(), conflicts.OrderBy(c => c.FirstWireId, StringComparer.Ordinal).ThenBy(c => c.SecondWireId, StringComparer.Ordinal).ThenBy(c => c.Code, StringComparer.Ordinal).ToArray());
    }

    private static SchematicAttachment? AttachmentAt(SchematicWire wire, SchematicPoint point) =>
        wire.Points[0] == point ? wire.Start : wire.Points[^1] == point ? wire.End : null;
}
