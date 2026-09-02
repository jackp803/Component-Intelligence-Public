using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

public enum TopologyPotentialClass
{
    Unknown,
    PositiveDc,
    NegativeOrReturnDc,
    ProtectiveOrFunctionalEarth
}

/// <summary>
/// Resolves a topology route's display potential from structured engineering data first and
/// conservative endpoint/net labels second. The result is a canvas styling hint only; it never
/// changes the electrical project or claims a physical conductor colour.
/// </summary>
public static class TopologyConnectionPotentialClassifier
{
    public static TopologyPotentialClass ClassifyEndpoint(ElectricalProject project, string endpointId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);

        var pin = project.Components.SelectMany(component => component.Ports)
            .SelectMany(port => port.Pins)
            .FirstOrDefault(item => string.Equals(item.PinId, endpointId, StringComparison.OrdinalIgnoreCase));
        if (pin is not null)
        {
            var structured = ClassifyPin(pin);
            if (structured != TopologyPotentialClass.Unknown) return structured;
        }

        return ResolveConsensus(ResolveEndpointLabels(project, endpointId).Select(ClassifyLabel));
    }

    public static TopologyPotentialClass Classify(ElectricalProject project, ElectricalConnection connection)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(connection);

        var net = string.IsNullOrWhiteSpace(connection.NetId)
            ? null
            : project.Nets.FirstOrDefault(item =>
                string.Equals(item.NetId, connection.NetId, StringComparison.OrdinalIgnoreCase));

        var netClass = ClassifyGroundReference(net?.GroundReferenceType ?? GroundReferenceType.None);
        if (netClass != TopologyPotentialClass.Unknown) return netClass;

        var connectedEndpoints = TopologyElectricalContinuity.ConnectedEndpoints(project, connection);
        var pins = ResolveEndpointPins(project, connectedEndpoints).ToArray();
        var structuredValues = pins.Select(ClassifyPin)
            .Where(value => value != TopologyPotentialClass.Unknown)
            .Distinct()
            .ToArray();
        if (structuredValues.Length > 0)
            return structuredValues.Length == 1 ? structuredValues[0] : TopologyPotentialClass.Unknown;

        var labels = new List<string?> { net?.Label, net?.NetId };
        foreach (var connected in project.Connections.Where(item =>
                     connectedEndpoints.Contains(item.FromEndpointId) &&
                     connectedEndpoints.Contains(item.ToEndpointId) &&
                     !string.IsNullOrWhiteSpace(item.NetId)))
        {
            var connectedNet = project.Nets.FirstOrDefault(item =>
                string.Equals(item.NetId, connected.NetId, StringComparison.OrdinalIgnoreCase));
            labels.Add(connectedNet?.Label);
            labels.Add(connectedNet?.NetId);
        }
        foreach (var endpointId in connectedEndpoints)
            labels.AddRange(ResolveEndpointLabels(project, endpointId));
        return ResolveConsensus(labels.Select(ClassifyLabel));
    }

    private static IEnumerable<ComponentPin> ResolveEndpointPins(
        ElectricalProject project,
        IReadOnlySet<string> endpointIds) =>
        project.Components.SelectMany(component => component.Ports)
            .SelectMany(port => port.Pins)
            .Where(pin => endpointIds.Contains(pin.PinId));

    private static IEnumerable<string?> ResolveEndpointLabels(ElectricalProject project, string endpointId)
    {
        foreach (var component in project.Components)
        foreach (var port in component.Ports)
        {
            if (string.Equals(port.PortId, endpointId, StringComparison.OrdinalIgnoreCase))
            {
                yield return port.Name;
                yield break;
            }

            var pin = port.Pins.FirstOrDefault(item =>
                string.Equals(item.PinId, endpointId, StringComparison.OrdinalIgnoreCase));
            if (pin is null) continue;
            yield return pin.PinName;
            yield return pin.Function;
            yield return pin.SignalStandardRaw;
            yield return port.Name;
            yield break;
        }

        foreach (var block in project.TerminalBlocks)
        {
            var ownsEndpoint = block.Positions.SelectMany(position => position.Levels)
                .SelectMany(level => level.ConnectionPoints)
                .Any(point => string.Equals(point.ConnectionPointId, endpointId, StringComparison.OrdinalIgnoreCase));
            if (!ownsEndpoint) continue;
            yield return block.FunctionTag;
            yield return block.DisplayName;
            yield break;
        }
    }

    private static TopologyPotentialClass ClassifyPin(ComponentPin pin)
    {
        var ground = ClassifyGroundReference(pin.GroundReferenceType);
        if (ground != TopologyPotentialClass.Unknown) return ground;
        return pin.Power?.Polarity switch
        {
            Polarity.Positive => TopologyPotentialClass.PositiveDc,
            Polarity.Negative or Polarity.Return => TopologyPotentialClass.NegativeOrReturnDc,
            _ => TopologyPotentialClass.Unknown
        };
    }

    private static TopologyPotentialClass ClassifyGroundReference(GroundReferenceType value) => value switch
    {
        GroundReferenceType.ProtectiveEarth or GroundReferenceType.FunctionalEarth =>
            TopologyPotentialClass.ProtectiveOrFunctionalEarth,
        GroundReferenceType.PowerReturn => TopologyPotentialClass.NegativeOrReturnDc,
        _ => TopologyPotentialClass.Unknown
    };

    private static TopologyPotentialClass ClassifyLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return TopologyPotentialClass.Unknown;
        var compact = new string(value.Trim().ToUpperInvariant()
            .Where(character => char.IsLetterOrDigit(character) || character is '+' or '-')
            .ToArray());

        if (compact is "PE" or "FG" or "PROTECTIVEEARTH" or "FUNCTIONALEARTH" ||
            compact.Contains("PROTECTIVEEARTH", StringComparison.Ordinal) ||
            compact.Contains("FUNCTIONALEARTH", StringComparison.Ordinal))
            return TopologyPotentialClass.ProtectiveOrFunctionalEarth;

        if (compact is "0V" or "V-" or "-V" or "DC-" or "GND" or "COM" or "COMMON" ||
            compact.StartsWith("COMBLACK", StringComparison.Ordinal) ||
            compact.Contains("SIGNALGROUND", StringComparison.Ordinal) ||
            compact.Contains("RTN", StringComparison.Ordinal) ||
            compact.Contains("RETURN", StringComparison.Ordinal) ||
            compact.Contains("0V", StringComparison.Ordinal) ||
            compact.Contains("MINUS", StringComparison.Ordinal) ||
            compact.Contains("V-", StringComparison.Ordinal) ||
            compact.Contains("-V", StringComparison.Ordinal))
            return TopologyPotentialClass.NegativeOrReturnDc;

        if (compact is "V+" or "+V" or "DC+" ||
            (compact.StartsWith('+') && compact.EndsWith('V')) ||
            (compact.EndsWith("V+", StringComparison.Ordinal) && compact.Any(char.IsDigit)) ||
            compact.Contains("PLUS", StringComparison.Ordinal) ||
            compact.Contains("V+", StringComparison.Ordinal) ||
            compact.Contains("+V", StringComparison.Ordinal) ||
            compact.Contains("+24V", StringComparison.Ordinal) ||
            compact.Contains("24V+", StringComparison.Ordinal))
            return TopologyPotentialClass.PositiveDc;

        return TopologyPotentialClass.Unknown;
    }

    private static TopologyPotentialClass ResolveConsensus(IEnumerable<TopologyPotentialClass> source)
    {
        var values = source.Where(value => value != TopologyPotentialClass.Unknown).Distinct().ToArray();
        return values.Length == 1 ? values[0] : TopologyPotentialClass.Unknown;
    }
}
