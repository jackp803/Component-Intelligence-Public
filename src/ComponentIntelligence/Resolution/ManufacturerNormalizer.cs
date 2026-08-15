using System.Text;

namespace ComponentIntelligence.Resolution;

public static class ManufacturerNormalizer
{
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["IFM ELECTRONIC"] = "IFM",
        ["IFM ELECTRONIC GMBH"] = "IFM",
        ["OMRON CORPORATION"] = "OMRON",
        ["SCHNEIDER"] = "SCHNEIDER ELECTRIC",
        ["SCHNEIDER ELECTRIC"] = "SCHNEIDER ELECTRIC",
        ["MEANWELL"] = "MEAN WELL",
        ["MEAN WELL ENTERPRISES"] = "MEAN WELL",
        ["MEAN WELL ENTERPRISES CO., LTD."] = "MEAN WELL",
        ["WAGO KONTAKTTECHNIK"] = "WAGO",
        ["WAGO KONTAKTTECHNIK GMBH & CO. KG"] = "WAGO",
        ["MOXA INC"] = "MOXA",
        ["MOXA INC."] = "MOXA",
        ["FUJI"] = "FUJI ELECTRIC",
        ["FUJI ELECTRIC"] = "FUJI ELECTRIC"
    };

    public static string? NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        return Aliases.TryGetValue(normalized, out var canonical) ? canonical : normalized;
    }
}
