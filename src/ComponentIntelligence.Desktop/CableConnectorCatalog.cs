using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Desktop;

internal static class CableConnectorCatalog
{
    public static IReadOnlyList<CableConnectorChoice> Choices(IReadOnlyList<ComponentIR> catalog)
    {
        var choices = new List<CableConnectorChoice> { new("自訂／待確認", null),
            new("散線端", new() { PortId = "loose", Name = "Loose leads", Connector = new() { ConnectorId = "loose", Family = "Loose leads" } }) };
        var bridge = new ComponentProjectBridge();
        foreach (var component in catalog)
        {
            var instance = bridge.CreateInstance(component, "connector-choice-" + component.Identity.ComponentId);
            foreach (var port in instance.Ports.Where(p => p.Connector is not null))
            {
                var connector = port.Connector!;
                choices.Add(new($"{component.Identity.Model} / {port.Name} / {connector.Family} {connector.Coding} {connector.PinCount}P {connector.Gender}".Trim(), port));
            }
        }
        return choices.DistinctBy(c => c.Label, StringComparer.Ordinal).ToArray();
    }
}
