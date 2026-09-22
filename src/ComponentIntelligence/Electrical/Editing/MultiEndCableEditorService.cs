using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Editing;

public sealed class MultiEndCableDraft
{
    public required string AssemblyId { get; init; }
    public required string CableId { get; init; }
    public bool IsNew { get; init; }
    public string? Reference { get; set; }
    public CableConstructionType ConstructionType { get; set; }
    public string? CommonPortId { get; set; }
    public double? TrunkLengthMm { get; set; }
    public List<string> ConnectionIds { get; init; } = new();
    public List<MultiEndCableBranch> Branches { get; init; } = new();
    internal Dictionary<string, string> OriginalConnections { get; init; } = new(StringComparer.Ordinal);
    internal string? OriginalAssembly { get; init; }
    internal string? OriginalCable { get; init; }
}

public sealed record MultiEndPortContext(string PortId, string Label);
public sealed record MultiEndConductorContext(string ConnectionId, string Label, string FromPortId, string ToPortId);

public sealed class MultiEndCableEditorService
{
    public MultiEndCableDraft PrepareNew(ElectricalProject project, IReadOnlyCollection<string> connectionIds)
    {
        var draft = new MultiEndCableDraft
        {
            AssemblyId = $"cable-assembly-{Guid.NewGuid():N}", CableId = $"cbl-{Guid.NewGuid():N}", IsNew = true,
            ConnectionIds = connectionIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            OriginalConnections = SnapshotConnections(project)
        };
        ResolveConductors(project, draft);
        return draft;
    }

    public MultiEndCableDraft PrepareExisting(ElectricalProject project, string assemblyId)
    {
        var assembly = project.CableAssemblies.Single(a => a.CableAssemblyId == assemblyId);
        var topology = assembly.PhysicalTopology ?? throw new InvalidOperationException("此複合線請使用既有線段編輯器。");
        var cable = project.Cables.Single(c => c.CableInstanceId == topology.CableInstanceId);
        return new MultiEndCableDraft
        {
            AssemblyId = assemblyId, CableId = cable.CableInstanceId, IsNew = false,
            Reference = assembly.ReferenceDesignator, ConstructionType = assembly.CableConstructionType,
            CommonPortId = topology.CommonPortId, TrunkLengthMm = topology.TrunkLengthMm,
            ConnectionIds = topology.ConnectionIds.ToList(),
            Branches = topology.Branches.Select(CopyBranch).ToList(),
            OriginalConnections = SnapshotConnections(project),
            OriginalAssembly = JsonSerializer.Serialize(assembly), OriginalCable = JsonSerializer.Serialize(cable)
        };
    }

    public IReadOnlyList<MultiEndPortContext> GetEnds(ElectricalProject project, MultiEndCableDraft draft) =>
        ResolveConductors(project, draft).SelectMany(c => new[] { c.FromPortId, c.ToPortId })
            .Distinct(StringComparer.Ordinal).Select(id => new MultiEndPortContext(id, PortLabel(project, id)))
            .OrderBy(x => x.Label, StringComparer.Ordinal).ToArray();

    public IReadOnlyList<MultiEndConductorContext> GetConductors(ElectricalProject project, MultiEndCableDraft draft) =>
        ResolveConductors(project, draft);

    public void ConfirmEnds(ElectricalProject project, MultiEndCableDraft draft, string commonPortId)
    {
        var branches = BranchPorts(ResolveConductors(project, draft), commonPortId);
        var previous = draft.Branches.ToDictionary(b => b.PortId, StringComparer.Ordinal);
        var next = draft.Branches.Select(b => b.Index).DefaultIfEmpty(0).Max() + 1;
        var confirmed = branches.OrderBy(id => PortLabel(project, id), StringComparer.Ordinal)
            .Select(id => previous.TryGetValue(id, out var old) ? CopyBranch(old) :
                new MultiEndCableBranch { PortId = id, Index = next++ }).ToArray();
        draft.CommonPortId = commonPortId;
        draft.Branches.Clear();
        draft.Branches.AddRange(confirmed);
    }

    public void Validate(ElectricalProject project, MultiEndCableDraft draft)
    {
        if (draft.ConstructionType is not (CableConstructionType.Purchased or CableConstructionType.Custom))
            throw new InvalidOperationException("請明確選擇外購 Purchased 或自製 Custom；多端形狀不代表自製。");
        if (string.IsNullOrWhiteSpace(draft.CommonPortId))
            throw new InvalidOperationException("請選擇共同端，並確認分支端分組。");
        var expected = BranchPorts(ResolveConductors(project, draft), draft.CommonPortId);
        if (!expected.SetEquals(draft.Branches.Select(b => b.PortId)) || draft.Branches.Count != expected.Count)
            throw new InvalidOperationException("配線範圍或共同端已變更，請重新確認分支分組。");
        if (draft.Branches.Any(b => b.Index <= 0) || draft.Branches.Select(b => b.Index).Distinct().Count() != draft.Branches.Count)
            throw new InvalidOperationException("分支編號必須是唯一的正整數。");
        CheckLength(draft.TrunkLengthMm);
        foreach (var branch in draft.Branches) CheckLength(branch.LengthMm);
        var existing = project.CableAssemblies.SingleOrDefault(a => a.CableAssemblyId == draft.AssemblyId);
        var cable = project.Cables.SingleOrDefault(c => c.CableInstanceId == draft.CableId);
        if (draft.IsNew ? existing is not null || cable is not null :
            existing is null || cable is null || JsonSerializer.Serialize(existing) != draft.OriginalAssembly ||
            JsonSerializer.Serialize(cable) != draft.OriginalCable)
            throw new InvalidOperationException("線材資料已變更，請關閉後重新開啟編輯器。");
        foreach (var id in (existing?.PhysicalTopology?.ConnectionIds ?? []).Concat(draft.ConnectionIds).Distinct(StringComparer.Ordinal))
        {
            var current = project.Connections.SingleOrDefault(c => c.ConnectionId == id);
            if (current is null || !draft.OriginalConnections.TryGetValue(id, out var original) || JsonSerializer.Serialize(current) != original)
                throw new InvalidOperationException("原始配線已變更，請重新選取；未套用任何修改。");
        }
        if (!draft.IsNew && project.Connections.Any(c => c.CableInstanceId == draft.CableId &&
            !existing!.PhysicalTopology!.ConnectionIds.Contains(c.ConnectionId, StringComparer.Ordinal)))
            throw new InvalidOperationException("線材另有未列入實體結構的配線，請先確認歸屬。");
    }

    public CableAssembly Apply(ElectricalProject project, MultiEndCableDraft draft)
    {
        Validate(project, draft);
        var existing = project.CableAssemblies.SingleOrDefault(a => a.CableAssemblyId == draft.AssemblyId);
        var cable = project.Cables.SingleOrDefault(c => c.CableInstanceId == draft.CableId);
        var replacement = new CableAssembly
        {
            CableAssemblyId = draft.AssemblyId, ReferenceDesignator = draft.Reference?.Trim(),
            CableConstructionType = draft.ConstructionType, IsCustom = existing?.IsCustom ?? false,
            Members = [new CableAssemblyMember { CableInstanceId = draft.CableId }],
            PhysicalTopology = new MultiEndCableTopology
            {
                CableInstanceId = draft.CableId, CommonPortId = draft.CommonPortId!, TrunkLengthMm = draft.TrunkLengthMm,
                Branches = draft.Branches.OrderBy(b => b.Index).Select(CopyBranch).ToList(),
                ConnectionIds = draft.ConnectionIds.Order(StringComparer.Ordinal).ToList()
            }
        };
        if (cable is null)
        {
            cable = new CableInstance { CableInstanceId = draft.CableId, CableDefinitionId = "UNRESOLVED-CABLE" };
            project.Cables.Add(cable);
        }
        cable.CableConstructionType = draft.ConstructionType;
        cable.ReferenceDesignator = replacement.ReferenceDesignator;
        foreach (var connection in project.Connections)
        {
            if (draft.ConnectionIds.Contains(connection.ConnectionId, StringComparer.Ordinal))
                connection.CableInstanceId = draft.CableId;
            else if (existing?.PhysicalTopology?.ConnectionIds.Contains(connection.ConnectionId, StringComparer.Ordinal) == true)
                connection.CableInstanceId = null;
        }
        if (existing is null) project.CableAssemblies.Add(replacement);
        else project.CableAssemblies[project.CableAssemblies.IndexOf(existing)] = replacement;
        return replacement;
    }

    private static IReadOnlyList<MultiEndConductorContext> ResolveConductors(ElectricalProject project, MultiEndCableDraft draft)
    {
        if (draft.ConnectionIds.Count < 2 || draft.ConnectionIds.Distinct(StringComparer.Ordinal).Count() != draft.ConnectionIds.Count)
            throw new InvalidOperationException("請選擇至少兩條不同的 Pin 對 Pin 配線。");
        var pins = project.Components.SelectMany(c => c.Ports.SelectMany(p => p.Pins.Select(pin => (Component: c, Port: p, Pin: pin))))
            .GroupBy(x => x.Pin.PinId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var result = new List<MultiEndConductorContext>();
        foreach (var id in draft.ConnectionIds)
        {
            var matches = project.Connections.Where(c => c.ConnectionId == id).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException($"配線不存在或 ID 重複：{id}");
            var c = matches[0];
            if (c.Kind != ConnectionKind.Wire || !pins.TryGetValue(c.FromEndpointId, out var from) || from.Length != 1 ||
                !pins.TryGetValue(c.ToEndpointId, out var to) || to.Length != 1 || from[0].Port.PortId == to[0].Port.PortId)
                throw new InvalidOperationException($"需要明確且唯一歸屬的 Pin 對 Pin 普通配線：{id}");
            if (!string.IsNullOrWhiteSpace(c.CableInstanceId) && (draft.IsNew || c.CableInstanceId != draft.CableId))
                throw new InvalidOperationException($"此配線已有其他 Cable 歸屬，不能自動吸收：{id}");
            if (project.CableAssemblies.Any(a => a.CableAssemblyId != draft.AssemblyId &&
                (a.PhysicalTopology?.ConnectionIds.Contains(id, StringComparer.Ordinal) == true ||
                 a.Members.Any(m => m.CableInstanceId == draft.CableId))))
                throw new InvalidOperationException($"此配線或線材已屬於其他複合線：{id}");
            foreach (var portId in new[] { from[0].Port.PortId, to[0].Port.PortId })
                if (project.Components.SelectMany(x => x.Ports).Count(p => p.PortId == portId) != 1)
                    throw new InvalidOperationException("介面歸屬不唯一，請先確認專案資料。");
            result.Add(new MultiEndConductorContext(id,
                $"{PortLabel(project, from[0].Port.PortId)} / Pin {from[0].Pin.PinNumber} -> {PortLabel(project, to[0].Port.PortId)} / Pin {to[0].Pin.PinNumber}",
                from[0].Port.PortId, to[0].Port.PortId));
        }
        return result;
    }

    private static HashSet<string> BranchPorts(IReadOnlyList<MultiEndConductorContext> conductors, string common)
    {
        if (conductors.Any(c => c.FromPortId != common && c.ToPortId != common))
            throw new InvalidOperationException("選取配線並非共用此共同端，請重新選取或確認共同端。");
        var branches = conductors.Select(c => c.FromPortId == common ? c.ToPortId : c.FromPortId).ToHashSet(StringComparer.Ordinal);
        if (branches.Count < 2) throw new InvalidOperationException("多端線材需要至少兩個不同的分支介面。");
        return branches;
    }

    private static string PortLabel(ElectricalProject project, string portId)
    {
        var owner = project.Components.SelectMany(c => c.Ports.Select(p => (Component: c, Port: p))).Single(x => x.Port.PortId == portId);
        var other = owner.Port.Connector is null ? string.Join(", ", owner.Component.Ports.Where(p => p.Connector is not null).Select(p => p.Connector!.Family).Distinct()) : null;
        return $"{owner.Component.ReferenceDesignator ?? owner.Component.DisplayName ?? "未命名元件"} / {owner.Port.Name}" +
            (owner.Port.Connector is { } connector ? $" / {connector.Family} {connector.Coding} {connector.Gender}" :
                string.IsNullOrEmpty(other) ? "" : $"（元件另有 {other} 介面；腳位不互換）");
    }

    private static void CheckLength(double? value)
    {
        if (value is { } length && (!double.IsFinite(length) || length <= 0))
            throw new InvalidOperationException("長度必須是大於零的有限 mm 數值；未知請留白。");
    }
    private static MultiEndCableBranch CopyBranch(MultiEndCableBranch branch) => new() { PortId = branch.PortId, Index = branch.Index, LengthMm = branch.LengthMm };
    private static Dictionary<string, string> SnapshotConnections(ElectricalProject p) => p.Connections.ToDictionary(c => c.ConnectionId, c => JsonSerializer.Serialize(c), StringComparer.Ordinal);
}
