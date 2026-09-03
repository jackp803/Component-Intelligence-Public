using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Editing;

public sealed record ProjectChangeSummary
{
    public IReadOnlyList<string> EngineeringChanges { get; init; } = [];
    public IReadOnlyList<string> DrawingStructureChanges { get; init; } = [];
    public IReadOnlyList<string> VisualChanges { get; init; } = [];

    public static ProjectChangeSummary Compare(ElectricalProject? before, ElectricalProject after)
    {
        ArgumentNullException.ThrowIfNull(after);
        if (before is null) return new ProjectChangeSummary { EngineeringChanges = ["ProjectCreated"] };
        var engineering = new List<string>(); var structure = new List<string>(); var visual = new List<string>();
        CompareIds(before.Components.Select(x => x.ComponentInstanceId), after.Components.Select(x => x.ComponentInstanceId), "Component", engineering);
        CompareIds(before.Connections.Select(x => x.ConnectionId), after.Connections.Select(x => x.ConnectionId), "Connection", engineering);
        CompareIds(before.Cables.Select(x => x.CableInstanceId), after.Cables.Select(x => x.CableInstanceId), "Cable", engineering);
        CompareIds(before.CableAssemblies.Select(x => x.CableAssemblyId), after.CableAssemblies.Select(x => x.CableAssemblyId), "CableAssembly", engineering);
        if (!LogicalEqual(before.Connections, after.Connections)) engineering.Add("ConnectionChanges");
        if (!LogicalEqual(before.Components, after.Components)) engineering.Add("ModifiedEngineeringObjects");
        var oldPlan = before.DrawingPlan; var newPlan = after.DrawingPlan;
        if (!LogicalEqual(oldPlan?.Pages, newPlan?.Pages)) structure.Add("PageAddDeleteOrderChanges");
        if (!LogicalEqual(oldPlan?.CrossPageRelations, newPlan?.CrossPageRelations)) structure.Add("DrawingStructureRelationsChanged");
        if (!LogicalEqual(oldPlan?.Placements, newPlan?.Placements)) visual.Add("PlacementMovesOrLockChanges");
        if (!LogicalEqual(oldPlan?.Routes, newPlan?.Routes)) visual.Add("RouteBendOrLockChanges");
        if (!LogicalEqual(oldPlan?.Groups, newPlan?.Groups)) visual.Add("GroupLayoutChanges");
        if (oldPlan?.SourcePlanningInputHash != newPlan?.SourcePlanningInputHash) structure.Add("RepresentationOrPlanningInputChanges");
        return new ProjectChangeSummary { EngineeringChanges = engineering.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray(), DrawingStructureChanges = structure.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray(), VisualChanges = visual.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray() };
    }

    private static void CompareIds(IEnumerable<string> before, IEnumerable<string> after, string kind, ICollection<string> changes)
    {
        var a = before.ToHashSet(StringComparer.Ordinal); var b = after.ToHashSet(StringComparer.Ordinal);
        if (b.Except(a, StringComparer.Ordinal).Any()) changes.Add($"Added{kind}Objects");
        if (a.Except(b, StringComparer.Ordinal).Any()) changes.Add($"Deleted{kind}Objects");
    }
    private static bool LogicalEqual<T>(T a, T b) => JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);
}

public enum ProjectRevisionTrigger { Save, GeneratePreview, GenerateAutoCad, MajorImport, TopologyChange, ManualRestore }
