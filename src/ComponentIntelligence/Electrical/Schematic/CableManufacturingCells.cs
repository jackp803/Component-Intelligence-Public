using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

// Legacy P1/P2 fields remain readable; later columns use stable source Port IDs.
public static class CableManufacturingCells
{
    public static CableManufacturingCell Get(CableManufacturingDraft draft, CableManufacturingRow row, string portId) =>
        portId == draft.FromSourcePortId ? new() { SourcePinId = row.FromSourcePinId, Function = row.FromFunction, Usage = row.FromUsage } :
        portId == draft.ToSourcePortId ? new() { SourcePinId = row.ToSourcePinId, Function = row.ToFunction, Usage = row.ToUsage } :
        row.AdditionalEnds.GetValueOrDefault(portId) ?? new();

    public static void Set(CableManufacturingDraft draft, CableManufacturingRow row, string portId, CableManufacturingCell cell)
    {
        if (portId == draft.FromSourcePortId)
        { row.FromSourcePinId = cell.SourcePinId; row.FromFunction = cell.Function; row.FromUsage = cell.Usage; }
        else if (portId == draft.ToSourcePortId)
        { row.ToSourcePinId = cell.SourcePinId; row.ToFunction = cell.Function; row.ToUsage = cell.Usage; }
        else row.AdditionalEnds[portId] = cell;
    }

    public static IEnumerable<CableManufacturingCell> All(CableManufacturingRow row)
    {
        yield return new() { SourcePinId = row.FromSourcePinId, Function = row.FromFunction, Usage = row.FromUsage };
        yield return new() { SourcePinId = row.ToSourcePinId, Function = row.ToFunction, Usage = row.ToUsage };
        foreach (var cell in row.AdditionalEnds.Values) yield return cell;
    }

    public static IEnumerable<CablePinMapping> Pairs(CableManufacturingRow row)
    {
        var ids = All(row).Where(c => c.SourcePinId is not null).Select(c => c.SourcePinId!).ToArray();
        for (var i = 1; i < ids.Length; i++) yield return new(ids[0], ids[i]);
    }

    public static void EnsureColumns(CableManufacturingDraft draft)
    {
        foreach (var row in draft.Rows)
            foreach (var port in draft.Ends.Where(p => p.PortId != draft.FromSourcePortId && p.PortId != draft.ToSourcePortId))
                row.AdditionalEnds.TryAdd(port.PortId, new());
    }

    public static string Path(CableManufacturingDraft draft, string portId, string field) =>
        portId == draft.FromSourcePortId ? field switch { "SourcePinId" => "FromSourcePinId", "Function" => "FromFunction", _ => "FromUsage" } :
        portId == draft.ToSourcePortId ? field switch { "SourcePinId" => "ToSourcePinId", "Function" => "ToFunction", _ => "ToUsage" } :
        $"AdditionalEnds[{portId}].{field}";
}
