using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

public static class ConnectorMatingService
{
    public static IEnumerable<ComponentPort> Ports(ElectricalProject project) =>
        project.Components.SelectMany(c => c.Ports).Concat(project.Cables.Where(c => c.ArchivedCable is not null).SelectMany(c => c.ArchivedCable!.Ports));

    public static ComponentPort RequirePort(ElectricalProject project, string id) =>
        Ports(project).SingleOrDefault(p => p.PortId == id) ?? throw new InvalidOperationException("請選擇確切的接頭 Port。");

    public static IReadOnlyList<ConnectorMatingPinPair> Prepare(ElectricalProject project, string fromPortId, string toPortId)
    {
        var from = RequirePort(project, fromPortId); var to = RequirePort(project, toPortId);
        CheckCompatibility(from, to); CheckOwners(project, fromPortId, toPortId);
        return from.Pins.OrderBy(p => p.PinNumber, Drawing.DrawingContactDisplayOrder.NumberComparer)
            .Where(p => to.Pins.Any(q => q.PinNumber.Trim() == p.PinNumber.Trim()))
            .Select(p => new ConnectorMatingPinPair(p.PinId, to.Pins.Single(q => q.PinNumber.Trim() == p.PinNumber.Trim()).PinId)).ToArray();
    }
    public static ElectricalConnection Connect(ElectricalProject project, string fromPortId, string toPortId,
        IReadOnlyList<ConnectorMatingPinPair> pairs, string evidence)
    {
        var connection = new ElectricalConnection { ConnectionId = "mate-" + Guid.NewGuid().ToString("N"),
            FromEndpointId = fromPortId, ToEndpointId = toPortId, Kind = ConnectionKind.DirectMating,
            MatingMap = new() { Pairs = pairs.ToList(), Confirmed = true, Evidence = evidence.Trim() } };
        Validate(project, connection); project.Connections.Add(connection); return connection;
    }

    public static void Validate(ElectricalProject project, ElectricalConnection connection)
    {
        if (project.Connections.Count(c => string.Equals(c.ConnectionId, connection.ConnectionId, StringComparison.OrdinalIgnoreCase)) > 1)
            throw new InvalidOperationException("連線身分重複；請核對專案接線資料，不能合併接頭回路。");
        if (connection.MatingMap is not { } map) return;
        if (connection.Kind != ConnectionKind.DirectMating || !map.Confirmed || string.IsNullOrWhiteSpace(map.Evidence) ||
            connection.CableInstanceId is not null || connection.CableCoreId is not null || connection.ConductorAreaMm2 is not null)
            throw new InvalidOperationException("接頭對接需確認針號；不能建立額外線材或 AWG。");
        var from = RequirePort(project, connection.FromEndpointId); var to = RequirePort(project, connection.ToEndpointId);
        CheckCompatibility(from, to); CheckOwners(project, from.PortId, to.PortId);
        if (map.Pairs.Count != from.Pins.Count ||
            !map.Pairs.Select(p => p.FromPinId).ToHashSet(StringComparer.Ordinal).SetEquals(from.Pins.Select(p => p.PinId)) ||
            !map.Pairs.Select(p => p.ToPinId).ToHashSet(StringComparer.Ordinal).SetEquals(to.Pins.Select(p => p.PinId)))
            throw new InvalidOperationException("接頭對接必須逐 Pin 完整且一對一；請補齊針號或移除重複配對。");
        var occupied = new[] { from.PortId, to.PortId }.Concat(from.Pins.Concat(to.Pins).Select(p => p.PinId)).ToHashSet(StringComparer.Ordinal);
        if (project.Connections.Where(c => c.ConnectionId != connection.ConnectionId).Any(c =>
            occupied.Contains(c.FromEndpointId) || occupied.Contains(c.ToEndpointId) ||
            c.MatingMap?.Pairs.Any(p => occupied.Contains(p.FromPinId) || occupied.Contains(p.ToPinId)) == true))
            throw new InvalidOperationException("這個接頭已對接或已逐 Pin 接線；請先解除原連線。");
    }

    public static bool UsesPin(ElectricalConnection connection, string pinId) =>
        connection.MatingMap?.Pairs.Any(p => p.FromPinId == pinId || p.ToPinId == pinId) == true;

    private static void CheckOwners(ElectricalProject project, string first, string second)
    {
        string? Owner(string id) => project.Components.FirstOrDefault(c => c.Ports.Any(p => p.PortId == id))?.ComponentInstanceId ??
            project.Cables.FirstOrDefault(c => c.ArchivedCable?.Ports.Any(p => p.PortId == id) == true)?.CableInstanceId;
        if (first == second || Owner(first) == Owner(second)) throw new InvalidOperationException("請選擇兩個不同實體的接頭。");
    }
    private static void CheckCompatibility(ComponentPort from, ComponentPort to)
    {
        var a = from.Connector; var b = to.Connector;
        if (a is null || b is null || string.IsNullOrWhiteSpace(a.Family) || a.Family.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            !Same(a.Family, b.Family) || !((a.Gender == ConnectorGender.Male && b.Gender == ConnectorGender.Female) ||
                (a.Gender == ConnectorGender.Female && b.Gender == ConnectorGender.Male)))
            throw new InvalidOperationException("接頭形式與公母不相容或未設定；請先在接頭／接法表設定。");
        var needsCode = a.Family.StartsWith("M12", StringComparison.OrdinalIgnoreCase) || a.Family.StartsWith("M8", StringComparison.OrdinalIgnoreCase);
        if (needsCode && (string.IsNullOrWhiteSpace(a.Coding) || string.IsNullOrWhiteSpace(b.Coding)) || !Same(a.Coding, b.Coding) ||
            !OptionalSame(a.SeriesOrSize, b.SeriesOrSize) || !OptionalSame(a.CompatibilityClass, b.CompatibilityClass))
            throw new InvalidOperationException("接頭 Coding／系列不相容或未設定。");
        if (from.Pins.Count == 0 || from.Pins.Count != to.Pins.Count || a.PinCount != from.Pins.Count || b.PinCount != to.Pins.Count ||
            new[] { from, to }.Any(p => p.Pins.Any(pin => string.IsNullOrWhiteSpace(pin.PinNumber)) ||
                p.Pins.Select(pin => pin.PinNumber.Trim()).Distinct(StringComparer.Ordinal).Count() != p.Pins.Count))
            throw new InvalidOperationException("接頭 Pin 數或針號明細不完整；請先補齊，不能依畫面順序猜測。");
    }
    private static bool Same(string? a, string? b) => string.Equals(a?.Trim() ?? "", b?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);
    private static bool OptionalSame(string? a, string? b) => string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) || Same(a, b);
}
