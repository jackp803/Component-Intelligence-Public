using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicCableLabel(string Text, SchematicPoint Center, int Rotation, double AvailableLength);

public static class SchematicCableLabelPresentation
{
    public static SchematicCableLabel? Resolve(ElectricalProject project, SchematicWire wire, SchematicCrossingResult? crossings = null)
    {
        if (wire.ConnectionId is null) return null;
        var connection = project.Connections.SingleOrDefault(c => c.ConnectionId == wire.ConnectionId);
        if (connection?.Kind != ConnectionKind.Cable || connection.CableInstanceId is null) return null;
        var cable = project.Cables.SingleOrDefault(c => c.CableInstanceId == connection.CableInstanceId);
        if (cable is null) return null;
        var models = project.BomItems.Where(b => b.ConnectionMaterial && b.ComponentDefinitionId == cable.CableDefinitionId)
            .Select(b => b.Row.ModelOrPartNumber?.Trim()).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.Ordinal).ToArray();
        var text = !string.IsNullOrWhiteSpace(cable.DisplayName) ? cable.DisplayName : models.Length == 1 ? models[0] : null;
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Derive the caption from the saved route; never add bends or change electrical endpoints.
        crossings ??= SchematicCrossingService.Analyze(project.Schematic?.Wires.Where(w => w.PageId == wire.PageId).ToArray() ?? [wire]);
        var obstacles = crossings.Junctions.Concat(crossings.Crossovers.Select(c => c.Position))
            .Concat(crossings.Conflicts.Select(c => c.Position)).ToArray();
        var span = ClearSpans(wire, obstacles).OrderByDescending(s => s.Length).FirstOrDefault();
        return span.Length > 4 ? new(text.Trim(), span.Center, span.Rotation, span.Length - 4) : null;
    }

    private static IEnumerable<(SchematicPoint Center, int Rotation, double Length)> ClearSpans(SchematicWire wire,
        IReadOnlyList<SchematicPoint> obstacles)
    {
        foreach (var (a, b) in wire.Points.Zip(wire.Points.Skip(1)))
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
