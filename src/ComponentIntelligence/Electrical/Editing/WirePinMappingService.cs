using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Electrical.Editing;

public sealed class WirePinMappingService
{
    public (ComponentPort From, ComponentPort To) GetPorts(ElectricalProject project, string connectionId)
    {
        var connection = Wire(project, connectionId);
        ComponentPort Resolve(string id)
        {
            var ports = project.Components.SelectMany(c => c.Ports)
                .Where(p => p.PortId == id || p.Pins.Any(pin => pin.PinId == id)).ToArray();
            if (ports.Length != 1) throw new InvalidOperationException("端點無法唯一對應至元件介面。");
            return ports[0];
        }
        return (Resolve(connection.FromEndpointId), Resolve(connection.ToEndpointId));
    }

    public void Apply(ElectricalProject project, string connectionId, string fromPinId, string toPinId)
    {
        var original = Wire(project, connectionId);
        var ports = GetPorts(project, connectionId);
        if (!ports.From.Pins.Any(p => p.PinId == fromPinId) || !ports.To.Pins.Any(p => p.PinId == toPinId))
            throw new InvalidOperationException("請選擇原連線兩側介面內明確的 Pin。");
        if (original.FromEndpointId == fromPinId && original.ToEndpointId == toPinId) return;
        // Validate both reconnections on a disposable graph before committing either endpoint.
        var working = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(project))!;
        var editor = new TopologyTerminalJunctionService();
        editor.ReconnectEndpoint(working, connectionId, true, fromPinId);
        var replacement = editor.ReconnectEndpoint(working, connectionId, false, toPinId);
        project.Connections[project.Connections.IndexOf(original)] = replacement;
        project.TopologyRoutes.RemoveAll(r => r.ConnectionId == connectionId);
    }

    private static ElectricalConnection Wire(ElectricalProject project, string id)
    {
        var connection = project.Connections.SingleOrDefault(c => c.ConnectionId == id)
            ?? throw new InvalidOperationException("連線已變更，請重新開啟。");
        if (connection.Kind != ConnectionKind.Wire || connection.CableInstanceId is not null || connection.CableCoreId is not null ||
            project.CableAssemblies.Any(a => a.PhysicalTopology?.ConnectionIds.Contains(id, StringComparer.Ordinal) == true))
            throw new InvalidOperationException("此操作只適用於沒有線材歸屬的普通配線。");
        return connection;
    }
}
