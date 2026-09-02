using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

/// <summary>
/// Detects terminal components that visually form one contiguous terminal strip. Grouping is a
/// presentation concern only: every component, endpoint and connection remains independent.
/// </summary>
public sealed class TopologyTerminalGroupingPolicy
{
    public IReadOnlyList<TopologyTerminalVisualGroup> BuildGroups(
        ElectricalProject project,
        double adjacencyTolerance = 18d)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (adjacencyTolerance < 0d) throw new ArgumentOutOfRangeException(nameof(adjacencyTolerance));

        var placements = project.TopologyPlacements
            .GroupBy(item => item.ObjectId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var candidates = project.Components
            .Where(component =>
                TopologyPaletteMaterialPolicy.Classify(component.TypeKey) ==
                TopologyPaletteMaterialKind.TerminalBlock)
            .Where(component => placements.ContainsKey(component.ComponentInstanceId))
            .Select(component => new Candidate(
                component.ComponentInstanceId,
                NormalizeRotation(placements[component.ComponentInstanceId].RotationDegrees),
                ScreenBounds(placements[component.ComponentInstanceId])))
            .OrderBy(candidate => candidate.Bounds.Y)
            .ThenBy(candidate => candidate.Bounds.X)
            .ThenBy(candidate => candidate.ComponentInstanceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = new List<TopologyTerminalVisualGroup>();
        foreach (var seed in candidates)
        {
            if (!visited.Add(seed.ComponentInstanceId)) continue;

            var queue = new Queue<Candidate>();
            var members = new List<Candidate>();
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                members.Add(current);
                foreach (var candidate in candidates)
                {
                    if (visited.Contains(candidate.ComponentInstanceId) ||
                        current.RotationDegrees != candidate.RotationDegrees ||
                        !AreAdjacent(current.Bounds, candidate.Bounds, adjacencyTolerance))
                        continue;

                    visited.Add(candidate.ComponentInstanceId);
                    queue.Enqueue(candidate);
                }
            }

            if (members.Count < 2) continue;
            var minX = members.Min(member => member.Bounds.X);
            var minY = members.Min(member => member.Bounds.Y);
            var maxRight = members.Max(member => member.Bounds.Right);
            var maxBottom = members.Max(member => member.Bounds.Bottom);
            groups.Add(new TopologyTerminalVisualGroup(
                members
                    .OrderBy(member => member.Bounds.Y)
                    .ThenBy(member => member.Bounds.X)
                    .Select(member => member.ComponentInstanceId)
                    .ToArray(),
                new TopologyTerminalGroupBounds(minX, minY, maxRight - minX, maxBottom - minY)));
        }

        return groups;
    }

    private static bool AreAdjacent(
        TopologyTerminalGroupBounds first,
        TopologyTerminalGroupBounds second,
        double tolerance)
    {
        var horizontalGap = AxisGap(first.X, first.Right, second.X, second.Right);
        var verticalGap = AxisGap(first.Y, first.Bottom, second.Y, second.Bottom);
        var horizontalOverlap = AxisOverlap(first.X, first.Right, second.X, second.Right);
        var verticalOverlap = AxisOverlap(first.Y, first.Bottom, second.Y, second.Bottom);

        var beside = horizontalGap <= tolerance &&
                     verticalOverlap >= Math.Min(first.Height, second.Height) * 0.45d;
        var stacked = verticalGap <= tolerance &&
                      horizontalOverlap >= Math.Min(first.Width, second.Width) * 0.45d;
        return beside || stacked;
    }

    private static double AxisGap(double firstStart, double firstEnd, double secondStart, double secondEnd)
    {
        if (firstEnd < secondStart) return secondStart - firstEnd;
        if (secondEnd < firstStart) return firstStart - secondEnd;
        return 0d;
    }

    private static double AxisOverlap(double firstStart, double firstEnd, double secondStart, double secondEnd) =>
        Math.Max(0d, Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart));

    private static TopologyTerminalGroupBounds ScreenBounds(TopologyPlacement placement)
    {
        var rotation = NormalizeRotation(placement.RotationDegrees);
        if (rotation is not (90 or 270))
            return new TopologyTerminalGroupBounds(placement.X, placement.Y, placement.Width, placement.Height);

        var centerX = placement.X + placement.Width / 2d;
        var centerY = placement.Y + placement.Height / 2d;
        return new TopologyTerminalGroupBounds(
            centerX - placement.Height / 2d,
            centerY - placement.Width / 2d,
            placement.Height,
            placement.Width);
    }

    private static int NormalizeRotation(int rotationDegrees)
    {
        var normalized = rotationDegrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private sealed record Candidate(
        string ComponentInstanceId,
        int RotationDegrees,
        TopologyTerminalGroupBounds Bounds);
}

public sealed record TopologyTerminalVisualGroup(
    IReadOnlyList<string> ComponentInstanceIds,
    TopologyTerminalGroupBounds Bounds);

public sealed record TopologyTerminalGroupBounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}
