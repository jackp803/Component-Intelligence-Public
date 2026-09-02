using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

/// <summary>
/// Allows controlled fan-out only on pins whose engineering identity explicitly describes a
/// common/return potential. Ordinary signal pins remain single-connection endpoints.
/// </summary>
public static class TopologyEndpointBranchPolicy
{
    public const int CommonPinMaximumConnections = 4;

    public static int MaximumConnections(ComponentPin pin) =>
        IsCommonBranchPin(pin) ? CommonPinMaximumConnections : 1;

    public static int MaximumConnections(ComponentPort port, ComponentPin pin) =>
        AllowsManualBranching(port) || IsCommonBranchPin(pin) ? CommonPinMaximumConnections : 1;

    public static bool AllowsBranching(ComponentPort port, ComponentPin pin) =>
        AllowsManualBranching(port) || IsCommonBranchPin(pin);

    public static bool AllowsManualBranching(ComponentPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        return port.Capabilities.Any(capability =>
            string.Equals(capability, "ALLOW_MANUAL_BRANCHING", StringComparison.OrdinalIgnoreCase) ||
            capability.StartsWith("ROLE:Loose Wire", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsCommonBranchPin(ComponentPin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        if (pin.GroundReferenceType is GroundReferenceType.PowerReturn or GroundReferenceType.SignalGround)
            return true;
        var compact = new string($"{pin.PinName} {pin.Function} {pin.SignalStandardRaw}"
            .ToUpperInvariant()
            .Where(character => char.IsLetterOrDigit(character) || character is '+' or '-')
            .ToArray());
        return compact.Contains("0V", StringComparison.Ordinal) ||
               compact.Contains("GND", StringComparison.Ordinal) ||
               compact.Contains("GROUND", StringComparison.Ordinal) ||
               compact.Contains("COMMON", StringComparison.Ordinal) ||
               compact.StartsWith("COM", StringComparison.Ordinal) ||
               compact.Contains("V-", StringComparison.Ordinal) ||
               compact.Contains("DC-", StringComparison.Ordinal);
    }
}
