using System.Text.Json;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    public ElectricalProject AddBomComponent(ElectricalProject project, ComponentIR definition, int quantity)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(definition);
        if (quantity is < 1 or > 1000) throw new InvalidOperationException("新增數量必須為 1 到 1000 的整數。");
        if (string.IsNullOrWhiteSpace(definition.Identity.ComponentId) || string.IsNullOrWhiteSpace(definition.Identity.Manufacturer) ||
            string.IsNullOrWhiteSpace(definition.Identity.Model)) throw new InvalidOperationException("請填寫廠牌及型號／元件名稱。");
        if (ComponentMaterialRolePolicy.Classify(definition) == BomTopologyDisposition.DeferredConnectionMaterial)
            throw new InvalidOperationException("線材及接頭請從線材庫新增，不能當成一般元件。");
        if (definition.Ports.Any(p => string.IsNullOrWhiteSpace(p.PortId)) ||
            definition.Ports.Select(p => p.PortId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != definition.Ports.Count ||
            definition.Pins.Any(p => string.IsNullOrWhiteSpace(p.PinNumber)) ||
            definition.Pins.GroupBy(p => (p.PortId?.Trim().ToUpperInvariant() ?? "") + "\u001f" + p.PinNumber.Trim().ToUpperInvariant()).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Port 名稱與同一 Port 內的 Pin 編號不能空白或重複。");

        var draft = EngineeringReviewService.Clone(project);
        var snapshot = JsonSerializer.Deserialize<ComponentIR>(JsonSerializer.Serialize(definition))!;
        var additions = Enumerable.Range(0, quantity).Select(_ => {
            var instance = new ComponentProjectBridge().CreateInstance(snapshot, "component-" + Guid.NewGuid().ToString("N"));
            instance.Footprint = ComponentPhysicalKnowledgeMapper.TryCreateFootprint(snapshot);
            instance.ResponsibilityScope = ResponsibilityScope.InScope;
            var ids = instance.Ports.Select(p => p.PortId).Concat(instance.Ports.SelectMany(p => p.Pins).Select(p => p.PinId)).ToArray();
            if (ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length)
                throw new InvalidOperationException("Port／Pin 識別碼衝突，請檢查接點設定。");
            return instance;
        }).ToArray();

        // Legacy/unlisted physical instances must not disappear when the first BOM row is added.
        var listed = draft.BomItems.SelectMany(b => b.ComponentInstanceIds).ToHashSet(StringComparer.Ordinal);
        foreach (var group in draft.Components.Where(c => !listed.Contains(c.ComponentInstanceId)).GroupBy(c => c.ComponentDefinitionId))
            draft.BomItems.Add(new() { ComponentDefinitionId = group.Key,
                Row = new() { RowId = "EXISTING-" + Guid.NewGuid().ToString("N"), ModelOrPartNumber = group.First().DisplayName ?? group.First().TypeKey,
                    UsedQuantity = group.Count(), TotalQuantity = group.Count(), SpareQuantity = 0 },
                ComponentInstanceIds = group.Select(c => c.ComponentInstanceId).ToArray() });
        draft.Components.AddRange(additions);
        draft.BomItems.Add(new() { ComponentDefinitionId = snapshot.Identity.ComponentId, DefinitionSnapshot = snapshot,
            Category = snapshot.Classification.Category, Subcategory = snapshot.Classification.Subcategory,
            Row = new() { RowId = "PROJECT-" + Guid.NewGuid().ToString("N"), Manufacturer = snapshot.Identity.Manufacturer.Trim(),
                ModelOrPartNumber = snapshot.Identity.Model.Trim(), UsedQuantity = quantity, TotalQuantity = quantity, SpareQuantity = 0 },
            ComponentInstanceIds = additions.Select(c => c.ComponentInstanceId).ToArray() });
        return draft;
    }
}
