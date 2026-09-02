using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Editing;

public enum CableAssemblyOpenStatus
{
    Found,
    NotInAssembly
}

public sealed record CableAssemblyOpenResult(
    CableAssemblyOpenStatus Status,
    CableAssemblyEditDraft? Draft);

public sealed class CableAssemblyEditDraft
{
    public required string CableAssemblyId { get; init; }
    public bool IsNew { get; init; }
    public string? ReferenceDesignator { get; set; }
    public CableConstructionType CableConstructionType { get; set; } = CableConstructionType.Unknown;
    public List<CableAssemblyMemberDraft> Members { get; init; } = new();
}

public sealed class CableAssemblyMemberDraft
{
    public required string CableInstanceId { get; init; }
    public required string DisplayLabel { get; init; }
    public string? EndpointSummary { get; init; }
    public CableAssemblySegmentRoleType SegmentRoleType { get; set; } = CableAssemblySegmentRoleType.Unknown;
    public int? SegmentRoleIndex { get; set; }
    public string? SegmentRoleName { get; set; }
    public double? ProvidedLengthMm { get; set; }
    public CableLengthSource LengthSource { get; set; } = CableLengthSource.Unknown;
    public double? OriginalProvidedLengthMm { get; init; }
    public CableLengthSource OriginalLengthSource { get; init; } = CableLengthSource.Unknown;
    public bool LengthWasEdited { get; set; }
    public string? LengthInputError { get; set; }
}

public sealed class CableAssemblyEditorService
{
    public CableAssemblyEditDraft PrepareNewFromConnections(
        ElectricalProject project,
        IReadOnlyCollection<string> connectionIds)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(connectionIds);

        var cableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var connectionId in connectionIds)
        {
            var connection = FindConnection(project, connectionId);
            if (string.IsNullOrWhiteSpace(connection.CableInstanceId))
                throw new InvalidOperationException($"連線 '{connection.ConnectionId}' 是普通配線，尚未明確指定為 Cable Segment。");

            var cable = FindCable(project, connection.CableInstanceId);
            EnsureUnowned(project, cable.CableInstanceId, exceptAssemblyId: null);
            cableIds.Add(cable.CableInstanceId);
        }

        if (cableIds.Count < 2)
            throw new InvalidOperationException("建立複合線至少需要兩個不同且已明確指定的 Cable Segment。");

        return new CableAssemblyEditDraft
        {
            CableAssemblyId = $"cable-assembly-{Guid.NewGuid():N}",
            IsNew = true,
            CableConstructionType = CableConstructionType.Unknown,
            Members = cableIds
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(id => id, StringComparer.Ordinal)
                .Select(id => CreateMemberDraft(project, FindCable(project, id), member: null))
                .ToList()
        };
    }

    public CableAssemblyOpenResult PrepareExistingFromConnection(
        ElectricalProject project,
        string connectionId)
    {
        ArgumentNullException.ThrowIfNull(project);
        var connection = FindConnection(project, connectionId);
        if (string.IsNullOrWhiteSpace(connection.CableInstanceId))
            return new CableAssemblyOpenResult(CableAssemblyOpenStatus.NotInAssembly, null);

        FindCable(project, connection.CableInstanceId);
        var owners = project.CableAssemblies
            .Where(assembly => assembly.Members.Any(member =>
                string.Equals(member.CableInstanceId, connection.CableInstanceId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (owners.Length == 0)
            return new CableAssemblyOpenResult(CableAssemblyOpenStatus.NotInAssembly, null);
        if (owners.Length > 1)
            throw new InvalidOperationException(
                $"線段 '{connection.CableInstanceId}' 同時屬於多個複合線：{string.Join(", ", owners.Select(item => item.CableAssemblyId))}。");

        var assembly = owners[0];
        var draft = new CableAssemblyEditDraft
        {
            CableAssemblyId = assembly.CableAssemblyId,
            IsNew = false,
            ReferenceDesignator = assembly.ReferenceDesignator,
            CableConstructionType = assembly.CableConstructionType,
            Members = assembly.Members
                .Select(member => CreateMemberDraft(project, FindCable(project, member.CableInstanceId), member))
                .ToList()
        };
        return new CableAssemblyOpenResult(CableAssemblyOpenStatus.Found, draft);
    }

    public int SuggestNextBranchIndex(CableAssemblyEditDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return draft.Members
            .Where(member => member.SegmentRoleType == CableAssemblySegmentRoleType.Branch)
            .Select(member => member.SegmentRoleIndex.GetValueOrDefault())
            .Where(index => index > 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
    }

    private static ElectricalConnection FindConnection(ElectricalProject project, string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        return project.Connections.FirstOrDefault(item =>
                   string.Equals(item.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"找不到連線 '{connectionId}'。");
    }

    private static CableInstance FindCable(ElectricalProject project, string cableInstanceId) =>
        project.Cables.FirstOrDefault(item =>
            string.Equals(item.CableInstanceId, cableInstanceId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"找不到 CableInstance '{cableInstanceId}'。");

    private static void EnsureUnowned(ElectricalProject project, string cableInstanceId, string? exceptAssemblyId)
    {
        var owner = project.CableAssemblies.FirstOrDefault(assembly =>
            !string.Equals(assembly.CableAssemblyId, exceptAssemblyId, StringComparison.OrdinalIgnoreCase) &&
            assembly.Members.Any(member =>
                string.Equals(member.CableInstanceId, cableInstanceId, StringComparison.OrdinalIgnoreCase)));
        if (owner is not null)
            throw new InvalidOperationException(
                $"線段 '{cableInstanceId}' 已屬於複合線 '{owner.CableAssemblyId}'，不可自動重新歸屬。");
    }

    private static CableAssemblyMemberDraft CreateMemberDraft(
        ElectricalProject project,
        CableInstance cable,
        CableAssemblyMember? member)
    {
        var endpointSummary = string.Join(
            "; ",
            project.Connections
                .Where(connection => string.Equals(
                    connection.CableInstanceId,
                    cable.CableInstanceId,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(connection => connection.ConnectionId, StringComparer.OrdinalIgnoreCase)
                .Select(connection => $"{connection.FromEndpointId} -> {connection.ToEndpointId}"));

        return new CableAssemblyMemberDraft
        {
            CableInstanceId = cable.CableInstanceId,
            DisplayLabel = cable.ReferenceDesignator ?? cable.DisplayName ?? cable.CableInstanceId,
            EndpointSummary = string.IsNullOrWhiteSpace(endpointSummary) ? null : endpointSummary,
            SegmentRoleType = member?.SegmentRoleType ?? CableAssemblySegmentRoleType.Unknown,
            SegmentRoleIndex = member?.SegmentRoleIndex,
            SegmentRoleName = member?.SegmentRoleName,
            ProvidedLengthMm = cable.ProvidedLengthMm,
            LengthSource = cable.LengthSource,
            OriginalProvidedLengthMm = cable.ProvidedLengthMm,
            OriginalLengthSource = cable.LengthSource
        };
    }
}
