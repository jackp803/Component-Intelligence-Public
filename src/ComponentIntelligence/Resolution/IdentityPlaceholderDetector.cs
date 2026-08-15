namespace ComponentIntelligence.Resolution;

public static class IdentityPlaceholderDetector
{
    private static readonly HashSet<string> PlaceholderManufacturers = new(StringComparer.OrdinalIgnoreCase)
    {
        "TBD", "UNKNOWN", "N/A", "NA", "TO BE DETERMINED"
    };

    public static bool TryGetReason(string? manufacturer, string? model, out string reason)
    {
        if (string.IsNullOrWhiteSpace(manufacturer) || string.IsNullOrWhiteSpace(model))
        {
            reason = ResolutionDiagnostics.MissingIdentity;
            return true;
        }

        var normalizedManufacturer = manufacturer.Trim();
        var normalizedModel = model.Trim();
        if (PlaceholderManufacturers.Contains(normalizedManufacturer) ||
            normalizedModel.Equals("TBD", StringComparison.OrdinalIgnoreCase) ||
            normalizedModel.StartsWith("TBD (", StringComparison.OrdinalIgnoreCase))
        {
            reason = ResolutionDiagnostics.PlaceholderIdentity;
            return true;
        }

        if (normalizedManufacturer.Equals("CUSTOM", StringComparison.OrdinalIgnoreCase) ||
            normalizedModel.Contains("PROJECT-SPECIFIC", StringComparison.OrdinalIgnoreCase))
        {
            reason = ResolutionDiagnostics.CustomComponent;
            return true;
        }

        reason = string.Empty;
        return false;
    }
}
