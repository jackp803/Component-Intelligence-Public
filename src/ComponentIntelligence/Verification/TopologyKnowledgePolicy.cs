using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Verification;

/// <summary>
/// Defines the minimum component knowledge required before Component Intelligence may treat a local
/// Component IR as sufficient for automatic Electrical Topology handoff.
///
/// Two valid topology shapes are supported:
/// 1) explicit physical/logical Ports, each with a connector and engineering role/protocol; or
/// 2) a connector with a known pin count and usable function for every required pin.
///
/// This lets devices such as Ethernet switches become topology-ready from ETH1..ETH8 port definitions
/// without pretending every RJ45 contact must be expanded before the network topology can be represented,
/// while sensor/field-device connectors still require pin-function coverage when pins are the connection model.
/// </summary>
public static class TopologyKnowledgePolicy
{
    public static TopologyKnowledgeAssessment Evaluate(ComponentIR component)
    {
        ArgumentNullException.ThrowIfNull(component);

        var explicitPorts = component.Ports
            .Where(port => !string.IsNullOrWhiteSpace(port.PortId))
            .GroupBy(port => port.PortId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(PortKnowledgeScore).First())
            .ToArray();

        var portsWithConnector = explicitPorts.Count(port => !string.IsNullOrWhiteSpace(port.ConnectorFamily));
        var portsWithRole = explicitPorts.Count(port =>
            !string.IsNullOrWhiteSpace(port.Protocol) ||
            !string.IsNullOrWhiteSpace(port.SignalType) ||
            !string.IsNullOrWhiteSpace(port.PortType));
        var explicitPortsReady = explicitPorts.Length > 0 &&
                                 portsWithConnector == explicitPorts.Length &&
                                 portsWithRole == explicitPorts.Length;

        var connectorKnown = !string.IsNullOrWhiteSpace(component.Connector.Family);
        var expectedPins = component.Connector.Pins.GetValueOrDefault();
        var distinctPins = component.Pins
            .Where(pin => !string.IsNullOrWhiteSpace(pin.PinNumber))
            .GroupBy(pin => pin.PinNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(pin => !string.IsNullOrWhiteSpace(pin.Function))
                .ThenByDescending(pin => SourceTrustPolicy.Score(pin.Evidence))
                .First())
            .ToArray();

        var detectedPins = distinctPins.Length;
        var knownFunctions = distinctPins.Count(pin => !string.IsNullOrWhiteSpace(pin.Function));
        var pinCountKnown = expectedPins > 0 || detectedPins > 0;
        var requiredPins = expectedPins > 0 ? expectedPins : detectedPins;
        var allExpectedPinsDetected = requiredPins > 0 && detectedPins >= requiredPins;
        var allDetectedFunctionsKnown = detectedPins > 0 && knownFunctions == detectedPins;
        var pinTopologyReady = connectorKnown && pinCountKnown && allExpectedPinsDetected && allDetectedFunctionsKnown;

        var issues = new List<string>();
        if (explicitPorts.Length > 0)
        {
            if (portsWithConnector < explicitPorts.Length)
                issues.Add($"TOPOLOGY_PORT_CONNECTOR_COVERAGE:{portsWithConnector}/{explicitPorts.Length}");
            if (portsWithRole < explicitPorts.Length)
                issues.Add($"TOPOLOGY_PORT_ROLE_COVERAGE:{portsWithRole}/{explicitPorts.Length}");
        }
        else
        {
            if (!connectorKnown) issues.Add("TOPOLOGY_MISSING_CONNECTOR_FAMILY");
            if (!pinCountKnown) issues.Add("TOPOLOGY_MISSING_PIN_COUNT");
            if (requiredPins > 0 && detectedPins < requiredPins)
                issues.Add($"TOPOLOGY_PIN_COVERAGE:{detectedPins}/{requiredPins}");
            if (detectedPins > 0 && knownFunctions < detectedPins)
                issues.Add($"TOPOLOGY_PIN_FUNCTION_COVERAGE:{knownFunctions}/{detectedPins}");
            if (detectedPins == 0) issues.Add("TOPOLOGY_MISSING_PINS");
        }

        var ready = explicitPortsReady || pinTopologyReady;
        var hasUsefulKnowledge = explicitPorts.Length > 0 || connectorKnown || detectedPins > 0 || component.Specifications.Count > 0;
        var status = ready
            ? ReadinessStatus.Ready
            : hasUsefulKnowledge
                ? ReadinessStatus.Partial
                : ReadinessStatus.NotReady;

        return new TopologyKnowledgeAssessment(
            status,
            expectedPins,
            detectedPins,
            knownFunctions,
            issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static int PortKnowledgeScore(ComponentPort port)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(port.PortType)) score++;
        if (!string.IsNullOrWhiteSpace(port.ConnectorFamily)) score++;
        if (!string.IsNullOrWhiteSpace(port.SignalType)) score++;
        if (!string.IsNullOrWhiteSpace(port.Direction)) score++;
        if (!string.IsNullOrWhiteSpace(port.VoltageDomain)) score++;
        if (!string.IsNullOrWhiteSpace(port.Protocol)) score++;
        score += port.AllowedConnections.Count;
        return score;
    }
}

public sealed record TopologyKnowledgeAssessment(
    ReadinessStatus Status,
    int ExpectedPins,
    int DetectedPins,
    int KnownPinFunctions,
    IReadOnlyList<string> Issues)
{
    public bool IsReady => Status == ReadinessStatus.Ready;
}
