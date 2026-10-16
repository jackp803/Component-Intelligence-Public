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
    public static bool IsEndpointMarkerSize(double width, double height) =>
        width >= 11.9d && width <= 14.1d &&
        height >= 11.9d && height <= 14.1d;

    public static TopologyPlacementBounds CalculateVisualBounds(TopologyPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        var radians = NormalizeDegrees(placement.RotationDegrees) * Math.PI / 180d;
        var visualWidth = Math.Abs(placement.Width * Math.Cos(radians)) +
                          Math.Abs(placement.Height * Math.Sin(radians));
        var visualHeight = Math.Abs(placement.Width * Math.Sin(radians)) +
                           Math.Abs(placement.Height * Math.Cos(radians));
        var centerX = placement.X + placement.Width / 2d;
        var centerY = placement.Y + placement.Height / 2d;
        return new TopologyPlacementBounds(
            centerX - visualWidth / 2d,
            centerY - visualHeight / 2d,
            visualWidth,
            visualHeight);
    }

    public static TopologyScreenSide DetermineScreenSide(ComponentInstance component, ComponentPort port)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(port);

        // Some products have a documented topology presentation that is intentionally not the same
        // as generic Input-left / Output-right signal flow. Keep that rule here in presentation logic;
        // never rewrite central PortRole, Direction, or PhysicalSide merely to force the drawing.
        if (TryDetermineComponentPresentationSide(component, port, out var presentationSide))
            return presentationSide;

        return DetermineScreenSide(port);
    }

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

    /// <summary>
    /// Keeps a port attached to its original component edge while the component rotates. For
    /// example, a right-edge output moves to the bottom edge after a clockwise 90-degree rotation,
    /// and a left-edge input moves to the top edge. This is used by the interactive canvas where
    /// component rotation must move the actual pins and connected wire endpoints together.
    /// </summary>
    public static TopologyPortAnchor CalculateRotatedSide(
        TopologyPlacement placement,
        TopologyScreenSide side,
        int portIndex,
        int portCount)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ValidateIndex(portIndex, portCount);

        var fraction = (portIndex + 1d) / (portCount + 1d);
        var isLeft = side == TopologyScreenSide.Left;
        var localX = isLeft ? placement.X : placement.X + placement.Width;
        var localY = placement.Y + placement.Height * fraction;
        return RotateLocalAnchor(
            placement,
            localX,
            localY,
            isLeft ? -1d : 1d,
            0d);
    }

    /// <summary>
    /// Places an endpoint label inside the component edge. Labels on the left/right edges remain
    /// horizontal; labels on the top/bottom edges rotate so their long axis points into the
    /// component. This preserves the endpoint spacing after a 90-degree component rotation instead
    /// of stacking every label horizontally along the same short edge.
    /// </summary>
    public static TopologyEndpointLabelLayout CalculateEndpointLabelLayout(
        TopologyPortAnchor anchor,
        double labelWidth,
        double labelHeight = 13d,
        double markerGap = 10d)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        if (labelWidth <= 0d) throw new ArgumentOutOfRangeException(nameof(labelWidth));
        if (labelHeight <= 0d) throw new ArgumentOutOfRangeException(nameof(labelHeight));
        if (markerGap < 0d) throw new ArgumentOutOfRangeException(nameof(markerGap));

        var isHorizontalEdge = Math.Abs(anchor.OutwardY) > 0.25d;
        var rotationDegrees = isHorizontalEdge
            ? anchor.OutwardY < 0d ? 90d : -90d
            : 0d;
        var inwardDistanceToCenter = markerGap + labelWidth / 2d;
        var centerX = anchor.X - anchor.OutwardX * inwardDistanceToCenter;
        var centerY = anchor.Y - anchor.OutwardY * inwardDistanceToCenter;

        return new TopologyEndpointLabelLayout(
            centerX - labelWidth / 2d,
            centerY - labelHeight / 2d,
            labelWidth,
            labelHeight,
            rotationDegrees);
    }

    public static TopologyEndpointComponentSize CalculateEndpointComponentSize(
        double baselineWidth,
        double baselineHeight,
        int leftEndpointCount,
        int rightEndpointCount,
        double leftLabelWidth,
        double rightLabelWidth,
        bool hasPinLevelEndpoints,
        bool compactTerminal)
    {
        if (baselineWidth <= 0d) throw new ArgumentOutOfRangeException(nameof(baselineWidth));
        if (baselineHeight <= 0d) throw new ArgumentOutOfRangeException(nameof(baselineHeight));
        if (leftEndpointCount < 0) throw new ArgumentOutOfRangeException(nameof(leftEndpointCount));
        if (rightEndpointCount < 0) throw new ArgumentOutOfRangeException(nameof(rightEndpointCount));

        // Compact terminal markers use a 12 px visual. A 13 px pitch leaves a visible separation
        // while keeping large terminal strips substantially narrower than ordinary components.
        var endpointPitch = compactTerminal ? 13d : 22d;
        var verticalPadding = compactTerminal ? 26d : 52d;
        var titleLane = compactTerminal ? 56d : 96d;
        var minimumPinWidth = compactTerminal ? 118d : 180d;
        var maxSideCount = Math.Max(leftEndpointCount, rightEndpointCount);

        return new TopologyEndpointComponentSize(
            Math.Max(
                baselineWidth,
                Math.Max(hasPinLevelEndpoints ? minimumPinWidth : 0d, leftLabelWidth + rightLabelWidth + titleLane)),
            Math.Max(baselineHeight, verticalPadding + maxSideCount * endpointPitch));
    }

    private static bool TryDetermineComponentPresentationSide(
        ComponentInstance component,
        ComponentPort port,
        out TopologyScreenSide side)
    {
        side = TopologyScreenSide.Right;
        var identity = $"{component.ComponentDefinitionId} {component.DisplayName}";
        if (!identity.Contains("K7L-AT50DP", StringComparison.OrdinalIgnoreCase)) return false;

        var sourcePortId = GetCapabilityValue(port, "SOURCE_PORT_ID:") ?? port.Name;
        var role = GetCapabilityValue(port, "ROLE:");
        var presentationKey = $"{sourcePortId} {port.Name} {role}";

        // User-approved K7L topology convention:
        //   left  = POWER terminals 1/8 + relay/output terminals 5/6/7
        //   right = SENSING terminals 2/3/4
        // Electrical Direction remains truthful in the archive (Input / Mixed / Output).
        if (presentationKey.Contains("SENSING", StringComparison.OrdinalIgnoreCase) ||
            presentationKey.Contains("SENSOR INPUT", StringComparison.OrdinalIgnoreCase))
        {
            side = TopologyScreenSide.Right;
            return true;
        }

        if (presentationKey.Contains("OUTPUT", StringComparison.OrdinalIgnoreCase) ||
            presentationKey.Contains("POWER", StringComparison.OrdinalIgnoreCase))
        {
            side = TopologyScreenSide.Left;
            return true;
        }

        return false;
    }

    private static string? GetCapabilityValue(ComponentPort port, string prefix)
    {
        // Newer archive knowledge is appended during conservative enrichment. Reading the last
        // declaration also repairs projects that were saved before authoritative synchronization
        // learned to replace stale ROLE/DIRECTION metadata.
        var capability = port.Capabilities
            .LastOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
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
            "BIDIRECTIONAL" or "MIXED" or "INOUT" or "I/O" or "IO")
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
public sealed record TopologyPlacementBounds(double X, double Y, double Width, double Height);
public sealed record TopologyEndpointLabelLayout(
    double X,
    double Y,
    double Width,
    double Height,
    double RotationDegrees);
public sealed record TopologyEndpointComponentSize(double Width, double Height);
