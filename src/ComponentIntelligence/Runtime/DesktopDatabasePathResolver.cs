namespace ComponentIntelligence.Runtime;

public static class DesktopDatabasePathResolver
{
    public const string EnvironmentVariableName = "COMPONENT_INTELLIGENCE_DB_PATH";

    public static string Resolve() => Resolve(
        Environment.GetEnvironmentVariable(EnvironmentVariableName),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static string Resolve(string? configuredPath, string localApplicationDataPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.Combine(
                localApplicationDataPath,
                "ComponentIntelligence",
                "component-intelligence.db");
        }

        var candidate = configuredPath.Trim();
        if (!Path.IsPathFullyQualified(candidate))
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariableName} must be an absolute SQLite database path. Configured value: '{candidate}'.");
        }

        try
        {
            return Path.GetFullPath(candidate);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariableName} is not a valid absolute SQLite database path.",
                exception);
        }
    }
}
