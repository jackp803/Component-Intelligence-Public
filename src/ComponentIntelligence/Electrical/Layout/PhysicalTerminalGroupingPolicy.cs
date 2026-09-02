using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Electrical.Layout;

/// <summary>
/// Detects adjacent terminal parts that should be rendered as one physical terminal strip.
/// The result is presentation-only; component identity, placement, footprint and wiring remain
/// independent so BOM quantities and engineering validation are unaffected.
/// </summary>
public sealed class PhysicalTerminalGroupingPolicy
{
    /// <summary>
    /// Repairs an already-recognized terminal strip so every terminal touches the next one
    /// without occupying the same physical area. Component identity and engineering data are
    /// unchanged; only the existing physical placement coordinates are normalized.
    /// </summary>
    public bool ArrangeContiguously(
        ElectricalProject project,
        string containerId,
        MountingSurface surface,
        double adjacencyToleranceMm = 2d)
    {
        var groups = BuildGroups(project, containerId, surface, adjacencyToleranceMm);
        if (groups.Count == 0) return false;

        var candidates = Candidates(project, containerId, surface)
            .ToDictionary(candidate => candidate.Member);
        var changed = false;
        foreach (var group in groups)
        {
            var members = group.Members
                .Where(candidates.ContainsKey)
                .Select(member => candidates[member])
                .ToArray();
            if (members.Length < 2) continue;

            // A DIN terminal strip grows along the narrow projected dimension. Using the
            // previous centre spread after rotation could pack 72.2 x 5.2 mm terminals along
            // their 72.2 mm long edge, producing the giant thin rectangle seen in Layout.
            var arrangeVertically = members.Average(member => member.Bounds.Width) >
                                    members.Average(member => member.Bounds.Height);
            var ordered = (arrangeVertically
                    ? members.OrderBy(member => member.Bounds.Y).ThenBy(member => member.Bounds.X)
                    : members.OrderBy(member => member.Bounds.X).ThenBy(member => member.Bounds.Y))
                .ThenBy(member => member.Member.ObjectId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var anchorX = members.Min(member => member.Bounds.X);
            var anchorY = members.Min(member => member.Bounds.Y);
            var cursor = arrangeVertically ? anchorY : anchorX;
            foreach (var member in ordered)
            {
                var nextX = arrangeVertically ? anchorX : cursor;
                var nextY = arrangeVertically ? cursor : anchorY;
                if (Math.Abs(member.Placement.XMm - nextX) > 0.001d ||
                    Math.Abs(member.Placement.YMm - nextY) > 0.001d)
                {
                    member.Placement.XMm = nextX;
                    member.Placement.YMm = nextY;
                    changed = true;
                }
                cursor += arrangeVertically ? member.Bounds.Height : member.Bounds.Width;
            }
        }
        return changed;
    }

    public IReadOnlyList<PhysicalTerminalVisualGroup> BuildGroups(
        ElectricalProject project,
        string containerId,
        MountingSurface surface,
        double adjacencyToleranceMm = 2d)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        if (adjacencyToleranceMm < 0d)
            throw new ArgumentOutOfRangeException(nameof(adjacencyToleranceMm));

        var ordered = Candidates(project, containerId, surface)
            .OrderBy(candidate => candidate.Bounds.Y)
            .ThenBy(candidate => candidate.Bounds.X)
            .ThenBy(candidate => candidate.Member.ObjectId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var visited = new HashSet<PhysicalTerminalGroupMember>();
        var groups = new List<PhysicalTerminalVisualGroup>();
        foreach (var seed in ordered)
        {
            if (!visited.Add(seed.Member)) continue;

            var queue = new Queue<Candidate>();
            var members = new List<Candidate>();
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                members.Add(current);
                foreach (var candidate in ordered)
                {
                    if (visited.Contains(candidate.Member) ||
                        current.RotationDegrees != candidate.RotationDegrees ||
                        current.MountOrientation != candidate.MountOrientation ||
                        !string.Equals(current.CompatibilityKey, candidate.CompatibilityKey, StringComparison.OrdinalIgnoreCase) ||
                        !AreAdjacent(current.Bounds, candidate.Bounds, adjacencyToleranceMm))
                        continue;

                    visited.Add(candidate.Member);
                    queue.Enqueue(candidate);
                }
            }

            if (members.Count < 2) continue;
            var minX = members.Min(member => member.Bounds.X);
            var minY = members.Min(member => member.Bounds.Y);
            var maxRight = members.Max(member => member.Bounds.Right);
            var maxBottom = members.Max(member => member.Bounds.Bottom);
            groups.Add(new PhysicalTerminalVisualGroup(
                members
                    .OrderBy(member => member.Bounds.Y)
                    .ThenBy(member => member.Bounds.X)
                    .Select(member => member.Member)
                    .ToArray(),
                new PhysicalTerminalGroupBounds(minX, minY, maxRight - minX, maxBottom - minY)));
        }

        return groups;
    }

    private static IReadOnlyList<Candidate> Candidates(
        ElectricalProject project,
        string containerId,
        MountingSurface surface)
    {
        var candidates = new List<Candidate>();
        candidates.AddRange(project.Components
            .Where(component =>
                TopologyPaletteMaterialPolicy.Classify(component.TypeKey) ==
                TopologyPaletteMaterialKind.TerminalBlock)
            .Where(component => IsOnSurface(component.Placement, containerId, surface) && HasSize(component.Footprint))
            .Select(component => CandidateFor(
                new PhysicalTerminalGroupMember(PhysicalTerminalObjectKind.Component, component.ComponentInstanceId),
                component.Placement!,
                component.Footprint!,
                $"COMPONENT:{component.ComponentDefinitionId}")));
        candidates.AddRange(project.TerminalBlocks
            .Where(block => IsOnSurface(block.Placement, containerId, surface) && HasSize(block.Footprint))
            .Select(block => CandidateFor(
                new PhysicalTerminalGroupMember(PhysicalTerminalObjectKind.TerminalBlock, block.TerminalBlockId),
                block.Placement!,
                block.Footprint!,
                TerminalBlockCompatibilityKey(block))));

        return candidates;
    }

    private static bool IsOnSurface(PhysicalPlacement? placement, string containerId, MountingSurface surface) =>
        placement is not null &&
        string.Equals(placement.ParentContainerId, containerId, StringComparison.OrdinalIgnoreCase) &&
        placement.Surface == surface;

    private static bool HasSize(PhysicalFootprint? footprint) =>
        footprint is not null && footprint.WidthMm > 0d && footprint.HeightMm > 0d;

    private static Candidate CandidateFor(
        PhysicalTerminalGroupMember member,
        PhysicalPlacement placement,
        PhysicalFootprint footprint,
        string compatibilityKey)
    {
        var projection = PhysicalFootprintProjection.Project(footprint, placement);
        return new Candidate(
            member,
            placement,
            compatibilityKey,
            PhysicalFootprintProjection.NormalizeRotation(placement.RotationDegrees),
            placement.MountOrientation,
            new PhysicalTerminalGroupBounds(
                placement.XMm,
                placement.YMm,
                projection.WidthMm,
                projection.HeightMm));
    }

    private static bool AreAdjacent(
        PhysicalTerminalGroupBounds first,
        PhysicalTerminalGroupBounds second,
        double tolerance)
    {
        var horizontalGap = AxisGap(first.X, first.Right, second.X, second.Right);
        var verticalGap = AxisGap(first.Y, first.Bottom, second.Y, second.Bottom);
        var horizontalOverlap = AxisOverlap(first.X, first.Right, second.X, second.Right);
        var verticalOverlap = AxisOverlap(first.Y, first.Bottom, second.Y, second.Bottom);

        var beside = horizontalGap <= tolerance &&
                     verticalOverlap >= Math.Min(first.Height, second.Height) * 0.65d;
        var stacked = verticalGap <= tolerance &&
                      horizontalOverlap >= Math.Min(first.Width, second.Width) * 0.65d;
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

    private static string TerminalBlockCompatibilityKey(TerminalBlock block)
    {
        var terminalTypes = block.Positions
            .Select(position => position.TerminalType)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (terminalTypes.Length > 0)
            return "TERMINAL_BLOCK:" + string.Join("|", terminalTypes);
        if (!string.IsNullOrWhiteSpace(block.DisplayName))
            return "TERMINAL_BLOCK:" + block.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(block.FunctionTag))
            return "TERMINAL_BLOCK:" + block.FunctionTag.Trim();
        return "TERMINAL_BLOCK:GENERIC";
    }

    private sealed record Candidate(
        PhysicalTerminalGroupMember Member,
        PhysicalPlacement Placement,
        string CompatibilityKey,
        int RotationDegrees,
        ComponentMountOrientation MountOrientation,
        PhysicalTerminalGroupBounds Bounds);
}

public enum PhysicalTerminalObjectKind
{
    Component,
    TerminalBlock
}

public sealed record PhysicalTerminalGroupMember(PhysicalTerminalObjectKind Kind, string ObjectId);

public sealed record PhysicalTerminalVisualGroup(
    IReadOnlyList<PhysicalTerminalGroupMember> Members,
    PhysicalTerminalGroupBounds Bounds);

public sealed record PhysicalTerminalGroupBounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}
