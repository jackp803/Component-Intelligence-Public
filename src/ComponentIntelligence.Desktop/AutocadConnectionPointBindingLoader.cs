using System.IO;
using System.Text.Json;
using ComponentIntelligence.Electrical.Export;

namespace ComponentIntelligence.Desktop;

/// <summary>
/// Reads the engineer-audited connection-point sidecar without creating, repairing, or changing it.
/// </summary>
public static class AutocadConnectionPointBindingLoader
{
    public const string SchemaVersion = "ci-acade-connection-points.v1";
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ComponentIntelligence",
        "autocad-connection-points.json");

    public static AutocadConnectionPointBindingLoadResult Load(string? path = null)
    {
        var sidecarPath = path ?? DefaultPath;
        if (!File.Exists(sidecarPath))
            return new AutocadConnectionPointBindingLoadResult(
                sidecarPath,
                [],
                [new AutocadReviewIssue(
                    "Error",
                    "AuditedBindingsSidecarMissing",
                    $"Audited AutoCAD connection-point sidecar was not found. No ACADE graph may be generated until every required connection point is audited: {sidecarPath}",
                    [])]);

        try
        {
            var document = JsonSerializer.Deserialize<SidecarDocument>(File.ReadAllText(sidecarPath), JsonOptions);
            if (document is null || !string.Equals(document.SchemaVersion, SchemaVersion, StringComparison.Ordinal) || document.Bindings is null)
                return Failure(sidecarPath, "AuditedBindingsSidecarInvalid",
                    $"Audited AutoCAD connection-point sidecar must use schema '{SchemaVersion}' and include a bindings array.");

            var bindings = new List<AutocadConnectionPointBinding>(document.Bindings.Count);
            var endpointIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in document.Bindings)
            {
                if (binding is null || string.IsNullOrWhiteSpace(binding.EndpointId) ||
                    string.IsNullOrWhiteSpace(binding.SymbolKey) || string.IsNullOrWhiteSpace(binding.ConnectionPointId))
                {
                    return Failure(sidecarPath, "AuditedBindingsSidecarInvalid",
                        "Each audited connection-point binding requires endpointId, symbolKey, and connectionPointId.");
                }

                var endpointId = binding.EndpointId.Trim();
                if (!endpointIds.Add(endpointId))
                    return Failure(sidecarPath, "AuditedBindingsDuplicate",
                        $"Audited AutoCAD connection-point sidecar has duplicate endpointId '{endpointId}'.", endpointId);

                bindings.Add(new AutocadConnectionPointBinding
                {
                    EndpointId = endpointId,
                    SymbolKey = binding.SymbolKey.Trim(),
                    ConnectionPointId = binding.ConnectionPointId.Trim()
                });
            }

            return new AutocadConnectionPointBindingLoadResult(sidecarPath, bindings, []);
        }
        catch (JsonException exception)
        {
            return Failure(sidecarPath, "AuditedBindingsSidecarInvalid",
                $"Audited AutoCAD connection-point sidecar is not valid JSON: {exception.Message}");
        }
        catch (IOException exception)
        {
            return Failure(sidecarPath, "AuditedBindingsSidecarUnreadable",
                $"Audited AutoCAD connection-point sidecar could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure(sidecarPath, "AuditedBindingsSidecarUnreadable",
                $"Audited AutoCAD connection-point sidecar could not be read: {exception.Message}");
        }
    }

    private static AutocadConnectionPointBindingLoadResult Failure(string sidecarPath, string code, string message, params string[] sourceIds) =>
        new(sidecarPath, [], [new AutocadReviewIssue("Error", code, message, sourceIds)]);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record SidecarDocument(string? SchemaVersion, List<SidecarBinding?>? Bindings);
    private sealed record SidecarBinding(string? EndpointId, string? SymbolKey, string? ConnectionPointId);
}

public sealed record AutocadConnectionPointBindingLoadResult(
    string SidecarPath,
    IReadOnlyList<AutocadConnectionPointBinding> Bindings,
    IReadOnlyList<AutocadReviewIssue> Issues)
{
    public bool Succeeded => Issues.All(issue => !string.Equals(issue.Severity, "Error", StringComparison.Ordinal));
}

public sealed record AutocadReviewIssue(
    string Severity,
    string Code,
    string Message,
    IReadOnlyList<string> SourceIds);
