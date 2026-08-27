namespace ComponentIntelligence.Electrical.Validation;

/// <summary>
/// Platform-neutral preflight contract for staging a confirmed electrical topology for AutoCAD
/// review. It deliberately consumes topology supplied by the responsible engineering team and
/// never creates connections, pin/core mappings, shielding paths, or wire numbers.
/// </summary>
public sealed class AutocadExportPreflightService
{
    public AutocadExportPreflightReport Evaluate(AutocadExportPreflightRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<AutocadExportPreflightIssue>();
        var endpointGroups = request.ConfirmedEndpoints
            .GroupBy(endpoint => endpoint.EndpointId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var endpoints = endpointGroups
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var duplicate in endpointGroups.Where(group => group.Count() > 1))
        {
            issues.Add(Error(AutocadExportPreflightIssueCode.DuplicateEndpointId,
                $"Confirmed topology endpoint ID '{duplicate.Key}' is not unique.", duplicate.Key));
        }

        foreach (var endpoint in endpoints.Values)
        {
            if (string.IsNullOrWhiteSpace(endpoint.EndpointId))
                issues.Add(Error(AutocadExportPreflightIssueCode.UnresolvedPinEndpoint,
                    "Confirmed topology endpoint ID is missing."));

            if (string.IsNullOrWhiteSpace(endpoint.MachineNetIdentity))
                issues.Add(Error(AutocadExportPreflightIssueCode.MissingMachineNetIdentity,
                    $"Endpoint '{endpoint.EndpointId}' has no stable machine net identity.", endpoint.EndpointId));

            if (!endpoint.IsResolved && !endpoint.AllowsFieldBoundary)
                issues.Add(Error(AutocadExportPreflightIssueCode.UnresolvedPinEndpoint,
                    $"Confirmed topology endpoint '{endpoint.EndpointId}' is unresolved.", endpoint.EndpointId));

            if (endpoint.IsResolved && !endpoint.HasSymbolConnectionPoint)
                issues.Add(Error(AutocadExportPreflightIssueCode.SymbolConnectionPointMissing,
                    $"Endpoint '{endpoint.EndpointId}' has no audited symbol connection point.", endpoint.EndpointId));
        }

        var edgeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in request.ConfirmedEdges)
        {
            if (string.IsNullOrWhiteSpace(edge.EdgeId) || !edgeIds.Add(edge.EdgeId))
            {
                issues.Add(Error(AutocadExportPreflightIssueCode.DuplicateEdgeId,
                    $"Confirmed topology edge ID '{edge.EdgeId}' is missing or not unique.", edge.EdgeId));
            }

            if (!endpoints.ContainsKey(edge.FromEndpointId) || !endpoints.ContainsKey(edge.ToEndpointId))
            {
                issues.Add(Error(AutocadExportPreflightIssueCode.UnresolvedPinEndpoint,
                    $"Confirmed topology edge '{edge.EdgeId}' references an unresolved endpoint.",
                    edge.EdgeId, edge.FromEndpointId, edge.ToEndpointId));
            }

            if (!edge.IsContinuous)
            {
                issues.Add(Error(AutocadExportPreflightIssueCode.TopologyDiscontinuity,
                    $"Confirmed topology edge '{edge.EdgeId}' is discontinuous.",
                    edge.EdgeId, edge.FromEndpointId, edge.ToEndpointId));
            }
        }

        foreach (var openItem in request.OpenItems)
            issues.Add(ToIssue(openItem));
        issues.AddRange(request.AdditionalIssues);

        var labels = ResolveVisibleLabels(endpoints.Values);
        return new AutocadExportPreflightReport
        {
            Issues = issues,
            NetLabels = labels,
            CanStageForReview = !issues.Any(issue => issue.Severity == AutocadExportPreflightSeverity.Error)
        };
    }

    /// <summary>
    /// Produces an export-facing label without changing the stable machine net identity. Signal
    /// wins over potential; a stable Wxx sequence is used only when neither is available.
    /// </summary>
    public static IReadOnlyList<AutocadExportNetLabel> ResolveVisibleLabels(
        IEnumerable<AutocadExportPreflightEndpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var candidates = endpoints
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.MachineNetIdentity))
            .GroupBy(endpoint => endpoint.MachineNetIdentity.Trim(), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new
            {
                MachineNetIdentity = group.Key,
                BaseLabel = FirstStableLabel(group.Select(endpoint => endpoint.TopologySignal))
                    ?? FirstStableLabel(group.Select(endpoint => endpoint.TopologyPotential))
            })
            .ToArray();
        var fallbackIndex = 0;
        var usedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var suffixes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var labels = new List<AutocadExportNetLabel>(candidates.Length);

        foreach (var candidate in candidates)
        {
            string visibleLabel;
            if (candidate.BaseLabel is null)
            {
                do
                {
                    visibleLabel = $"W{++fallbackIndex:D2}";
                }
                while (!usedLabels.Add(visibleLabel));
            }
            else if (usedLabels.Add(candidate.BaseLabel))
            {
                visibleLabel = candidate.BaseLabel;
            }
            else
            {
                var suffix = suffixes.TryGetValue(candidate.BaseLabel, out var current) ? current + 1 : 2;
                do
                {
                    visibleLabel = $"{candidate.BaseLabel}-{suffix:D2}";
                    suffix++;
                }
                while (!usedLabels.Add(visibleLabel));

                suffixes[candidate.BaseLabel] = suffix - 1;
            }

            labels.Add(new AutocadExportNetLabel
            {
                MachineNetIdentity = candidate.MachineNetIdentity,
                VisibleLabel = visibleLabel
            });
        }

        return labels;
    }

    private static string? FirstStableLabel(IEnumerable<string?> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!.Trim())
        .OrderBy(value => value, StringComparer.Ordinal)
        .FirstOrDefault();

    private static AutocadExportPreflightIssue ToIssue(AutocadExportPreflightOpenItem openItem) => openItem.Kind switch
    {
        AutocadExportPreflightOpenItemKind.ShieldTerminationTbd => Warning(
            AutocadExportPreflightIssueCode.ShieldTerminationTbd,
            "Shield termination is TBD; no grounding strategy is inferred.", openItem.ItemId),
        AutocadExportPreflightOpenItemKind.CableLengthTbd => Warning(
            AutocadExportPreflightIssueCode.CableLengthTbd,
            "Cable length is TBD; no installed length is inferred from topology geometry.", openItem.ItemId),
        AutocadExportPreflightOpenItemKind.PowerTeamResponsibilityBoundary => Warning(
            AutocadExportPreflightIssueCode.PowerTeamResponsibilityBoundary,
            "Power Team responsibility boundary is open; no power wiring is derived beyond it.", openItem.ItemId),
        AutocadExportPreflightOpenItemKind.ProcurementTbd => Info(
            AutocadExportPreflightIssueCode.ProcurementTbd,
            "Procurement data is TBD and does not affect confirmed wiring correctness.", openItem.ItemId),
        AutocadExportPreflightOpenItemKind.BomTbd => Info(
            AutocadExportPreflightIssueCode.BomTbd,
            "BOM data is TBD and does not affect confirmed wiring correctness.", openItem.ItemId),
        AutocadExportPreflightOpenItemKind.LayoutTbd => Info(
            AutocadExportPreflightIssueCode.LayoutTbd,
            "Layout data is TBD and does not affect confirmed wiring correctness.", openItem.ItemId),
        _ => throw new ArgumentOutOfRangeException(nameof(openItem.Kind), openItem.Kind, "Unknown preflight open-item kind.")
    };

    private static AutocadExportPreflightIssue Error(
        AutocadExportPreflightIssueCode code,
        string message,
        params string[] sourceIds) => CreateIssue(code, AutocadExportPreflightSeverity.Error, message, sourceIds);

    private static AutocadExportPreflightIssue Warning(
        AutocadExportPreflightIssueCode code,
        string message,
        params string[] sourceIds) => CreateIssue(code, AutocadExportPreflightSeverity.Warning, message, sourceIds);

    private static AutocadExportPreflightIssue Info(
        AutocadExportPreflightIssueCode code,
        string message,
        params string[] sourceIds) => CreateIssue(code, AutocadExportPreflightSeverity.Info, message, sourceIds);

    private static AutocadExportPreflightIssue CreateIssue(
        AutocadExportPreflightIssueCode code,
        AutocadExportPreflightSeverity severity,
        string message,
        IEnumerable<string> sourceIds) => new()
    {
        Code = code,
        Severity = severity,
        Message = message,
        SourceObjectIds = sourceIds.Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
    };
}

public enum AutocadExportPreflightSeverity
{
    Info,
    Warning,
    Error
}

public enum AutocadExportPreflightIssueCode
{
    UnresolvedPinEndpoint,
    DuplicateEndpointId,
    DuplicateEdgeId,
    MissingMachineNetIdentity,
    SymbolConnectionPointMissing,
    TopologyDiscontinuity,
    ShieldTerminationTbd,
    CableLengthTbd,
    PowerTeamResponsibilityBoundary,
    ProcurementTbd,
    BomTbd,
    LayoutTbd,
    PortLevelEndpoint,
    UnprovenPhysicalSegment,
    UnknownComponentDrawingRole,
    DuplicateComponentDrawingRole,
    PowerFlowOrientationUnknown,
    InvalidPowerFlowOrientationEvidence,
    InvalidPageArchetypeEvidence,
    InvalidCrossPageContinuation,
    DuplicateCableInstanceOverride,
    InvalidCableInstanceOverride,
    ConflictingExplicitNetIdentity
}

public enum AutocadExportPreflightOpenItemKind
{
    ShieldTerminationTbd,
    CableLengthTbd,
    PowerTeamResponsibilityBoundary,
    ProcurementTbd,
    BomTbd,
    LayoutTbd
}

public sealed record AutocadExportPreflightRequest
{
    public List<AutocadExportPreflightEndpoint> ConfirmedEndpoints { get; init; } = new();
    public List<AutocadExportPreflightEdge> ConfirmedEdges { get; init; } = new();
    public List<AutocadExportPreflightOpenItem> OpenItems { get; init; } = new();
    /// <summary>Builder-owned errors that concern the source topology rather than user-declared TBD work.</summary>
    public List<AutocadExportPreflightIssue> AdditionalIssues { get; init; } = new();
}

/// <summary>
/// An endpoint already confirmed by its owning topology team. The preflight service will not
/// populate or repair this record.
/// </summary>
public sealed record AutocadExportPreflightEndpoint
{
    public required string EndpointId { get; init; }
    public required string MachineNetIdentity { get; init; }
    public string? TopologySignal { get; init; }
    public string? TopologyPotential { get; init; }
    public bool IsResolved { get; init; } = true;
    public bool HasSymbolConnectionPoint { get; init; } = true;
    /// <summary>
    /// This known source endpoint may appear only as a FieldBoundary placeholder. It must not be
    /// converted to a pin, core, symbol connection point, or drawable conductor by preflight.
    /// </summary>
    public bool AllowsFieldBoundary { get; init; }
    /// <summary>
    /// Legacy input metadata retained for compatibility. It never authorizes staging when an
    /// audited ACADE symbol connection point is missing.
    /// </summary>
    public bool AllowsUndrawnConfirmedTopology { get; init; }
}

/// <summary>
/// A confirmed edge. The service validates it as provided and never expands it into field wiring.
/// </summary>
public sealed record AutocadExportPreflightEdge
{
    public required string EdgeId { get; init; }
    public required string FromEndpointId { get; init; }
    public required string ToEndpointId { get; init; }
    public bool IsContinuous { get; init; } = true;
}

public sealed record AutocadExportPreflightOpenItem
{
    public required string ItemId { get; init; }
    public required AutocadExportPreflightOpenItemKind Kind { get; init; }
}

public sealed record AutocadExportPreflightIssue
{
    public required AutocadExportPreflightIssueCode Code { get; init; }
    public required AutocadExportPreflightSeverity Severity { get; init; }
    public required string Message { get; init; }
    public required IReadOnlyList<string> SourceObjectIds { get; init; }
}

public sealed record AutocadExportPreflightReport
{
    public required IReadOnlyList<AutocadExportPreflightIssue> Issues { get; init; }
    public required IReadOnlyList<AutocadExportNetLabel> NetLabels { get; init; }
    public required bool CanStageForReview { get; init; }
}

/// <summary>
/// The immutable machine identity for joins and traceability, paired with the human-visible
/// topology label selected for the drawing. These values must not be conflated.
/// </summary>
public sealed record AutocadExportNetLabel
{
    public required string MachineNetIdentity { get; init; }
    public required string VisibleLabel { get; init; }
}
