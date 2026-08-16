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
/// rotation. Screen-side selection is a topology presentation rule, not a rewrite of electrical
/// direction: an archived passive Port may still be visually an Input-side or Output-side interface.
/// </summary>
public static class TopologyPortGeometry
{
    public static TopologyScreenSide DetermineScreenSide(ComponentPort port)
    {
        ArgumentNullException.ThrowIfNull(port);

        // Topology role is the primary visual semantic. This intentionally comes before electrical
        // Direction so passive adapters/terminal blocks such as OMRON F03-20 can preserve truthful
        // Direction=Passive while still rendering Input Port on the left and Output Port on the right.
        var declaredRole = GetCapabilityValue(port, "ROLE:");
        if (TryDetermineRoleSide(declaredRole, out var roleSide))
            return roleSide;

        // If the archive does not provide a directional role, use the actual electrical direction.
        var declaredDirection = GetCapabilityValue(port, "DIRECTION:");
        if (TryDetermineDirectionSide(declaredDirection, out var directionSide))
            return directionSide;

        // Finally infer from engineering pin semantics. Mixed/ambiguous or fully passive pin sets are
        // deliberately not forced into an Input/Output meaning and retain the neutral right-side default.
        var hasInput = port.Pins.Any(pin =>
            pin.Power?.Role is PowerRole.Input or PowerRole.Return ||
            pin.Digital?.IoType == DigitalIoType.Di ||
            pin.Analog?.Direction == AnalogDirection.Input);
        var hasOutput = port.Pins.Any(pin =>
            pin.Power?.Role == PowerRole.Source ||
            pin.Digital?.IoType == DigitalIoType.Do ||
            pin.Analog?.Direction == AnalogDirection.Output);

        if (hasInput && !hasOutput) return TopologyScreenSide.Left;
        if (hasOutput && !hasInput) return TopologyScreenSide.Right;

        return TopologyScreenSide.Right;
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

    private static string? GetCapabilityValue(ComponentPort port, string prefix)
    {
        var capability = port.Capabilities
            .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(capability)) return null;

        var separator = capability.IndexOf(':');
        if (separator < 0 || separator == capability.Length - 1) return null;
        var value = capability[(separator + 1)..].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool TryDetermineRoleSide(string? role, out TopologyScreenSide side)
    {
        side = TopologyScreenSide.Right;
        if (string.IsNullOrWhiteSpace(role)) return false;

        var tokens = role
            .Split([' ', '\t', '-', '_', '/', '\\', ':', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hasInputRole = tokens.Any(token => token.Equals("INPUT", StringComparison.OrdinalIgnoreCase));
        var hasOutputRole = tokens.Any(token => token.Equals("OUTPUT", StringComparison.OrdinalIgnoreCase));

        // A role such as "Input/Output" is intentionally ambiguous and must fall through to Direction.
        if (hasInputRole == hasOutputRole) return false;

        side = hasInputRole ? TopologyScreenSide.Left : TopologyScreenSide.Right;
        return true;
    }

    private static bool TryDetermineDirectionSide(string? direction, out TopologyScreenSide side)
    {
        side = TopologyScreenSide.Right;
        if (string.IsNullOrWhiteSpace(direction)) return false;

        var value = direction.Trim().ToUpperInvariant();
        if (value is "INPUT" or "IN" or "SINK" or "RECEIVE" or "RX")
        {
            side = TopologyScreenSide.Left;
            return true;
        }

        if (value is "OUTPUT" or "OUT" or "SOURCE" or "TRANSMIT" or "TX" or
            "BIDIRECTIONAL" or "INOUT" or "I/O" or "IO")
        {
            side = TopologyScreenSide.Right;
            return true;
        }

        return false;
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
