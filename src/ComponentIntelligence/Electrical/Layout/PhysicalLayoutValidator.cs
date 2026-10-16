using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Layout;

/// <summary>
/// Validates cabinet fit using a 2D editing model plus 2.5D engineering facts. XY overlap alone is
/// never sufficient to declare a collision: mounting surface, component depth and depth offset must
/// also support that conclusion. Cable length is intentionally outside this validator.
/// </summary>
public sealed class PhysicalLayoutValidator
{
    public IReadOnlyList<LayoutIssue> Validate(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var issues = new List<LayoutIssue>();
        var containers = project.LayoutContainers.ToDictionary(container => container.ContainerId, StringComparer.OrdinalIgnoreCase);
        var rails = project.DinRails.ToDictionary(rail => rail.DinRailId, StringComparer.OrdinalIgnoreCase);
        var placed = GetPlacedObjects(project).ToList();

        foreach (var item in placed)
        {
            var placement = item.Placement;
            var footprint = item.Footprint;
            if (!containers.TryGetValue(placement.ParentContainerId, out var container))
            {
                issues.Add(Block("RULE-LAYOUT-001", item.ObjectId, $"Parent container '{placement.ParentContainerId}' does not exist."));
                continue;
            }

            var body = GetBodyBounds(placement, footprint);
            var clearance = GetClearanceBounds(placement, footprint);
            var face = GetFaceSize(container, placement.Surface);

            if (face is null)
            {
                issues.Add(Warning("RULE-LAYOUT-008", item.ObjectId,
                    $"Mounting surface '{placement.Surface}' cannot be fully checked because the required cabinet dimension is unknown."));
            }
            else
            {
                if (IsOutside(body, face.Value.Width, face.Value.Height))
                    issues.Add(Block("RULE-LAYOUT-001", item.ObjectId,
                        $"Component body exceeds the '{placement.Surface}' mounting-face boundary."));
                else if (IsOutside(clearance, face.Value.Width, face.Value.Height))
                    issues.Add(Warning("RULE-LAYOUT-004", item.ObjectId,
                        "Required top/bottom/left/right clearance extends beyond the mounting-face boundary."));
            }

            if (placement.Surface == MountingSurface.Unknown)
                issues.Add(Warning("RULE-LAYOUT-008", item.ObjectId,
                    "Mounting Surface（安裝面）未知；2.5D collision / cabinet-fit verification is incomplete."));

            ValidateDepth(item, container, issues);
            ValidateMountTarget(item, rails, issues);
            ValidateZones(item, container, body, issues);
        }

        ValidatePairwiseCollisions(placed, containers, issues);
        ValidateDinRailUsage(placed, rails, issues);
        return issues;
    }

    private static void ValidateDepth(PlacedObject item, LayoutContainer container, ICollection<LayoutIssue> issues)
    {
        var placement = item.Placement;
        if (placement.Surface is MountingSurface.Unknown or MountingSurface.External) return;

        var projection = PhysicalFootprintProjection.Project(item.Footprint, placement);
        if (projection.ProtrusionMm is not double depth || depth <= 0)
        {
            issues.Add(Warning("RULE-LAYOUT-006", item.ObjectId,
                "Component Depth（深度）未知；無法完整確認箱內深度與關門空間。"));
            return;
        }

        var availableDepth = GetNormalExtent(container, placement.Surface);
        if (availableDepth is null)
        {
            issues.Add(Warning("RULE-LAYOUT-006", item.ObjectId,
                $"Cabinet dimension required to verify depth on '{placement.Surface}' is unknown."));
            return;
        }

        if (placement.DepthOffsetMm < 0 || placement.DepthOffsetMm + depth > availableDepth.Value + 1e-9)
        {
            issues.Add(Block("RULE-LAYOUT-006", item.ObjectId,
                $"Depth occupancy {placement.DepthOffsetMm:0.###}..{placement.DepthOffsetMm + depth:0.###} mm exceeds the available {availableDepth.Value:0.###} mm normal to '{placement.Surface}'."));
        }
    }

    private static void ValidateMountTarget(PlacedObject item, IReadOnlyDictionary<string, DinRail> rails, ICollection<LayoutIssue> issues)
    {
        if (item.Footprint.MountingType != MountingType.DinRail) return;

        var placement = item.Placement;
        if (string.IsNullOrWhiteSpace(placement.MountTargetId) || !rails.TryGetValue(placement.MountTargetId, out var rail))
        {
            issues.Add(Block("RULE-LAYOUT-003", item.ObjectId, "DIN-rail object has no valid DIN rail mount target."));
            return;
        }

        if (!string.Equals(rail.ParentContainerId, placement.ParentContainerId, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Block("RULE-LAYOUT-003", item.ObjectId, "DIN rail mount target is in a different layout container."));
            return;
        }

        if (placement.Surface != MountingSurface.Unknown && rail.Surface != MountingSurface.Unknown && placement.Surface != rail.Surface)
            issues.Add(Block("RULE-LAYOUT-010", item.ObjectId,
                $"DIN rail is on '{rail.Surface}' but the mounted object is assigned to '{placement.Surface}'."));
    }

    private static void ValidateZones(PlacedObject item, LayoutContainer container, RectMm body, ICollection<LayoutIssue> issues)
    {
        foreach (var zone in container.Zones.Where(zone => zone.IsKeepOut || zone.IsForbidden))
        {
            if (zone.Surface != MountingSurface.Unknown && item.Placement.Surface != MountingSurface.Unknown && zone.Surface != item.Placement.Surface)
                continue;
            if (!Intersects(body, zone.Bounds)) continue;

            issues.Add(new LayoutIssue
            {
                RuleId = "RULE-LAYOUT-005",
                Severity = zone.IsForbidden ? ValidationSeverity.Block : ValidationSeverity.Warning,
                ObjectId = item.ObjectId,
                Message = $"Placement intersects zone '{zone.Name}' on surface '{zone.Surface}'.",
                AffectsDrawingExport = zone.IsForbidden
            });
        }
    }

    private static void ValidatePairwiseCollisions(
        IReadOnlyList<PlacedObject> placed,
        IReadOnlyDictionary<string, LayoutContainer> containers,
        ICollection<LayoutIssue> issues)
    {
        for (var firstIndex = 0; firstIndex < placed.Count; firstIndex++)
        for (var secondIndex = firstIndex + 1; secondIndex < placed.Count; secondIndex++)
        {
            var first = placed[firstIndex];
            var second = placed[secondIndex];
            if (!string.Equals(first.Placement.ParentContainerId, second.Placement.ParentContainerId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!containers.TryGetValue(first.Placement.ParentContainerId, out var container)) continue;

            var firstBody = GetBodyBounds(first.Placement, first.Footprint);
            var secondBody = GetBodyBounds(second.Placement, second.Footprint);
            var bodyOverlap = Intersects(firstBody, secondBody);

            if (first.Placement.Surface == MountingSurface.Unknown || second.Placement.Surface == MountingSurface.Unknown)
            {
                if (bodyOverlap)
                    issues.Add(Warning("RULE-LAYOUT-008", first.ObjectId,
                        $"2D projection overlaps '{second.DisplayName}', but at least one Mounting Surface（安裝面）is unknown; collision is not assumed."));
                continue;
            }

            if (first.Placement.Surface == second.Placement.Surface)
            {
                if (bodyOverlap)
                {
                    var firstDepth = GetLocalDepthInterval(first);
                    var secondDepth = GetLocalDepthInterval(second);
                    if (firstDepth is null || secondDepth is null)
                    {
                        issues.Add(Warning("RULE-LAYOUT-006", first.ObjectId,
                            $"2D body overlaps '{second.DisplayName}' on the same mounting surface, but depth data is incomplete; physical collision requires review."));
                    }
                    else if (IntervalsOverlap(firstDepth.Value, secondDepth.Value))
                    {
                        issues.Add(Block("RULE-LAYOUT-002", first.ObjectId,
                            $"Physical body collides with '{second.DisplayName}' on mounting surface '{first.Placement.Surface}'."));
                    }
                }
                else
                {
                    var firstClearance = GetClearanceBounds(first.Placement, first.Footprint);
                    var secondClearance = GetClearanceBounds(second.Placement, second.Footprint);
                    if (Intersects(firstClearance, secondBody) || Intersects(secondClearance, firstBody))
                        issues.Add(Warning("RULE-LAYOUT-004", first.ObjectId,
                            $"Required planar clearance overlaps '{second.DisplayName}' on mounting surface '{first.Placement.Surface}'."));
                }
                continue;
            }

            if (!IsBackplateDoorPair(first.Placement.Surface, second.Placement.Surface) || !bodyOverlap) continue;

            var firstCabinetDepth = GetCabinetDepthInterval(first, container);
            var secondCabinetDepth = GetCabinetDepthInterval(second, container);
            if (firstCabinetDepth is null || secondCabinetDepth is null)
            {
                issues.Add(Warning("RULE-LAYOUT-009", first.ObjectId,
                    $"Door/backplate projections overlap '{second.DisplayName}', but cabinet/component depth is incomplete; door-closure collision cannot be verified."));
            }
            else if (IntervalsOverlap(firstCabinetDepth.Value, secondCabinetDepth.Value))
            {
                issues.Add(Block("RULE-LAYOUT-009", first.ObjectId,
                    $"Door Closure Collision（關門碰撞）with '{second.DisplayName}': backplate and door-mounted depth volumes overlap."));
            }
        }
    }

    private static void ValidateDinRailUsage(
        IReadOnlyList<PlacedObject> placed,
        IReadOnlyDictionary<string, DinRail> rails,
        ICollection<LayoutIssue> issues)
    {
        foreach (var rail in rails.Values)
        {
            var used = placed
                .Where(item => item.Placement.MountTargetId is not null &&
                               string.Equals(item.Placement.MountTargetId, rail.DinRailId, StringComparison.OrdinalIgnoreCase))
                .Sum(item => PhysicalFootprintProjection.Project(item.Footprint, item.Placement).WidthMm);

            if (used > rail.LengthMm + 1e-9)
                issues.Add(Block("RULE-LAYOUT-007", rail.DinRailId, $"DIN rail usage {used:g} mm exceeds rail length {rail.LengthMm:g} mm."));
        }
    }

    private static IEnumerable<PlacedObject> GetPlacedObjects(ElectricalProject project)
    {
        foreach (var component in project.Components.Where(component => component.Placement is not null && component.Footprint is not null))
        {
            yield return new PlacedObject(
                component.ComponentInstanceId,
                component.ReferenceDesignator ?? component.DisplayName ?? component.ComponentInstanceId,
                component.Placement!,
                component.Footprint!);
        }

        foreach (var block in project.TerminalBlocks.Where(block => block.Placement is not null && block.Footprint is not null))
        {
            yield return new PlacedObject(
                block.TerminalBlockId,
                block.ReferenceDesignator,
                block.Placement!,
                block.Footprint!);
        }
    }

    private static RectMm GetBodyBounds(PhysicalPlacement placement, PhysicalFootprint footprint)
    {
        var projection = PhysicalFootprintProjection.Project(footprint, placement);
        return new RectMm(placement.XMm, placement.YMm, projection.WidthMm, projection.HeightMm);
    }

    private static RectMm GetClearanceBounds(PhysicalPlacement placement, PhysicalFootprint footprint)
    {
        var body = GetBodyBounds(placement, footprint);
        var clearance = footprint.Clearance;
        return new RectMm(
            body.X - clearance.LeftMm,
            body.Y - clearance.TopMm,
            body.Width + clearance.LeftMm + clearance.RightMm,
            body.Height + clearance.TopMm + clearance.BottomMm);
    }

    private static (double Width, double Height)? GetFaceSize(LayoutContainer container, MountingSurface surface) => surface switch
    {
        MountingSurface.LeftWall or MountingSurface.RightWall when container.DepthMm is double depth => (depth, container.HeightMm),
        MountingSurface.Top or MountingSurface.Bottom when container.DepthMm is double depth => (container.WidthMm, depth),
        MountingSurface.LeftWall or MountingSurface.RightWall or MountingSurface.Top or MountingSurface.Bottom => null,
        _ => (container.WidthMm, container.HeightMm)
    };

    private static double? GetNormalExtent(LayoutContainer container, MountingSurface surface) => surface switch
    {
        MountingSurface.Backplate or MountingSurface.Door => container.DepthMm,
        MountingSurface.LeftWall or MountingSurface.RightWall => container.WidthMm,
        MountingSurface.Top or MountingSurface.Bottom => container.HeightMm,
        _ => null
    };

    private static (double Start, double End)? GetLocalDepthInterval(PlacedObject item)
    {
        if (PhysicalFootprintProjection.Project(item.Footprint, item.Placement).ProtrusionMm is not double depth || depth <= 0) return null;
        return (item.Placement.DepthOffsetMm, item.Placement.DepthOffsetMm + depth);
    }

    private static (double Start, double End)? GetCabinetDepthInterval(PlacedObject item, LayoutContainer container)
    {
        if (container.DepthMm is not double cabinetDepth ||
            PhysicalFootprintProjection.Project(item.Footprint, item.Placement).ProtrusionMm is not double depth || depth <= 0) return null;
        var offset = item.Placement.DepthOffsetMm;
        return item.Placement.Surface switch
        {
            MountingSurface.Backplate => (offset, offset + depth),
            MountingSurface.Door => (cabinetDepth - offset - depth, cabinetDepth - offset),
            _ => null
        };
    }

    private static bool IsBackplateDoorPair(MountingSurface first, MountingSurface second) =>
        (first == MountingSurface.Backplate && second == MountingSurface.Door) ||
        (first == MountingSurface.Door && second == MountingSurface.Backplate);

    private static bool IsOutside(RectMm bounds, double width, double height) =>
        bounds.X < -1e-9 || bounds.Y < -1e-9 || bounds.X + bounds.Width > width + 1e-9 || bounds.Y + bounds.Height > height + 1e-9;

    private static bool Intersects(RectMm first, RectMm second) =>
        first.X < second.X + second.Width &&
        first.X + first.Width > second.X &&
        first.Y < second.Y + second.Height &&
        first.Y + first.Height > second.Y;

    private static bool IntervalsOverlap((double Start, double End) first, (double Start, double End) second) =>
        first.Start < second.End - 1e-9 && first.End > second.Start + 1e-9;

    private static LayoutIssue Block(string ruleId, string objectId, string message) => new()
    {
        RuleId = ruleId,
        Severity = ValidationSeverity.Block,
        ObjectId = objectId,
        Message = message,
        AffectsDrawingExport = true
    };

    private static LayoutIssue Warning(string ruleId, string objectId, string message) => new()
    {
        RuleId = ruleId,
        Severity = ValidationSeverity.Warning,
        ObjectId = objectId,
        Message = message,
        AffectsDrawingExport = false
    };

    private sealed record PlacedObject(
        string ObjectId,
        string DisplayName,
        PhysicalPlacement Placement,
        PhysicalFootprint Footprint);
}

public sealed record LayoutIssue
{
    public required string RuleId { get; init; }
    public required ValidationSeverity Severity { get; init; }
    public required string ObjectId { get; init; }
    public required string Message { get; init; }
    public bool AffectsDrawingExport { get; init; }
}
