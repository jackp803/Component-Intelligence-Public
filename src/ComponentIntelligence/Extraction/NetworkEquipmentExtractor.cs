using System.Text.RegularExpressions;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Extraction;

/// <summary>
/// Deterministically converts network-interface facts found in product pages / datasheets into
/// ComponentPort objects. This is intentionally conservative: it only creates ports when a count and
/// interface family can be tied to network-specific wording. Unknown connector details remain null.
/// </summary>
public sealed class NetworkEquipmentExtractor
{
    private static readonly Regex CountFirstRj45 = new(
        @"(?<!\d)(?<count>\d{1,3})\s*(?:x|×)?\s*(?:[0-9./-]+\s*(?:base[- ]?[a-z0-9()]+)?\s*)?(?:ethernet\s*)?(?:rj-?45)\s*(?:ports?|connectors?|interfaces?)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CountFirstSfp = new(
        @"(?<!\d)(?<count>\d{1,3})\s*(?:x|×)?\s*(?<family>sfp\+?|sfp28)\s*(?:ports?|slots?|interfaces?)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CountFirstM12Ethernet = new(
        @"(?<!\d)(?<count>\d{1,3})\s*(?:x|×)?\s*(?:ethernet\s*)?m12(?:\s*(?<coding>[dx])-?cod(?:ed|ing))?\s*(?:ports?|connectors?|interfaces?)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LabelCount = new(
        @"^\s*(?<count>\d{1,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public NetworkEquipmentExtractionResult Extract(IEnumerable<RawSpecification> specifications)
    {
        var specs = specifications?
            .Where(spec => !string.IsNullOrWhiteSpace(spec.RawName) && !string.IsNullOrWhiteSpace(spec.RawValue))
            .ToArray() ?? Array.Empty<RawSpecification>();

        var groups = new List<NetworkPortGroup>();
        foreach (var spec in specs)
        {
            var text = Clean($"{spec.Section} {spec.RawName} {spec.RawValue}");
            if (!LooksNetworkRelated(text)) continue;

            AddRegexGroups(groups, CountFirstRj45, text, "RJ45", "Ethernet", "ETH");
            AddRegexGroups(groups, CountFirstSfp, text, null, "Ethernet", "SFP");
            AddM12Groups(groups, text);

            var label = Clean($"{spec.Section} {spec.RawName}");
            var value = spec.RawValue ?? string.Empty;
            var countMatch = LabelCount.Match(value);
            if (!countMatch.Success || !int.TryParse(countMatch.Groups["count"].Value, out var labelCount) || labelCount <= 0)
                continue;

            if (ContainsAny(label, "rj45", "rj-45"))
                groups.Add(new NetworkPortGroup("ETH", labelCount, "RJ45", "Ethernet", null));
            else if (ContainsAny(label, "sfp", "sfp+", "sfp28"))
                groups.Add(new NetworkPortGroup("SFP", labelCount, null, "Ethernet", null));
            else if (ContainsAny(label, "ethernet port", "network port", "lan port", "ethernet interface"))
                groups.Add(new NetworkPortGroup("ETH", labelCount, InferConnector(value), "Ethernet", null));
        }

        var normalizedGroups = groups
            .Where(group => group.Count is > 0 and <= 128)
            .GroupBy(group => $"{group.Prefix}\u001f{group.ConnectorFamily}\u001f{group.Protocol}\u001f{group.Coding}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Count).First())
            .ToArray();

        var ports = new List<ComponentPort>();
        foreach (var group in normalizedGroups)
        {
            for (var index = 1; index <= group.Count; index++)
            {
                ports.Add(new ComponentPort
                {
                    PortId = $"{group.Prefix}{index}",
                    PortType = "Network",
                    ConnectorFamily = group.ConnectorFamily,
                    SignalType = "Communication",
                    Direction = "Bidirectional",
                    Protocol = group.Protocol,
                    AllowedConnections = group.ConnectorFamily is null
                        ? Array.Empty<string>()
                        : [$"{group.ConnectorFamily}:{group.Protocol}"]
                });
            }
        }

        var expected = normalizedGroups.Sum(group => group.Count);
        return new NetworkEquipmentExtractionResult(
            ports
                .GroupBy(port => port.PortId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray(),
            expected,
            normalizedGroups.Length > 0);
    }

    private static void AddRegexGroups(
        List<NetworkPortGroup> groups,
        Regex regex,
        string? text,
        string? connector,
        string protocol,
        string prefix)
    {
        foreach (Match match in regex.Matches(text ?? string.Empty))
        {
            if (!int.TryParse(match.Groups["count"].Value, out var count)) continue;
            groups.Add(new NetworkPortGroup(prefix, count, connector, protocol, null));
        }
    }

    private static void AddM12Groups(List<NetworkPortGroup> groups, string text)
    {
        foreach (Match match in CountFirstM12Ethernet.Matches(text))
        {
            if (!int.TryParse(match.Groups["count"].Value, out var count)) continue;
            var coding = match.Groups["coding"].Success ? match.Groups["coding"].Value.ToUpperInvariant() : null;
            groups.Add(new NetworkPortGroup("ETH", count, "M12", "Ethernet", coding));
        }
    }

    private static string? InferConnector(string value)
    {
        if (ContainsAny(value, "rj45", "rj-45")) return "RJ45";
        if (ContainsAny(value, "m12")) return "M12";
        return null;
    }

    private static bool LooksNetworkRelated(string value) => ContainsAny(
        value,
        "ethernet", "network", "lan", "rj45", "rj-45", "sfp", "10/100", "100/1000", "1000base", "ethercat", "profinet", "ethernet/ip");

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static string Clean(string? value) => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();

    private sealed record NetworkPortGroup(
        string Prefix,
        int Count,
        string? ConnectorFamily,
        string Protocol,
        string? Coding);
}

public sealed record NetworkEquipmentExtractionResult(
    IReadOnlyList<ComponentPort> Ports,
    int ExpectedPortCount,
    bool NetworkEvidenceDetected);
