using ComponentIntelligence.Electrical.Domain;
using System.Text.Json;

namespace ComponentIntelligence.Electrical.Topology;

public sealed record CommonConnectorContact(string EndpointId, string PortId, string Label);

public sealed class CommonConnectorInsertionDraft
{
    internal ComponentInstance Component { get; }
    internal string ConnectionId { get; }
    internal string OriginalConnection { get; }
    public IReadOnlyList<CommonConnectorContact> Contacts { get; }
    public string? FromContactId { get; set; }
    public string? ToContactId { get; set; }
    public string Reference { get; set; }

    internal CommonConnectorInsertionDraft(ComponentInstance component, ElectricalConnection connection)
    {
        Component = component;
        ConnectionId = connection.ConnectionId;
        OriginalConnection = JsonSerializer.Serialize(connection);
        Reference = component.ReferenceDesignator!;
        Contacts = component.Ports.SelectMany(p => p.Pins.Count == 0
            ? new[] { new CommonConnectorContact(p.PortId, p.PortId, p.Name ?? p.PortId) }
            : p.Pins.Select(pin => new CommonConnectorContact(pin.PinId, p.PortId, $"{p.Name} / Pin {pin.PinNumber}"))).ToArray();
    }
}

public sealed partial class TopologyConnectionEditor
{
    public CommonConnectorInsertionDraft PrepareCommonConnector(ElectricalProject project, string connectionId, string definitionId)
    {
        var connection = FindConnection(project, connectionId);
        EnsureInterfaceInsertionAllowed(project, connection);
        return new(CommonConnectorCatalog.Create(definitionId, NextReference(project, "X")), connection);
    }

    public ComponentInstance ApplyCommonConnector(ElectricalProject project, CommonConnectorInsertionDraft draft)
    {
        var connection = FindConnection(project, draft.ConnectionId);
        EnsureInterfaceInsertionAllowed(project, connection);
        if (JsonSerializer.Serialize(connection) != draft.OriginalConnection)
            throw new InvalidOperationException("原連線已變更，請重新開啟插入介面。");
        var from = draft.Contacts.SingleOrDefault(c => c.EndpointId == draft.FromContactId);
        var to = draft.Contacts.SingleOrDefault(c => c.EndpointId == draft.ToContactId);
        if (from is null || to is null || from.PortId == to.PortId)
            throw new InvalidOperationException("請明確選擇接頭兩側不同介面的接點。");
        if (string.IsNullOrWhiteSpace(draft.Reference) || project.Components.Any(c =>
            c.ComponentInstanceId == draft.Component.ComponentInstanceId ||
            string.Equals(c.ReferenceDesignator, draft.Reference.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("接頭代號不可空白或重複。");

        draft.Component.ReferenceDesignator = draft.Reference.Trim();
        project.Components.Add(draft.Component);
        InsertPlacementAtConnectionMidpoint(project, connection, draft.Component.ComponentInstanceId, "COMPONENT", 130, 68);
        ReplaceWithTwoSegments(project, connection, from.EndpointId, to.EndpointId);
        return draft.Component;
    }

    private static void EnsureInterfaceInsertionAllowed(ElectricalProject project, ElectricalConnection connection)
    {
        if (connection.Kind != ConnectionKind.Wire || connection.CableInstanceId is not null || connection.CableCoreId is not null ||
            project.CableAssemblies.Any(a => a.PhysicalTopology?.ConnectionIds.Contains(connection.ConnectionId, StringComparer.Ordinal) == true))
            throw new InvalidOperationException("請在普通配線插入介面；既有線材歸屬不可隱含拆分。");
        if (connection.ProvidedLengthMm is not null || connection.LengthSource != CableLengthSource.Unknown)
            throw new InvalidOperationException("此配線已有工程線長；插入介面前需先確認分段線長，不可自動分配。");
    }
}
