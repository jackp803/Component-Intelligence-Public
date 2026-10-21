using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private string _autoFanoutNotice = "";
    private ElectricalProject CompleteExpandedFanoutEdit(ElectricalProject before, ElectricalProject after)
    {
        _autoFanoutNotice = "";
        if (before.Schematic is not { } original || after.Schematic is not { } document) return after;
        var oldSymbols = original.Symbols.ToDictionary(s => s.SymbolId, StringComparer.Ordinal);
        var oldWires = original.Wires.ToDictionary(w => w.WireId, StringComparer.Ordinal);
        var candidates = document.Symbols.Where(s => s.EndpointDisplayPlacements.Count > 0 && oldSymbols.GetValueOrDefault(s.SymbolId) is { } old &&
            (s.Position != old.Position || s.Rotation != old.Rotation || !s.EndpointDisplayPlacements.SequenceEqual(old.EndpointDisplayPlacements)))
            .Select(s => s.SymbolId).ToHashSet(StringComparer.Ordinal);
        foreach (var wire in document.Wires.Where(w => !oldWires.TryGetValue(w.WireId, out var old) || old.Start != w.Start || old.End != w.End))
            foreach (var a in new[] { wire.Start, wire.End }) if (a.SymbolId is { } id) candidates.Add(id);
        var warnings = new List<string>(); var aligned = 0;
        foreach (var id in candidates)
        {
            var result = _service.AutoAlignExpandedPorts(after, id);
            after = result.Project; aligned += result.AlignedPorts; warnings.AddRange(result.Warnings);
        }
        _autoFanoutNotice = warnings.Count > 0 ? "自動對齊未完成：" + string.Join("；", warnings.Distinct()) : aligned > 0 ? "已自動對齊展開接點並調整間距。" : "";
        return after;
    }
}
