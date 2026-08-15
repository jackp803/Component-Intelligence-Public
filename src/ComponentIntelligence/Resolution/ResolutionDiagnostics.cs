namespace ComponentIntelligence.Resolution;

public static class ResolutionDiagnostics
{
    public const string MissingIdentity = "MISSING_IDENTITY";
    public const string PlaceholderIdentity = "PLACEHOLDER_IDENTITY";
    public const string CustomComponent = "CUSTOM_COMPONENT_MANUAL_DATA_REQUIRED";
    public const string UnsupportedManufacturer = "UNSUPPORTED_MANUFACTURER";
    public const string SearchFailed = "SEARCH_FAILED";
    public const string ProductNotFound = "PRODUCT_NOT_FOUND";
    public const string AmbiguousCandidates = "AMBIGUOUS_CANDIDATES";
    public const string LocalRepositoryHit = "LOCAL_REPOSITORY_HIT";

    public static string WithValue(string code, string? value) =>
        string.IsNullOrWhiteSpace(value) ? code : $"{code}:{value}";
}
