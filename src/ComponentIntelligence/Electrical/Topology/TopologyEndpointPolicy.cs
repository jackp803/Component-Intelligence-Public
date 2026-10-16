using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

public enum TopologyEndpointDisplayMode
{
    Connector,
    Pins
}

/// <summary>
/// Decides whether a topology port should be shown as one mateable connector endpoint or as
/// individually wireable pins/terminals. This is a presentation/interaction policy only; the
/// underlying engineering model always retains every physical pin/contact.
/// </summary>
public static class TopologyEndpointPolicy
{
    public static TopologyEndpointDisplayMode DetermineDisplayMode(ComponentPort port)
    {
        ArgumentNullException.ThrowIfNull(port);

        var declared = GetCapabilityValue(port, "TOPOLOGY_ENDPOINT_MODE:") ??
                       GetCapabilityValue(port, "ENDPOINT_MODE:");
        if (!string.IsNullOrWhiteSpace(declared))
        {
            if (declared.Equals("PINS", StringComparison.OrdinalIgnoreCase) ||
                declared.Equals("TERMINALS", StringComparison.OrdinalIgnoreCase) ||
                declared.Equals("WIRES", StringComparison.OrdinalIgnoreCase))
                return TopologyEndpointDisplayMode.Pins;
            if (declared.Equals("CONNECTOR", StringComparison.OrdinalIgnoreCase) ||
                declared.Equals("PORT", StringComparison.OrdinalIgnoreCase))
                return TopologyEndpointDisplayMode.Connector;
        }

        var family = port.Connector?.Family?.Trim() ?? string.Empty;
        if (ContainsAny(family, "M12", "RJ45", "RJ-45", "M8", "USB", "HDMI", "D-SUB", "DSUB"))
            return TopologyEndpointDisplayMode.Connector;

        if (ContainsAny(family,
                "TERMINAL",
                "SCREW",
                "LOOSE WIRE",
                "FLYING LEAD",
                "FLYING WIRE",
                "BARE WIRE",
                "OPEN WIRE",
                "FIXED CABLE"))
            return TopologyEndpointDisplayMode.Pins;

        // If there is no meaningful connector body but engineering pins exist, exposing the
        // physical conductors is safer than hiding them behind an artificial aggregate endpoint.
        if (port.Connector is null && port.Pins.Count > 0)
            return TopologyEndpointDisplayMode.Pins;

        return TopologyEndpointDisplayMode.Connector;
    }

    public static bool ShouldShowPins(ComponentPort port, IReadOnlySet<string> expandedPortIds) =>
        DetermineDisplayMode(port) == TopologyEndpointDisplayMode.Pins || expandedPortIds.Contains(port.PortId);

    private static string? GetCapabilityValue(ComponentPort port, string prefix)
    {
        var capability = port.Capabilities
            .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(capability)) return null;
        var separator = capability.IndexOf(':');
        if (separator < 0 || separator == capability.Length - 1) return null;
        return capability[(separator + 1)..].Trim();
    }

    private static bool ContainsAny(string source, params string[] values) =>
        values.Any(value => source.Contains(value, StringComparison.OrdinalIgnoreCase));
}
