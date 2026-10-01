using System.Globalization;
using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed class CableManufacturingEditorService
{
    public CableManufacturingDraft Prepare(ArchivedCableTemplate template)
    {
        var draft = new CableManufacturingDraft { Ends = Clone(template.Ports),
            FromSourcePortId = template.Manufacturing?.FromSourcePortId ?? template.Ports.FirstOrDefault()?.PortId,
            ToSourcePortId = template.Manufacturing?.ToSourcePortId ?? template.Ports.Skip(1).FirstOrDefault()?.PortId,
            MappingRevision = template.MappingRevision, MappingEvidence = template.MappingEvidence };
        var metadata = template.Manufacturing;
        draft.Rows.AddRange(template.Mapping.Select(m => new CableManufacturingRow { FromSourcePinId = m.FromSourcePinId, ToSourcePinId = m.ToSourcePinId }));
        draft.Rows.AddRange(Clone(metadata?.IncompleteRows ?? []));
        var represented = draft.Rows.SelectMany(r => new[] { r.FromSourcePinId, r.ToSourcePinId }).Where(s => s is not null).ToHashSet(StringComparer.Ordinal);
        foreach (var port in draft.Ends)
            foreach (var pin in port.Pins.Where(p => !represented.Contains(p.PinId)))
                draft.Rows.Add(port.PortId == draft.ToSourcePortId
                    ? new() { ToSourcePinId = pin.PinId } : new() { FromSourcePinId = pin.PinId });
        var pins = draft.Ends.SelectMany(e => e.Pins).ToDictionary(p => p.PinId, StringComparer.Ordinal);
        var usages = metadata?.PinUsage.ToDictionary(p => p.SourcePinId, p => p.Usage, StringComparer.Ordinal) ?? [];
        foreach (var row in draft.Rows)
        {
            if (row.FromSourcePinId is { } from && pins.TryGetValue(from, out var fromPin))
            { row.FromFunction = fromPin.Function; row.FromUsage = usages.GetValueOrDefault(from); }
            if (row.ToSourcePinId is { } to && pins.TryGetValue(to, out var toPin))
            { row.ToFunction = toPin.Function; row.ToUsage = usages.GetValueOrDefault(to); }
        }
        return draft;
    }

    public void SetPinCount(CableManufacturingDraft draft, string sourcePortId, int count)
    {
        if (count is < 1 or > 512) throw new InvalidOperationException("Pin count must be between 1 and 512.");
        var port = draft.Ends.Single(p => p.PortId == sourcePortId);
        var removed = port.Pins.Skip(count).Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        if (draft.Rows.Any(r => r.FromSourcePinId is not null && removed.Contains(r.FromSourcePinId) ||
            r.ToSourcePinId is not null && removed.Contains(r.ToSourcePinId)))
            throw new InvalidOperationException("Remove or explicitly rebind affected table rows before reducing Pin count.");
        if (removed.Count > 0) port.Pins.RemoveRange(count, port.Pins.Count - count);
        while (port.Pins.Count < count)
        {
            var pin = new ComponentPin { PinId = sourcePortId + ".pin-" + Guid.NewGuid().ToString("N"),
                PinNumber = (port.Pins.Count + 1).ToString(CultureInfo.InvariantCulture) };
            port.Pins.Add(pin);
            draft.Rows.Add(sourcePortId == draft.ToSourcePortId ? new() { ToSourcePinId = pin.PinId } : new() { FromSourcePinId = pin.PinId });
        }
        if (port.Connector is not null) port.Connector.PinCount = count;
    }

    public IReadOnlyList<string> Validate(CableManufacturingDraft draft)
    {
        var errors = new List<string>();
        var ids = draft.Ends.Select(e => e.PortId).Concat(draft.Ends.SelectMany(e => e.Pins).Select(p => p.PinId)).ToArray();
        if (draft.Ends.Count == 0 || draft.Ends.Any(e => e.Pins.Count == 0) || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length)
            errors.Add("Cable ends and Pins require unique identities and at least one Pin per end.");
        var pins = draft.Ends.SelectMany(e => e.Pins).Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        foreach (var portId in new[] { draft.FromSourcePortId, draft.ToSourcePortId }.Where(p => p is not null))
            if (!draft.Ends.Any(p => p.PortId == portId)) errors.Add("Table end refers to a missing Port.");
        var pairs = new HashSet<(string, string)>();
        var functions = new Dictionary<string, string?>(StringComparer.Ordinal);
        var usages = new Dictionary<string, CablePinUsage>(StringComparer.Ordinal);
        foreach (var row in draft.Rows)
        {
            foreach (var (id, function, usage) in new[] { (row.FromSourcePinId, row.FromFunction, row.FromUsage), (row.ToSourcePinId, row.ToFunction, row.ToUsage) })
            {
                if (!Enum.IsDefined(usage)) { errors.Add("Unknown Pin usage."); continue; }
                if (id is null) { if (usage != CablePinUsage.Pending) errors.Add("NC/unused must identify an exact Pin."); continue; }
                if (!pins.Contains(id)) { errors.Add("Table refers to a missing Pin."); continue; }
                var value = Normalize(function);
                if (functions.TryGetValue(id, out var previous) && previous != value) errors.Add("One Pin has conflicting function labels.");
                functions[id] = value;
                if (usages.TryGetValue(id, out var previousUsage) && previousUsage != usage) errors.Add("One Pin has conflicting usage declarations.");
                usages[id] = usage;
            }
            if (row.FromSourcePinId is not { } from || row.ToSourcePinId is not { } to) continue;
            if (from == to) errors.Add("Mapping requires two distinct Pins.");
            if (row.FromUsage != CablePinUsage.Pending || row.ToUsage != CablePinUsage.Pending) errors.Add("NC/unused Pins cannot have a mapping pair.");
            var key = string.CompareOrdinal(from, to) < 0 ? (from, to) : (to, from);
            if (!pairs.Add(key)) errors.Add("Duplicate Pin mapping pair.");
        }
        if (draft.ConfirmMapping && (string.IsNullOrWhiteSpace(draft.MappingRevision) || string.IsNullOrWhiteSpace(draft.MappingEvidence)))
            errors.Add("Mapping confirmation requires a revision and source evidence.");
        if (draft.ConfirmMapping)
        {
            var mapped = draft.Rows.Where(r => r.FromSourcePinId is not null && r.ToSourcePinId is not null)
                .SelectMany(r => new[] { r.FromSourcePinId!, r.ToSourcePinId! }).ToHashSet(StringComparer.Ordinal);
            if (pins.Any(p => !mapped.Contains(p) && usages.GetValueOrDefault(p) == CablePinUsage.Pending))
                errors.Add("Unmapped Pins need explicit NC/unused confirmation or remain a draft.");
            if (draft.Rows.Any(r => r.FromSourcePinId is null && r.ToSourcePinId is null))
                errors.Add("Unbound table rows remain unfinished.");
        }
        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    public ArchivedCableTemplate Apply(ArchivedCableTemplate template, CableManufacturingDraft draft)
    {
        var errors = Validate(draft);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
        var ports = Clone(draft.Ends);
        var pins = ports.SelectMany(e => e.Pins).ToDictionary(p => p.PinId, StringComparer.Ordinal);
        var oldMapped = template.Mapping.SelectMany(m => new[] { m.FromSourcePinId, m.ToSourcePinId });
        if (oldMapped.Any(p => !pins.ContainsKey(p))) throw new InvalidOperationException("A mapped Pin cannot be silently removed.");
        var mapping = new List<CablePinMapping>();
        var incomplete = new List<CableManufacturingRow>();
        var usage = new Dictionary<string, CablePinUsage>(StringComparer.Ordinal);
        foreach (var row in draft.Rows)
        {
            if (row.FromSourcePinId is { } from) { pins[from].Function = Normalize(row.FromFunction); usage[from] = row.FromUsage; }
            if (row.ToSourcePinId is { } to) { pins[to].Function = Normalize(row.ToFunction); usage[to] = row.ToUsage; }
            if (row.FromSourcePinId is not null && row.ToSourcePinId is not null)
                mapping.Add(new(row.FromSourcePinId, row.ToSourcePinId));
            else incomplete.Add(new() { FromSourcePinId = row.FromSourcePinId, ToSourcePinId = row.ToSourcePinId,
                FromFunction = row.FromSourcePinId is null ? Normalize(row.FromFunction) : null,
                ToFunction = row.ToSourcePinId is null ? Normalize(row.ToFunction) : null });
        }
        var definition = new CableManufacturingDefinition { FromSourcePortId = draft.FromSourcePortId,
            ToSourcePortId = draft.ToSourcePortId, IncompleteRows = incomplete,
            PinUsage = usage.Where(u => u.Value != CablePinUsage.Pending).Select(u => new CablePinUsageDeclaration(u.Key, u.Value)).ToList() };
        var changed = JsonSerializer.Serialize(Prepare(template)) != JsonSerializer.Serialize(draft);
        var next = template with { Ports = ports, Mapping = mapping, Manufacturing = definition,
            MappingConfirmed = draft.ConfirmMapping || !changed && template.MappingConfirmed,
            MappingRevision = Normalize(draft.MappingRevision), MappingEvidence = Normalize(draft.MappingEvidence) };
        ArchivedCableInstanceFactory.ValidateTemplate(next);
        return next;
    }

    public static void ValidateMetadata(ArchivedCableTemplate template)
    {
        if (template.Manufacturing is not { } metadata) return;
        var pins = template.Ports.SelectMany(e => e.Pins).Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        var usages = new HashSet<string>(StringComparer.Ordinal);
        var mapped = template.Mapping.SelectMany(m => new[] { m.FromSourcePinId, m.ToSourcePinId }).ToHashSet(StringComparer.Ordinal);
        if (metadata.PinUsage.Any(u => !pins.Contains(u.SourcePinId) || !usages.Add(u.SourcePinId) || !Enum.IsDefined(u.Usage) ||
                u.Usage != CablePinUsage.Pending && mapped.Contains(u.SourcePinId)) ||
            metadata.IncompleteRows.Any(r => r.FromSourcePinId is not null && r.ToSourcePinId is not null ||
                r.FromSourcePinId is not null && !pins.Contains(r.FromSourcePinId) || r.ToSourcePinId is not null && !pins.Contains(r.ToSourcePinId)) ||
            new[] { metadata.FromSourcePortId, metadata.ToSourcePortId }.Any(id => id is not null && !template.Ports.Any(p => p.PortId == id)))
            throw new InvalidOperationException("Manufacturing draft has conflicting or missing exact Pin/Port identities.");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
