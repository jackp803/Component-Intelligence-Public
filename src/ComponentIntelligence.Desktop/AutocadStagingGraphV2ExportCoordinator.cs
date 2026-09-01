using System.IO;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Export;
using ComponentIntelligence.Electrical.Validation;

namespace ComponentIntelligence.Desktop;

/// <summary>
/// Loads existing audited drawing evidence and writes one inspectable v2 contract artifact.
/// This path does not validate launch-only symbol acceptance or start a drawing process.
/// </summary>
public sealed class AutocadStagingGraphV2ExportCoordinator
{
    public static string DefaultStagingRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ComponentIntelligence",
        "autocad-staging");

    private readonly string _stagingRoot;
    private readonly Func<string> _runDirectoryNameFactory;

    public AutocadStagingGraphV2ExportCoordinator()
        : this(DefaultStagingRoot, CreateRunDirectoryName)
    {
    }

    public AutocadStagingGraphV2ExportCoordinator(
        string stagingRoot,
        Func<string> runDirectoryNameFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);
        _stagingRoot = Path.GetFullPath(stagingRoot);
        _runDirectoryNameFactory = runDirectoryNameFactory
            ?? throw new ArgumentNullException(nameof(runDirectoryNameFactory));
    }

    public AutocadStagingGraphV2ExportCoordinatorResult Export(
        ElectricalProject project,
        string? bindingSidecarPath = null,
        string? drawingEvidenceSidecarPath = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var bindingLoad = AutocadConnectionPointBindingLoader.Load(bindingSidecarPath);
        var evidenceLoad = AutocadEngineeringDrawingEvidenceLoader.Load(project, drawingEvidenceSidecarPath);
        var issues = bindingLoad.Issues.Concat(evidenceLoad.Issues).ToList();
        if (!bindingLoad.Succeeded || !evidenceLoad.Succeeded)
            return Result(bindingLoad, evidenceLoad, issues);

        try
        {
            var runDirectory = Path.Combine(_stagingRoot, RequireRunDirectoryName());
            var export = new AutocadStagingGraphV2Exporter().PrepareAndWrite(
                project,
                bindingLoad.Bindings,
                evidenceLoad.Evidence,
                runDirectory);
            issues.AddRange(export.Preparation.Preflight.Issues.Select(ToReviewIssue));
            if (export.GraphPath is null || export.Preparation.Graph is null)
                return Result(bindingLoad, evidenceLoad, issues);

            return Result(
                bindingLoad,
                evidenceLoad,
                issues,
                runDirectory,
                export.GraphPath,
                export.Preparation.Graph.SchemaVersion,
                export.Preparation.Graph.ProjectId);
        }
        catch (Exception exception)
        {
            issues.Add(new AutocadReviewIssue(
                "Error",
                "AutocadV2ExportFailed",
                $"AutoCAD v2 contract export could not prepare a safe artifact: {exception.Message}",
                []));
            return Result(bindingLoad, evidenceLoad, issues);
        }
    }

    private string RequireRunDirectoryName()
    {
        var value = _runDirectoryNameFactory();
        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The v2 export run directory name is invalid.");
        }
        return value;
    }

    private static string CreateRunDirectoryName() =>
        $"{DateTime.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}";

    private static AutocadReviewIssue ToReviewIssue(AutocadExportPreflightIssue issue) => new(
        issue.Severity.ToString(),
        issue.Code.ToString(),
        issue.Message,
        issue.SourceObjectIds);

    private static AutocadStagingGraphV2ExportCoordinatorResult Result(
        AutocadConnectionPointBindingLoadResult bindingLoad,
        AutocadEngineeringDrawingEvidenceLoadResult evidenceLoad,
        IReadOnlyList<AutocadReviewIssue> issues,
        string? outputDirectory = null,
        string? graphPath = null,
        string? schemaVersion = null,
        string? projectId = null) => new(
            bindingLoad.SidecarPath,
            evidenceLoad.SidecarPath,
            issues,
            outputDirectory,
            graphPath,
            schemaVersion,
            projectId);
}

public sealed record AutocadStagingGraphV2ExportCoordinatorResult(
    string BindingSidecarPath,
    string DrawingEvidenceSidecarPath,
    IReadOnlyList<AutocadReviewIssue> Issues,
    string? OutputDirectory,
    string? GraphPath,
    string? SchemaVersion,
    string? ProjectId)
{
    public bool Succeeded => GraphPath is not null &&
                             Issues.All(issue =>
                                 !string.Equals(issue.Severity, "Error", StringComparison.Ordinal));
}
