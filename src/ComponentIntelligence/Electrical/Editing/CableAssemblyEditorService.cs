using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Validation;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    public string? LegacyPurpose { get; init; }
    public double? ProvidedLengthMm { get; set; }
    public CableLengthSource LengthSource { get; set; } = CableLengthSource.Unknown;
    public double? OriginalProvidedLengthMm { get; init; }
    public CableLengthSource OriginalLengthSource { get; init; } = CableLengthSource.Unknown;
    public bool LengthWasEdited { get; set; }
    public string? LengthInputError { get; set; }
}

public enum CableAssemblyEditIssueSeverity
{
    Warning,
    Block
}

public sealed record CableAssemblyEditIssue(
    string Code,
    CableAssemblyEditIssueSeverity Severity,
    string Message,
    IReadOnlyList<string> SourceObjectIds)
{
    public bool IsBlocking => Severity == CableAssemblyEditIssueSeverity.Block;
}

public sealed record CableAssemblyEditValidation(IReadOnlyList<CableAssemblyEditIssue> Issues)
{
    public bool CanSave => Issues.All(issue => !issue.IsBlocking);
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

    public void SetLengthMetres(CableAssemblyMemberDraft member, string? metresText)
    {
        ArgumentNullException.ThrowIfNull(member);
        member.LengthWasEdited = true;
        member.LengthInputError = null;

        if (string.IsNullOrWhiteSpace(metresText))
        {
            member.ProvidedLengthMm = null;
            member.LengthSource = CableLengthSource.Unknown;
            return;
        }

        if (!TryParsePositiveNumber(metresText, out var metres))
        {
            member.LengthInputError = "長度必須是大於 0 的數字（公尺）。";
            return;
        }

        member.ProvidedLengthMm = metres * 1000d;
        member.LengthSource = CableLengthSource.User;
    }

    public CableAssemblyMemberDraft AddMember(
        ElectricalProject project,
        CableAssemblyEditDraft draft,
        string cableInstanceId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Members.Any(member => string.Equals(
                member.CableInstanceId,
                cableInstanceId,
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"線段 '{cableInstanceId}' 已在目前複合線中。");

        var cable = FindCable(project, cableInstanceId);
        EnsureUnowned(project, cable.CableInstanceId, draft.IsNew ? null : draft.CableAssemblyId);
        var member = CreateMemberDraft(project, cable, member: null);
        draft.Members.Add(member);
        return member;
    }

    public bool RemoveMember(CableAssemblyEditDraft draft, string cableInstanceId)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var member = draft.Members.FirstOrDefault(item => string.Equals(
            item.CableInstanceId,
            cableInstanceId,
            StringComparison.OrdinalIgnoreCase));
        return member is not null && draft.Members.Remove(member);
    }

    public IReadOnlyList<CableAssemblyMemberDraft> GetEligibleMembers(
        ElectricalProject project,
        CableAssemblyEditDraft draft)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(draft);
        return project.Cables
            .Where(cable => !draft.Members.Any(member => string.Equals(
                member.CableInstanceId,
                cable.CableInstanceId,
                StringComparison.OrdinalIgnoreCase)))
            .Where(cable => !HasOtherOwner(project, cable.CableInstanceId, draft.IsNew ? null : draft.CableAssemblyId))
            .OrderBy(cable => cable.ReferenceDesignator ?? cable.DisplayName ?? cable.CableInstanceId, StringComparer.OrdinalIgnoreCase)
            .Select(cable => CreateMemberDraft(project, cable, member: null))
            .ToArray();
    }

    public CableAssemblyEditValidation Validate(
        ElectricalProject project,
        CableAssemblyEditDraft draft)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(draft);
        var issues = new List<CableAssemblyEditIssue>();

        if (draft.IsNew && draft.Members.Count < 2)
            issues.Add(Block("INPUT-ASSEMBLY-MEMBERS", "複合線至少需要兩個線段。", [draft.CableAssemblyId]));

        foreach (var member in draft.Members.Where(member => !string.IsNullOrWhiteSpace(member.LengthInputError)))
            issues.Add(Block("INPUT-CABLE-LENGTH", member.LengthInputError!, [draft.CableAssemblyId, member.CableInstanceId]));

        foreach (var member in draft.Members)
        {
            if (HasOtherOwner(project, member.CableInstanceId, draft.IsNew ? null : draft.CableAssemblyId))
                issues.Add(Block(
                    "EDIT-CABLE-ASSEMBLY-OWNERSHIP",
                    $"線段 '{member.DisplayLabel}' 已屬於其他複合線，不可自動重新歸屬。",
                    [draft.CableAssemblyId, member.CableInstanceId]));
        }

        var candidate = Clone(project);
        UpsertCandidateAssembly(candidate, draft);
        var structural = new ElectricalProjectValidator().Validate(candidate).Results
            .Where(result => result.RuleId.StartsWith("RULE-CABLE-ASSEMBLY-", StringComparison.Ordinal))
            .Where(result => result.SourceObjectIds.Contains(draft.CableAssemblyId, StringComparer.OrdinalIgnoreCase))
            .Select(result => Block(result.RuleId, ToHumanMessage(result), result.SourceObjectIds));
        issues.AddRange(structural);

        if (draft.CableConstructionType == CableConstructionType.Unknown)
            issues.Add(Warning("WARNING-CONSTRUCTION-UNKNOWN", "尚未設定線材類型。", [draft.CableAssemblyId]));
        issues.AddRange(draft.Members
            .Where(member => member.SegmentRoleType == CableAssemblySegmentRoleType.Unknown)
            .Select(member => Warning(
                "WARNING-ROLE-UNKNOWN",
                $"線段 '{member.DisplayLabel}' 的角色尚未設定。",
                [draft.CableAssemblyId, member.CableInstanceId])));

        return new CableAssemblyEditValidation(issues);
    }

    public CableAssembly Apply(ElectricalProject project, CableAssemblyEditDraft draft)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(draft);
        var validation = Validate(project, draft);
        if (!validation.CanSave)
            throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Issues
                .Where(issue => issue.IsBlocking)
                .Select(issue => $"{issue.Code}: {issue.Message}")));

        var existing = project.CableAssemblies.FirstOrDefault(assembly => string.Equals(
            assembly.CableAssemblyId,
            draft.CableAssemblyId,
            StringComparison.OrdinalIgnoreCase));
        if (!draft.IsNew && existing is null)
            throw new InvalidOperationException($"找不到要更新的複合線 '{draft.CableAssemblyId}'。");
        if (draft.IsNew && existing is not null)
            throw new InvalidOperationException($"複合線 ID '{draft.CableAssemblyId}' 已存在。");

        var cableLengthUpdates = draft.Members
            .Where(member => member.LengthWasEdited)
            .Select(member => (Cable: FindCable(project, member.CableInstanceId), Member: member))
            .ToArray();
        var replacement = BuildAssembly(draft, existing);

        if (existing is null)
        {
            project.CableAssemblies.Add(replacement);
        }
        else
        {
            var index = project.CableAssemblies.IndexOf(existing);
            project.CableAssemblies[index] = replacement;
        }

        foreach (var update in cableLengthUpdates)
        {
            update.Cable.ProvidedLengthMm = update.Member.ProvidedLengthMm;
            update.Cable.LengthSource = update.Member.LengthSource;
        }

        return replacement;
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
            LegacyPurpose = member?.Purpose,
            ProvidedLengthMm = cable.ProvidedLengthMm,
            LengthSource = cable.LengthSource,
            OriginalProvidedLengthMm = cable.ProvidedLengthMm,
            OriginalLengthSource = cable.LengthSource
        };
    }

    private static void UpsertCandidateAssembly(ElectricalProject candidate, CableAssemblyEditDraft draft)
    {
        var existing = candidate.CableAssemblies.FirstOrDefault(assembly => string.Equals(
            assembly.CableAssemblyId,
            draft.CableAssemblyId,
            StringComparison.OrdinalIgnoreCase));
        var replacement = BuildAssembly(draft, existing);
        if (existing is null)
        {
            candidate.CableAssemblies.Add(replacement);
            return;
        }

        candidate.CableAssemblies[candidate.CableAssemblies.IndexOf(existing)] = replacement;
    }

    private static CableAssembly BuildAssembly(CableAssemblyEditDraft draft, CableAssembly? existing)
    {
        var assembly = new CableAssembly
        {
            CableAssemblyId = draft.CableAssemblyId,
            ReferenceDesignator = draft.ReferenceDesignator,
            CableConstructionType = draft.CableConstructionType,
            IsCustom = existing?.IsCustom ?? false,
            EndAConnectorId = existing?.EndAConnectorId,
            EndBConnectorId = existing?.EndBConnectorId
        };
        foreach (var member in draft.Members)
        {
            assembly.Members.Add(new CableAssemblyMember
            {
                CableInstanceId = member.CableInstanceId,
                SegmentRoleType = member.SegmentRoleType,
                SegmentRoleIndex = member.SegmentRoleIndex,
                SegmentRoleName = member.SegmentRoleName,
                Purpose = member.LegacyPurpose
            });
        }

        return assembly;
    }

    private static bool HasOtherOwner(ElectricalProject project, string cableInstanceId, string? exceptAssemblyId) =>
        project.CableAssemblies.Any(assembly =>
            !string.Equals(assembly.CableAssemblyId, exceptAssemblyId, StringComparison.OrdinalIgnoreCase) &&
            assembly.Members.Any(member => string.Equals(
                member.CableInstanceId,
                cableInstanceId,
                StringComparison.OrdinalIgnoreCase)));

    private static bool TryParsePositiveNumber(string text, out double value)
    {
        var styles = NumberStyles.Float | NumberStyles.AllowThousands;
        var parsed = double.TryParse(text.Trim(), styles, CultureInfo.InvariantCulture, out value) ||
                     double.TryParse(text.Trim(), styles, CultureInfo.CurrentCulture, out value);
        return parsed && double.IsFinite(value) && value > 0;
    }

    private static CableAssemblyEditIssue Block(string code, string message, IReadOnlyList<string> sourceIds) =>
        new(code, CableAssemblyEditIssueSeverity.Block, message, sourceIds);

    private static CableAssemblyEditIssue Warning(string code, string message, IReadOnlyList<string> sourceIds) =>
        new(code, CableAssemblyEditIssueSeverity.Warning, message, sourceIds);

    private static string ToHumanMessage(ValidationResult result) => result.RuleId switch
    {
        "RULE-CABLE-ASSEMBLY-001" => "只能有一條主幹，請重新指定。",
        "RULE-CABLE-ASSEMBLY-002" => "分支編號重複，請使用其他分支編號。",
        "RULE-CABLE-ASSEMBLY-003" => "分支編號必須是大於 0 的整數。",
        "RULE-CABLE-ASSEMBLY-004" => "主幹不能帶有分支編號。",
        "RULE-CABLE-ASSEMBLY-005" => "「其他」角色需要填寫名稱。",
        "RULE-CABLE-ASSEMBLY-006" => "此線段已不存在，請重新整理或移除。",
        "RULE-CABLE-ASSEMBLY-007" => "同一線段不能在同一複合線中重複加入。",
        _ => result.Message
    };

    private static ElectricalProject Clone(ElectricalProject project)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(project, options), options)
               ?? throw new InvalidOperationException("無法建立複合線驗證 working copy。");
    }
}
