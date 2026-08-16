using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

public enum TopologyScreenSide
{
    Left,
    Right
}

/// <summary>
/// Computes visual anchors for Component Ports on the topology canvas.
/// The legacy Calculate method keeps the historical local-right-edge behavior for compatibility.
/// CalculateScreenSide places a Port on the requested screen-left/screen-right edge after component
/// rotation, which keeps Input on the left and Output on the right while still touching the rotated
/// component perimeter.
/// </summary>
public static class TopologyPortGeometry
{
    public static TopologyScreenSide DetermineScreenSide(ComponentPort port)
    {
        ArgumentNullException.ThrowIfNull(port);

        var declaredDirection = port.Capabilities
            .FirstOrDefault(capability => capability.StartsWith("DIRECTION:", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(declaredDirection))
        {
            var value = declaredDirection[(declaredDirection.IndexOf(':') + 1)..].Trim().ToUpperInvariant();
            if (value is "INPUT" or "IN" or "SINK" or "RECEIVE" or "RX")
                return TopologyScreenSide.Left;
            if (value is "OUTPUT" or "OUT" or "SOURCE" or "TRANSMIT" or "TX")
                return TopologyScreenSide.Right;
        }

        var hasInput = port.Pins.Any(pin =>
            pin.Power?.Role is PowerRole.Input or PowerRole.Return ||
            pin.Digital?.IoType == DigitalIoType.Di ||
            pin.Analog?.Direction == AnalogDirection.Input);
        var hasOutput = port.Pins.Any(pin =>
            pin.Power?.Role == PowerRole.Source ||
            pin.Digital?.IoType == DigitalIoType.Do ||
            pin.Analog?.Direction == AnalogDirection.Output);

        return hasInput && !hasOutput ? TopologyScreenSide.Left : TopologyScreenSide.Right;
    }

    public static TopologyPortAnchor Calculate(TopologyPlacement placement, int portIndex, int portCount)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ValidateIndex(portIndex, portCount);

        var spacing = Math.Max(15d, placement.Height / (portCount + 1d));
        var localX = placement.X + placement.Width;
        var localY = placement.Y + spacing * (portIndex + 1d);
        return RotateLocalAnchor(placement, localX, localY, 1d, 0d);
    }

    public static TopologyPortAnchor CalculateScreenSide(
        TopologyPlacement placement,
        TopologyScreenSide side,
        int portIndex,
        int portCount)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ValidateIndex(portIndex, portCount);

        var radians = NormalizeDegrees(placement.RotationDegrees) * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var desiredX = side == TopologyScreenSide.Left ? -1d : 1d;

        // Pick the local rectangle edge whose rotated outward normal points furthest toward the
        // requested screen side. With the UI's 90-degree rotation increments this produces an exact
        // screen-left or screen-right edge, while still behaving deterministically for other angles.
        var candidates = new[]
        {
            new LocalEdge(LocalEdgeKind.Left, -1d, 0d),
            new LocalEdge(LocalEdgeKind.Right, 1d, 0d),
            new LocalEdge(LocalEdgeKind.Top, 0d, -1d),
            new LocalEdge(LocalEdgeKind.Bottom, 0d, 1d)
        };

        var edge = candidates
            .OrderByDescending(candidate => desiredX * (candidate.OutwardX * cos - candidate.OutwardY * sin))
            .First();

        var fraction = (portIndex + 1d) / (portCount + 1d);
        var localX = edge.Kind switch
        {
            LocalEdgeKind.Left => placement.X,
            LocalEdgeKind.Right => placement.X + placement.Width,
            _ => placement.X + placement.Width * fraction
        };
        var localY = edge.Kind switch
        {
            LocalEdgeKind.Top => placement.Y,
            LocalEdgeKind.Bottom => placement.Y + placement.Height,
            _ => placement.Y + placement.Height * fraction
        };

        return RotateLocalAnchor(placement, localX, localY, edge.OutwardX, edge.OutwardY);
    }

    private static TopologyPortAnchor RotateLocalAnchor(
        TopologyPlacement placement,
        double localX,
        double localY,
        double localOutwardX,
        double localOutwardY)
    {
        var centerX = placement.X + placement.Width / 2d;
        var centerY = placement.Y + placement.Height / 2d;
        var radians = NormalizeDegrees(placement.RotationDegrees) * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var dx = localX - centerX;
        var dy = localY - centerY;

        return new TopologyPortAnchor(
            centerX + dx * cos - dy * sin,
            centerY + dx * sin + dy * cos,
            localOutwardX * cos - localOutwardY * sin,
            localOutwardX * sin + localOutwardY * cos);
    }

    private static int NormalizeDegrees(int degrees)
    {
        var normalized = degrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static void ValidateIndex(int portIndex, int portCount)
    {
        if (portCount <= 0) throw new ArgumentOutOfRangeException(nameof(portCount));
        if (portIndex < 0 || portIndex >= portCount) throw new ArgumentOutOfRangeException(nameof(portIndex));
    }

    private enum LocalEdgeKind { Left, Right, Top, Bottom }
    private sealed record LocalEdge(LocalEdgeKind Kind, double OutwardX, double OutwardY);
}

public sealed record TopologyPortAnchor(double X, double Y, double OutwardX, double OutwardY);
