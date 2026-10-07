using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Desktop;

internal static class CableConnectorCatalog
{
    public static IReadOnlyList<CableConnectorChoice> Choices(IReadOnlyList<ComponentIR> catalog)
    {
        return new[] { ("Custom", "自訂／待確認"), ("M8", "M8"), ("M12", "M12"), ("RJ45", "RJ45"),
            ("USB", "USB"), ("D-sub", "D-sub"), ("Heavy Duty", "Heavy Duty"),
            ("Terminal", "端子接頭"), ("Loose leads", "散線端") }
            .Select(f => new CableConnectorChoice(f.Item2, new() { PortId = "family-" + f.Item1, Name = f.Item1,
                Connector = new() { ConnectorId = "family-" + f.Item1, Family = f.Item1 } })).ToArray();
    }

    public static IReadOnlyList<string> CodingOptions(string? family) => string.Equals(family, "M12", StringComparison.OrdinalIgnoreCase)
        ? ["未確認", "A-code", "B-code", "D-code", "X-code", "K-code", "L-code", "M-code", "S-code", "T-code", "Y-code"]
        : ["未確認"];
}
