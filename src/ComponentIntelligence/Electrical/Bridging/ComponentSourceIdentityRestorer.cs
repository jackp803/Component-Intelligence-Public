using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Bridging;

public enum SourceIdentityStatus
{
    ALREADY_TYPED, EXPLICIT_LEGACY_RESTORABLE, DETERMINISTIC_EXACT_RESTORABLE,
    CONFLICT, UNRESOLVED, NOT_APPLICABLE
}

public sealed record SourceEndpointRestoration(string EndpointId, string Kind, string? Before,
    string? SourceId, SourceIdentityStatus Status, string? Reason);

public sealed record ComponentSourceRestoration(string ComponentInstanceId, string ComponentDefinitionId,
    SourceIdentityStatus Status, int RestoredCount, IReadOnlyList<SourceEndpointRestoration> Endpoints);

/// <summary>Plans lineage-only changes from explicit authority; never infers engineering identity.</summary>
public sealed class ComponentSourceIdentityRestorer
{
    public ComponentSourceRestoration Analyze(ComponentInstance instance, ComponentIR? source)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var rows = new List<SourceEndpointRestoration>();
        if (source is null || !string.Equals(source.Identity.ComponentId, instance.ComponentDefinitionId, StringComparison.Ordinal))
            return new(instance.ComponentInstanceId, instance.ComponentDefinitionId, SourceIdentityStatus.UNRESOLVED, 0, []);

        var validPorts = source.Ports.Where(p => !string.IsNullOrWhiteSpace(p.PortId)).Select(p => p.PortId!).ToArray();
        var validPins = source.Pins.Where(p => !string.IsNullOrWhiteSpace(p.PinId)).Select(p => p.PinId!).ToArray();
        var catalogConflict = validPorts.Distinct(StringComparer.Ordinal).Count() != validPorts.Length
            || validPins.Distinct(StringComparer.Ordinal).Count() != validPins.Length;
        var expected = new ComponentProjectBridge().CreateInstance(source, instance.ComponentInstanceId);
        var expectedPorts = expected.Ports.ToLookup(p => p.PortId, StringComparer.Ordinal);
        var expectedPins = expected.Ports.SelectMany(p => p.Pins).ToLookup(p => p.PinId, StringComparer.Ordinal);
        foreach (var port in instance.Ports)
        {
            var candidates = expectedPorts[port.PortId].ToArray();
            var legacy = port.Capabilities.Where(c => c.StartsWith("SOURCE_PORT_ID:", StringComparison.Ordinal))
                .Select(c => c["SOURCE_PORT_ID:".Length..]).ToArray();
            rows.Add(Decide(port.PortId, "Port", port.SourcePortId, validPorts,
                candidates.Length == 1 ? candidates[0].SourcePortId : null, legacy,
                catalogConflict || candidates.Length > 1));
            foreach (var pin in port.Pins)
            {
                var pins = expectedPins[pin.PinId].ToArray();
                rows.Add(Decide(pin.PinId, "Pin", pin.SourcePinId, validPins,
                    pins.Length == 1 ? pins[0].SourcePinId : null, [], catalogConflict || pins.Length > 1));
            }
        }

        var duplicateEndpoints = rows.GroupBy(r => r.EndpointId, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var duplicateSources = rows.Where(r => !string.IsNullOrWhiteSpace(r.SourceId))
            .GroupBy(r => r.SourceId!, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        rows = rows.Select(r => duplicateEndpoints.Contains(r.EndpointId) || r.SourceId is not null && duplicateSources.Contains(r.SourceId)
            ? r with { Status = SourceIdentityStatus.CONFLICT, Reason = "Duplicate endpoint or source identity." } : r)
            .OrderBy(r => r.EndpointId, StringComparer.Ordinal).ToList();
        var status = catalogConflict || rows.Any(r => r.Status == SourceIdentityStatus.CONFLICT) ? SourceIdentityStatus.CONFLICT
            : rows.Any(r => r.Status == SourceIdentityStatus.UNRESOLVED) ? SourceIdentityStatus.UNRESOLVED
            : rows.Any(r => r.Status == SourceIdentityStatus.DETERMINISTIC_EXACT_RESTORABLE) ? SourceIdentityStatus.DETERMINISTIC_EXACT_RESTORABLE
            : rows.Any(r => r.Status == SourceIdentityStatus.EXPLICIT_LEGACY_RESTORABLE) ? SourceIdentityStatus.EXPLICIT_LEGACY_RESTORABLE
            : rows.Count == 0 ? SourceIdentityStatus.NOT_APPLICABLE : SourceIdentityStatus.ALREADY_TYPED;
        var count = status == SourceIdentityStatus.CONFLICT ? 0 : rows.Count(IsRestoration);
        return new(instance.ComponentInstanceId, instance.ComponentDefinitionId, status, count, rows);
    }

    public ComponentSourceRestoration Restore(ComponentInstance instance, ComponentIR? source)
    {
        var report = Analyze(instance, source);
        // Component-level conflicts are atomic: no partial lineage changes survive.
        if (report.Status == SourceIdentityStatus.CONFLICT) return report;
        var changes = report.Endpoints.Where(IsRestoration).ToDictionary(r => r.EndpointId, StringComparer.Ordinal);
        foreach (var port in instance.Ports)
        {
            if (changes.TryGetValue(port.PortId, out var p)) port.SourcePortId = p.SourceId;
            foreach (var pin in port.Pins)
                if (changes.TryGetValue(pin.PinId, out var r)) pin.SourcePinId = r.SourceId;
        }
        return report;
    }

    private static bool IsRestoration(SourceEndpointRestoration row) => row.Status is
        SourceIdentityStatus.EXPLICIT_LEGACY_RESTORABLE or SourceIdentityStatus.DETERMINISTIC_EXACT_RESTORABLE;

    private static SourceEndpointRestoration Decide(string id, string kind, string? typed,
        string[] valid, string? reconstructed, string[] legacy, bool conflict)
    {
        var hasTyped = !string.IsNullOrWhiteSpace(typed);
        var legacyId = legacy.Length == 1 ? legacy[0] : null;
        var hasReconstruction = !string.IsNullOrWhiteSpace(reconstructed);
        var proposed = hasTyped ? typed : legacyId ?? (hasReconstruction ? reconstructed : null);
        if (conflict || legacy.Length > 1 || legacyId is not null && !valid.Contains(legacyId, StringComparer.Ordinal)
            || proposed is not null && !valid.Contains(proposed, StringComparer.Ordinal)
            || legacyId is not null && proposed != legacyId
            || hasReconstruction && proposed is not null && proposed != reconstructed)
            return new(id, kind, typed, proposed, SourceIdentityStatus.CONFLICT, "Source authority is invalid, duplicate or contradictory.");
        return new(id, kind, typed, proposed,
            hasTyped ? SourceIdentityStatus.ALREADY_TYPED
            : legacyId is not null ? SourceIdentityStatus.EXPLICIT_LEGACY_RESTORABLE
            : hasReconstruction ? SourceIdentityStatus.DETERMINISTIC_EXACT_RESTORABLE
            : SourceIdentityStatus.UNRESOLVED,
            proposed is null ? "No explicit source or full reconstructed endpoint-ID match." : null);
    }
}
