using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Editing;

public sealed record CableEndpointContext(string EndpointGroupId, string Label);

public sealed class UnifiedCableSettingsService
{
    public IReadOnlyList<CableEndpointContext> DescribeEnds(ElectricalProject project, IReadOnlyCollection<string> connectionIds) =>
        Selected(project, connectionIds).SelectMany(c => new[] { c.FromEndpointId, c.ToEndpointId })
            .Select(id => End(project, id)).DistinctBy(e => e.EndpointGroupId, StringComparer.Ordinal)
            .OrderBy(e => e.Label, StringComparer.Ordinal).ToArray();

    public CableInstance ApplyPointToPoint(ElectricalProject project, IReadOnlyCollection<string> connectionIds,
        CableConstructionType construction, string? reference, string? definitionId, double? lengthMm,
        bool confirmConsolidation)
    {
        if (construction is not (CableConstructionType.Purchased or CableConstructionType.Custom))
            throw new InvalidOperationException("請明確選擇外購成品線或自製加工線。");
        if (lengthMm.HasValue && (!double.IsFinite(lengthMm.Value) || lengthMm <= 0))
            throw new InvalidOperationException("線長必須為正數；未知請留白。");
        var connections = Selected(project, connectionIds);
        if (DescribeEnds(project, connectionIds).Count != 2)
            throw new InvalidOperationException("此選取包含多個實體端點，請確認多端線材的共同端與分支。");
        var previous = ValidateOwnership(project, connections);
        if (previous.Length > 1 && !confirmConsolidation)
            throw new InvalidOperationException("請先確認將選取的既有線材合併為同一條實體線。");
        if (previous.Length > 1 && (previous.Any(c => c.CoreAssignments.Count > 0) || connections.Any(c => c.CableCoreId is not null)))
            throw new InvalidOperationException("既有線材含獨立 Core 證據；不可自動重新編號或合併，請先確認 Core 對應。");

        var cable = previous.Length == 1 ? previous[0] : new CableInstance {
            CableInstanceId = $"cbl-{Guid.NewGuid():N}", CableDefinitionId = "UNRESOLVED-CABLE"
        };
        // All checks precede mutation. No core number, connector, assembly or electrical edge is invented.
        if (previous.Length != 1) project.Cables.Add(cable);
        cable.CableConstructionType = construction;
        if (reference is not null) cable.ReferenceDesignator = reference.Trim();
        if (!string.IsNullOrWhiteSpace(definitionId)) cable.CableDefinitionId = definitionId.Trim();
        if (lengthMm.HasValue) { cable.ProvidedLengthMm = lengthMm; cable.LengthSource = CableLengthSource.User; }
        foreach (var connection in connections) { connection.CableInstanceId = cable.CableInstanceId; connection.Kind = ConnectionKind.Cable; }
        foreach (var old in previous.Where(c => !ReferenceEquals(c, cable))) project.Cables.Remove(old);
        return cable;
    }

    public void ApplyOrdinaryWire(ElectricalProject project, IReadOnlyCollection<string> connectionIds)
    {
        var connections = Selected(project, connectionIds);
        var previous = ValidateOwnership(project, connections);
        if (previous.Any(c => c.CoreAssignments.Count > 0) || connections.Any(c => c.CableCoreId is not null))
            throw new InvalidOperationException("此線材仍有 Core 對應證據，請先確認，不可隱含刪除。");
        foreach (var connection in connections) { connection.CableInstanceId = null; connection.Kind = ConnectionKind.Wire; }
        foreach (var cable in previous) project.Cables.Remove(cable);
    }

    private static ElectricalConnection[] Selected(ElectricalProject project, IReadOnlyCollection<string> ids)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (ids.Count == 0 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new InvalidOperationException("請選取不重複的連線。");
        var connections = ids.Select(id => project.Connections.SingleOrDefault(c => c.ConnectionId == id)
            ?? throw new InvalidOperationException("選取的連線已變更，請重新選取。")).ToArray();
        if (connections.Any(c => c.Kind == ConnectionKind.DirectMating))
            throw new InvalidOperationException("接頭直接對接不是線材。");
        return connections;
    }

    private static CableInstance[] ValidateOwnership(ElectricalProject project, ElectricalConnection[] connections)
    {
        var selected = connections.Select(c => c.ConnectionId).ToHashSet(StringComparer.Ordinal);
        var cableIds = connections.Select(c => c.CableInstanceId).Where(id => id is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
        if (project.CableAssemblies.Any(a => a.Members.Any(m => cableIds.Contains(m.CableInstanceId)) ||
            a.PhysicalTopology is { } p && (cableIds.Contains(p.CableInstanceId) || p.ConnectionIds.Any(selected.Contains))))
            throw new InvalidOperationException("此線路屬於既有組合線材，請使用其線材設定，不可拆除成獨立線段。");
        if (project.Connections.Any(c => c.CableInstanceId is { } id && cableIds.Contains(id) && !selected.Contains(c.ConnectionId)))
            throw new InvalidOperationException("請選取該實體線材的全部導體；不可只修改其中一條。");
        return cableIds.Select(id => project.Cables.SingleOrDefault(c => c.CableInstanceId == id)
            ?? throw new InvalidOperationException("線材歸屬資料缺失，請先確認。")).ToArray();
    }

    private static CableEndpointContext End(ElectricalProject project, string id)
    {
        var matches = project.Components.SelectMany(c => c.Ports.Where(p => p.PortId == id || p.Pins.Any(pin => pin.PinId == id))
            .Select(p => new CableEndpointContext(p.PortId, $"{c.ReferenceDesignator ?? c.DisplayName ?? c.ComponentInstanceId} / {p.Name}"))).ToArray();
        if (matches.Length == 1) return matches[0];
        var terminals = project.TerminalBlocks.SelectMany(b => b.Positions.SelectMany(p => p.Levels
            .Where(l => l.ConnectionPoints.Any(cp => cp.ConnectionPointId == id))
            .Select(l => new CableEndpointContext(id, $"{b.ReferenceDesignator} / {p.PositionLabel} / {l.LevelName}")))).ToArray();
        if (matches.Length == 0 && terminals.Length == 1) return terminals[0];
        throw new InvalidOperationException("連線端點缺失或不唯一，無法判定實體端點群組。");
    }
}
