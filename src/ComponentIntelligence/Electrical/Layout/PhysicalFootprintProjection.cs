using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Layout;

/// <summary>
/// Converts a component's three physical dimensions into the two dimensions shown on a selected
/// cabinet face. The remaining dimension is the protrusion normal to that face.
/// </summary>
public static class PhysicalFootprintProjection
{
    public static ProjectedFootprint Project(PhysicalFootprint footprint, PhysicalPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(footprint);
        ArgumentNullException.ThrowIfNull(placement);

        var (width, height, protrusion) = placement.MountOrientation switch
        {
            ComponentMountOrientation.Side => (footprint.DepthMm ?? 0, footprint.HeightMm, (double?)footprint.WidthMm),
            ComponentMountOrientation.Top => (footprint.WidthMm, footprint.DepthMm ?? 0, (double?)footprint.HeightMm),
            _ => (footprint.WidthMm, footprint.HeightMm, footprint.DepthMm)
        };

        if (NormalizeRotation(placement.RotationDegrees) is 90 or 270)
            (width, height) = (height, width);

        return new ProjectedFootprint(width, height, protrusion);
    }

    public static int NormalizeRotation(int degrees)
    {
        var normalized = degrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}

public readonly record struct ProjectedFootprint(double WidthMm, double HeightMm, double? ProtrusionMm);
