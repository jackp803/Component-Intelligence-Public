using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Editing;

/// <summary>Project-local interface authority, never a catalog identity or an archive approval.</summary>
public static class InlineInterfaceRepresentation
{
    private static readonly HashSet<string> Definitions = new(StringComparer.Ordinal)
    {
        "common:cable-end:m12-female-a-4pin",
        "common:cable-end:m12-male-a-4pin",
        "common:cable-end:rj45-male-8p8c",
        "common:cat6-rj45-female-female-shielded-8c-coupler",
        "inline-mated-adapter:M12 female cable end (3 cores)",
        "inline-mated-adapter:M12 male cable end (3 cores)"
    };

    public static bool IsRecognized(string definitionId) => Definitions.Contains(definitionId);

    public static string? BlockingReason(ElectricalProject project, ComponentInstance instance)
    {
        if (!IsRecognized(instance.ComponentDefinitionId)) return "Not an authorized inline interface.";
        if (instance.Ports.Count == 0 || !instance.Ports.Any(p => p.Connector is not null))
            return "Missing explicit project connector evidence.";
        foreach (var port in instance.Ports)
        {
            if (port.Connector is { } connector)
            {
                if (string.IsNullOrWhiteSpace(connector.Family) || connector.PinCount is null or <= 0 || port.Pins.Count > connector.PinCount)
                    return "Missing or inconsistent explicit interface/contact evidence.";
            }
            else if (port.Pins.Count == 0) return "Non-connector interface has no explicit contacts.";
        }

        var counts = project.Components.SelectMany(c => c.Ports.SelectMany(p => new[] { p.PortId }.Concat(p.Pins.Select(x => x.PinId))))
            .Concat(project.TerminalBlocks.SelectMany(b => b.Positions.SelectMany(p => p.Levels.SelectMany(l => l.ConnectionPoints.Select(c => c.ConnectionPointId)))))
            .GroupBy(id => id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var own = instance.Ports.SelectMany(p => new[] { p.PortId }.Concat(p.Pins.Select(x => x.PinId)));
        var used = project.Connections.SelectMany(c => new[] { c.FromEndpointId, c.ToEndpointId })
            .Concat(project.Cables.SelectMany(c => c.CoreAssignments.SelectMany(a => new[] { a.FromEndpointId, a.ToEndpointId })).Where(id => id is not null)!);
        // Unowned broken endpoints cannot safely be assigned to a component by parsing their IDs.
        if (own.Concat(used).Any(id => string.IsNullOrWhiteSpace(id) || !counts.TryGetValue(id, out var count) || count != 1))
            return "Missing or non-unique explicit project endpoint; no inferred identity mapping is allowed.";
        return null;
    }
}
