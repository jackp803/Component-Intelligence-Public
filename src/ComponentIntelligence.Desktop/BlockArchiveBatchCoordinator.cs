using ComponentIntelligence.Contracts;
using ComponentIntelligence.SymbolArchive;
using ComponentIntelligence.Cache;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComponentIntelligence.Desktop;

public sealed record BlockArchiveComponentChoice(string ComponentId, string DisplayName);

public sealed class BlockArchiveReviewRow
{
    internal BlockArchiveReviewRow(
        BlockArchiveCandidate candidate,
        IReadOnlyList<SymbolComponentMatch> suggestedComponents,
        IReadOnlyList<BlockArchiveComponentChoice> componentChoices)
    {
        Candidate = candidate;
        SuggestedComponents = suggestedComponents;
        ComponentChoices = componentChoices;
    }

    public BlockArchiveCandidate Candidate { get; internal set; }
    public IReadOnlyList<SymbolComponentMatch> SuggestedComponents { get; internal set; }
    public IReadOnlyList<BlockArchiveComponentChoice> ComponentChoices { get; }
    public IReadOnlyList<SymbolRole> AvailableRoles { get; } = Enum.GetValues<SymbolRole>();
    public IReadOnlyList<SymbolSourceType> ImportSourceTypes { get; } =
    [
        SymbolSourceType.ApprovedCustom,
        SymbolSourceType.Manufacturer,
        SymbolSourceType.LibraryStandard
    ];

    public string? SelectedComponentId { get; set; }
    public SymbolRole? SelectedRole { get; set; }
    public SymbolSourceType? SelectedSourceType { get; set; }
    public IReadOnlyList<SymbolPortBinding> PortBindings { get; set; } = [];
    public bool UserConfirmed { get; set; }
    public string? ApprovedRevision { get; internal set; }
    public string? ApprovedAssetPath { get; internal set; }
    public string? ApprovedSha256 { get; internal set; }
    public IReadOnlyList<string> DeepInspectionDiagnostics { get; internal set; } = [];

    public string SuggestedComponentDisplay => SuggestedComponents.Count == 0
        ? "No suggestion"
        : string.Join(" | ", SuggestedComponents.Take(3).Select(match => $"{match.ComponentId} ({match.Score})"));

    public string DuplicateDisplay => Candidate.ExactDuplicateRevision ?? string.Empty;

    public string ReviewStatus => ApprovedRevision is not null
        ? "Approved"
        : Candidate.SourceIntegrityFailed
            ? "BlockedIntegrity"
            : string.IsNullOrWhiteSpace(SelectedComponentId) || SelectedRole is null || SelectedSourceType is null
                ? "ReviewRequired"
                : !UserConfirmed
                    ? "PendingConfirmation"
                    : "ReadyToArchive";
}

public sealed class BlockArchiveBatchCoordinator
{
    private readonly IReadOnlyList<ComponentIR> _components;
    private readonly IReadOnlyDictionary<string, ComponentIR> _componentsById;
    private readonly IReadOnlyList<BlockArchiveComponentChoice> _componentChoices;
    private readonly SymbolArchiveRepository _repository;
    private readonly SymbolArchiveApprovalService _approvalService;
    private readonly BlockArchiveScanner _scanner;
    private readonly SymbolCandidateMatcher _matcher;
    private readonly IBlockDeepInspector _deepInspector;

    public BlockArchiveBatchCoordinator(
        string centralArchiveRootOrWorkbookPath,
        IReadOnlyList<ComponentIR> components,
        IBlockDeepInspector deepInspector,
        BlockArchiveScanner? scanner = null,
        SymbolCandidateMatcher? matcher = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(centralArchiveRootOrWorkbookPath);
        _components = components ?? throw new ArgumentNullException(nameof(components));
        _componentsById = components.ToDictionary(component => component.Identity.ComponentId, StringComparer.Ordinal);
        _componentChoices = components
            .OrderBy(component => component.Identity.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(component => component.Identity.Model, StringComparer.OrdinalIgnoreCase)
            .ThenBy(component => component.Identity.ComponentId, StringComparer.Ordinal)
            .Select(component => new BlockArchiveComponentChoice(
                component.Identity.ComponentId,
                $"{component.Identity.Manufacturer} {component.Identity.Model} [{component.Identity.ComponentId}]"))
            .ToArray();
        _repository = new SymbolArchiveRepository(centralArchiveRootOrWorkbookPath);
        _approvalService = new SymbolArchiveApprovalService(_repository, components);
        _scanner = scanner ?? new BlockArchiveScanner();
        _matcher = matcher ?? new SymbolCandidateMatcher();
        _deepInspector = deepInspector ?? throw new ArgumentNullException(nameof(deepInspector));
    }

    public string ArchiveRoot => _repository.ArchiveRoot;

    public string ReviewDraftPath => Path.Combine(ArchiveRoot, "BlockArchive.review-draft.json");
    private static readonly JsonSerializerOptions DraftJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private sealed record ReviewDraft(int Version, IReadOnlyList<ReviewDraftRow> Rows);
    private sealed record ReviewDraftRow(BlockArchiveCandidate Candidate, string? ComponentId,
        SymbolRole? Role, SymbolSourceType? SourceType, IReadOnlyList<SymbolPortBinding> Bindings,
        IReadOnlyList<string> Diagnostics);

    public async Task SaveReviewDraftAsync(IEnumerable<BlockArchiveReviewRow> rows, CancellationToken cancellationToken = default)
    {
        var snapshots = new List<ReviewDraftRow>();
        foreach (var row in rows)
        {
            var hash = await HashService.Sha256FileAsync(row.Candidate.SourcePath, cancellationToken);
            if (row.Candidate.SourceIntegrityFailed || !string.Equals(hash, row.Candidate.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Source changed: {row.Candidate.RelativePath}. Rescan before saving draft.");
            snapshots.Add(new(row.Candidate, row.SelectedComponentId, row.SelectedRole, row.SelectedSourceType,
                row.PortBindings.ToArray(), row.DeepInspectionDiagnostics.ToArray()));
        }
        if (snapshots.Count == 0) throw new InvalidOperationException("No candidates to save.");
        Directory.CreateDirectory(ArchiveRoot);
        var temp = ReviewDraftPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(new ReviewDraft(1, snapshots), DraftJson), cancellationToken);
            File.Move(temp, ReviewDraftPath, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public async Task<IReadOnlyList<BlockArchiveReviewRow>> LoadReviewDraftAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ReviewDraftPath)) return [];
        var draft = JsonSerializer.Deserialize<ReviewDraft>(await File.ReadAllTextAsync(ReviewDraftPath, cancellationToken), DraftJson)
            ?? throw new InvalidDataException("Empty review draft.");
        if (draft.Version != 1 || draft.Rows is null) throw new InvalidDataException("Unsupported review draft.");
        var result = new List<BlockArchiveReviewRow>();
        foreach (var saved in draft.Rows)
        {
            var changed = saved.Candidate.SourceIntegrityFailed;
            var diagnostics = saved.Diagnostics.ToList();
            try
            {
                var hash = await HashService.Sha256FileAsync(saved.Candidate.SourcePath, cancellationToken);
                changed |= !string.Equals(hash, saved.Candidate.Sha256, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                changed = true;
                diagnostics.Add("DRAFT_SOURCE_UNAVAILABLE: " + ex.Message);
            }
            if (changed) diagnostics.Add("DRAFT_SOURCE_INTEGRITY_REQUIRES_RESCAN");
            var candidate = saved.Candidate with { SourceIntegrityFailed = changed };
            result.Add(new BlockArchiveReviewRow(candidate, _matcher.Rank(candidate, _components), _componentChoices)
            {
                SelectedComponentId = saved.ComponentId,
                SelectedRole = saved.Role,
                SelectedSourceType = saved.SourceType,
                PortBindings = saved.Bindings,
                DeepInspectionDiagnostics = diagnostics,
                UserConfirmed = false
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<BlockArchiveReviewRow>> ScanAsync(
        string sourceRoot,
        CancellationToken cancellationToken = default)
    {
        var archive = _repository.Load();
        var candidates = await _scanner.ScanAsync(sourceRoot, archive, cancellationToken);
        return candidates.Select(candidate => new BlockArchiveReviewRow(
                candidate,
                _matcher.Rank(candidate, _components),
                _componentChoices))
            .ToArray();
    }

    public async Task<BlockDeepInspectionResult> DeepInspectAsync(
        BlockArchiveReviewRow row,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        var result = await _deepInspector.InspectAsync(row.Candidate, cancellationToken);
        row.Candidate = row.Candidate with
        {
            DeepInspectionStatus = result.Status,
            DeepMetadata = result.Metadata,
            SourceIntegrityFailed = row.Candidate.SourceIntegrityFailed || result.SourceIntegrityFailed
        };
        row.DeepInspectionDiagnostics = result.Diagnostics ?? [];
        row.SuggestedComponents = _matcher.Rank(row.Candidate, _components);
        return result;
    }

    public async Task<IReadOnlyList<SymbolApprovalResult>> ApproveSelectedAsync(
        IEnumerable<BlockArchiveReviewRow> selectedRows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedRows);
        var rows = selectedRows.Distinct().ToArray();
        if (rows.Length == 0) throw new InvalidOperationException("Select at least one Block Archive row to approve.");

        // Preflight the entire batch before any archive write so one incomplete row cannot create a partial approval batch.
        foreach (var row in rows) ValidateForApproval(row);

        var results = new List<SymbolApprovalResult>(rows.Length);
        foreach (var row in rows.OrderBy(item => item.Candidate.RelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new ApproveSymbolRequest
            {
                SourcePath = row.Candidate.SourcePath,
                ComponentId = row.SelectedComponentId!,
                Role = row.SelectedRole!.Value,
                SourceType = row.SelectedSourceType!.Value,
                PortBindings = row.PortBindings
                    .OrderBy(binding => binding.EngineeringEndpointId, StringComparer.Ordinal)
                    .ToArray(),
                UserConfirmed = row.UserConfirmed
            };
            var result = await _approvalService.ApproveAsync(request, cancellationToken);
            row.ApprovedRevision = result.Revision;
            row.ApprovedAssetPath = result.AssetPath;
            row.ApprovedSha256 = result.Sha256;
            results.Add(result);
        }
        return results;
    }

    public void ValidateForApproval(BlockArchiveReviewRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Candidate.SourceIntegrityFailed)
            throw new InvalidOperationException($"Source integrity failed for '{row.Candidate.RelativePath}'; rescan before approval.");
        if (string.IsNullOrWhiteSpace(row.SelectedComponentId) || !_componentsById.TryGetValue(row.SelectedComponentId.Trim(), out var component))
            throw new InvalidOperationException($"Select a valid Component for '{row.Candidate.RelativePath}'.");
        if (row.SelectedRole is null)
            throw new InvalidOperationException($"Select a Symbol Role for '{row.Candidate.RelativePath}'.");
        if (row.SelectedSourceType is null || row.SelectedSourceType == SymbolSourceType.GeneratedGeneric)
            throw new InvalidOperationException($"Select ApprovedCustom, Manufacturer, or LibraryStandard for file import '{row.Candidate.RelativePath}'.");
        if (!row.UserConfirmed)
            throw new InvalidOperationException($"Explicit user confirmation is required for '{row.Candidate.RelativePath}'.");

        var explicitEndpointIds = component.Ports
            .Where(port => !string.IsNullOrWhiteSpace(port.PortId))
            .Select(port => port.PortId.Trim())
            .Concat(component.Pins
                .Where(pin => !string.IsNullOrWhiteSpace(pin.PinId))
                .Select(pin => pin.PinId!.Trim()))
            .ToHashSet(StringComparer.Ordinal);
        var mapped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in row.PortBindings ?? [])
        {
            var endpoint = binding.EngineeringEndpointId?.Trim();
            if (string.IsNullOrWhiteSpace(endpoint) || !explicitEndpointIds.Contains(endpoint))
                throw new InvalidOperationException($"Endpoint '{binding.EngineeringEndpointId}' is not an explicit stable PortID/PinID on Component '{component.Identity.ComponentId}'.");
            if (!mapped.Add(endpoint))
                throw new InvalidOperationException($"Endpoint '{endpoint}' is mapped more than once.");
            if (string.IsNullOrWhiteSpace(binding.ConnectionPointId))
                throw new InvalidOperationException($"ConnectionPointId is required for endpoint '{endpoint}'.");
        }
    }
}
