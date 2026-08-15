using ComponentIntelligence.Contracts;
using ComponentIntelligence.Resolution;

namespace ComponentIntelligence.Sources.Secondary;

internal static class SourceDomainTrustClassifier
{
    private static readonly IReadOnlyDictionary<string, string[]> ManufacturerDomains =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["IFM"] = ["ifm.com", "ifm.cn"],
            ["OMRON"] = ["omron.com", "omron.eu", "omron-ap.com"],
            ["WAGO"] = ["wago.com"],
            ["SCHNEIDER ELECTRIC"] = ["se.com", "schneider-electric.com"],
            ["MEAN WELL"] = ["meanwell.com"],
            ["MOXA"] = ["moxa.com"],
            ["FUJI ELECTRIC"] = ["fujielectric.com"],
            ["SIEMENS"] = ["siemens.com"],
            ["PHOENIX CONTACT"] = ["phoenixcontact.com"],
            ["PHOENIXCONTACT"] = ["phoenixcontact.com"],
            ["BECKHOFF"] = ["beckhoff.com"],
            ["SICK"] = ["sick.com"],
            ["KEYENCE"] = ["keyence.com"],
            ["BALLUFF"] = ["balluff.com"],
            ["FESTO"] = ["festo.com"],
            ["TI"] = ["ti.com"],
            ["TEXAS INSTRUMENTS"] = ["ti.com"],
            ["TEXASINSTRUMENTS"] = ["ti.com"],
            ["ST"] = ["st.com"],
            ["STMICROELECTRONICS"] = ["st.com"],
            ["ST MICROELECTRONICS"] = ["st.com"]
        };

    private static readonly string[] AuthorizedDistributorDomains =
    [
        "digikey.com", "digikey.tw", "digikey.ca",
        "mouser.com", "mouser.tw",
        "rs-online.com",
        "farnell.com", "element14.com", "newark.com",
        "tme.eu"
    ];

    private static readonly string[] TrustedAggregatorDomains =
    [
        "octopart.com", "findchips.com", "globalspec.com"
    ];

    public static ComponentSourceType Classify(
        string manufacturer,
        string host,
        string? documentType = null)
    {
        var normalizedHost = host.Trim('.').ToLowerInvariant();
        if (IsManufacturerHost(manufacturer, normalizedHost))
        {
            if (documentType?.Contains("datasheet", StringComparison.OrdinalIgnoreCase) == true)
                return ComponentSourceType.ManufacturerDatasheet;
            if (documentType?.Contains("manual", StringComparison.OrdinalIgnoreCase) == true ||
                documentType?.Contains("instruction", StringComparison.OrdinalIgnoreCase) == true)
                return ComponentSourceType.ManufacturerManual;
            return ComponentSourceType.ManufacturerDownloadCenter;
        }

        if (AuthorizedDistributorDomains.Any(domain => HostMatches(normalizedHost, domain)))
            return ComponentSourceType.AuthorizedDistributor;

        if (TrustedAggregatorDomains.Any(domain => HostMatches(normalizedHost, domain)))
            return ComponentSourceType.TrustedThirdParty;

        return ComponentSourceType.GenericWeb;
    }

    public static bool IsManufacturerHost(string manufacturer, string host)
    {
        var key = ManufacturerNormalizer.NormalizeKey(manufacturer) ?? Compact(manufacturer);
        if (ManufacturerDomains.TryGetValue(key, out var domains) &&
            domains.Any(domain => HostMatches(host, domain)))
            return true;

        var compactManufacturer = Compact(manufacturer).ToLowerInvariant();
        var compactHost = Compact(host).ToLowerInvariant();
        return compactManufacturer.Length >= 4 && compactHost.Contains(compactManufacturer, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HostMatches(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase);

    private static string Compact(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
