using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Repository;

public sealed record ComponentSyncResult(
    bool LocalSaved,
    bool CentralAttempted,
    bool CentralSucceeded,
    ComponentSyncStatus Status,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> Conflicts)
{
    public bool HasConflict => Conflicts.Count > 0;
}

/// <summary>
/// Local-first manual knowledge synchronizer.
///
/// Rules:
/// - local SQLite is always saved first;
/// - central Notion is optional and failures never roll back valid local edits;
/// - an explicit conflict with Verified central Pin/Specification knowledge is not silently overwritten;
/// - pending/conflict state is persisted for later review/sync.
/// </summary>
public sealed class ComponentKnowledgeSyncService
{
    private readonly SqliteComponentIrRepository _local;
    private readonly IComponentKnowledgeStore _central;
    private readonly ComponentSyncStateRepository _state;

    public ComponentKnowledgeSyncService(
        string databasePath,
        IComponentKnowledgeStore central,
        SqliteComponentIrRepository? local = null,
        ComponentSyncStateRepository? state = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _central = central ?? throw new ArgumentNullException(nameof(central));
        _local = local ?? new SqliteComponentIrRepository(databasePath);
        _state = state ?? new ComponentSyncStateRepository(databasePath);
    }

    public async Task<ComponentSyncResult> SaveLocalAsync(
        ComponentIR component,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        await _local.SaveAsync(component, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var previous = await _state.FindAsync(component.Identity.Manufacturer, component.Identity.Model, cancellationToken);
        await _state.SaveAsync(new ComponentSyncState(
            component.Identity.Manufacturer,
            component.Identity.Model,
            component.Identity.ComponentId,
            ComponentSyncStatus.LocalOnly,
            now,
            previous?.LastSuccessfulSyncAt,
            "LOCAL_COMPONENT_EDIT_SAVED"), cancellationToken);
        return new ComponentSyncResult(true, false, false, ComponentSyncStatus.LocalOnly, ["LOCAL_COMPONENT_EDIT_SAVED"], []);
    }

    public async Task<ComponentSyncResult> SaveAndSyncAsync(
        ComponentIR component,
        bool allowVerifiedOverride = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        await _local.SaveAsync(component, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var previousState = await _state.FindAsync(component.Identity.Manufacturer, component.Identity.Model, cancellationToken);
        await _state.SaveAsync(new ComponentSyncState(
            component.Identity.Manufacturer,
            component.Identity.Model,
            component.Identity.ComponentId,
            ComponentSyncStatus.Pending,
            now,
            previousState?.LastSuccessfulSyncAt,
            "LOCAL_SAVED_CENTRAL_PENDING"), cancellationToken);

        if (!_central.IsEnabled)
        {
            return new ComponentSyncResult(
                true,
                false,
                false,
                ComponentSyncStatus.Pending,
                ["LOCAL_SAVED_CENTRAL_PENDING", "NOTION_CENTRAL_DISABLED_NO_TOKEN"],
                []);
        }

        ComponentKnowledgeLookup lookup;
        try
        {
            lookup = await _central.FindByIdentityAsync(
                component.Identity.Manufacturer,
                component.Identity.Model,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await MarkFailedAsync(component, previousState, $"CENTRAL_LOOKUP_FAILED:{exception.GetType().Name}:{exception.Message}", cancellationToken);
        }

        var conflicts = lookup.Component is null
            ? Array.Empty<string>()
            : DetectVerifiedConflicts(lookup.Component, component).ToArray();

        if (conflicts.Length > 0 && !allowVerifiedOverride)
        {
            var diagnostics = lookup.Diagnostics.Concat(["CENTRAL_VERIFIED_CONFLICT_REQUIRES_REVIEW"]).Concat(conflicts).ToArray();
            await _state.SaveAsync(new ComponentSyncState(
                component.Identity.Manufacturer,
                component.Identity.Model,
                component.Identity.ComponentId,
                ComponentSyncStatus.Conflict,
                DateTimeOffset.UtcNow,
                previousState?.LastSuccessfulSyncAt,
                string.Join(Environment.NewLine, diagnostics)), cancellationToken);
            return new ComponentSyncResult(true, true, false, ComponentSyncStatus.Conflict, diagnostics, conflicts);
        }

        ComponentKnowledgeWriteResult write;
        try
        {
            write = await _central.UpsertAsync(component, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await MarkFailedAsync(component, previousState, $"CENTRAL_WRITE_FAILED:{exception.GetType().Name}:{exception.Message}", cancellationToken);
        }

        if (!write.Succeeded)
        {
            var diagnostics = lookup.Diagnostics.Concat(write.Diagnostics).ToArray();
            await _state.SaveAsync(new ComponentSyncState(
                component.Identity.Manufacturer,
                component.Identity.Model,
                component.Identity.ComponentId,
                ComponentSyncStatus.Pending,
                DateTimeOffset.UtcNow,
                previousState?.LastSuccessfulSyncAt,
                string.Join(Environment.NewLine, diagnostics)), cancellationToken);
            return new ComponentSyncResult(true, true, false, ComponentSyncStatus.Pending, diagnostics, []);
        }

        var successfulAt = DateTimeOffset.UtcNow;
        var successDiagnostics = lookup.Diagnostics.Concat(write.Diagnostics).Append("LOCAL_AND_CENTRAL_IN_SYNC").ToArray();
        await _state.SaveAsync(new ComponentSyncState(
            component.Identity.Manufacturer,
            component.Identity.Model,
            component.Identity.ComponentId,
            ComponentSyncStatus.Synced,
            successfulAt,
            successfulAt,
            string.Join(Environment.NewLine, successDiagnostics)), cancellationToken);
        return new ComponentSyncResult(true, true, true, ComponentSyncStatus.Synced, successDiagnostics, []);
    }

    public async Task<ComponentKnowledgeLookup> ReloadCentralAsync(
        string manufacturer,
        string model,
        bool saveToLocal = true,
        CancellationToken cancellationToken = default)
    {
        if (!_central.IsEnabled)
            return new ComponentKnowledgeLookup(null, ["NOTION_CENTRAL_DISABLED_NO_TOKEN"]);

        var lookup = await _central.FindByIdentityAsync(manufacturer, model, cancellationToken);
        if (lookup.Component is not null && saveToLocal)
        {
            await _local.SaveAsync(lookup.Component, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            await _state.SaveAsync(new ComponentSyncState(
                lookup.Component.Identity.Manufacturer,
                lookup.Component.Identity.Model,
                lookup.Component.Identity.ComponentId,
                ComponentSyncStatus.Synced,
                now,
                now,
                string.Join(Environment.NewLine, lookup.Diagnostics.Append("CENTRAL_RELOADED_TO_LOCAL"))), cancellationToken);
        }
        return lookup;
    }

    public Task<ComponentSyncState?> GetStateAsync(
        string manufacturer,
        string model,
        CancellationToken cancellationToken = default) =>
        _state.FindAsync(manufacturer, model, cancellationToken);

    internal static IEnumerable<string> DetectVerifiedConflicts(ComponentIR central, ComponentIR edited)
    {
        if (!string.Equals(central.Identity.Manufacturer.Trim(), edited.Identity.Manufacturer.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(central.Identity.Model.Trim(), edited.Identity.Model.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            yield return "IDENTITY_MISMATCH";
            yield break;
        }

        var editedPins = edited.Pins
            .Where(pin => !string.IsNullOrWhiteSpace(pin.PinNumber))
            .GroupBy(pin => pin.PinNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var centralPin in central.Pins)
        {
            if (!editedPins.TryGetValue(centralPin.PinNumber, out var editedPin)) continue;
            if (Same(centralPin.Function, editedPin.Function)) continue;
            if (!HasVerifiedEvidence(centralPin.Evidence)) continue;
            yield return $"PIN_VERIFIED_CONFLICT:{centralPin.PinNumber}:{centralPin.Function ?? "<null>"}->{editedPin.Function ?? "<null>"}";
        }

        var editedSpecs = edited.Specifications
            .Where(specification => !string.IsNullOrWhiteSpace(SpecIdentity(specification)))
            .GroupBy(SpecIdentity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var centralSpecification in central.Specifications)
        {
            var key = SpecIdentity(centralSpecification);
            if (string.IsNullOrWhiteSpace(key) || !editedSpecs.TryGetValue(key, out var editedSpecification)) continue;
            if (Same(centralSpecification.Value, editedSpecification.Value)) continue;
            if (centralSpecification.Status != VerificationStatus.Verified && !HasVerifiedEvidence(centralSpecification.Evidence)) continue;
            yield return $"SPEC_VERIFIED_CONFLICT:{key}:{centralSpecification.Value ?? "<null>"}->{editedSpecification.Value ?? "<null>"}";
        }
    }

    private async Task<ComponentSyncResult> MarkFailedAsync(
        ComponentIR component,
        ComponentSyncState? previousState,
        string diagnostic,
        CancellationToken cancellationToken)
    {
        await _state.SaveAsync(new ComponentSyncState(
            component.Identity.Manufacturer,
            component.Identity.Model,
            component.Identity.ComponentId,
            ComponentSyncStatus.Pending,
            DateTimeOffset.UtcNow,
            previousState?.LastSuccessfulSyncAt,
            diagnostic), cancellationToken);
        return new ComponentSyncResult(true, true, false, ComponentSyncStatus.Pending, [diagnostic], []);
    }

    private static bool HasVerifiedEvidence(IEnumerable<Evidence> evidence) =>
        evidence.Any(item => item.VerificationStatus == VerificationStatus.Verified);

    private static string SpecIdentity(ComponentSpecification specification) =>
        !string.IsNullOrWhiteSpace(specification.Key) ? specification.Key!.Trim() : specification.Name.Trim();

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
