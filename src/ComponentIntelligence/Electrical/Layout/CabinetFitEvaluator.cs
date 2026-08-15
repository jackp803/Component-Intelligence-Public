using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Layout;

public enum CabinetFitStatus
{
    Fit,
    Review,
    NotFit
}

public sealed record CabinetFitReport
{
    public required CabinetFitStatus Status { get; init; }
    public required IReadOnlyList<LayoutIssue> Issues { get; init; }
    public int ClassifiedObjectCount { get; init; }
    public int UnclassifiedObjectCount { get; init; }
}

/// <summary>
/// Combines geometric 2.5D validation with coverage checks. A cabinet cannot be declared FIT while
/// in-scope electrical objects are still unclassified: each must either have a physical placement
/// or be explicitly classified as External. Unknown stays reviewable rather than being guessed.
/// </summary>
public sealed class CabinetFitEvaluator
{
    public CabinetFitReport Evaluate(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var issues = new List<LayoutIssue>(new PhysicalLayoutValidator().Validate(project));
        var classified = 0;
        var unclassified = 0;

        foreach (var component in project.Components.Where(IsInPhysicalScope))
        {
            if (component.Placement is null)
            {
                unclassified++;
                issues.Add(Review(component.ComponentInstanceId,
                    $"{component.ReferenceDesignator ?? component.DisplayName ?? component.ComponentInstanceId} has no Physical Placement（實體位置）classification. Place it on a cabinet surface or mark it External（箱外）."));
                continue;
            }

            classified++;
            if (component.Placement.Surface == MountingSurface.External) continue;
            if (component.Footprint is null || component.Footprint.WidthMm <= 0 || component.Footprint.HeightMm <= 0)
                issues.Add(Review(component.ComponentInstanceId,
                    $"{component.ReferenceDesignator ?? component.ComponentInstanceId} is assigned inside a cabinet but Width / Height（寬／高）is incomplete."));
        }

        foreach (var block in project.TerminalBlocks)
        {
            if (block.Placement is null)
            {
                unclassified++;
                issues.Add(Review(block.TerminalBlockId,
                    $"Terminal block {block.ReferenceDesignator} has no Physical Placement（實體位置）classification."));
                continue;
            }

            classified++;
            if (block.Placement.Surface == MountingSurface.External) continue;
            if (block.Footprint is null || block.Footprint.WidthMm <= 0 || block.Footprint.HeightMm <= 0)
                issues.Add(Review(block.TerminalBlockId,
                    $"Terminal block {block.ReferenceDesignator} is assigned inside a cabinet but physical dimensions are incomplete."));
        }

        var status = issues.Any(issue => issue.Severity == ValidationSeverity.Block)
            ? CabinetFitStatus.NotFit
            : issues.Count > 0 ? CabinetFitStatus.Review : CabinetFitStatus.Fit;

        return new CabinetFitReport
        {
            Status = status,
            Issues = issues,
            ClassifiedObjectCount = classified,
            UnclassifiedObjectCount = unclassified
        };
    }

    private static bool IsInPhysicalScope(ComponentInstance component) => component.ResponsibilityScope switch
    {
        ResponsibilityScope.OutOfScope or ResponsibilityScope.NotRequired => false,
        _ => true
    };

    private static LayoutIssue Review(string objectId, string message) => new()
    {
        RuleId = "RULE-LAYOUT-011",
        Severity = ValidationSeverity.Warning,
        ObjectId = objectId,
        Message = message,
        AffectsDrawingExport = false
    };
}
