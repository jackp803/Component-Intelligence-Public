using System.Text.Json;

namespace ComponentIntelligence.Archive;

public sealed class ArchiveValidationJsonRunner
{
    private readonly ArchiveChangeSetValidator _validator = new();

    public async Task<(int ExitCode, string Json)> ValidateFileAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return ToolFailure(path, "Changeset file does not exist.");

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var changeSet = JsonSerializer.Deserialize<ArchiveChangeSet>(json, ArchiveJson.CreateOptions());
            if (changeSet is null)
                return ToolFailure(path, "Changeset JSON did not produce an object.");

            var report = _validator.Validate(changeSet);
            var exitCode = report.Status switch
            {
                ArchiveValidationStatus.PASS => 0,
                ArchiveValidationStatus.REVIEW_REQUIRED => 2,
                ArchiveValidationStatus.REJECT => 3,
                _ => 64
            };
            return (exitCode, JsonSerializer.Serialize(report, ArchiveJson.CreateOptions(true)));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return ToolFailure(path, exception.Message);
        }
    }

    private static (int ExitCode, string Json) ToolFailure(string? path, string message)
    {
        var report = new ArchiveValidationReport
        {
            JobId = string.IsNullOrWhiteSpace(path) ? "INPUT" : Path.GetFileName(path),
            Status = ArchiveValidationStatus.TOOL_FAILURE,
            Issues =
            [
                new ArchiveValidationIssue
                {
                    RuleId = "ARCHIVE-INPUT-001",
                    Severity = ArchiveValidationSeverity.ERROR,
                    Message = message,
                    ObjectIds = string.IsNullOrWhiteSpace(path) ? Array.Empty<string>() : new[] { path }
                }
            ]
        };
        return (64, JsonSerializer.Serialize(report, ArchiveJson.CreateOptions(true)));
    }
}
