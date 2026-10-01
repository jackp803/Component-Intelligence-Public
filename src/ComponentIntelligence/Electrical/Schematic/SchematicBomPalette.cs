using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicBomPaletteEntry(string Key, string Label, string Category, string? Subcategory,
    string? DefinitionId, int? UsedQuantity, int? TotalQuantity, int? SpareQuantity,
    bool Imported, bool ConnectionMaterial, IReadOnlyList<string> InstanceIds, int PlacedCount)
{
    public int RemainingCount => InstanceIds.Count - PlacedCount;
    public string QuantityLabel => Imported && UsedQuantity is null ? $"數量待確認 · 已放 {PlacedCount}" :
        $"{(Imported ? "使用" : "專案")} {UsedQuantity} · 已放 {PlacedCount} · 待放 {RemainingCount}";
    public string StockLabel => Imported && (TotalQuantity is not null || SpareQuantity is not null)
        ? $"總數 {TotalQuantity?.ToString() ?? "?"} · 備品 {SpareQuantity?.ToString() ?? "?"}" : "";
}

public static class SchematicBomPalette
{
    public static IReadOnlyList<SchematicBomPaletteEntry> Build(ElectricalProject project, IReadOnlyList<ComponentIR> catalog)
    {
        var definitions = catalog.GroupBy(c => c.Identity.ComponentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var represented = project.Schematic?.Symbols.Select(s => s.ComponentInstanceId).ToHashSet(StringComparer.Ordinal) ?? [];
        var existing = project.Components.Select(c => c.ComponentInstanceId).ToHashSet(StringComparer.Ordinal);
        var entries = new List<SchematicBomPaletteEntry>();
        if (project.BomItems.Count > 0)
        {
            foreach (var group in project.BomItems.Select(item => (Item: item, Source: Resolve(item, catalog, definitions)))
                .GroupBy(item => item.Source?.Identity.ComponentId ?? ItemKey(item.Item), StringComparer.Ordinal))
            {
                var items = group.Select(i => i.Item).ToArray();
                var first = items.FirstOrDefault(i => i.ComponentDefinitionId is not null) ?? items[0];
                var source = group.Select(i => i.Source).FirstOrDefault(i => i is not null);
                var ids = items.SelectMany(i => i.ComponentInstanceIds).Where(existing.Contains).Distinct(StringComparer.Ordinal).ToArray();
                entries.Add(new(group.Key, $"{first.Row.Manufacturer} {first.Row.ModelOrPartNumber}".Trim(),
                    Category(source?.Classification.Category ?? first.Category), source?.Classification.Subcategory ?? first.Subcategory,
                    source?.Identity.ComponentId ?? first.ComponentDefinitionId, Sum(items.Select(i => i.Row.UsedQuantity)),
                    Sum(items.Select(i => i.Row.TotalQuantity)), Sum(items.Select(i => i.Row.SpareQuantity)), true,
                    items.Any(i => i.ConnectionMaterial), ids, ids.Count(represented.Contains)));
            }
        }
        else
        {
            // Older projects did not store the imported rows; retain only their own physical inventory.
            foreach (var group in project.Components.GroupBy(c => c.ComponentDefinitionId, StringComparer.Ordinal))
            {
                definitions.TryGetValue(group.Key, out var source);
                var first = group.First();
                var ids = group.Select(c => c.ComponentInstanceId).Distinct(StringComparer.Ordinal).ToArray();
                entries.Add(new(group.Key, source is null ? first.DisplayName ?? first.TypeKey :
                    $"{source.Identity.Manufacturer} {source.Identity.Model}", Category(source?.Classification.Category),
                    source?.Classification.Subcategory, group.Key, ids.Length, null, null, false, false,
                    ids, ids.Count(represented.Contains)));
            }
        }
        return entries.OrderBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Label, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string NextInstance(ElectricalProject project, SchematicBomPaletteEntry entry)
    {
        if (entry.ConnectionMaterial) throw new InvalidOperationException("此 BOM 項目是連線材料，請使用線材庫或線材設定。");
        var represented = project.Schematic?.Symbols.Select(s => s.ComponentInstanceId).ToHashSet(StringComparer.Ordinal) ?? [];
        return entry.InstanceIds.FirstOrDefault(id => project.Components.Any(c => c.ComponentInstanceId == id) && !represented.Contains(id))
            ?? throw new InvalidOperationException("此 BOM 項目沒有待放置的實體。另一個圖面表示請從既有元件建立。");
    }

    public static IReadOnlyList<BomConnectionMaterialOption> MaterialOptions(ElectricalProject project) =>
        project.BomItems.Where(i => i.ConnectionMaterial && i.ComponentDefinitionId is not null)
            .GroupBy(i => i.ComponentDefinitionId!, StringComparer.Ordinal).Select(g =>
            {
                var first = g.First();
                return new BomConnectionMaterialOption(g.Key, first.Row.Manufacturer ?? "", first.Row.ModelOrPartNumber ?? "",
                    Category(first.Category), Sum(g.Select(i => i.Row.UsedQuantity))) { CableProduct = first.CableProduct };
            }).ToArray();

    private static string ItemKey(ProjectBomItem item) =>
        string.IsNullOrWhiteSpace(item.Row.Manufacturer) || string.IsNullOrWhiteSpace(item.Row.ModelOrPartNumber)
            ? "row:" + item.Row.RowId : $"identity:{item.Row.Manufacturer.Trim().ToUpperInvariant()}\u001f{item.Row.ModelOrPartNumber.Trim().ToUpperInvariant()}";
    private static string Category(string? value) => string.IsNullOrWhiteSpace(value) ? "未分類" : value.Trim();
    private static int? Sum(IEnumerable<int?> values)
    {
        var items = values.ToArray();
        return items.Any(v => v is null) ? null : items.Sum(v => v!.Value);
    }
    private static ComponentIR? Resolve(ProjectBomItem item, IReadOnlyList<ComponentIR> catalog,
        IReadOnlyDictionary<string, ComponentIR> definitions)
    {
        if (item.ComponentDefinitionId is not null && definitions.TryGetValue(item.ComponentDefinitionId, out var exact)) return exact;
        var matches = catalog.Where(c => string.Equals(c.Identity.Manufacturer, item.Row.Manufacturer?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Identity.Model, item.Row.ModelOrPartNumber?.Trim(), StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
