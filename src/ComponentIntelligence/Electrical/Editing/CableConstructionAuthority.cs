using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Editing;

public sealed record CableConstructionState(string State, string Evidence, CableConstructionType Construction)
{
    public bool RequiresChoice => State == "Unknown Cable";
}

public static class CableConstructionAuthority
{
    public static CableConstructionState Describe(CableInstance? cable, ComponentIR? definition = null)
    {
        if (cable is null) return new("Ordinary Wire", "未指定 CableInstance", CableConstructionType.Unknown);
        if (cable.CableConstructionType is CableConstructionType.Purchased or CableConstructionType.Custom)
            return new(cable.CableConstructionType.ToString(), "已儲存的實體線材工程分類", cable.CableConstructionType);
        if (definition?.Identity.ComponentId == cable.CableDefinitionId && IsPurchased(definition.CableProduct))
            return new("Purchased", "型錄成品線證據：" + definition.CableProduct!.Evidence, CableConstructionType.Purchased);
        return new("Unknown Cable", "已有實體線材歸屬，但尚無明確製作方式證據", CableConstructionType.Unknown);
    }

    public static bool IsPurchased(CableProductAuthority? authority) =>
        authority?.Kind == CableProductKind.PurchasedPreassembled && !string.IsNullOrWhiteSpace(authority.Evidence);

    public static ElectricalProject ApplyDefinitions(ElectricalProject original, IEnumerable<ComponentIR> definitions)
    {
        var catalog = definitions.ToDictionary(c => c.Identity.ComponentId, StringComparer.Ordinal);
        var draft = EngineeringReviewService.Clone(original);
        foreach (var cable in draft.Cables)
        {
            catalog.TryGetValue(cable.CableDefinitionId, out var definition);
            cable.CableConstructionType = Describe(cable, definition).Construction;
        }
        return draft;
    }
}
