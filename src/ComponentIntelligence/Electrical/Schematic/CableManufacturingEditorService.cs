using System.Globalization;
using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed class CableManufacturingEditorService
{
    public CableManufacturingDraft Prepare(CableInstance cable) => Prepare(EffectiveTemplate(cable));

    public static ArchivedCableTemplate EffectiveTemplate(CableInstance cable)
    {
        var binding = cable.ArchivedCable ?? throw new InvalidOperationException("Select an archived cable.");
        var ports = Clone(binding.Template.Ports);
        foreach (var port in ports)
        {
            var runtime = binding.Ports.Single(p => p.SourcePortId == port.PortId);
            port.Name = runtime.Name; port.Connector = Clone(runtime.Connector);
            foreach (var pin in port.Pins)
            {
                var current = runtime.Pins.Single(p => p.SourcePinId == pin.PinId);
                pin.PinNumber = current.PinNumber; pin.PinName = current.PinName; pin.Function = current.Function;
            }
        }
        return binding.Template with { Ports = ports, Mapping = Clone(binding.Mapping), MappingConfirmed = binding.MappingConfirmed,
            Manufacturing = Clone(binding.Manufacturing ?? binding.Template.Manufacturing) };
    }

    public CableInstance ApplyToInstance(CableInstance cable, CableManufacturingDraft draft)
    {
        var binding = cable.ArchivedCable ?? throw new InvalidOperationException("Select an archived cable.");
        if (draft.ConfirmMapping) throw new InvalidOperationException("Instance mapping edits remain pending confirmation; they cannot inherit archive approval.");
        var original = EffectiveTemplate(cable);
        if (!original.Ports.Select(p => p.PortId).ToHashSet(StringComparer.Ordinal).SetEquals(draft.Ends.Select(p => p.PortId)) ||
            original.Ports.Any(p => !draft.Ends.Single(e => e.PortId == p.PortId).Pins.Select(pin => pin.PinId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(p.Pins.Select(pin => pin.PinId))))
            throw new InvalidOperationException("An instance cannot silently replace its Pin inventory; create a new template revision and rebind explicitly.");
        var changed = JsonSerializer.Serialize(Prepare(original)) != JsonSerializer.Serialize(draft);
        if (!changed) return Clone(cable);
        var result = Apply(original, draft);
        var copy = Clone(cable);
        foreach (var port in copy.ArchivedCable!.Ports)
        {
            var end = result.Ports.Single(p => p.PortId == port.SourcePortId);
            port.Name = end.Name; port.Connector = Clone(end.Connector);
            foreach (var pin in port.Pins)
            {
                var source = end.Pins.Single(p => p.PinId == pin.SourcePinId);
                pin.PinNumber = source.PinNumber; pin.PinName = source.PinName; pin.Function = source.Function;
            }
        }
        copy.ArchivedCable = copy.ArchivedCable with { Mapping = Clone(result.Mapping), Manufacturing = Clone(result.Manufacturing),
            MappingConfirmed = !changed && binding.MappingConfirmed, HasMappingOverride = binding.HasMappingOverride || changed };
        return copy;
    }

    public CableManufacturingDraft Prepare(ArchivedCableTemplate template)
    {
        var draft = new CableManufacturingDraft { Ends = Clone(template.Ports),
            FromSourcePortId = template.Manufacturing is { } fromMetadata ? fromMetadata.FromSourcePortId : template.Ports.FirstOrDefault()?.PortId,
            ToSourcePortId = template.Manufacturing is { } toMetadata ? toMetadata.ToSourcePortId : template.Ports.Skip(1).FirstOrDefault()?.PortId,
            MappingRevision = template.MappingRevision, MappingEvidence = template.MappingEvidence };
        var metadata = template.Manufacturing;
        if (metadata?.Rows is { } rows) draft.Rows.AddRange(Clone(rows));
        else
        {
            draft.Rows.AddRange(template.Mapping.Select(m => new CableManufacturingRow { FromSourcePinId = m.FromSourcePinId, ToSourcePinId = m.ToSourcePinId }));
            draft.Rows.AddRange(Clone(metadata?.IncompleteRows ?? []));
            foreach (var row in draft.Rows) NormalizeLegacyColumns(draft, row);
        }
        var represented = draft.Rows.SelectMany(CableManufacturingCells.All).Select(c => c.SourcePinId).Where(s => s is not null).ToHashSet(StringComparer.Ordinal);
        foreach (var port in draft.Ends)
            foreach (var pin in port.Pins.Where(p => !represented.Contains(p.PinId)))
                AddPinRow(draft, port.PortId, pin.PinId);
        var pins = draft.Ends.SelectMany(e => e.Pins).ToDictionary(p => p.PinId, StringComparer.Ordinal);
        var usages = metadata?.PinUsage.ToDictionary(p => p.SourcePinId, p => p.Usage, StringComparer.Ordinal) ?? [];
        foreach (var row in draft.Rows)
        {
            if (row.FromSourcePinId is { } from && pins.TryGetValue(from, out var fromPin))
            { row.FromFunction = fromPin.Function; row.FromUsage = usages.GetValueOrDefault(from); }
            if (row.ToSourcePinId is { } to && pins.TryGetValue(to, out var toPin))
            { row.ToFunction = toPin.Function; row.ToUsage = usages.GetValueOrDefault(to); }
            foreach (var cell in row.AdditionalEnds.Values)
                if (cell.SourcePinId is { } id && pins.TryGetValue(id, out var pin))
                { cell.Function = pin.Function; cell.Usage = usages.GetValueOrDefault(id); }
        }
        CableManufacturingCells.EnsureColumns(draft);
        draft.Rows = SortRows(draft).ToList();
        return draft;
    }

    private static void AddPinRow(CableManufacturingDraft draft, string portId, string pinId)
    {
        var row = new CableManufacturingRow();
        CableManufacturingCells.Set(draft, row, portId, new() { SourcePinId = pinId });
        draft.Rows.Add(row);
    }

    private static void NormalizeLegacyColumns(CableManufacturingDraft draft, CableManufacturingRow row)
    {
        var cells = CableManufacturingCells.All(row).Where(c => c.SourcePinId is not null).ToArray();
        if (cells.Length == 0) return;
        var ports = cells.Select(c => draft.Ends.SingleOrDefault(p => p.Pins.Any(pin => pin.PinId == c.SourcePinId))).ToArray();
        // Preserve unusual historical same-Port pairs instead of silently dropping a Pin.
        if (ports.Any(p => p is null) || ports.Select(p => p!.PortId).Distinct().Count() != cells.Length) return;
        var unbound = draft.Ends.Select(p => (p.PortId, Cell: CableManufacturingCells.Get(draft, row, p.PortId)))
            .Where(c => c.Cell.SourcePinId is null && (c.Cell.Function is not null || c.Cell.Usage != CablePinUsage.Pending)).ToArray();
        if (unbound.Any(c => ports.Any(p => p!.PortId == c.PortId))) return;
        row.FromSourcePinId = null; row.ToSourcePinId = null;
        row.FromFunction = null; row.ToFunction = null; row.FromUsage = CablePinUsage.Pending; row.ToUsage = CablePinUsage.Pending;
        row.AdditionalEnds.Clear();
        foreach (var cell in unbound) CableManufacturingCells.Set(draft, row, cell.PortId, cell.Cell);
        for (var i = 0; i < cells.Length; i++) CableManufacturingCells.Set(draft, row, ports[i]!.PortId, cells[i]);
    }

    public static IOrderedEnumerable<CableManufacturingRow> SortRows(CableManufacturingDraft draft, bool from = true, bool descending = false)
        => SortRows(draft, from ? draft.FromSourcePortId : draft.ToSourcePortId, descending);

    public static IOrderedEnumerable<CableManufacturingRow> SortRows(CableManufacturingDraft draft, string? portId, bool descending = false)
    {
        var pins = draft.Ends.SelectMany(p => p.Pins.Select(pin => (pin.PinId, Port: p.Name, pin.PinNumber)))
            .ToDictionary(p => p.PinId, StringComparer.Ordinal);
        string? Id(CableManufacturingRow row) => portId is null ? row.FromSourcePinId ?? row.ToSourcePinId : CableManufacturingCells.Get(draft, row, portId).SourcePinId;
        var comparer = descending ? Comparer<string>.Create((a, b) => DrawingContactDisplayOrder.NumberComparer.Compare(b, a)) :
            DrawingContactDisplayOrder.NumberComparer;
        return draft.Rows.OrderBy(row => Id(row) is not { } id || !pins.ContainsKey(id))
            .ThenBy(row => Id(row) is { } id && pins.TryGetValue(id, out var pin) ? pin.Port : "", comparer)
            .ThenBy(row => Id(row) is { } id && pins.TryGetValue(id, out var pin) ? pin.PinNumber : "", comparer);
    }

    public void SetPinCount(CableManufacturingDraft draft, string sourcePortId, int count,
        IReadOnlySet<string>? boundPins = null, bool confirmRemoval = false)
    {
        if (count is < 1 or > 512) throw new InvalidOperationException("Pin count must be between 1 and 512.");
        var port = draft.Ends.Single(p => p.PortId == sourcePortId);
        var removed = port.Pins.Skip(count).Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        RemovePinReferences(draft, removed, boundPins, confirmRemoval);
        if (port.Pins.Count != count) draft.ConfirmMapping = false;
        if (removed.Count > 0) port.Pins.RemoveRange(count, port.Pins.Count - count);
        while (port.Pins.Count < count)
        {
            var number = port.Pins.Count + 1;
            while (port.Pins.Any(p => p.PinNumber == number.ToString(CultureInfo.InvariantCulture))) number++;
            var pin = new ComponentPin { PinId = sourcePortId + ".pin-" + Guid.NewGuid().ToString("N"),
                PinNumber = number.ToString(CultureInfo.InvariantCulture) };
            port.Pins.Add(pin);
            AddPinRow(draft, sourcePortId, pin.PinId);
        }
        CableManufacturingCells.EnsureColumns(draft);
        if (port.Connector is not null) port.Connector.PinCount = count;
    }

    public ComponentPort AddEnd(CableManufacturingDraft draft)
    {
        var index = 1;
        while (draft.Ends.Any(p => p.Name == "P" + index)) index++;
        var id = "end-" + Guid.NewGuid().ToString("N");
        var port = new ComponentPort { PortId = id, Name = "P" + index,
            Pins = [new() { PinId = id + ".1", PinNumber = "1" }] };
        draft.Ends.Add(port);
        if (draft.FromSourcePortId is null) draft.FromSourcePortId = port.PortId;
        else if (draft.ToSourcePortId is null) draft.ToSourcePortId = port.PortId;
        AddPinRow(draft, port.PortId, port.Pins[0].PinId);
        CableManufacturingCells.EnsureColumns(draft);
        draft.ConfirmMapping = false;
        return port;
    }

    public void RemoveEnd(CableManufacturingDraft draft, string sourcePortId,
        IReadOnlySet<string>? boundPins = null, bool confirmRemoval = false)
    {
        if (draft.Ends.Count <= 1) throw new InvalidOperationException("Keep at least one cable endpoint.");
        var port = draft.Ends.Single(p => p.PortId == sourcePortId);
        RemovePinReferences(draft, port.Pins.Select(p => p.PinId).ToHashSet(StringComparer.Ordinal), boundPins, confirmRemoval);
        draft.Ends.Remove(port);
        foreach (var row in draft.Rows) row.AdditionalEnds.Remove(sourcePortId);
        if (draft.FromSourcePortId == sourcePortId) draft.FromSourcePortId = null;
        if (draft.ToSourcePortId == sourcePortId) draft.ToSourcePortId = null;
        draft.ConfirmMapping = false;
    }

    public static bool HasRemovalImpact(CableManufacturingDraft draft, IReadOnlySet<string> pins, IReadOnlySet<string>? boundPins = null) =>
        boundPins?.Overlaps(pins) == true || draft.Ends.SelectMany(p => p.Pins).Any(p => pins.Contains(p.PinId) && !string.IsNullOrWhiteSpace(p.Function)) ||
        draft.Rows.Any(r => r.FromSourcePinId is { } f && pins.Contains(f) &&
            (r.ToSourcePinId is not null || r.FromUsage != CablePinUsage.Pending || !string.IsNullOrWhiteSpace(r.FromFunction) || !string.IsNullOrWhiteSpace(r.ToFunction)) ||
            r.ToSourcePinId is { } t && pins.Contains(t) &&
            (r.FromSourcePinId is not null || r.ToUsage != CablePinUsage.Pending || !string.IsNullOrWhiteSpace(r.ToFunction) || !string.IsNullOrWhiteSpace(r.FromFunction)) ||
            CableManufacturingCells.All(r).Any(c => c.SourcePinId is { } id && pins.Contains(id) &&
                (CableManufacturingCells.All(r).Count(v => v.SourcePinId is not null) > 1 || c.Usage != CablePinUsage.Pending || !string.IsNullOrWhiteSpace(c.Function))));

    private static void RemovePinReferences(CableManufacturingDraft draft, IReadOnlySet<string> removed,
        IReadOnlySet<string>? boundPins, bool confirmRemoval)
    {
        if (removed.Count == 0) return;
        if (HasRemovalImpact(draft, removed, boundPins) && !confirmRemoval)
            throw new InvalidOperationException("Confirm removal of affected mapping, function, usage and CAD bindings first.");
        foreach (var row in draft.Rows.ToArray())
        {
            var affected = false;
            if (row.FromSourcePinId is { } from && removed.Contains(from))
            { row.FromSourcePinId = null; row.FromFunction = null; row.FromUsage = CablePinUsage.Pending; affected = true; }
            if (row.ToSourcePinId is { } to && removed.Contains(to))
            { row.ToSourcePinId = null; row.ToFunction = null; row.ToUsage = CablePinUsage.Pending; affected = true; }
            foreach (var cell in row.AdditionalEnds.Values.Where(c => c.SourcePinId is { } id && removed.Contains(id)))
            { cell.SourcePinId = null; cell.Function = null; cell.Usage = CablePinUsage.Pending; affected = true; }
            if (affected && row.FromSourcePinId is null && row.ToSourcePinId is null &&
                CableManufacturingCells.All(row).All(c => c.SourcePinId is null && string.IsNullOrWhiteSpace(c.Function))) draft.Rows.Remove(row);
        }
        foreach (var id in removed.Except(draft.ExplicitlyRemovedPinIds, StringComparer.Ordinal)) draft.ExplicitlyRemovedPinIds.Add(id);
    }

    public void SetConnector(CableManufacturingDraft draft, string sourcePortId, ComponentPort source,
        IReadOnlySet<string>? boundPins = null, bool allowInventoryChanges = true)
    {
        var port = draft.Ends.Single(p => p.PortId == sourcePortId);
        if (source.Pins.Count == 0 || source.Connector is { PinCount: > 0 } definition && definition.PinCount != source.Pins.Count)
            throw new InvalidOperationException("接頭的實際腳位資料不完整；請先確認腳位，不會自動編造端子號碼。");
        if (!allowInventoryChanges && source.Pins.Count != port.Pins.Count)
            throw new InvalidOperationException("變更 Pin 數需建立新模板修訂，不能替換已放入線材的接點。");
        var ids = port.Pins.Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        if (HasRemovalImpact(draft, ids, boundPins) || port.Pins.Any(p => !string.IsNullOrWhiteSpace(p.Function)) ||
            draft.Rows.Any(r => r.FromSourcePinId is { } f && ids.Contains(f) &&
                (r.ToSourcePinId is not null || r.FromUsage != CablePinUsage.Pending || !string.IsNullOrWhiteSpace(r.FromFunction)) ||
                r.ToSourcePinId is { } t && ids.Contains(t) &&
                (r.FromSourcePinId is not null || r.ToUsage != CablePinUsage.Pending || !string.IsNullOrWhiteSpace(r.ToFunction))))
            throw new InvalidOperationException("此接頭已有接法、狀態或 CAD 綁定。請先明確核對及解除相關設定，再換用接頭；可用腳位編輯保留身分並修改標示。");
        SetPinCount(draft, sourcePortId, source.Pins.Count);
        for (var i = 0; i < port.Pins.Count; i++)
        {
            port.Pins[i].PinNumber = source.Pins[i].PinNumber;
            port.Pins[i].PinName = source.Pins[i].PinName;
        }
        port.Connector = source.Connector is null
            ? new() { ConnectorId = "custom-" + port.PortId, Family = "Custom", PinCount = port.Pins.Count }
            : Clone(source.Connector);
    }

    public IReadOnlyList<string> Validate(CableManufacturingDraft draft)
    {
        var errors = new List<string>();
        var ids = draft.Ends.Select(e => e.PortId).Concat(draft.Ends.SelectMany(e => e.Pins).Select(p => p.PinId)).ToArray();
        if (draft.Ends.Count == 0 || draft.Ends.Any(e => e.Pins.Count == 0) || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length)
            errors.Add("Cable ends and Pins require unique identities and at least one Pin per end.");
        var pins = draft.Ends.SelectMany(e => e.Pins).Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        if (draft.ExplicitlyRemovedPinIds.Any(p => string.IsNullOrWhiteSpace(p) || pins.Contains(p)) ||
            draft.ExplicitlyRemovedPinIds.Distinct(StringComparer.Ordinal).Count() != draft.ExplicitlyRemovedPinIds.Count)
            errors.Add("Explicitly removed Pins must be unique and absent from the current inventory.");
        foreach (var portId in new[] { draft.FromSourcePortId, draft.ToSourcePortId }.Where(p => p is not null))
            if (!draft.Ends.Any(p => p.PortId == portId)) errors.Add("Table end refers to a missing Port.");
        var pairs = new HashSet<(string, string)>();
        var functions = new Dictionary<string, string?>(StringComparer.Ordinal);
        var usages = new Dictionary<string, CablePinUsage>(StringComparer.Ordinal);
        foreach (var row in draft.Rows)
        {
            foreach (var (portId, cell) in row.AdditionalEnds)
                if (draft.Ends.SingleOrDefault(p => p.PortId == portId) is not { } port || portId == draft.FromSourcePortId || portId == draft.ToSourcePortId ||
                    cell.SourcePinId is { } pinId && !port.Pins.Any(p => p.PinId == pinId)) errors.Add("Table cell must belong to its exact Port.");
            foreach (var cell in CableManufacturingCells.All(row))
            {
                var (id, function, usage) = (cell.SourcePinId, cell.Function, cell.Usage);
                if (!Enum.IsDefined(usage)) { errors.Add("Unknown Pin usage."); continue; }
                if (id is null) { if (usage != CablePinUsage.Pending) errors.Add("NC/unused must identify an exact Pin."); continue; }
                if (!pins.Contains(id)) { errors.Add("Table refers to a missing Pin."); continue; }
                var value = Normalize(function);
                if (value is not null)
                {
                    if (functions.TryGetValue(id, out var previous) && previous != value) errors.Add("One Pin has conflicting function labels.");
                    functions[id] = value;
                }
                if (usages.TryGetValue(id, out var previousUsage) && previousUsage != usage) errors.Add("One Pin has conflicting usage declarations.");
                usages[id] = usage;
            }
            var mappedCells = CableManufacturingCells.All(row).Where(c => c.SourcePinId is not null).ToArray();
            if (mappedCells.Length > 1 && mappedCells.Any(c => c.Usage != CablePinUsage.Pending)) errors.Add("NC/unused Pins cannot have a mapping pair.");
            foreach (var pair in CableManufacturingCells.Pairs(row))
            {
                var (from, to) = (pair.FromSourcePinId, pair.ToSourcePinId);
                if (from == to) errors.Add("Mapping requires two distinct Pins.");
                var key = string.CompareOrdinal(from, to) < 0 ? (from, to) : (to, from);
                if (!pairs.Add(key)) errors.Add("Duplicate Pin mapping pair.");
            }
        }
        if (draft.ConfirmMapping && (string.IsNullOrWhiteSpace(draft.MappingRevision) || string.IsNullOrWhiteSpace(draft.MappingEvidence)))
            errors.Add("Mapping confirmation requires a revision and source evidence.");
        if (draft.ConfirmMapping)
        {
            var mapped = draft.Rows.SelectMany(CableManufacturingCells.Pairs)
                .SelectMany(p => new[] { p.FromSourcePinId, p.ToSourcePinId }).ToHashSet(StringComparer.Ordinal);
            if (pins.Any(p => !mapped.Contains(p) && usages.GetValueOrDefault(p) == CablePinUsage.Pending))
                errors.Add("Unmapped Pins need explicit NC/unused confirmation or remain a draft.");
            if (draft.Rows.Any(r => CableManufacturingCells.All(r).All(c => c.SourcePinId is null)))
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
        if (oldMapped.Any(p => !pins.ContainsKey(p) && !draft.ExplicitlyRemovedPinIds.Contains(p, StringComparer.Ordinal)))
            throw new InvalidOperationException("A mapped Pin cannot be silently removed.");
        var mapping = new List<CablePinMapping>();
        var incomplete = new List<CableManufacturingRow>();
        var usage = new Dictionary<string, CablePinUsage>(StringComparer.Ordinal);
        var functions = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var row in draft.Rows)
        {
            foreach (var cell in CableManufacturingCells.All(row))
            {
                var (id, function, declaration) = (cell.SourcePinId, cell.Function, cell.Usage);
                if (id is not null)
                {
                    var value = Normalize(function);
                    if (value is not null || !functions.ContainsKey(id)) functions[id] = value;
                    usage[id] = declaration;
                }
            }
            var rowPairs = CableManufacturingCells.Pairs(row).ToArray();
            mapping.AddRange(rowPairs);
            if (rowPairs.Length == 0) incomplete.Add(Clone(row));
        }
        foreach (var (id, function) in functions) pins[id].Function = function;
        var definition = new CableManufacturingDefinition { FromSourcePortId = draft.FromSourcePortId,
            ToSourcePortId = draft.ToSourcePortId, IncompleteRows = incomplete, Rows = Clone(draft.Rows),
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
            metadata.IncompleteRows.Any(r => CableManufacturingCells.Pairs(r).Any() ||
                CableManufacturingCells.All(r).Any(c => c.SourcePinId is { } id && !pins.Contains(id))) ||
            new[] { metadata.FromSourcePortId, metadata.ToSourcePortId }.Any(id => id is not null && !template.Ports.Any(p => p.PortId == id)))
            throw new InvalidOperationException("Manufacturing draft has conflicting or missing exact Pin/Port identities.");
        if (metadata.Rows is not null)
        {
            var draft = new CableManufacturingDraft { Ends = template.Ports, FromSourcePortId = metadata.FromSourcePortId,
                ToSourcePortId = metadata.ToSourcePortId, Rows = metadata.Rows };
            var errors = new CableManufacturingEditorService().Validate(draft);
            string Key(CablePinMapping pair) => string.CompareOrdinal(pair.FromSourcePinId, pair.ToSourcePinId) < 0
                ? pair.FromSourcePinId + "\0" + pair.ToSourcePinId : pair.ToSourcePinId + "\0" + pair.FromSourcePinId;
            if (errors.Count > 0 || !draft.Rows.SelectMany(CableManufacturingCells.Pairs).Select(Key).ToHashSet(StringComparer.Ordinal)
                .SetEquals(template.Mapping.Select(Key)))
                throw new InvalidOperationException("Manufacturing table and exact Pin mapping disagree.");
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
