using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

/// <summary>
/// Computes the visual anchor for a component Port on the topology canvas. Ports are laid out on the
/// component's local right edge, then rotated around the same center/origin as the component node.
/// Keeping this math outside WPF makes rotation behavior deterministic and directly testable.
/// </summary>
public static class TopologyPortGeometry
{
    public static TopologyPortAnchor Calculate(TopologyPlacement placement, int portIndex, int portCount)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (portCount <= 0) throw new ArgumentOutOfRangeException(nameof(portCount));
        if (portIndex < 0 || portIndex >= portCount) throw new ArgumentOutOfRangeException(nameof(portIndex));

        var spacing = Math.Max(15d, placement.Height / (portCount + 1d));
        var localX = placement.X + placement.Width;
        var localY = placement.Y + spacing * (portIndex + 1d);
        var centerX = placement.X + placement.Width / 2d;
        var centerY = placement.Y + placement.Height / 2d;

        var normalized = placement.RotationDegrees % 360;
        if (normalized < 0) normalized += 360;
        var radians = normalized * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var dx = localX - centerX;
        var dy = localY - centerY;

        return new TopologyPortAnchor(
            centerX + dx * cos - dy * sin,
            centerY + dx * sin + dy * cos,
            cos,
            sin);
    }
}

public sealed record TopologyPortAnchor(double X, double Y, double OutwardX, double OutwardY);
