using System.IO;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Export;
using ComponentIntelligence.Electrical.Validation;

namespace ComponentIntelligence.Desktop;

/// <summary>
/// Joins the three read-only engineer gates used by the one-click AutoCAD review action. This
/// coordinator prepares an in-memory graph only; it never serializes output or starts a process.
/// </summary>
public sealed class AutocadReviewPreflightCoordinator
{
    public AutocadReviewPreflightResult Prepare(
        ElectricalProject project,
        string? bindingSidecarPath = null,
        string? drawingEvidenceSidecarPath = null,
        string? symbolAcceptanceRegistryPath = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var bindingLoad = AutocadConnectionPointBindingLoader.Load(bindingSidecarPath);
        var evidenceLoad = AutocadEngineeringDrawingEvidenceLoader.Load(project, drawingEvidenceSidecarPath);
        var registryPath = string.IsNullOrWhiteSpace(symbolAcceptanceRegistryPath)
            ? AutocadStagingReviewRunner.ResolveSymbolAcceptanceRegistryPath()
            : Path.GetFullPath(symbolAcceptanceRegistryPath);
        var issues = bindingLoad.Issues.Concat(evidenceLoad.Issues).ToList();
        if (!File.Exists(registryPath))
            issues.Add(new AutocadReviewIssue(
                "Error",
                "SymbolAcceptanceRegistryMissing",
                $"Engineer-approved LRDU symbol acceptance registry was not found. No staging subprocess may start: {registryPath}",
                []));

        AutocadStagingGraphPreparationResult? preparation = null;
        if (bindingLoad.Succeeded && evidenceLoad.Succeeded)
        {
            try
            {
                preparation = new AutocadStagingGraphBuilder().Prepare(
                    project,
                    bindingLoad.Bindings,
                    evidenceLoad.Evidence);
                issues.AddRange(preparation.Preflight.Issues.Select(ToReviewIssue));
            }
            catch (Exception exception)
            {
                issues.Add(new AutocadReviewIssue(
                    "Error",
                    "AutocadPreflightFailed",
                    $"AutoCAD review preflight could not prepare a safe graph: {exception.Message}",
                    []));
            }
        }

        return new AutocadReviewPreflightResult(
            preparation,
            issues,
            bindingLoad.SidecarPath,
            evidenceLoad.SidecarPath,
            registryPath);
    }

    private static AutocadReviewIssue ToReviewIssue(AutocadExportPreflightIssue issue) => new(
        issue.Severity.ToString(),
        issue.Code.ToString(),
        issue.Message,
        issue.SourceObjectIds);
}

public sealed record AutocadReviewPreflightResult(
    AutocadStagingGraphPreparationResult? Preparation,
    IReadOnlyList<AutocadReviewIssue> Issues,
    string BindingSidecarPath,
    string DrawingEvidenceSidecarPath,
    string SymbolAcceptanceRegistryPath)
{
    public bool CanLaunch => Preparation?.Graph is not null &&
                             Issues.All(issue => !string.Equals(issue.Severity, "Error", StringComparison.Ordinal));
}
