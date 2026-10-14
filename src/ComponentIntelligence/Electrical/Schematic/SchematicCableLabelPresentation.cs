using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicLabelSpan(SchematicPoint Center, int Rotation, double AvailableLength);

public sealed record SchematicCableLabel(string Text, SchematicPoint Center, int Rotation, double AvailableLength)
{
    public string FullText { get; init; } = Text;
    public bool IsOrdinaryWire { get; init; }
    public IReadOnlyList<SchematicLabelSpan> Spans { get; init; } = [];
}

public static class SchematicCableLabelPresentation
{
    public static SchematicCableLabel? Resolve(ElectricalProject project, SchematicWire wire, SchematicCrossingResult? crossings = null,
        SchematicWireLegendEntry? preparedDetail = null)
    {
        var connection = project.Connections.SingleOrDefault(c => c.ConnectionId == wire.ConnectionId);
        if (connection?.Kind == ConnectionKind.DirectMating) return null;
        string? text;
        string? fullText = null;
        var ordinary = connection?.Kind != ConnectionKind.Cable || connection.CableInstanceId is null;
        if (ordinary)
        {
            if (string.IsNullOrWhiteSpace(wire.Designation)) return null;
            var detail = preparedDetail ?? SchematicWireLegendPresentation.Entry(project, wire);
            text = detail.Caption;
            fullText = detail.FullText;
        }
        else
        {
        var cable = project.Cables.SingleOrDefault(c => c.CableInstanceId == connection!.CableInstanceId);
        if (cable is null) return null;
        var models = project.BomItems.Where(b => b.ConnectionMaterial && b.ComponentDefinitionId == cable.CableDefinitionId)
            .Select(b => b.Row.ModelOrPartNumber?.Trim()).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.Ordinal).ToArray();
        text = !string.IsNullOrWhiteSpace(cable.DisplayName) ? cable.DisplayName : models.Length == 1 ? models[0] : null;
        }
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Derive the caption from the saved route; never add bends or change electrical endpoints.
        crossings ??= SchematicCrossingService.Analyze(project.Schematic?.Wires.Where(w => w.PageId == wire.PageId).ToArray() ?? [wire]);
        var obstacles = crossings.Junctions.Concat(crossings.Crossovers.Select(c => c.Position))
            .Concat(crossings.Conflicts.Select(c => c.Position)).ToArray();
        var spans = ClearSpans(wire, obstacles).Where(s => s.Length > 0).OrderByDescending(s => s.Length)
            .Select(s => new SchematicLabelSpan(s.Center, s.Rotation, Math.Max(0, s.Length - 4))).ToArray();
        return spans.Length > 0 ? new(text.Trim(), spans[0].Center, spans[0].Rotation, spans[0].AvailableLength)
            { FullText = fullText ?? text.Trim(), IsOrdinaryWire = ordinary, Spans = spans } : null;
    }

    private static IEnumerable<(SchematicPoint Center, int Rotation, double Length)> ClearSpans(SchematicWire wire,
        IReadOnlyList<SchematicPoint> obstacles)
    {
        var points = new List<SchematicPoint>();
        foreach (var point in wire.Points)
        {
            if (points.Count > 0 && points[^1] == point) continue;
            points.Add(point);
            while (points.Count >= 3)
            {
                var a = points[^3]; var b = points[^2]; var c = points[^1];
                if (a.X == b.X && b.X == c.X && (b.Y - a.Y) * (c.Y - b.Y) >= 0 ||
                    a.Y == b.Y && b.Y == c.Y && (b.X - a.X) * (c.X - b.X) >= 0) points.RemoveAt(points.Count - 2);
                else break;
            }
        }
        foreach (var (a, b) in points.Zip(points.Skip(1)))
        {
            if (a.X != b.X && a.Y != b.Y || a == b) continue;
            var vertical = a.X == b.X;
            var low = vertical ? Math.Min(a.Y, b.Y) : Math.Min(a.X, b.X);
            var high = vertical ? Math.Max(a.Y, b.Y) : Math.Max(a.X, b.X);
            var cuts = obstacles.Where(p => vertical ? p.X == a.X : p.Y == a.Y)
                .Select(p => vertical ? p.Y : p.X).Where(v => v > low && v < high)
                .Append(low).Append(high).Distinct().Order().ToArray();
            foreach (var (start, end) in cuts.Zip(cuts.Skip(1)))
                yield return (vertical ? new(a.X, (start + end) / 2) : new((start + end) / 2, a.Y),
                    vertical ? -90 : 0, end - start);
        }
    }
}
