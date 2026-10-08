using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public static class CableFunctionAssociationService
{
    public static void SetFunction(CableManufacturingDraft draft, string id, string? value)
    {
        if (!draft.InventoryOnly || !draft.Ends.Any(p => p.Pins.Any(pin => pin.PinId == id)))
            throw new InvalidOperationException("請先選擇接線圖中的 Pin。");
        draft.FunctionLinks.Remove(id);
        if (!draft.FunctionOverrides.Contains(id, StringComparer.Ordinal)) draft.FunctionOverrides.Add(id);
        Write(draft, id, Normalize(value)); draft.ConfirmMapping = false;
        Synchronize(draft);
    }

    public static void Synchronize(CableManufacturingDraft draft)
    {
        if (!draft.InventoryOnly) return;
        var cells = Cells(draft);
        draft.FunctionOverrides.RemoveAll(id => !cells.ContainsKey(id));
        foreach (var (id, link) in draft.FunctionLinks.ToArray())
        {
            if (!cells.TryGetValue(id, out var cell)) { draft.FunctionLinks.Remove(id); continue; }
            if (Normalize(cell.Function) == link.Value) continue;
            draft.FunctionLinks.Remove(id);
            if (!draft.FunctionOverrides.Contains(id, StringComparer.Ordinal)) draft.FunctionOverrides.Add(id);
            draft.ConfirmMapping = false;
        }
        foreach (var pin in draft.Ends.SelectMany(p => p.Pins))
        {
            if (!cells.TryGetValue(pin.PinId, out var cell) || draft.FunctionLinks.ContainsKey(pin.PinId)) continue;
            if (Normalize(pin.Function) == Normalize(cell.Function)) continue;
            if (!draft.FunctionOverrides.Contains(pin.PinId, StringComparer.Ordinal)) draft.FunctionOverrides.Add(pin.PinId);
            pin.Function = Normalize(cell.Function); draft.ConfirmMapping = false;
        }
        foreach (var component in Components(draft))
        {
            // Generated labels are never sources. A disconnected root cannot leave a stale label behind.
            foreach (var id in component.Where(draft.FunctionLinks.ContainsKey).ToArray())
            {
                var link = draft.FunctionLinks[id];
                if (component.Contains(link.SourcePinId) && !draft.FunctionLinks.ContainsKey(link.SourcePinId) &&
                    cells.TryGetValue(link.SourcePinId, out var source) && Normalize(source.Function) is { } value)
                {
                    if (link.Value != value) { Write(draft, id, value); draft.ConfirmMapping = false; }
                    draft.FunctionLinks[id] = new(link.SourcePinId, value);
                }
                else { Write(draft, id, null); draft.FunctionLinks.Remove(id); draft.ConfirmMapping = false; }
            }
            cells = Cells(draft);
            var sources = component.Where(id => !draft.FunctionLinks.ContainsKey(id) && Normalize(cells[id].Function) is not null)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray();
            if (sources.Length == 0 || sources.Select(id => Normalize(cells[id].Function)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1) continue;
            var root = sources[0]; var function = Normalize(cells[root].Function)!;
            foreach (var id in component.Where(id => Normalize(cells[id].Function) is null && !draft.FunctionOverrides.Contains(id, StringComparer.Ordinal)))
            { Write(draft, id, function); draft.FunctionLinks[id] = new(root, function); draft.ConfirmMapping = false; }
        }
    }

    public static IReadOnlyList<string> Conflicts(CableManufacturingDraft draft)
    {
        if (!draft.InventoryOnly) return [];
        var cells = Cells(draft);
        return Components(draft).Select(component => component.Where(id => !draft.FunctionLinks.ContainsKey(id))
            .Select(id => Normalize(cells[id].Function)).Where(value => value is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            .Where(values => values.Length > 1).Select(values => "功能標示不同（保留手動值）：" + string.Join(" / ", values)).ToArray();
    }

    public static IReadOnlyList<string> Validate(CableManufacturingDraft draft)
    {
        if (draft.FunctionLinks.Count == 0 && draft.FunctionOverrides.Count == 0) return [];
        var cells = Cells(draft);
        var groups = Components(draft);
        if (!draft.InventoryOnly || draft.FunctionOverrides.Distinct(StringComparer.Ordinal).Count() != draft.FunctionOverrides.Count ||
            draft.FunctionOverrides.Any(id => !cells.ContainsKey(id)) || draft.FunctionLinks.Any(pair =>
                !cells.ContainsKey(pair.Key) || !cells.ContainsKey(pair.Value.SourcePinId) || pair.Key == pair.Value.SourcePinId ||
                draft.FunctionOverrides.Contains(pair.Key, StringComparer.Ordinal) || draft.FunctionLinks.ContainsKey(pair.Value.SourcePinId) ||
                !groups.Any(g => g.Contains(pair.Key) && g.Contains(pair.Value.SourcePinId)) ||
                Normalize(pair.Value.Value) is null || Normalize(cells[pair.Key].Function) != pair.Value.Value ||
                Normalize(cells[pair.Value.SourcePinId].Function) != pair.Value.Value)) return ["Invalid exact-Pin function association."];
        return [];
    }

    private static Dictionary<string, CableManufacturingCell> Cells(CableManufacturingDraft draft) => draft.Rows
        .SelectMany(CableManufacturingCells.All).Where(c => c.SourcePinId is not null)
        .GroupBy(c => c.SourcePinId!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    private static IReadOnlyList<HashSet<string>> Components(CableManufacturingDraft draft)
    {
        var cells = Cells(draft);
        var neighbours = cells.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in CableWiringEditorService.Mapping(draft))
        {
            if (!neighbours.ContainsKey(edge.FromSourcePinId) || !neighbours.ContainsKey(edge.ToSourcePinId)) continue;
            neighbours[edge.FromSourcePinId].Add(edge.ToSourcePinId); neighbours[edge.ToSourcePinId].Add(edge.FromSourcePinId);
        }
        var visited = new HashSet<string>(StringComparer.Ordinal); var output = new List<HashSet<string>>();
        foreach (var id in neighbours.Keys)
        {
            if (!visited.Add(id)) continue;
            var group = new HashSet<string>(StringComparer.Ordinal) { id }; var queue = new Queue<string>(); queue.Enqueue(id);
            while (queue.TryDequeue(out var current)) foreach (var next in neighbours[current])
                if (visited.Add(next)) { group.Add(next); queue.Enqueue(next); }
            output.Add(group);
        }
        return output;
    }

    private static void Write(CableManufacturingDraft draft, string id, string? value)
    {
        foreach (var row in draft.Rows)
        foreach (var port in draft.Ends)
        {
            var cell = CableManufacturingCells.Get(draft, row, port.PortId);
            if (cell.SourcePinId != id) continue;
            cell.Function = value; CableManufacturingCells.Set(draft, row, port.PortId, cell);
        }
        var pin = draft.Ends.SelectMany(p => p.Pins).SingleOrDefault(p => p.PinId == id);
        if (pin is not null) pin.Function = value;
    }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) || value.Trim() == "待確認" ? null : value.Trim();
}
